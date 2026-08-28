param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$DeviceSerial = "",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$BaselineProfile = "Baseline-Clean",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$CandidateProfile = "Candidate",
    [int]$Pairs = 0,
    [int]$PlayerTimeoutSeconds = 300,
    [int]$InterSampleCooldownSeconds = 10,
    [switch]$SkipPairEnvironmentCapture
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial
$policy = Get-Content -Raw (Join-Path $LabRoot "manifests/benchmark-policy.json") | ConvertFrom-Json
$referenceProject = Join-Path $LabRoot "runners/benchmark-reference/HybridCLR.BenchmarkReference.csproj"
$referenceRunner = Join-Path $LabRoot "runners/benchmark-reference/bin/Release/net6.0/HybridCLR.BenchmarkReference.dll"
dotnet build $referenceProject --configuration Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$expectedWorkloadIds = @(dotnet $referenceRunner --list | Sort-Object)
if ($LASTEXITCODE -ne 0 -or $expectedWorkloadIds.Count -lt 1) { throw "Unable to enumerate benchmark workloads." }
if ($Pairs -le 0) { $Pairs = [int]$policy.minimumIndependentProcesses }
if ($Pairs -lt 1) { throw "Pairs must be at least 1." }
if ($InterSampleCooldownSeconds -lt 0) { throw "InterSampleCooldownSeconds cannot be negative." }

function Read-ProfileBuild([string]$Profile) {
    $slug = $Profile.ToLowerInvariant()
    $manifestPath = Join-Path $LabRoot "reports/$slug-android-arm64-build-manifest.json"
    if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Android build manifest was not found: $manifestPath" }
    $manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
    $apk = [IO.Path]::GetFullPath((Join-Path $LabRoot ([string]$manifest.apk.path)))
    if ($manifest.target -ne "Android" -or $manifest.architecture -ne "arm64-v8a" -or -not (Test-Path -LiteralPath $apk)) {
        throw "$Profile is not a complete Android arm64-v8a build."
    }
    if ((Get-FileHash -LiteralPath $apk -Algorithm SHA256).Hash -ne $manifest.apk.sha256) {
        throw "$Profile APK hash does not match its build manifest."
    }
    return [PSCustomObject]@{
        Profile = $Profile
        Slug = $slug
        Manifest = $manifest
        ManifestPath = $manifestPath
        Apk = $apk
    }
}

$baseline = Read-ProfileBuild $BaselineProfile
$candidate = Read-ProfileBuild $CandidateProfile
if ($baseline.Manifest.applicationIdentifier -ne $candidate.Manifest.applicationIdentifier) {
    throw "Paired APKs must use the same application identifier so replacement installation has identical package state."
}
if ($baseline.Manifest.managedAssemblySha256 -ne $candidate.Manifest.managedAssemblySha256 -or
    $baseline.Manifest.benchmarkPolicySha256 -ne $candidate.Manifest.benchmarkPolicySha256) {
    throw "Paired APKs do not share the same managed workload and benchmark policy."
}

$packageName = [string]$baseline.Manifest.applicationIdentifier
$deviceResult = "/sdcard/Android/data/$packageName/files/hybridclr-lab-player-benchmark.json"
$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$rawDirectory = Join-Path $LabRoot "reports/raw/android-arm64-paired-$runId"
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
$baselineResults = @()
$candidateResults = @()
$runOrder = @()
$script:pairedInstalledApkSha256 = ""

