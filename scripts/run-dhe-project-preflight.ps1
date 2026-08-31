[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SettingsFile,
    [Parameter(Mandatory = $true)]
    [string]$BaselineRoot,
    [Parameter(Mandatory = $true)]
    [string]$CurrentRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [string]$ProjectRoot = "",
    [string]$RuntimeSource = "",
    [string]$PackageLockPath = "",
    [string]$IdentityTemplatePath = "",
    [string]$DnlibPath = "",
    [switch]$RequireRuntime,
    [switch]$RequireEmbeddedPackage,
    [switch]$RequireIdentityTemplate,
    [switch]$RequireCompleteCoverage,
    [switch]$RequireDheEqualsHotUpdate,
    [switch]$RequireNonSurrogateExternalHeaders,
    [switch]$RequireCleanRuntimeSources,
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$LabRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$settingsPath = [IO.Path]::GetFullPath($SettingsFile)
$baselinePath = [IO.Path]::GetFullPath($BaselineRoot)
$currentPath = [IO.Path]::GetFullPath($CurrentRoot)
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
$projectRootPath = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    Split-Path -Parent (Split-Path -Parent $settingsPath)
} else {
    [IO.Path]::GetFullPath($ProjectRoot)
}
$reportPath = Join-Path $outputPath "project-preflight-report.json"
$outputRootSafe = $false
$sourcePreflightPath = $null
$sourcePreflightPassed = $null
$resolvedDnlibPath = $null

