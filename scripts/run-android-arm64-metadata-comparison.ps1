param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DeviceSerial = "",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$BaselineProfile = "Baseline-Clean",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$CandidateProfile = "Candidate",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "supplemental",
    [ValidateSet("none", "entry", "entry-method", "entry-graph", "entry-method-graph")]
    [string]$MetadataWarmup = "none",
    [ValidateSet("entry-first", "reflection-first")]
    [string]$MetadataScenario = "reflection-first",
    [ValidateSet("exhaustive", "selective")]
    [string]$ReflectionProfile = "exhaustive",
    [int]$ReflectionTypeLimit = 0,
    [int]$Pairs = 0,
    [int]$PlayerTimeoutSeconds = 300,
    [int]$InterSampleCooldownSeconds = 10,
    [string]$BaselineOutput = "",
    [string]$CandidateOutput = "",
    [string]$ComparisonOutput = "",
    [string]$PairedOutput = "",
    [switch]$SkipPairEnvironmentCapture,
    [switch]$ValidateOnly,
    [switch]$DiagnosticOnly
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$policy = Get-Content -Raw -LiteralPath (Join-Path $LabRoot "manifests/metadata-benchmark-policy.json") | ConvertFrom-Json
if ($Pairs -le 0) { $Pairs = [int]$policy.minimumIndependentProcesses }
if ($Pairs -lt [int]$policy.minimumIndependentProcesses) {
    throw "Pairs must be at least $($policy.minimumIndependentProcesses)."
}
if ($InterSampleCooldownSeconds -lt 0) {
    throw "InterSampleCooldownSeconds cannot be negative."
}
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") {
    throw "A no-metadata APK can only run with metadata mode none."
}
if ($ReflectionProfile -eq "selective") {
    if ($ReflectionTypeLimit -lt 1 -or $ReflectionTypeLimit -gt [int]$policy.stressAssembly.typeCount) {
        throw "ReflectionTypeLimit must be between 1 and $($policy.stressAssembly.typeCount) for selective reflection."
    }
}
elseif ($ReflectionTypeLimit -ne 0) {
    throw "ReflectionTypeLimit must be zero for exhaustive reflection."
}

function Get-BuildVariant([string]$Profile) {
    $codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
    if ($AotMetadataPackaging -eq "exclude") { return "$codeGenerationVariant-NoMetadata" }
    return $codeGenerationVariant
}

