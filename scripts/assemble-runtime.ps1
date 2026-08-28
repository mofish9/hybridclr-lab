param(
    [ValidateSet("Baseline-Clean", "DHE-Tuanjie2022", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Metadata-Tuanjie2022", "Metadata-Instrumented", "Metadata-Unity2021", "Metadata-Unity2022", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic", "Compatibility-Tuanjie2022-Fgs", "Compatibility-Unity2022-Fgs", "Compatibility-Unity2021-Standard")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs", "Unity2021Standard")]
    [string]$EngineWorkflow = "Tuanjie2022Fgs",
    [string]$LabRoot = "",
    [string]$OutputRoot = "staging/runtime",
    [string]$HybridClrSource = "",
    [string]$Il2CppPlusSource = "",
    [string]$PackageRoot = "",
    [string]$ReposRoot = "",
    [switch]$AllowDirty,
    [switch]$AllowSurrogateExternalHeaders
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
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

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$lockPath = Join-Path $LabRoot "manifests/repo-lock.json"
$lock = Get-Content -Raw $lockPath | ConvertFrom-Json
$workflowPath = Join-Path $LabRoot "manifests/runtime-workflows.json"
$workflowManifest = Get-Content -Raw $workflowPath | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found in $workflowPath" }
$workflow = $workflow[0]
$null = . (Join-Path $PSScriptRoot "resolve-repos-root.ps1")
$reposRoot = Resolve-LabReposRoot -LabRoot $LabRoot -Lock $lock -RequestedRoot $ReposRoot
$outputRootPath = if ([IO.Path]::IsPathRooted($OutputRoot)) {
    [IO.Path]::GetFullPath($OutputRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $OutputRoot))
}
$null = Assert-DheSafeOutputRoot -Path $outputRootPath
$null = Assert-DheOutputNotAncestor -Path $outputRootPath -Root $LabRoot
$stagingPath = Join-Path $outputRootPath $Profile
$stagedLibil2cpp = Join-Path $stagingPath "libil2cpp"
$stagedExternal = Join-Path $stagingPath "external"

$dheEnabled = $Profile -eq "DHE-Tuanjie2022"
if ($dheEnabled -and $EngineWorkflow -ne "Tuanjie2022Fgs") {
    throw "Profile '$Profile' currently supports only the Tuanjie2022Fgs engine workflow; got '$EngineWorkflow'."
}
if ($dheEnabled -and $AllowDirty) {
    throw "Profile '$Profile' is a publishable DHE runtime and cannot be assembled with -AllowDirty. Use a diagnostic/non-DHE profile for dirty source experiments."
}
$noCheckout = $Profile -notin @("Baseline-Clean", "DHE-Tuanjie2022")
$bootstrapNoCheckout = $noCheckout -or
    -not [string]::IsNullOrWhiteSpace($HybridClrSource) -or
    -not [string]::IsNullOrWhiteSpace($Il2CppPlusSource)
$bootstrapParameters = @{
    LabRoot = $LabRoot
    ReposRoot = $reposRoot
    AllowDirty = [bool]$AllowDirty
    NoCheckout = $bootstrapNoCheckout
}
$skipBootstrapDirtyCheck = @()
if (-not [string]::IsNullOrWhiteSpace($HybridClrSource)) { $skipBootstrapDirtyCheck += "hybridclr" }
if (-not [string]::IsNullOrWhiteSpace($Il2CppPlusSource)) { $skipBootstrapDirtyCheck += "il2cpp_plus" }
if ($skipBootstrapDirtyCheck.Count -gt 0) {
    $bootstrapParameters.SkipDirtyCheckFor = $skipBootstrapDirtyCheck
}
& (Join-Path $PSScriptRoot "bootstrap-repos.ps1") @bootstrapParameters

$hybridclrPath = if ([string]::IsNullOrWhiteSpace($HybridClrSource)) {
    Join-Path $reposRoot "hybridclr"
} else {
    [IO.Path]::GetFullPath($HybridClrSource)
}
$il2cppPath = if ([string]::IsNullOrWhiteSpace($Il2CppPlusSource)) {
    Join-Path $reposRoot "il2cpp_plus"
} else {
    [IO.Path]::GetFullPath($Il2CppPlusSource)
}
$hybridclrSpec = $lock.repositories.hybridclr
$il2cppSpec = $lock.repositories.il2cpp_plus

foreach ($item in @(@("hybridclr", $hybridclrPath, $hybridclrSpec.commit), @("il2cpp_plus", $il2cppPath, $il2cppSpec.commit))) {
    $name = $item[0]
    $path = $item[1]
    $expected = $item[2]
    $actual = (Invoke-Git $path @("rev-parse", "HEAD")).Trim()
    if (-not $noCheckout -and $actual -ne $expected) { throw "$name is at $actual, expected $expected" }
    $dirty = @(Get-MeaningfulGitStatus @(Invoke-Git $path @("status", "--porcelain"))).Count -gt 0
    if ($dirty -and -not $AllowDirty) { throw "$name is dirty; pass -AllowDirty only for an explicit local profile." }
}

if (Test-Path $stagingPath) {
    # The default staging directory is intentionally inside LabRoot. Protect
    # the lab root from being selected as the staging directory itself or as
    # its ancestor, while allowing the normal descendant path.
    Assert-DheSafeOutputRoot -Path $stagingPath
    Assert-DheOutputNotAncestor -Path $stagingPath -Root $LabRoot
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

if ($dheEnabled) {
    if ([string]::IsNullOrWhiteSpace($PackageRoot)) {
        throw "DHE runtime assembly requires -PackageRoot so the locked HybridCLR package patches can be applied and verified. Native-only DHE staging is not a publishable runtime; use an explicit non-DHE profile for native-only tests."
    }
    $patchArguments = @{
        NativeRoot = $stagedLibil2cpp
        HybridClrSource = $hybridclrPath
        Il2CppPlusSource = $il2cppPath
        LabRoot = $LabRoot
    }
    $patchArguments.PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
    $patchArguments.HybridClrUnitySource = Join-Path $reposRoot "hybridclr_unity"
    & (Join-Path $PSScriptRoot "apply-dhe-runtime-patches.ps1") @patchArguments
    if ($LASTEXITCODE -ne 0) { throw "DHE runtime patch application failed." }
}

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
$externalHeadersHash = Get-TreeHash $stagedExternal
$dheRuntimeLock = Get-Content -Raw (Join-Path $LabRoot "manifests/dhe-runtime-lock.json") | ConvertFrom-Json
$dheRuntimeLockHash = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/dhe-runtime-lock.json") -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    dheEnabled = $dheEnabled
    # Paths in a freshly assembled workspace are intentionally absolute so
    # the runtime preflight can bind the manifest to the exact local inputs.
    # Archive creation rewrites these provenance fields to archive semantics.
    pathSemantics = "workspace-absolute-v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    engineWorkflow = $EngineWorkflow
    engine = $workflow.engine
    fullGenericSharingDiagnostics = $fgsDiagnosticsEnabled
    externalHeaders = [ordered]@{
        sourcePath = $editorExternal
        stagedTreeSha256 = $externalHeadersHash
        surrogate = $externalHeadersAreSurrogate
        editorAvailable = $editorAvailable
        explicitlyAllowed = $externalHeadersAreSurrogate -and [bool]$AllowSurrogateExternalHeaders
    }
    source = [ordered]@{
        hybridclr = [ordered]@{ url = $hybridclrSpec.fork; path = $hybridclrPath; commit = (Invoke-Git $hybridclrPath @("rev-parse", "HEAD")).Trim(); dirty = (@(Get-MeaningfulGitStatus @(Invoke-Git $hybridclrPath @("status", "--porcelain"))).Count -gt 0); treeSha256 = (Get-TreeHash (Join-Path $hybridclrPath "hybridclr")) }
        il2cpp_plus = [ordered]@{ url = $il2cppSpec.fork; path = $il2cppPath; commit = (Invoke-Git $il2cppPath @("rev-parse", "HEAD")).Trim(); dirty = (@(Get-MeaningfulGitStatus @(Invoke-Git $il2cppPath @("status", "--porcelain"))).Count -gt 0); treeSha256 = (Get-TreeHash (Join-Path $il2cppPath "libil2cpp")) }
        hybridclr_unity = [ordered]@{ url = $lock.repositories.hybridclr_unity.fork; path = (Join-Path $reposRoot "hybridclr_unity"); commit = (Invoke-Git (Join-Path $reposRoot "hybridclr_unity") @("rev-parse", "HEAD")).Trim(); dirty = (@(Get-MeaningfulGitStatus @(Invoke-Git (Join-Path $reposRoot "hybridclr_unity") @("status", "--porcelain"))).Count -gt 0) }
    }
    stagedLibil2cpp = $stagedLibil2cpp
    stagedRuntimeSha256 = $runtimeHash
    dheRuntimeLock = Join-Path $LabRoot "manifests/dhe-runtime-lock.json"
    dheRuntimeLockSha256 = $dheRuntimeLockHash
    dhePatches = @($dheRuntimeLock.patches | ForEach-Object {
        [ordered]@{
            id = [string]$_.id
            repository = [string]$_.repository
            baseCommit = [string]$_.baseCommit
            path = [string]$_.path
            sha256 = [string]$_.sha256
            applyRoot = [string]$_.applyRoot
            stripComponents = [int]$_.stripComponents
        }
    })
}
$manifestPath = Join-Path $stagingPath "runtime-manifest.json"
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
Write-Host "Assembled $Profile runtime: $stagedLibil2cpp"
Write-Host "Runtime SHA-256: $runtimeHash"
Write-Host "Manifest: $manifestPath"
