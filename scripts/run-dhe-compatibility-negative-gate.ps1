[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$OutputRoot = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$project = Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCasesAot/HybridCLR.ManagedCasesAot.csproj"
$dnlibPath = Join-Path $LabRoot "unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll"
$outputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-compatibility-negative-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
Assert-DheSafeOutputRoot -Path $outputRoot
Assert-DheOutputNotAncestor -Path $outputRoot -Root $LabRoot
Assert-DheSafeOutputRoot -Path $outputRoot -ProtectedPaths @($project)
$null = Initialize-DheOutputRoot -Path $outputRoot -Force:$ForceOutput -ProtectedPaths @($project)

function Build-Variant([string]$destination, [string]$variant) {
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $defines = "HYBRIDCLR_AOT_BENCHMARK%3BDHE_NEGATIVE_FIXTURE"
    if (-not [string]::IsNullOrWhiteSpace($variant)) {
        $defines += "%3B$variant"
    }
    # Keep each define set in its own intermediate directory. MSBuild can
    # otherwise reuse a successful baseline compile when only DefineConstants
    # changed, making a negative fixture look unchanged.
    $variantName = if ([string]::IsNullOrWhiteSpace($variant)) { "baseline" } else { $variant }
    $intermediate = Join-Path (Join-Path (Split-Path -Parent $project) "obj") ("DHE-Negative-" + $variantName)
    New-Item -ItemType Directory -Force -Path $intermediate | Out-Null
    $intermediate = $intermediate + [IO.Path]::DirectorySeparatorChar
    & dotnet build $project --configuration $Configuration --output $destination --nologo -v:minimal `
        "-p:DefineConstants=$defines" "-p:IntermediateOutputPath=$intermediate" `
        "-p:MSBuildProjectExtensionsPath=$intermediate" | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to build negative fixture '$variant'."
    }
    return (Join-Path $destination "HybridCLR.ManagedCasesAot.dll")
}

function Invoke-Scenario([string]$name, [string]$variant) {
    $scenarioRoot = Join-Path $outputRoot $name
    New-Item -ItemType Directory -Force -Path $scenarioRoot | Out-Null
    $baselineRoot = Join-Path $scenarioRoot "baseline"
    $currentRoot = Join-Path $scenarioRoot "current"
    $analysisPath = Join-Path $scenarioRoot "HybridCLR.ManagedCasesAot.analysis.mv.json"
    $strictPath = Join-Path $scenarioRoot "HybridCLR.ManagedCasesAot.mv.json"
    $baseline = Build-Variant $baselineRoot ""
    $current = Build-Variant $currentRoot $variant

    # Output paths are caller-controlled and must never be able to destroy the
    # DLLs that the diff is reading. Exercise both output channels against
    # private copies so an implementation regression cannot damage the fixture
    # inputs themselves.
    $guardBaseline = Join-Path $scenarioRoot "path-guard-baseline.dll"
    $guardCurrent = Join-Path $scenarioRoot "path-guard-current.dll"
    Copy-Item -LiteralPath $baseline -Destination $guardBaseline -Force
    Copy-Item -LiteralPath $current -Destination $guardCurrent -Force
    $guardBaselineHash = (Get-FileHash -LiteralPath $guardBaseline -Algorithm SHA256).Hash
    $guardCurrentHash = (Get-FileHash -LiteralPath $guardCurrent -Algorithm SHA256).Hash
    $jsonOverwriteRejected = $false
    try {
        & (Join-Path $LabRoot "scripts/generate-dhe-mv.ps1") `
            -BaselineAssembly $guardBaseline `
            -CurrentAssembly $guardCurrent `
            -DnlibPath $dnlibPath `
            -Output $guardBaseline
    }
    catch {
        $jsonOverwriteRejected = $true
    }
    if (-not $jsonOverwriteRejected -or
        (Get-FileHash -LiteralPath $guardBaseline -Algorithm SHA256).Hash -ne $guardBaselineHash) {
        throw "Scenario '$name' allowed MV JSON output to overwrite a baseline input."
    }

    $binaryOverwriteRejected = $false
    try {
        & (Join-Path $LabRoot "scripts/generate-dhe-mv.ps1") `
            -BaselineAssembly $guardBaseline `
            -CurrentAssembly $guardCurrent `
            -DnlibPath $dnlibPath `
            -Output (Join-Path $scenarioRoot "path-guard.mv.json") `
            -BinaryOutput $guardCurrent `
            -StrictCompatibility
    }
    catch {
        $binaryOverwriteRejected = $true
    }
    if (-not $binaryOverwriteRejected -or
        (Get-FileHash -LiteralPath $guardCurrent -Algorithm SHA256).Hash -ne $guardCurrentHash) {
        throw "Scenario '$name' allowed MV binary output to overwrite a current input."
    }

    $strictRejected = $false
    try {
        & (Join-Path $LabRoot "scripts/generate-dhe-mv.ps1") `
            -BaselineAssembly $baseline `
            -CurrentAssembly $current `
            -DnlibPath $dnlibPath `
            -Output $strictPath `
            -StrictCompatibility
    }
    catch {
        $strictRejected = $true
    }
    if (-not $strictRejected -or -not (Test-Path -LiteralPath $strictPath -PathType Leaf)) {
        throw "Scenario '$name' did not produce a strict incompatibility report."
    }
    $strictMv = Get-Content -Raw -LiteralPath $strictPath | ConvertFrom-Json
    if ([string]$strictMv.compatibility.status -ne "incompatible") {
        throw "Scenario '$name' strict generation failed for an unexpected reason or did not classify the diff as incompatible."
    }
    & (Join-Path $LabRoot "scripts/generate-dhe-mv.ps1") `
        -BaselineAssembly $baseline `
        -CurrentAssembly $current `
        -DnlibPath $dnlibPath `
        -Output $analysisPath
    if (-not (Test-Path -LiteralPath $analysisPath -PathType Leaf)) {
        throw "Scenario '$name' did not produce an analysis report."
    }

    $mv = Get-Content -Raw -LiteralPath $analysisPath | ConvertFrom-Json
    if ($mv.compatibility.status -ne "incompatible") {
        throw "Scenario '$name' was expected to be rejected by strict compatibility."
    }

    return [ordered]@{
        name = $name
        variant = $variant
        strictRejected = $strictRejected
        strictReport = $strictPath
        analysisReport = $analysisPath
        jsonOverwriteRejected = $jsonOverwriteRejected
        binaryOverwriteRejected = $binaryOverwriteRejected
        compatibility = $mv.compatibility
        summary = $mv.summary
    }
}

$scenarios = @(
    (Invoke-Scenario "token-drift" "DHE_TOKEN_DRIFT"),
    (Invoke-Scenario "method-addition" "DHE_ADD_METHOD"),
    (Invoke-Scenario "type-layout-change" "DHE_LAYOUT_CHANGE"),
    (Invoke-Scenario "field-constant-change" "DHE_FIELD_CONSTANT_CHANGE")
)

$reportPath = Join-Path $outputRoot "compatibility-negative-gate-report.json"
$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-compatibility-negative-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $true
    scenarios = $scenarios
}
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 14), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE compatibility negative gate: $reportPath"
foreach ($scenario in $scenarios) {
    Write-Host ("{0}: rejected={1}; reasons={2}" -f $scenario.name, $scenario.strictRejected, @($scenario.compatibility.reasons).Count)
}
exit 0