function Read-ProfileBuild([string]$Profile) {
    $variant = Get-BuildVariant $Profile
    $slug = $variant.ToLowerInvariant()
    $manifestRelative = "reports/$slug-android-arm64-build-manifest.json"
    $manifestPath = Join-Path $LabRoot $manifestRelative
    if (-not (Test-Path -LiteralPath $manifestPath)) {
        throw "Android build manifest was not found: $manifestPath"
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    if ($manifest.profile -ne $Profile -or $manifest.target -ne "Android" -or
        $manifest.architecture -ne "arm64-v8a" -or
        $manifest.il2cppCodeGeneration -ne $Il2CppCodeGeneration -or
        $manifest.aotMetadataPackaging -ne $AotMetadataPackaging) {
        throw "$Profile manifest does not match the requested Android metadata configuration."
    }
    $apk = [IO.Path]::GetFullPath((Join-Path $LabRoot ([string]$manifest.apk.path)))
    if (-not (Test-Path -LiteralPath $apk)) {
        throw "$Profile APK was not found: $apk"
    }
    if ((Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash -ne [string]$manifest.apk.sha256) {
        throw "$Profile APK hash does not match its build manifest."
    }
    if ($AotMetadataMode -eq "supplemental" -and
        [int64]$manifest.apk.supplementalAotMetadataBytes -le 0) {
        throw "$Profile manifest has no supplemental AOT metadata payload."
    }
    return [pscustomobject]@{
        Profile = $Profile
        Variant = $variant
        Slug = $slug
        Manifest = $manifest
        ManifestPath = $manifestPath
        ManifestRelative = $manifestRelative
        Apk = $apk
    }
}

$baseline = Read-ProfileBuild $BaselineProfile
$candidate = Read-ProfileBuild $CandidateProfile
if ($baseline.Profile -eq $candidate.Profile) {
    throw "BaselineProfile and CandidateProfile must be different."
}
if ($baseline.Manifest.applicationIdentifier -ne $candidate.Manifest.applicationIdentifier -or
    $baseline.Manifest.managedAssemblySha256 -ne $candidate.Manifest.managedAssemblySha256 -or
    $baseline.Manifest.metadataStressAssemblySha256 -ne $candidate.Manifest.metadataStressAssemblySha256 -or
    $baseline.Manifest.metadataBenchmarkPolicySha256 -ne $candidate.Manifest.metadataBenchmarkPolicySha256 -or
    $baseline.Manifest.aotMetadataPackaging -ne $candidate.Manifest.aotMetadataPackaging) {
    throw "Baseline and Candidate APKs do not share the same package and metadata workload contract."
}

$reflectionSlug = if ($ReflectionProfile -eq "selective") { "selective-$ReflectionTypeLimit" } else { "exhaustive" }
$warmupSuffix = if ($MetadataWarmup -eq "none") { "" } else { "-warmup-$MetadataWarmup" }
$outputStem = "android-arm64-metadata-$AotMetadataMode-$MetadataScenario-$reflectionSlug$warmupSuffix"
if ([string]::IsNullOrWhiteSpace($BaselineOutput)) {
    $BaselineOutput = "reports/$($baseline.Slug)-$outputStem-summary.json"
}
if ([string]::IsNullOrWhiteSpace($CandidateOutput)) {
    $CandidateOutput = "reports/$($candidate.Slug)-$outputStem-summary.json"
}
if ([string]::IsNullOrWhiteSpace($ComparisonOutput)) {
    $ComparisonOutput = "reports/$($candidate.Slug)-vs-$($baseline.Slug)-$outputStem-comparison.json"
}
if ([string]::IsNullOrWhiteSpace($PairedOutput)) {
    $PairedOutput = "reports/$($candidate.Slug)-vs-$($baseline.Slug)-$outputStem-paired.json"
}

if ($ValidateOnly) {
    Write-Host "Android ARM64 metadata comparison inputs are valid."
    [pscustomobject]@{
        Baseline = $baseline.Profile
        BaselineApkSha256 = $baseline.Manifest.apk.sha256
        Candidate = $candidate.Profile
        CandidateApkSha256 = $candidate.Manifest.apk.sha256
        MetadataBytes = $candidate.Manifest.apk.supplementalAotMetadataBytes
        Pairs = $Pairs
        MetadataScenario = $MetadataScenario
        MetadataWarmup = $MetadataWarmup
        ReflectionProfile = $ReflectionProfile
    }
    return
}

. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial
$packageName = [string]$baseline.Manifest.applicationIdentifier
$deviceResult = "/sdcard/Android/data/$packageName/files/hybridclr-lab-metadata-benchmark.json"
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$rawRoot = Join-Path $LabRoot "reports/raw/android-arm64-metadata-comparison-$AotMetadataMode-$MetadataScenario-$reflectionSlug$warmupSuffix-$runId"
$baselineRaw = Join-Path $rawRoot "baseline"
$candidateRaw = Join-Path $rawRoot "candidate"
New-Item -ItemType Directory -Force -Path $baselineRaw, $candidateRaw | Out-Null
$baselineResults = @()
$candidateResults = @()
$runOrder = @()

function Assert-MetadataSample([object]$Sample, [object]$Build, [string]$ResultPath) {
    $warmupProperty = $Sample.PSObject.Properties["metadataWarmup"]
    $sampleWarmupMode = if ($null -eq $warmupProperty) { "none" } else { [string]$Sample.metadataWarmup.mode }
    $sampleWarmupNanoseconds = if ($null -eq $warmupProperty) { 0L } else { [long]$Sample.metadataWarmup.nanoseconds }
    $expectedSnapshotCount = if ($MetadataWarmup -eq "none") { 7 } else { 8 }
    if ($Sample.schemaVersion -ne 1 -or $Sample.suiteId -ne "hybridclr-metadata-load-v2" -or
        $Sample.metadataMode -ne $AotMetadataMode -or
        $Sample.metadataScenario -ne $MetadataScenario -or
        $sampleWarmupMode -ne $MetadataWarmup -or
        $sampleWarmupNanoseconds -lt 0 -or
        $Sample.reflectionContract.profile -ne $ReflectionProfile -or
        [int]$Sample.reflectionContract.requestedTypeCount -ne $ReflectionTypeLimit -or
        $Sample.architecture -ne "arm64" -or [string]$Sample.platform -notmatch "Android" -or
        $Sample.buildIdentity.sha256 -ne $Build.Manifest.buildIdentitySha256 -or
        $Sample.buildIdentity.stagedRuntimeSha256 -ne $Build.Manifest.stagedRuntimeSha256 -or
        $Sample.buildIdentity.aotMetadataPackaging -ne $AotMetadataPackaging -or
        $Sample.stressAssembly.sha256 -ne $Build.Manifest.metadataStressAssemblySha256 -or
        $Sample.snapshots.Count -ne $expectedSnapshotCount) {
        throw "$($Build.Profile) produced an invalid metadata result: $ResultPath"
    }
    if ($ReflectionProfile -eq "selective" -and
        [int]$Sample.touchCounts.types -ne $ReflectionTypeLimit) {
        throw "$($Build.Profile) did not honor the selective reflection type limit."
    }
    if (@($Sample.snapshots | Where-Object { [int64]$_.androidPssBytes -le 0 }).Count -ne 0) {
        throw "$($Build.Profile) has an unavailable Android PSS snapshot: $ResultPath"
    }
    if ($AotMetadataMode -eq "none") {
        if ([int]$Sample.aotMetadata.fileCount -ne 0 -or
            [int64]$Sample.aotMetadata.totalBytes -ne 0 -or
            [int64]$Sample.durationsNanoseconds.aotMetadataLoad -ne 0) {
            throw "$($Build.Profile) loaded supplemental AOT metadata in none mode."
        }
    }
    elseif ([int]$Sample.aotMetadata.fileCount -lt 1 -or
        [int64]$Sample.aotMetadata.totalBytes -ne [int64]$Build.Manifest.apk.supplementalAotMetadataBytes) {
        throw "$($Build.Profile) supplemental metadata payload does not match its APK manifest."
    }
}

function Invoke-ProfileSample([object]$Build, [int]$PairIndex) {
    Install-AndroidLabApk -Adb $tools.Adb -Serial $device.Serial -Apk $Build.Apk -PackageName $packageName
    $activity = Get-AndroidLabActivity -Adb $tools.Adb -Serial $device.Serial -PackageName $packageName
    Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "pm", "clear", $packageName) | Out-Null
    Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
    Remove-AndroidLabDeviceFile -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult

    $sideDirectory = if ($Build.Profile -eq $baseline.Profile) { $baselineRaw } else { $candidateRaw }
    $resultPath = Join-Path $sideDirectory ("sample-{0:D3}.json" -f $PairIndex)
    $failureLog = [IO.Path]::ChangeExtension($resultPath, ".failure.log")
    $arguments = @(
        "-batchmode", "-nographics",
        "-labTarget", "Android",
        "-labMode", "metadata",
        "-labAotMetadataMode", $AotMetadataMode,
        "-labMetadataWarmup", $MetadataWarmup,
        "-labMetadataScenario", $MetadataScenario,
        "-labReflectionProfile", $ReflectionProfile,
        "-labSettleMilliseconds", [string]$policy.settleMilliseconds
    )
    if ($ReflectionProfile -eq "selective") {
        $arguments += @("-labReflectionTypeLimit", [string]$ReflectionTypeLimit)
    }
    Start-AndroidLabPlayer -Adb $tools.Adb -Serial $device.Serial -Component $activity -UnityArguments $arguments
    try {
        Receive-AndroidLabResult -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult `
            -LocalPath $resultPath -TimeoutSeconds $PlayerTimeoutSeconds -FailureLogPath $failureLog
    }
    finally {
        Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
    }
    $sample = Get-Content -Raw -LiteralPath $resultPath | ConvertFrom-Json
    Assert-MetadataSample -Sample $sample -Build $Build -ResultPath $resultPath
    return $resultPath
}

& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawRoot "environment-before.json")
for ($pairIndex = 1; $pairIndex -le $Pairs; ++$pairIndex) {
    $order = if (($pairIndex % 2) -eq 1) { @($baseline, $candidate) } else { @($candidate, $baseline) }
    $runOrder += [ordered]@{ pair = $pairIndex; profiles = @($order | ForEach-Object Profile) }
    foreach ($build in $order) {
        $resultPath = Invoke-ProfileSample -Build $build -PairIndex $pairIndex
        if ($build.Profile -eq $baseline.Profile) { $baselineResults += $resultPath } else { $candidateResults += $resultPath }
        Write-Host "[android-arm64-metadata-comparison] pair $pairIndex/${Pairs}: $($build.Profile)"
        if ($InterSampleCooldownSeconds -gt 0) { Start-Sleep -Seconds $InterSampleCooldownSeconds }
    }
    if (-not $SkipPairEnvironmentCapture) {
        & (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawRoot ("environment-pair-{0:D3}.json" -f $pairIndex))
    }
}
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawRoot "environment-after.json")

$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot "summarize-metadata-benchmark.ps1") -LabRoot $LabRoot `
    -InputPath $baselineResults -BuildManifest $baseline.ManifestRelative -Output $BaselineOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot "summarize-metadata-benchmark.ps1") -LabRoot $LabRoot `
    -InputPath $candidateResults -BuildManifest $candidate.ManifestRelative -Output $CandidateOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot "compare-metadata-benchmarks.ps1") -LabRoot $LabRoot `
    -Baseline $BaselineOutput -Candidate $CandidateOutput `
    -BaselineBuildManifest $baseline.ManifestRelative -CandidateBuildManifest $candidate.ManifestRelative `
    -Output $ComparisonOutput -DiagnosticOnly
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$global:LASTEXITCODE = 0
& (Join-Path $PSScriptRoot "summarize-metadata-pairs.ps1") -LabRoot $LabRoot `
    -RawDirectory $rawRoot -Output $PairedOutput
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$comparisonPath = if ([IO.Path]::IsPathRooted($ComparisonOutput)) { $ComparisonOutput } else { Join-Path $LabRoot $ComparisonOutput }
$comparison = Get-Content -Raw -LiteralPath $comparisonPath | ConvertFrom-Json
$runManifest = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    deviceSerial = $device.Serial
    pairs = $Pairs
    runOrder = $runOrder
    metadataWarmup = $MetadataWarmup
    rawDirectory = [IO.Path]::GetRelativePath($LabRoot, $rawRoot).Replace("\", "/")
    baselineBuildManifest = $baseline.ManifestRelative
    candidateBuildManifest = $candidate.ManifestRelative
    comparison = $ComparisonOutput
    paired = $PairedOutput
}
$runManifestPath = Join-Path $rawRoot "run-manifest.json"
$runManifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $runManifestPath -Encoding UTF8
Write-Host "Android ARM64 metadata comparison: $comparisonPath"
Write-Host "Hard gate passed: $($comparison.summary.hardGatePassed)"
if (-not $DiagnosticOnly -and -not $comparison.summary.hardGatePassed) { exit 2 }
