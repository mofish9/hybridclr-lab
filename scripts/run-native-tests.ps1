param(
    [string]$LabRoot = "",
    [ValidateSet("Baseline-Clean", "DHE-Tuanjie2022", "DHE-Unity2022", "DHE-Unity2021", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Metadata-Tuanjie2022", "Metadata-Instrumented", "Metadata-Unity2021", "Metadata-Unity2022", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic", "Compatibility-Tuanjie2022-Fgs", "Compatibility-Unity2022-Fgs", "Compatibility-Unity2021-Standard")]
    [string]$Profile = "Baseline-Clean",
    [string]$Configuration = "Release",
    [string]$Generator = "Visual Studio 17 2022",
    [switch]$AllowSurrogateExternalHeaders
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$buildRoot = Join-Path $LabRoot "artifacts/native-tests/$Profile"
$sourceRoot = Join-Path $LabRoot "native-unit-tests"
$runtimeRoot = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$externalRoot = Join-Path $LabRoot "staging/runtime/$Profile/external"
$runtimeManifestPath = Join-Path $LabRoot "staging/runtime/$Profile/runtime-manifest.json"

function Resolve-FirstExistingPath([string[]]$Candidates) {
    foreach ($candidate in @($Candidates | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        $resolved = [IO.Path]::GetFullPath($candidate)
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            return $resolved
        }
    }
    return $null
}

$cmakeCommand = Get-Command cmake -ErrorAction SilentlyContinue
$cmakeCandidates = New-Object System.Collections.Generic.List[string]
if ($null -ne $cmakeCommand -and -not [string]::IsNullOrWhiteSpace([string]$cmakeCommand.Source)) {
    $cmakeCandidates.Add([string]$cmakeCommand.Source)
}
$cmakeCandidates.Add((Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"))
$cmakeCandidates.Add((Join-Path ${env:ProgramFiles} "Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe"))

$vswhereCandidates = @(
    (Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/Installer/vswhere.exe"),
    (Join-Path ${env:ProgramFiles} "Microsoft Visual Studio/Installer/vswhere.exe")
)
$vswhere = Resolve-FirstExistingPath $vswhereCandidates
$vsInstallation = $null
if ($null -ne $vswhere) {
    $vsInstallation = (& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null |
        Select-Object -First 1)
    if (-not [string]::IsNullOrWhiteSpace([string]$vsInstallation)) {
        $vsInstallation = ([string]$vsInstallation).Trim()
    }
}
$vcvarsCandidates = New-Object System.Collections.Generic.List[string]
if (-not [string]::IsNullOrWhiteSpace([string]$vsInstallation)) {
    $vcvarsCandidates.Add((Join-Path $vsInstallation "VC/Auxiliary/Build/vcvars64.bat"))
}
$vcvarsCandidates.Add((Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio/2022/BuildTools/VC/Auxiliary/Build/vcvars64.bat"))
$vcvarsCandidates.Add((Join-Path ${env:ProgramFiles} "Microsoft Visual Studio/2022/BuildTools/VC/Auxiliary/Build/vcvars64.bat"))

$cmake = Resolve-FirstExistingPath $cmakeCandidates.ToArray()
$vcvars = Resolve-FirstExistingPath $vcvarsCandidates.ToArray()
if ([string]::IsNullOrWhiteSpace($cmake)) {
    throw "CMake was not found. Install CMake or the Visual Studio CMake component."
}
if ([string]::IsNullOrWhiteSpace($vcvars)) {
    throw "MSVC environment was not found. Install the Visual Studio C++ workload."
}
if (-not (Test-Path (Join-Path $runtimeRoot "hybridclr/metadata/Opcodes.cpp"))) {
    throw "Runtime sources were not assembled for profile '$Profile': $runtimeRoot"
}
$dheRuntimeCpp = Join-Path $runtimeRoot "hybridclr/DheRuntime.cpp"
$dheRuntimeHeader = Join-Path $runtimeRoot "hybridclr/DheRuntime.h"
$dheExpected = $Profile -in @("DHE-Tuanjie2022", "DHE-Unity2022", "DHE-Unity2021")
if ($dheExpected -and (-not (Test-Path -LiteralPath $dheRuntimeCpp -PathType Leaf) -or
        -not (Test-Path -LiteralPath $dheRuntimeHeader -PathType Leaf))) {
    throw "DHE profile '$Profile' requires both hybridclr/DheRuntime.cpp and hybridclr/DheRuntime.h: $runtimeRoot"
}
if (-not (Test-Path $runtimeManifestPath)) { throw "Runtime manifest was not found: $runtimeManifestPath" }
$runtimeManifest = Get-Content -Raw $runtimeManifestPath | ConvertFrom-Json
$dheEnabledProperty = $runtimeManifest.PSObject.Properties["dheEnabled"]
if ($null -eq $dheEnabledProperty -or $dheEnabledProperty.Value -isnot [bool]) {
    throw "Runtime manifest dheEnabled must be a JSON boolean."
}
$dheActual = [bool]$dheEnabledProperty.Value
if ($dheActual -ne $dheExpected) {
    throw "Runtime manifest DHE mode does not match profile '$Profile'. Expected dheEnabled=$dheExpected, got $dheActual."
}
$externalHeadersSurrogate = $false
if ($dheExpected -and ($null -eq $runtimeManifest.PSObject.Properties["externalHeaders"] -or
    $null -eq $runtimeManifest.externalHeaders -or
    $null -eq $runtimeManifest.externalHeaders.PSObject.Properties["surrogate"] -or
    $runtimeManifest.externalHeaders.surrogate -isnot [bool])) {
    throw "DHE runtime manifest is missing a strict externalHeaders.surrogate provenance boolean."
}
if ($null -ne $runtimeManifest.PSObject.Properties["externalHeaders"] -and
    $null -ne $runtimeManifest.externalHeaders -and
    $null -ne $runtimeManifest.externalHeaders.PSObject.Properties["surrogate"]) {
    $externalHeadersSurrogate = [bool]$runtimeManifest.externalHeaders.surrogate
}
if ($dheExpected -and $externalHeadersSurrogate -and -not $AllowSurrogateExternalHeaders) {
    throw "DHE native tests refuse surrogate external headers by default. Pass -AllowSurrogateExternalHeaders only for exploratory native validation."
}
if ($dheExpected -and ($null -eq $runtimeManifest.PSObject.Properties["stagedRuntimeSha256"] -or
    (Get-TreeHash $runtimeRoot) -ne [string]$runtimeManifest.stagedRuntimeSha256)) {
    throw "Runtime tree does not match its manifest: $runtimeRoot"
}
$externalHeadersObject = if ($null -ne $runtimeManifest.PSObject.Properties["externalHeaders"]) {
    $runtimeManifest.externalHeaders
} else {
    $null
}
$externalHashProperty = if ($null -eq $externalHeadersObject) {
    $null
} else {
    $externalHeadersObject.PSObject.Properties["stagedTreeSha256"]
}
if ($dheExpected -and ($null -eq $externalHashProperty -or
    [string]$externalHashProperty.Value -notmatch '^[0-9a-fA-F]{64}$')) {
    throw "DHE runtime manifest is missing a valid externalHeaders.stagedTreeSha256 value."
}
$externalTreeRoot = Join-Path ([IO.Path]::GetDirectoryName($runtimeRoot)) "external"
if ($dheExpected -and (-not (Test-Path -LiteralPath $externalTreeRoot -PathType Container) -or
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
        (Get-TreeHash $externalTreeRoot), [string]$externalHashProperty.Value))) {
    throw "Staged external headers do not match runtime manifest: $externalTreeRoot"
}
$runtimeLockPath = Join-Path $LabRoot "manifests/dhe-runtime-lock.json"
if ($dheExpected -and $null -eq $runtimeManifest.PSObject.Properties["dheRuntimeLockSha256"]) {
    throw "DHE runtime manifest is missing dheRuntimeLockSha256 provenance."
}
if ($dheExpected -and $null -ne $runtimeManifest.PSObject.Properties["dheRuntimeLockSha256"] -and
    -not [StringComparer]::OrdinalIgnoreCase.Equals(
        [string]$runtimeManifest.dheRuntimeLockSha256,
        (Get-FileHash -LiteralPath $runtimeLockPath -Algorithm SHA256).Hash)) {
    throw "Runtime manifest was produced from a different DHE runtime lock."
}
$fgsTests = [bool]$runtimeManifest.fullGenericSharingDiagnostics
$engine = $runtimeManifest.engine
if ($null -eq $engine) {
    $workflowManifest = Get-Content -Raw (Join-Path $LabRoot "manifests/runtime-workflows.json") | ConvertFrom-Json
    $engine = @($workflowManifest.workflows | Where-Object id -eq "Tuanjie2022Fgs")[0].engine
}
$baselibRoot = Join-Path $externalRoot "baselib"
$baselibInclude = Join-Path $baselibRoot "Include"
$baselibPlatformInclude = Join-Path $baselibRoot "Platforms/Windows/Include"
if (-not (Test-Path $baselibInclude) -or -not (Test-Path $baselibPlatformInclude)) {
    throw "Baselib headers were not found for $($engine.family) $($engine.unityVersion)."
}

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
$fgsValue = if ($fgsTests) { 1 } else { 0 }
$dheExpectedValue = if ($dheExpected) { 1 } else { 0 }
$configure = "call `"$vcvars`" && `"$cmake`" -S `"$sourceRoot`" -B `"$buildRoot`" -G `"$Generator`" -A x64 -DHYBRIDCLR_RUNTIME_ROOT=`"$runtimeRoot`" -DIL2CPP_EXTERNAL=`"$externalRoot`" -DIL2CPP_BASELIB_INCLUDE=`"$baselibInclude`" -DIL2CPP_BASELIB_PLATFORM_INCLUDE=`"$baselibPlatformInclude`" -DHYBRIDCLR_TEST_UNITY_VERSION=$($engine.unityVersionNumber) -DHYBRIDCLR_TEST_TUANJIE_VERSION=$($engine.tuanjieVersionNumber) -DHYBRIDCLR_TEST_FULL_GENERIC_SHARING=$fgsValue -DHYBRIDCLR_TEST_EXPECT_DHE=$dheExpectedValue"
cmd.exe /d /s /c $configure
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$build = "call `"$vcvars`" && `"$cmake`" --build `"$buildRoot`" --config $Configuration --parallel"
cmd.exe /d /s /c $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$test = "call `"$vcvars`" && `"$cmake`" --build `"$buildRoot`" --config $Configuration --target RUN_TESTS"
cmd.exe /d /s /c $test
exit $LASTEXITCODE
