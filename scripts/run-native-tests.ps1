param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Metadata-Tuanjie2022", "Metadata-Instrumented", "Metadata-Unity2021", "Metadata-Unity2022", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic", "Compatibility-Tuanjie2022-Fgs", "Compatibility-Unity2022-Fgs", "Compatibility-Unity2021-Standard")]
    [string]$Profile = "Baseline-Clean",
    [string]$Configuration = "Release",
    [string]$Generator = "Visual Studio 17 2022"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$buildRoot = Join-Path $LabRoot "artifacts/native-tests/$Profile"
$sourceRoot = Join-Path $LabRoot "native-unit-tests"
$runtimeRoot = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$externalRoot = Join-Path $LabRoot "staging/runtime/$Profile/external"
$runtimeManifestPath = Join-Path $LabRoot "staging/runtime/$Profile/runtime-manifest.json"
$cmake = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$vcvars = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvars64.bat"
if (-not (Test-Path $cmake)) { throw "CMake was not found: $cmake" }
if (-not (Test-Path $vcvars)) { throw "MSVC environment was not found: $vcvars" }
if (-not (Test-Path (Join-Path $runtimeRoot "hybridclr/metadata/Opcodes.cpp"))) {
    throw "Runtime sources were not assembled for profile '$Profile': $runtimeRoot"
}
if (-not (Test-Path $runtimeManifestPath)) { throw "Runtime manifest was not found: $runtimeManifestPath" }
$runtimeManifest = Get-Content -Raw $runtimeManifestPath | ConvertFrom-Json
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
$configure = "call `"$vcvars`" && `"$cmake`" -S `"$sourceRoot`" -B `"$buildRoot`" -G `"$Generator`" -A x64 -DHYBRIDCLR_RUNTIME_ROOT=`"$runtimeRoot`" -DIL2CPP_EXTERNAL=`"$externalRoot`" -DIL2CPP_BASELIB_INCLUDE=`"$baselibInclude`" -DIL2CPP_BASELIB_PLATFORM_INCLUDE=`"$baselibPlatformInclude`" -DHYBRIDCLR_TEST_UNITY_VERSION=$($engine.unityVersionNumber) -DHYBRIDCLR_TEST_TUANJIE_VERSION=$($engine.tuanjieVersionNumber) -DHYBRIDCLR_TEST_FULL_GENERIC_SHARING=$fgsValue"
cmd.exe /d /s /c $configure
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$build = "call `"$vcvars`" && `"$cmake`" --build `"$buildRoot`" --config $Configuration --parallel"
cmd.exe /d /s /c $build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$test = "call `"$vcvars`" && `"$cmake`" --build `"$buildRoot`" --config $Configuration --target RUN_TESTS"
cmd.exe /d /s /c $test
exit $LASTEXITCODE