function Invoke-ProfileSample($Build, [int]$PairIndex, [int]$Position) {
    if ($script:pairedInstalledApkSha256 -ne [string]$Build.Manifest.apk.sha256) {
        Install-AndroidLabApk -Adb $tools.Adb -Serial $device.Serial -Apk $Build.Apk -PackageName $packageName
        $script:pairedInstalledApkSha256 = [string]$Build.Manifest.apk.sha256
    }
    $activity = Get-AndroidLabActivity -Adb $tools.Adb -Serial $device.Serial -PackageName $packageName
    Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
    Remove-AndroidLabDeviceFile -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult
    $localResult = Join-Path $rawDirectory ("pair-{0:D2}-{1}-{2}.json" -f $PairIndex, $Position, $Build.Slug)
    Start-AndroidLabPlayer -Adb $tools.Adb -Serial $device.Serial -Component $activity -UnityArguments @(
        "-batchmode", "-nographics",
        "-labMode", "benchmark",
        "-labBenchmarkRuntime", "hybridclr",
        "-labBenchmarkMode", "steady",
        "-labWarmupBatches", [string]$policy.warmupBatches,
        "-labMeasurementBatches", [string]$policy.measurementBatches,
        "-labBenchmarkRepeat", "1"
    )
    $failureLog = [IO.Path]::ChangeExtension($localResult, ".failure.log")
    try {
        Receive-AndroidLabResult -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult `
            -LocalPath $localResult -TimeoutSeconds $PlayerTimeoutSeconds -FailureLogPath $failureLog
    }
    finally {
        Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
    }

    $result = Get-Content -Raw $localResult | ConvertFrom-Json
    $actualWorkloadIds = @($result.workloads | ForEach-Object { [string]$_.id } | Sort-Object)
    $workloadDifferences = @(Compare-Object -ReferenceObject $expectedWorkloadIds -DifferenceObject $actualWorkloadIds)
    if ($result.architecture -ne "arm64" -or [string]$result.platform -notmatch "Android" -or
        $result.benchmarkMode -ne "steady" -or $result.managedAssemblySha256 -ne $Build.Manifest.managedAssemblySha256 -or
        $result.policySha256 -ne $Build.Manifest.benchmarkPolicySha256 -or $workloadDifferences.Count -ne 0) {
        throw "$($Build.Profile) produced an invalid paired result: $localResult"
    }
    return $localResult
}

& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory "environment-before.json")
for ($pairIndex = 1; $pairIndex -le $Pairs; $pairIndex++) {
    $order = if (($pairIndex % 2) -eq 1) { @($baseline, $candidate) } else { @($candidate, $baseline) }
    $runOrder += [ordered]@{ pair = $pairIndex; profiles = @($order | ForEach-Object { $_.Profile }) }
    for ($position = 0; $position -lt $order.Count; $position++) {
        $build = $order[$position]
        $path = Invoke-ProfileSample -Build $build -PairIndex $pairIndex -Position ($position + 1)
        if ($build.Profile -eq $baseline.Profile) { $baselineResults += $path } else { $candidateResults += $path }
        Write-Host ("[android-arm64-paired] pair {0}/{1}, position {2}: {3}" -f $pairIndex, $Pairs, ($position + 1), $build.Profile)
        if ($InterSampleCooldownSeconds -gt 0) { Start-Sleep -Seconds $InterSampleCooldownSeconds }
    }
    if (-not $SkipPairEnvironmentCapture) {
        & (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory ("environment-pair-{0:D2}.json" -f $pairIndex))
    }
}
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory "environment-after.json")

$baselineSummaryPath = Join-Path $LabRoot "reports/$($baseline.Slug)-android-arm64-player-hybridclr-steady-paired-benchmark.json"
$candidateSummaryPath = Join-Path $LabRoot "reports/$($candidate.Slug)-android-arm64-player-hybridclr-steady-paired-benchmark.json"
& (Join-Path $PSScriptRoot "summarize-benchmark.ps1") -LabRoot $LabRoot -InputPath $baselineResults -Output $baselineSummaryPath -BuildManifest $baseline.ManifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& (Join-Path $PSScriptRoot "summarize-benchmark.ps1") -LabRoot $LabRoot -InputPath $candidateResults -Output $candidateSummaryPath -BuildManifest $candidate.ManifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$baselineSummary = Get-Content -Raw $baselineSummaryPath | ConvertFrom-Json
$candidateSummary = Get-Content -Raw $candidateSummaryPath | ConvertFrom-Json
$noiseThreshold = [Math]::Max(
    [double]$baselineSummary.calibration.recommendedNoiseThresholdPercent,
    [double]$candidateSummary.calibration.recommendedNoiseThresholdPercent)
$rows = @()
foreach ($baselineWorkload in $baselineSummary.workloads) {
    $candidateWorkload = @($candidateSummary.workloads | Where-Object { $_.id -eq $baselineWorkload.id })
    if ($candidateWorkload.Count -ne 1 -or $candidateWorkload[0].checksum -ne $baselineWorkload.checksum -or
        $candidateWorkload[0].iterations -ne $baselineWorkload.iterations) {
        throw "Paired workload contract mismatch: $($baselineWorkload.id)"
    }
    $baselineMedian = [double]$baselineWorkload.aggregate.median
    $candidateMedian = [double]$candidateWorkload[0].aggregate.median
    $deltaPercent = ($candidateMedian - $baselineMedian) * 100.0 / $baselineMedian
    $rows += [ordered]@{
        id = $baselineWorkload.id
        category = $baselineWorkload.category
        baselineMedianNanosecondsPerIteration = $baselineMedian
        candidateMedianNanosecondsPerIteration = $candidateMedian
        candidateVsBaselineSpeedup = $baselineMedian / $candidateMedian
        candidateVsBaselineDeltaPercent = $deltaPercent
        verdict = if ($deltaPercent -le -$noiseThreshold) { "candidate-faster" } elseif ($deltaPercent -ge $noiseThreshold) { "candidate-regression" } else { "within-noise" }
    }
}
function Get-GeometricMean([object[]]$Items) {
    if ($Items.Count -eq 0) { return 0.0 }
    return [Math]::Exp((@($Items | ForEach-Object { [Math]::Log([double]$_) }) | Measure-Object -Average).Average)
}
$coreRows = @($rows | Where-Object { $_.id -notin @("interp_boxing", "interp_boxing_escape", "interp_boxing_mixed", "interp_string_allocation", "interp_exception") })
$comparison = [ordered]@{
    schemaVersion = 1
    comparison = "android-arm64-paired-$($baseline.Slug)-vs-$($candidate.Slug)"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    deviceSerial = $device.Serial
    pairs = $Pairs
    runOrder = $runOrder
    rawDirectory = [IO.Path]::GetRelativePath($LabRoot, $rawDirectory).Replace('\', '/')
    baselineBuildManifestSha256 = (Get-FileHash -LiteralPath $baseline.ManifestPath -Algorithm SHA256).Hash
    candidateBuildManifestSha256 = (Get-FileHash -LiteralPath $candidate.ManifestPath -Algorithm SHA256).Hash
    noiseThresholdPercent = $noiseThreshold
    allWorkloadsGeometricMeanSpeedup = Get-GeometricMean @($rows | ForEach-Object { $_.candidateVsBaselineSpeedup })
    interpreterAndBoundaryCoreGeometricMeanSpeedup = Get-GeometricMean @($coreRows | ForEach-Object { $_.candidateVsBaselineSpeedup })
    candidateFasterCount = @($rows | Where-Object { $_.verdict -eq "candidate-faster" }).Count
    candidateRegressionCount = @($rows | Where-Object { $_.verdict -eq "candidate-regression" }).Count
    withinNoiseCount = @($rows | Where-Object { $_.verdict -eq "within-noise" }).Count
    workloads = $rows
}
$comparisonFileName = if ($BaselineProfile -eq "Baseline-Clean" -and $CandidateProfile -eq "Candidate") {
    "candidate-android-arm64-paired-comparison.json"
} else {
    "$($candidate.Slug)-vs-$($baseline.Slug)-android-arm64-paired-comparison.json"
}
$comparisonPath = Join-Path $LabRoot "reports/$comparisonFileName"
$comparison | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $comparisonPath -Encoding UTF8
Write-Host ("Android ARM64 paired core speedup: {0:N3}x" -f $comparison.interpreterAndBoundaryCoreGeometricMeanSpeedup)
Write-Host "Android ARM64 paired comparison: $comparisonPath"
