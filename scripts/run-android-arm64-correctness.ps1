param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [string]$DeviceSerial = "",
    [string]$Apk = "",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "none",
    [int]$PlayerTimeoutSeconds = 300,
    [switch]$SkipInstall,
    [switch]$SkipReference
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial
$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$profileSlug = $buildVariant.ToLowerInvariant()
$manifestPath = Join-Path $LabRoot "reports/$profileSlug-android-arm64-build-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Android build manifest was not found: $manifestPath" }
$build = Get-Content -Raw $manifestPath | ConvertFrom-Json
if ($build.target -ne "Android" -or $build.architecture -ne "arm64-v8a") {
    throw "Build manifest is not an Android arm64-v8a build: $manifestPath"
}
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") { throw "A no-metadata APK can only run with metadata mode none." }
if ($build.il2cppCodeGeneration -ne $Il2CppCodeGeneration -or $build.aotMetadataPackaging -ne $AotMetadataPackaging) {
    throw "Android build manifest does not match the requested correctness configuration."
}
$packageName = [string]$build.applicationIdentifier
if ([string]::IsNullOrWhiteSpace($Apk)) { $Apk = Join-Path $LabRoot ([string]$build.apk.path) }
elseif (-not [IO.Path]::IsPathRooted($Apk)) { $Apk = Join-Path $LabRoot $Apk }
$Apk = [IO.Path]::GetFullPath($Apk)
if (-not (Test-Path -LiteralPath $Apk)) { throw "Android APK was not found: $Apk" }
if ((Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash -ne $build.apk.sha256) {
    throw "APK hash does not match the Android build manifest."
}

if (-not $SkipReference) {
    & (Join-Path $PSScriptRoot "run-reference.ps1") -Output "reports/reference-result.json"
    if ($LASTEXITCODE -ne 0) { throw ".NET reference tests failed." }
}
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output "reports/$profileSlug-android-arm64-correctness-environment-before.json"

if (-not $SkipInstall) {
    Install-AndroidLabApk -Adb $tools.Adb -Serial $device.Serial -Apk $Apk -PackageName $packageName
}
$activity = Get-AndroidLabActivity -Adb $tools.Adb -Serial $device.Serial -PackageName $packageName
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "pm", "clear", $packageName) | Out-Null
$deviceResult = "/sdcard/Android/data/$packageName/files/hybridclr-lab-player-result.json"
Remove-AndroidLabDeviceFile -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult
$localResult = Join-Path $LabRoot "reports/$profileSlug-android-arm64-player-result.json"
$failureLog = Join-Path $LabRoot "reports/$profileSlug-android-arm64-correctness-failure.log"
if (Test-Path -LiteralPath $localResult) { Remove-Item -LiteralPath $localResult -Force }
Start-AndroidLabPlayer -Adb $tools.Adb -Serial $device.Serial -Component $activity -UnityArguments @(
    "-batchmode", "-nographics", "-labTarget", "Android", "-labAotMetadataMode", $AotMetadataMode)
try {
    Receive-AndroidLabResult -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult `
        -LocalPath $localResult -TimeoutSeconds $PlayerTimeoutSeconds -FailureLogPath $failureLog
}
finally {
    Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
}

$result = Get-Content -Raw $localResult | ConvertFrom-Json
if ($result.architecture -ne "arm64" -or [string]$result.platform -notmatch "Android") {
    throw "Player result is not from an Android ARM64 process: platform='$($result.platform)', architecture='$($result.architecture)'."
}
if ($result.managedAssemblySha256 -ne $build.managedAssemblySha256) {
    throw "Player managed assembly hash does not match the Android build manifest."
}
if ($result.aotMetadataMode -ne $AotMetadataMode -or
    $result.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
    $result.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
    $result.buildIdentity.aotMetadataPackaging -ne $AotMetadataPackaging -or
    $result.goldenContractSha256 -ne $build.testGoldenContractSha256) {
    throw "Android correctness result provenance does not match its build manifest."
}
if ($AotMetadataMode -eq "none" -and
    ([int]$result.aotMetadata.fileCount -ne 0 -or [int64]$result.aotMetadata.totalBytes -ne 0 -or
     [int64]$result.aotMetadata.loadNanoseconds -ne 0)) {
    throw "Android none correctness run loaded supplemental AOT metadata."
}
if ([int]$result.summary.failed -ne 0) {
    throw "Android ARM64 correctness suite failed: $($result.summary.failed) cases. See $localResult"
}
if ($result.correctnessProbes.crossAssemblyLazyVTable -ne $true -or
    $result.correctnessProbes.lazyMetadataConcurrentFirstTouch -ne $true) {
    throw "Android ARM64 lazy metadata correctness probes did not pass. See $localResult"
}
$differentialPath = "reports/$profileSlug-android-arm64-differential-result.json"
& (Join-Path $PSScriptRoot "compare-results.ps1") -LabRoot $LabRoot -Actual $localResult -Output $differentialPath
if ($LASTEXITCODE -ne 0) { throw "Android ARM64 differential comparison failed." }
& (Join-Path $PSScriptRoot "summarize-coverage.ps1") -LabRoot $LabRoot `
    -Manifest "manifests/test-manifest.json" `
    -Reference "reports/reference-result.json" `
    -Player "reports/$profileSlug-android-arm64-player-result.json" `
    -Output "reports/$profileSlug-android-arm64-coverage-summary.json"
if ($LASTEXITCODE -ne 0) { throw "Android ARM64 coverage summary failed." }
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output "reports/$profileSlug-android-arm64-correctness-environment-after.json"
Write-Host "Android ARM64 Player: $($result.summary.passed)/$($result.summary.total) passed"
Write-Host "Android ARM64 differential: 0 differences"
