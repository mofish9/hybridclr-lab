param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [string]$DeviceSerial = "",
    [switch]$BuildOnly
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$sourceRoot = Join-Path $LabRoot "native-unit-tests"
$runtimeRoot = Join-Path $LabRoot "staging/runtime/$Profile/libil2cpp"
$fgsProfiles = @("Fgs-Diagnostic", "Fgs-Candidate")
$fgsTests = $fgsProfiles -contains $Profile
$externalRoot = Join-Path $LabRoot "staging/runtime/$Profile/external"
$buildRoot = Join-Path $LabRoot "artifacts/native-tests-android-arm64/$Profile"
$cmake = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin\cmake.exe"
$ninja = "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\Common7\IDE\CommonExtensions\Microsoft\CMake\Ninja\ninja.exe"
$toolchain = Join-Path $tools.Ndk "build/cmake/android.toolchain.cmake"
$baselibRoot = Join-Path (Split-Path $tools.Editor) "Data/il2cpp/external/baselib"
$baselibInclude = Join-Path $baselibRoot "Include"
$baselibPlatformInclude = Join-Path $baselibRoot "Platforms/Android/Include"
if (-not (Test-Path -LiteralPath $cmake)) { throw "CMake was not found: $cmake" }
if (-not (Test-Path -LiteralPath $ninja)) { throw "Ninja was not found: $ninja" }
if (-not (Test-Path -LiteralPath $toolchain)) { throw "Android NDK CMake toolchain was not found: $toolchain" }
if (-not (Test-Path -LiteralPath (Join-Path $runtimeRoot "hybridclr/metadata/Opcodes.cpp"))) {
    throw "Runtime sources were not assembled for profile '$Profile': $runtimeRoot"
}

New-Item -ItemType Directory -Force -Path $buildRoot | Out-Null
$fgsValue = if ($fgsTests) { 1 } else { 0 }
& $cmake -S $sourceRoot -B $buildRoot -G Ninja `
    "-DCMAKE_MAKE_PROGRAM=$ninja" `
    "-DCMAKE_TOOLCHAIN_FILE=$toolchain" `
    "-DANDROID_ABI=arm64-v8a" `
    "-DANDROID_PLATFORM=android-23" `
    "-DANDROID_STL=c++_static" `
    "-DCMAKE_BUILD_TYPE=Release" `
    "-DHYBRIDCLR_RUNTIME_ROOT=$runtimeRoot" `
    "-DHYBRIDCLR_TEST_FULL_GENERIC_SHARING=$fgsValue" `
    "-DIL2CPP_EXTERNAL=$externalRoot" `
    "-DIL2CPP_BASELIB_INCLUDE=$baselibInclude" `
    "-DIL2CPP_BASELIB_PLATFORM_INCLUDE=$baselibPlatformInclude"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $cmake --build $buildRoot --parallel
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$executable = Join-Path $buildRoot "hybridclr_native_tests"
if (-not (Test-Path -LiteralPath $executable)) { throw "ARM64 native test executable was not produced: $executable" }
Write-Host "Android ARM64 native tests built: $executable"
if ($BuildOnly) { return }

$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial
$profileSlug = $Profile.ToLowerInvariant()
$remoteDirectory = "/data/local/tmp/hybridclr-lab-native-$profileSlug"
$remoteExecutable = "$remoteDirectory/hybridclr_native_tests"
if (-not $remoteDirectory.StartsWith("/data/local/tmp/hybridclr-lab-native-", [StringComparison]::Ordinal)) {
    throw "Invalid remote native-test directory: $remoteDirectory"
}
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "rm", "-rf", $remoteDirectory) | Out-Null
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "mkdir", "-p", $remoteDirectory) | Out-Null
$pushOutput = @(& $tools.Adb -s $device.Serial push $executable $remoteExecutable 2>&1)
if ($LASTEXITCODE -ne 0) { throw "Unable to push ARM64 native tests.`n$($pushOutput -join [Environment]::NewLine)" }
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "chmod", "700", $remoteExecutable) | Out-Null
$execution = Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", $remoteExecutable) -AllowFailure
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "rm", "-rf", $remoteDirectory) | Out-Null

$reportPath = Join-Path $LabRoot "reports/$profileSlug-android-arm64-native-tests.json"
$report = [ordered]@{
    schemaVersion = 1
    profile = $Profile
    executedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    deviceSerial = $device.Serial
    primaryAbi = $device.Abi
    executableSha256 = (Get-FileHash -LiteralPath $executable -Algorithm SHA256).Hash
    exitCode = $execution.ExitCode
    passed = $execution.ExitCode -eq 0
    output = @($execution.Output | ForEach-Object { [string]$_ })
}
$report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
if ($execution.ExitCode -ne 0) {
    throw "ARM64 native tests failed with exit code $($execution.ExitCode). See $reportPath"
}
Write-Host "Android ARM64 native tests passed: $reportPath"
