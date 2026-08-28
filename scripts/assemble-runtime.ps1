param(
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Metadata-Tuanjie2022", "Metadata-Instrumented", "Metadata-Unity2021", "Metadata-Unity2022", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic", "Compatibility-Tuanjie2022-Fgs", "Compatibility-Unity2022-Fgs", "Compatibility-Unity2021-Standard")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs", "Unity2021Standard")]
    [string]$EngineWorkflow = "Tuanjie2022Fgs",
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OutputRoot = "staging/runtime",
    [string]$HybridClrSource = "",
    [string]$Il2CppPlusSource = "",
    [switch]$AllowDirty,
    [switch]$AllowSurrogateExternalHeaders
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")

function Invoke-Git([string]$RepoPath, [string[]]$Arguments) {
    $output = & git -C $RepoPath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$RepoPath': $($output -join [Environment]::NewLine)"
    }
    return $output
}

function Get-MeaningfulGitStatus([string[]]$StatusLines) {
    return @($StatusLines | Where-Object {
        $_ -notmatch [regex]::Escape("Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta")
    })
}

function Copy-DirectoryContents([string]$Source, [string]$Destination) {
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$lockPath = Join-Path $LabRoot "manifests/repo-lock.json"
$lock = Get-Content -Raw $lockPath | ConvertFrom-Json
$workflowPath = Join-Path $LabRoot "manifests/runtime-workflows.json"
$workflowManifest = Get-Content -Raw $workflowPath | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found in $workflowPath" }
$workflow = $workflow[0]
$reposRoot = [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos"))
$outputRootPath = [IO.Path]::GetFullPath((Join-Path $LabRoot $OutputRoot))
$stagingPath = Join-Path $outputRootPath $Profile
$stagedLibil2cpp = Join-Path $stagingPath "libil2cpp"
$stagedExternal = Join-Path $stagingPath "external"

$noCheckout = $Profile -ne "Baseline-Clean"
$hybridClrSourceExplicit = -not [string]::IsNullOrWhiteSpace($HybridClrSource)
$il2CppPlusSourceExplicit = -not [string]::IsNullOrWhiteSpace($Il2CppPlusSource)
$bootstrapNoCheckout = $noCheckout -or
    $hybridClrSourceExplicit -or
    $il2CppPlusSourceExplicit
$bootstrapParameters = @{
    LabRoot = $LabRoot
    AllowDirty = [bool]$AllowDirty
    NoCheckout = $bootstrapNoCheckout
}
$skipBootstrapDirtyCheck = @()
if ($hybridClrSourceExplicit) { $skipBootstrapDirtyCheck += "hybridclr" }
if ($il2CppPlusSourceExplicit) { $skipBootstrapDirtyCheck += "il2cpp_plus" }
if ($skipBootstrapDirtyCheck.Count -gt 0) {
    $bootstrapParameters.SkipDirtyCheckFor = $skipBootstrapDirtyCheck
}
& (Join-Path $PSScriptRoot "bootstrap-repos.ps1") @bootstrapParameters

$hybridclrPath = if (-not $hybridClrSourceExplicit) {
    Join-Path $reposRoot "hybridclr"
} else {
    [IO.Path]::GetFullPath($HybridClrSource)
}
$il2cppPath = if (-not $il2CppPlusSourceExplicit) {
    Join-Path $reposRoot "il2cpp_plus"
} else {
    [IO.Path]::GetFullPath($Il2CppPlusSource)
}
$hybridclrSpec = $lock.repositories.hybridclr
$il2cppSpec = $lock.repositories.il2cpp_plus

foreach ($item in @(
    [pscustomobject]@{ Name = "hybridclr"; Path = $hybridclrPath; Expected = $hybridclrSpec.commit; SourceExplicit = $hybridClrSourceExplicit },
    [pscustomobject]@{ Name = "il2cpp_plus"; Path = $il2cppPath; Expected = $il2cppSpec.commit; SourceExplicit = $il2CppPlusSourceExplicit }
)) {
    $name = $item.Name
    $path = $item.Path
    $expected = $item.Expected
    $sourceExplicit = $item.SourceExplicit
    $actual = (Invoke-Git $path @("rev-parse", "HEAD")).Trim()
    if (-not $noCheckout -and -not $sourceExplicit -and $actual -ne $expected) { throw "$name is at $actual, expected $expected" }
    $dirty = (Get-MeaningfulGitStatus @(Invoke-Git $path @("status", "--porcelain"))).Count -gt 0
    if ($dirty -and -not $AllowDirty) { throw "$name is dirty; pass -AllowDirty only for an explicit local profile." }
}

if (Test-Path $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stagingPath | Out-Null
Copy-DirectoryContents (Join-Path $il2cppPath "libil2cpp") $stagedLibil2cpp
$editorExecutable = [IO.Path]::GetFullPath($workflow.engine.executablePath)
$editorAvailable = Test-Path $editorExecutable
$editorExternal = if ($editorAvailable) {
    Join-Path (Split-Path -Parent $editorExecutable) "Data/il2cpp/external"
} elseif ($null -ne $workflow.engine.PSObject.Properties["nativeTestExternalPath"]) {
    [IO.Path]::GetFullPath([string]$workflow.engine.nativeTestExternalPath)
} else {
    ""
}
$externalHeadersAreSurrogate = -not $editorAvailable
if ([string]::IsNullOrWhiteSpace($editorExternal) -or -not (Test-Path $editorExternal)) {
    throw "Engine il2cpp external headers were not found for '$EngineWorkflow': $editorExternal"
}
if ($externalHeadersAreSurrogate -and -not $AllowSurrogateExternalHeaders) {
    throw "Engine editor for '$EngineWorkflow' is unavailable. Refusing surrogate external headers from '$editorExternal'; pass -AllowSurrogateExternalHeaders only for an explicit non-merge-ready native test."
}
if ($externalHeadersAreSurrogate) {
    Write-Warning "Using surrogate native-test external headers for $EngineWorkflow from '$editorExternal'. This is not Player evidence."
}
Copy-DirectoryContents $editorExternal $stagedExternal
$stagedHybridclr = Join-Path $stagedLibil2cpp "hybridclr"
if (Test-Path $stagedHybridclr) { Remove-Item -LiteralPath $stagedHybridclr -Recurse -Force }
Copy-DirectoryContents (Join-Path $hybridclrPath "hybridclr") $stagedHybridclr

$fgsDiagnosticsProfiles = @(
    "Fgs-Diagnostic",
    "Fgs-Candidate",
    "Unity2022-Fgs-Diagnostic",
    "Compatibility-Tuanjie2022-Fgs",
    "Compatibility-Unity2022-Fgs"
)
$fgsDiagnosticsEnabled = $fgsDiagnosticsProfiles -contains $Profile
if ($Profile -in @("Baseline-Instrumented", "Metadata-Instrumented") -or $fgsDiagnosticsEnabled) {
    $instrumentationConfig = Join-Path $stagedHybridclr "lab/InstrumentationConfig.h"
    New-Item -ItemType Directory -Force -Path (Split-Path $instrumentationConfig) | Out-Null
    $configLines = @("#pragma once")
    if ($Profile -in @("Baseline-Instrumented", "Metadata-Instrumented")) {
        $configLines += "#define HYBRIDCLR_LAB_INSTRUMENTED 1"
    }
    if ($fgsDiagnosticsEnabled) {
        $configLines += "#define HYBRIDCLR_LAB_FGS_TESTS 1"
    }
    $configLines | Set-Content -LiteralPath $instrumentationConfig -Encoding ASCII
}

$runtimeHash = Get-TreeHash $stagedLibil2cpp
$manifest = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    engineWorkflow = $EngineWorkflow
    engine = $workflow.engine
    fullGenericSharingDiagnostics = $fgsDiagnosticsEnabled
    externalHeaders = [ordered]@{
        sourcePath = $editorExternal
        surrogate = $externalHeadersAreSurrogate
        editorAvailable = $editorAvailable
        explicitlyAllowed = $externalHeadersAreSurrogate -and [bool]$AllowSurrogateExternalHeaders
    }
    source = [ordered]@{
        hybridclr = [ordered]@{ url = $hybridclrSpec.fork; path = $hybridclrPath; commit = (Invoke-Git $hybridclrPath @("rev-parse", "HEAD")).Trim(); dirty = ((Get-MeaningfulGitStatus @(Invoke-Git $hybridclrPath @("status", "--porcelain"))).Count -gt 0); treeSha256 = (Get-TreeHash (Join-Path $hybridclrPath "hybridclr")) }
        il2cpp_plus = [ordered]@{ url = $il2cppSpec.fork; path = $il2cppPath; commit = (Invoke-Git $il2cppPath @("rev-parse", "HEAD")).Trim(); dirty = ((Get-MeaningfulGitStatus @(Invoke-Git $il2cppPath @("status", "--porcelain"))).Count -gt 0); treeSha256 = (Get-TreeHash (Join-Path $il2cppPath "libil2cpp")) }
    }
    stagedLibil2cpp = $stagedLibil2cpp
    stagedRuntimeSha256 = $runtimeHash
}
$manifestPath = Join-Path $stagingPath "runtime-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Assembled $Profile runtime: $stagedLibil2cpp"
Write-Host "Runtime SHA-256: $runtimeHash"
Write-Host "Manifest: $manifestPath"