try {
Assert-DheSafeOutputRoot -Path $outputPath -ProtectedPaths @(
    $projectRootPath, $baselinePath, $currentPath, $RuntimeSource)
Assert-DheOutputNotAncestor -Path $outputPath -Root $LabRoot
if (Test-Path -LiteralPath $outputPath) {
    if (-not $ForceOutput -and @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0) {
        throw "OutputRoot is not empty: $outputPath. Pass -ForceOutput to replace a prior run."
    }
    if ($ForceOutput) { Remove-Item -LiteralPath $outputPath -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
$outputRootSafe = $true

foreach ($item in @(@($settingsPath, "HybridCLR settings", "Leaf"), @($baselinePath, "Baseline root", "Container"), @($currentPath, "Current root", "Container"))) {
    if (-not (Test-Path -LiteralPath $item[0] -PathType $item[2])) {
        throw "$($item[1]) was not found: $($item[0])"
    }
}
$resolvedDnlibPath = Resolve-DheDnlibPath -RequestedPath $DnlibPath -ProjectRoot $projectRootPath -PackageLockPath $PackageLockPath
$scriptHost = Resolve-DhePowerShellHost
$runSourcePreflight = $RequireRuntime -or $RequireEmbeddedPackage -or $RequireIdentityTemplate -or
    $RequireCleanRuntimeSources -or
    -not [string]::IsNullOrWhiteSpace($RuntimeSource) -or
    -not [string]::IsNullOrWhiteSpace($PackageLockPath) -or
    -not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)
if ($runSourcePreflight) {
    $sourcePreflightPath = Join-Path $outputPath "source-preflight/source-preflight-report.json"
    $sourcePreflightArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
        (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1"),
        "-LabRoot", $LabRoot,
        "-ProjectPath", $projectRootPath,
        "-SettingsFile", $settingsPath,
        "-OutputRoot", (Split-Path -Parent $sourcePreflightPath),
        "-ForceOutput"
    )
    if (-not [string]::IsNullOrWhiteSpace($RuntimeSource)) {
        $sourcePreflightArgs += @("-RuntimeSource", [IO.Path]::GetFullPath($RuntimeSource))
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        $sourcePreflightArgs += @("-PackageLockPath", [IO.Path]::GetFullPath($PackageLockPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
        $sourcePreflightArgs += @("-IdentityTemplatePath", [IO.Path]::GetFullPath($IdentityTemplatePath))
    }
    if ($RequireRuntime) { $sourcePreflightArgs += "-RequireRuntime" }
    if ($RequireEmbeddedPackage) { $sourcePreflightArgs += "-RequireEmbeddedPackage" }
    if ($RequireIdentityTemplate) { $sourcePreflightArgs += "-RequireIdentityTemplate" }
    if ($RequireNonSurrogateExternalHeaders) { $sourcePreflightArgs += "-RequireNonSurrogateExternalHeaders" }
    if ($RequireCleanRuntimeSources) { $sourcePreflightArgs += "-RequireCleanRuntimeSources" }
    & $scriptHost @sourcePreflightArgs | Out-Null
    $sourcePreflightExitCode = $LASTEXITCODE
    if (-not (Test-Path -LiteralPath $sourcePreflightPath -PathType Leaf)) {
        throw "DHE source preflight did not produce a report: $sourcePreflightPath"
    }
$sourcePreflightReport = Get-Content -Raw -LiteralPath $sourcePreflightPath | ConvertFrom-Json
$sourcePreflightPassed = $sourcePreflightExitCode -eq 0 -and
    (Get-DheStrictBooleanProperty $sourcePreflightReport "passed" "DHE source preflight passed")
$sourceSettingsFileProperty = $sourcePreflightReport.PSObject.Properties["settingsFile"]
if ($null -eq $sourceSettingsFileProperty -or
    [string]::IsNullOrWhiteSpace([string]$sourceSettingsFileProperty.Value) -or
    ([IO.Path]::GetFullPath([string]$sourceSettingsFileProperty.Value) -ne $settingsPath)) {
    $sourcePreflightPassed = $false
}
    if (-not $sourcePreflightPassed) {
        throw "DHE source preflight failed. See $sourcePreflightPath"
    }
}

$batchPath = Join-Path $outputPath "batch"
$batchScript = Join-Path $LabRoot "scripts/generate-dhe-batch.ps1"
$batchArgs = @(
    "-NoProfile", "-File", $batchScript,
    "-BaselineRoot", $baselinePath,
    "-CurrentRoot", $currentPath,
    "-OutputRoot", $batchPath,
    "-SettingsFile", $settingsPath,
    "-ProjectRoot", $projectRootPath,
    "-StrictCompatibility"
)
if ($RequireCompleteCoverage) { $batchArgs += "-FailOnIncompatible" }
if ($RequireDheEqualsHotUpdate) { $batchArgs += "-RequireDheEqualsHotUpdate" }
if (-not [string]::IsNullOrWhiteSpace($DnlibPath)) { $batchArgs += @("-DnlibPath", [IO.Path]::GetFullPath($DnlibPath)) }
if ([string]::IsNullOrWhiteSpace($DnlibPath)) { $batchArgs += @("-DnlibPath", $resolvedDnlibPath) }
if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) { $batchArgs += @("-PackageLockPath", [IO.Path]::GetFullPath($PackageLockPath)) }
& $scriptHost -ExecutionPolicy Bypass @batchArgs | Out-Null
$batchExitCode = $LASTEXITCODE
$batchReportPath = Join-Path $batchPath "dhe-batch-summary.json"
if (-not (Test-Path -LiteralPath $batchReportPath -PathType Leaf)) {
    throw "DHE batch generator did not produce a summary: $batchReportPath"
}
$batch = Get-Content -Raw -LiteralPath $batchReportPath | ConvertFrom-Json

$validationRoot = Join-Path $outputPath "validation"
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
$validationRecords = New-Object System.Collections.Generic.List[object]
foreach ($assembly in @($batch.assemblies)) {
    $assemblyName = [string]$assembly.assemblyName
    $validationPath = Join-Path $validationRoot "$assemblyName.json"
    $validationExitCode = 0
    if ([string]$assembly.status -eq "compatible" -and
        (Test-Path -LiteralPath ([string]$assembly.report) -PathType Leaf) -and
        (Test-Path -LiteralPath ([string]$assembly.binary) -PathType Leaf)) {
        & $scriptHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
            -MvJson ([string]$assembly.report) `
            -MvBytes ([string]$assembly.binary) `
            -BaselineAssembly ([string]$assembly.baseline) `
            -CurrentAssembly ([string]$assembly.current) `
            -Output $validationPath | Out-Null
        $validationExitCode = $LASTEXITCODE
    } else {
        $validationExitCode = 1
        $validation = [ordered]@{
            schemaVersion = 1
            format = "hybridclr.dhe-artifact-validation.json"
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
            passed = $false
            errors = @("Batch assembly did not produce a compatible strict MV and binary.")
            warnings = @()
            mvJson = if ([string]::IsNullOrWhiteSpace([string]$assembly.report)) { $null } else { [string]$assembly.report }
            mvBytes = if ([string]::IsNullOrWhiteSpace([string]$assembly.binary)) { $null } else { [string]$assembly.binary }
            baselineAssembly = if ([string]::IsNullOrWhiteSpace([string]$assembly.baseline)) { $null } else { [string]$assembly.baseline }
            currentAssembly = if ([string]::IsNullOrWhiteSpace([string]$assembly.current)) { $null } else { [string]$assembly.current }
            nativeManifest = $null
            buildIdentity = $null
            workflowReport = $null
            batchReport = $batchReportPath
        }
        [IO.File]::WriteAllText($validationPath, ($validation | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    }
    $validationResult = Get-Content -Raw -LiteralPath $validationPath | ConvertFrom-Json
    $validationRecords.Add([ordered]@{
        assemblyName = $assemblyName
        batchStatus = [string]$assembly.status
        validationPassed = Get-DheStrictBooleanProperty $validationResult "passed" "Artifact validation passed for $assemblyName"
        validationReport = $validationPath
        error = [string]$assembly.error
    })
}

$counts = $batch.counts
$batchConfigurationPassed = Get-DheStrictBooleanProperty $batch "configurationPassed" "DHE batch configurationPassed"
$batchConfigurationErrors = New-Object System.Collections.Generic.List[string]
if ($null -ne $batch.PSObject.Properties["configurationErrors"]) {
    foreach ($configurationError in @($batch.configurationErrors)) {
        $batchConfigurationErrors.Add([string]$configurationError)
    }
}
$hotUpdateAssemblyList = New-Object System.Collections.Generic.List[string]
foreach ($assemblyName in @($batch.hotUpdateAssemblies)) {
    if (-not [string]::IsNullOrWhiteSpace([string]$assemblyName)) {
        $hotUpdateAssemblyList.Add(([string]$assemblyName).Trim())
    }
}
$dheAssemblyList = New-Object System.Collections.Generic.List[string]
foreach ($assemblyName in @($batch.dheAotAssemblies)) {
    if (-not [string]::IsNullOrWhiteSpace([string]$assemblyName)) {
        $dheAssemblyList.Add(([string]$assemblyName).Trim())
    }
}
$batchRequiresDheCoverage = Get-DheStrictBooleanProperty $batch "requireDheEqualsHotUpdate" "DHE batch requireDheEqualsHotUpdate"
$planRecords = New-Object System.Collections.Generic.List[object]
foreach ($assembly in @($batch.assemblies)) {
    $mv = $null
    if ([string]$assembly.status -eq "compatible" -and
        (Test-Path -LiteralPath ([string]$assembly.report) -PathType Leaf)) {
        try { $mv = Get-Content -Raw -LiteralPath ([string]$assembly.report) | ConvertFrom-Json } catch { $mv = $null }
    }
    $planRecords.Add([ordered]@{
        assemblyName = [string]$assembly.assemblyName
        status = [string]$assembly.status
        baseline = [string]$assembly.baseline
        current = [string]$assembly.current
        mvJson = if ($null -eq $mv) { $null } else { [string]$assembly.report }
        mvBytes = if ($null -eq $mv -or [string]::IsNullOrWhiteSpace([string]$assembly.binary)) { $null } else { [string]$assembly.binary }
        baselineSha256 = if ($null -eq $mv) { $null } else { [string]$mv.baseline.sha256 }
        currentSha256 = if ($null -eq $mv) { $null } else { [string]$mv.current.sha256 }
        changedMethodCount = if ($null -eq $mv) { 0 } else { [int]$mv.summary.changedMethodCount }
        compatibility = if ($null -eq $mv) { $null } else { [string]$mv.compatibility.status }
    })
}
$artifactRecordsComplete =
    [int]$counts.incompatible -eq 0 -and
    [int]$counts.missing -eq 0 -and [int]$counts.error -eq 0 -and
    @($validationRecords | Where-Object { -not $_.validationPassed }).Count -eq 0
$sourcePreflightGatePassed = $null -eq $sourcePreflightPassed -or [bool]$sourcePreflightPassed
$coverageComplete = $sourcePreflightGatePassed -and $batchConfigurationPassed -and $artifactRecordsComplete
$validationFailures = @($validationRecords | Where-Object {
    $_.batchStatus -eq "compatible" -and -not $_.validationPassed
}).Count
$planPath = Join-Path $outputPath "dhe-project-plan.json"
$plan = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-project-plan.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    # Plan completeness describes the generated/validated assembly records.
    # Settings-scope errors are carried separately in the preflight report so
    # the plan validator can still audit the records it was given.
    complete = $artifactRecordsComplete
    requireDheEqualsHotUpdate = $batchRequiresDheCoverage
    hotUpdateAssemblies = $hotUpdateAssemblyList.ToArray()
    dheAotAssemblies = $dheAssemblyList.ToArray()
    dheEqualsHotUpdate = if ($batchRequiresDheCoverage) {
        (@($hotUpdateAssemblyList.ToArray() | Sort-Object) -join ",") -eq (@($dheAssemblyList.ToArray() | Sort-Object) -join ",")
    } else { $null }
    settingsFile = $settingsPath
    baselineRoot = $baselinePath
    currentRoot = $currentPath
    batchReport = $batchReportPath
    assemblies = $planRecords.ToArray()
}
[IO.File]::WriteAllText($planPath, ($plan | ConvertTo-Json -Depth 14), (New-Object Text.UTF8Encoding($false)))
$planValidationPath = Join-Path $outputPath "project-plan-validation.json"
$planValidationArgs = @(
    "-Plan", $planPath,
    "-Output", $planValidationPath
)
# Pass switch parameters by presence, rather than `-Switch:$bool` text. The
# latter is accepted by PowerShell 7 but is parsed as a string by Windows
# PowerShell 5.1 when forwarded through a child process argument array.
if ($RequireCompleteCoverage) { $planValidationArgs += "-RequireCompleteCoverage" }
& $scriptHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/validate-dhe-project-plan.ps1") @planValidationArgs | Out-Null
$planValidationExitCode = $LASTEXITCODE
if (-not (Test-Path -LiteralPath $planValidationPath -PathType Leaf)) {
    throw "DHE project plan validator did not produce a report: $planValidationPath"
}
$planValidation = Get-Content -Raw -LiteralPath $planValidationPath | ConvertFrom-Json
$planValidationPassed = (Get-DheStrictBooleanProperty $planValidation "passed" "DHE project plan validation passed") -and
    $planValidationExitCode -eq 0
$validationPassed = ($validationFailures -eq 0) -and $planValidationPassed
$generationPassed = $sourcePreflightGatePassed -and $batchConfigurationPassed -and [int]$counts.error -eq 0 -and
    $batchExitCode -eq 0 -and $validationPassed
$passed = $generationPassed -and (-not $RequireCompleteCoverage -or $coverageComplete)
$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-project-preflight.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $passed
    generationPassed = $generationPassed
    validationPassed = $validationPassed
    coverageRequired = [bool]$RequireCompleteCoverage
    dheCoverageRequired = [bool]$RequireDheEqualsHotUpdate
    configurationPassed = $batchConfigurationPassed
    configurationErrors = $batchConfigurationErrors.ToArray()
    hotUpdateAssemblies = $hotUpdateAssemblyList.ToArray()
    dheAotAssemblies = $dheAssemblyList.ToArray()
    dheEqualsHotUpdate = if ($batchRequiresDheCoverage) {
        (@($batch.hotUpdateAssemblies | Sort-Object) -join ",") -eq (@($batch.dheAotAssemblies | Sort-Object) -join ",")
    } else { $null }
    coverageComplete = $coverageComplete
    # This stage has no native ABI or Player evidence. Keep the artifact-level
    # result separate so a consumer cannot mistake an offline pass for a
    # publishable build.
    artifactReady = $passed -and [bool]$RequireCompleteCoverage
    releaseReady = $false
    sourcePreflight = $sourcePreflightPath
    sourcePreflightPassed = $sourcePreflightPassed
    settingsFile = $settingsPath
    projectRoot = $projectRootPath
    baselineRoot = $baselinePath
    currentRoot = $currentPath
    batchReport = $batchReportPath
    projectPlan = $planPath
    projectPlanValidation = $planValidationPath
    batchExitCode = $batchExitCode
    counts = $counts
    assemblies = $validationRecords.ToArray()
    nativeAbiCoverage = "not-evaluated-by-offline-preflight"
    dnlibPath = $resolvedDnlibPath
}
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 14), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE project preflight: $reportPath"
Write-Host ("Assemblies: {0}/{1} compatible; coverageComplete={2}" -f $counts.compatible, $counts.total, $coverageComplete)

if ($RequireCompleteCoverage -and -not $passed) {
    throw "DHE project preflight failed complete coverage. See $reportPath"
}
if (-not $passed) {
    exit 1
}
exit 0
}
catch {
    $message = $_.Exception.Message
    try {
        if ($outputRootSafe -and [IO.Directory]::Exists($outputPath) -and
            -not (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
            $failure = [ordered]@{
                schemaVersion = 1
                format = "hybridclr.dhe-project-preflight-failure.json"
                generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
                passed = $false
                generationPassed = $false
                validationPassed = $false
                coverageRequired = [bool]$RequireCompleteCoverage
                dheCoverageRequired = [bool]$RequireDheEqualsHotUpdate
                sourcePreflight = $sourcePreflightPath
                sourcePreflightPassed = if ($null -eq $sourcePreflightPassed) { $false } else { [bool]$sourcePreflightPassed }
                settingsFile = $settingsPath
                projectRoot = $projectRootPath
                baselineRoot = $baselinePath
                currentRoot = $currentPath
                outputRoot = $outputPath
                dnlibPath = $resolvedDnlibPath
                error = $message
                errors = @($message)
            }
            [IO.File]::WriteAllText($reportPath, ($failure | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
        }
    } catch {
        Write-Warning "Unable to write DHE project preflight failure report: $($_.Exception.Message)"
    }
    Write-Error "DHE project preflight failed: $message"
    exit 1
}
