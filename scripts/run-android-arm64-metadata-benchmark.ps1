param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Fgs-Candidate",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "exclude",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "none",
    [ValidateSet("none", "entry", "entry-method", "entry-graph", "entry-method-graph")]
    [string]$MetadataWarmup = "none",
    [string]$DeviceSerial = "",
    [string]$Apk = "",
    [int]$Processes = 0,
    [string]$Output = "",
    [int]$PlayerTimeoutSeconds = 300,
    [int]$InterProcessCooldownSeconds = 5,
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial
$policyPath = Join-Path $LabRoot "manifests/metadata-benchmark-policy.json"
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json
if ($Processes -le 0) { $Processes = [int]$policy.minimumIndependentProcesses }
if ($Processes -lt 1) { throw "Processes must be at least 1." }
if ($InterProcessCooldownSeconds -lt 0) { throw "InterProcessCooldownSeconds cannot be negative." }
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") {
    throw "A no-metadata APK can only run with metadata mode none."
}

$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$profileSlug = $buildVariant.ToLowerInvariant()
$manifestPath = Join-Path $LabRoot "reports/$profileSlug-android-arm64-build-manifest.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Android build manifest was not found: $manifestPath" }
$build = Get-Content -Raw $manifestPath | ConvertFrom-Json
if ($build.profile -ne $Profile -or $build.target -ne "Android" -or $build.architecture -ne "arm64-v8a" -or
    $build.il2cppCodeGeneration -ne $Il2CppCodeGeneration -or $build.aotMetadataPackaging -ne $AotMetadataPackaging) {
    throw "Android build manifest does not match the requested metadata benchmark configuration."
}

$packageName = [string]$build.applicationIdentifier
if ([string]::IsNullOrWhiteSpace($Apk)) { $Apk = Join-Path $LabRoot ([string]$build.apk.path) }
elseif (-not [IO.Path]::IsPathRooted($Apk)) { $Apk = Join-Path $LabRoot $Apk }
$Apk = [IO.Path]::GetFullPath($Apk)
if (-not (Test-Path -LiteralPath $Apk)) { throw "Android APK was not found: $Apk" }
if ((Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash -ne $build.apk.sha256) {
    throw "APK hash does not match its build manifest."
}

if (-not $SkipInstall) {
    Install-AndroidLabApk -Adb $tools.Adb -Serial $device.Serial -Apk $Apk -PackageName $packageName
}
$activity = Get-AndroidLabActivity -Adb $tools.Adb -Serial $device.Serial -PackageName $packageName
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "pm", "clear", $packageName) | Out-Null

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$warmupSuffix = if ($MetadataWarmup -eq "none") { "" } else { "-warmup-$MetadataWarmup" }
$rawDirectory = Join-Path $LabRoot "reports/raw/$profileSlug-android-arm64-metadata-$AotMetadataMode$warmupSuffix-$runId"
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory "environment-before.json")

$deviceResult = "/sdcard/Android/data/$packageName/files/hybridclr-lab-metadata-benchmark.json"
$results = @()
for ($index = 1; $index -le $Processes; $index++) {
    $resultPath = Join-Path $rawDirectory ("sample-{0:D3}.json" -f $index)
    $failureLog = Join-Path $rawDirectory ("sample-{0:D3}-failure.log" -f $index)
    Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
    Remove-AndroidLabDeviceFile -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult
    Start-AndroidLabPlayer -Adb $tools.Adb -Serial $device.Serial -Component $activity -UnityArguments @(
        "-batchmode", "-nographics",
        "-labTarget", "Android",
        "-labMode", "metadata",
        "-labAotMetadataMode", $AotMetadataMode,
        "-labMetadataWarmup", $MetadataWarmup,
        "-labSettleMilliseconds", [string]$policy.settleMilliseconds)
    try {
        Receive-AndroidLabResult -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult `
            -LocalPath $resultPath -TimeoutSeconds $PlayerTimeoutSeconds -FailureLogPath $failureLog
    }
    finally {
        Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
    }

    $sample = Get-Content -Raw $resultPath | ConvertFrom-Json
    $warmupProperty = $sample.PSObject.Properties["metadataWarmup"]
    $sampleWarmupMode = if ($null -eq $warmupProperty) { "none" } else { [string]$sample.metadataWarmup.mode }
    $sampleWarmupNanoseconds = if ($null -eq $warmupProperty) { 0L } else { [long]$sample.metadataWarmup.nanoseconds }
    $expectedSnapshotCount = if ($MetadataWarmup -eq "none") { 7 } else { 8 }
    if ($sample.metadataMode -ne $AotMetadataMode -or $sample.architecture -ne "arm64" -or
        $sampleWarmupMode -ne $MetadataWarmup -or
        $sampleWarmupNanoseconds -lt 0 -or
        [string]$sample.platform -notmatch "Android" -or $sample.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
        $sample.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
        $sample.buildIdentity.aotMetadataPackaging -ne $AotMetadataPackaging -or
        $sample.stressAssembly.sha256 -ne $build.metadataStressAssemblySha256 -or $sample.snapshots.Count -ne $expectedSnapshotCount) {
        throw "Android metadata benchmark produced an invalid or mismatched result: $resultPath"
    }
    if (@($sample.snapshots | Where-Object { [int64]$_.androidPssBytes -le 0 }).Count -ne 0) {
        throw "Android PSS was unavailable in one or more metadata snapshots: $resultPath"
    }
    if ($AotMetadataMode -eq "none") {
        if ([int]$sample.aotMetadata.fileCount -ne 0 -or [int64]$sample.aotMetadata.totalBytes -ne 0 -or
            [int64]$sample.durationsNanoseconds.aotMetadataLoad -ne 0) {
            throw "Android none metadata benchmark loaded supplemental AOT metadata: $resultPath"
        }
    }
    elseif ([int64]$sample.aotMetadata.totalBytes -ne [int64]$build.apk.supplementalAotMetadataBytes -or
            [int]$sample.aotMetadata.fileCount -lt 1) {
        throw "Android supplemental metadata payload does not match the APK manifest: $resultPath"
    }

    $results += $resultPath
    Write-Host "[android-arm64-metadata] $buildVariant / $AotMetadataMode / $index of $Processes"
    if ($InterProcessCooldownSeconds -gt 0) { Start-Sleep -Seconds $InterProcessCooldownSeconds }
}

& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory "environment-after.json")
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = "reports/$profileSlug-android-arm64-metadata-$AotMetadataMode$warmupSuffix-summary.json"
}
& (Join-Path $PSScriptRoot "summarize-metadata-benchmark.ps1") `
    -LabRoot $LabRoot `
    -InputPath $results `
    -BuildManifest $manifestPath `
    -Output $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Android ARM64 metadata benchmark summary: $Output"
