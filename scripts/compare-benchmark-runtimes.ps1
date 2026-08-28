param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$HybridClrSteady = "reports/baseline-clean-player-hybridclr-steady-benchmark.json",
    [string]$AotSteady = "reports/baseline-clean-player-aot-steady-benchmark.json",
    [string]$HybridClrCold = "reports/baseline-clean-player-hybridclr-cold-benchmark.json",
    [string]$AotCold = "reports/baseline-clean-player-aot-cold-benchmark.json",
    [string]$Output = "reports/baseline-clean-hybridclr-vs-aot.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
function Read-Report([string]$Path) {
    $resolved = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $LabRoot $Path }
    if (-not (Test-Path $resolved)) { throw "Benchmark report was not found: $resolved" }
    return Get-Content -Raw $resolved | ConvertFrom-Json
}
function Get-GeometricMean([double[]]$Values) {
    if ($Values.Count -eq 0) { return 0.0 }
    return [Math]::Exp(($Values | ForEach-Object { [Math]::Log($_) } | Measure-Object -Average).Average)
}
function Compare-Mode($HybridClr, $Aot) {
    if ($HybridClr.suiteId -ne $Aot.suiteId -or
        $HybridClr.benchmarkMode -ne $Aot.benchmarkMode -or
        $HybridClr.policySha256 -ne $Aot.policySha256 -or
        $HybridClr.buildManifestSha256 -ne $Aot.buildManifestSha256) {
        throw ("HybridCLR and AOT reports do not share the same suite, mode, policy, and build manifest: " +
            "suite={0}/{1}, mode={2}/{3}, policy={4}/{5}, build={6}/{7}" -f
            $HybridClr.suiteId, $Aot.suiteId,
            $HybridClr.benchmarkMode, $Aot.benchmarkMode,
            $HybridClr.policySha256, $Aot.policySha256,
            $HybridClr.buildManifestSha256, $Aot.buildManifestSha256)
    }
    $noiseThreshold = [Math]::Max(
        [double]$HybridClr.calibration.recommendedNoiseThresholdPercent,
        [double]$Aot.calibration.recommendedNoiseThresholdPercent)
    $rows = @()
    foreach ($hybridWorkload in $HybridClr.workloads) {
        $aotWorkload = @($Aot.workloads | Where-Object { $_.id -eq $hybridWorkload.id })
        if ($aotWorkload.Count -ne 1) { throw "AOT result is missing workload '$($hybridWorkload.id)'." }
        $aotWorkload = $aotWorkload[0]
        if ($hybridWorkload.iterations -ne $aotWorkload.iterations -or
            $hybridWorkload.checksum -ne $aotWorkload.checksum -or
            (@($hybridWorkload.features) -join "`n") -ne (@($aotWorkload.features) -join "`n")) {
            throw "Workload contract mismatch for '$($hybridWorkload.id)'."
        }
        $hybridMedian = [double]$hybridWorkload.aggregate.median
        $aotMedian = [double]$aotWorkload.aggregate.median
        $speedup = $hybridMedian / $aotMedian
        $differencePercent = ($hybridMedian - $aotMedian) * 100.0 / $aotMedian
        $verdict = if ($differencePercent -gt $noiseThreshold) {
            "aot-faster"
        } elseif ($differencePercent -lt -$noiseThreshold) {
            "hybridclr-faster"
        } else {
            "within-noise"
        }
        $aotRepetitions = @($aotWorkload.processSamples | ForEach-Object { $_.repetitions } | Sort-Object -Unique)
        $rows += [ordered]@{
            id = $hybridWorkload.id
            category = $hybridWorkload.category
            features = @($hybridWorkload.features)
            iterations = [int]$hybridWorkload.iterations
            checksum = $hybridWorkload.checksum
            hybridClrMedianNanosecondsPerIteration = $hybridMedian
            aotMedianNanosecondsPerIteration = $aotMedian
            aotSpeedup = $speedup
            differencePercent = $differencePercent
            noiseThresholdPercent = $noiseThreshold
            verdict = $verdict
            hybridClrProcessCount = [int]$hybridWorkload.processCount
            aotProcessCount = [int]$aotWorkload.processCount
            aotRepetitions = $aotRepetitions
        }
    }
    $coreRows = @($rows | Where-Object { $_.id -notin @("interp_boxing", "interp_boxing_escape", "interp_boxing_mixed", "interp_string_allocation", "interp_exception") })
    return [ordered]@{
        mode = $HybridClr.benchmarkMode
        noiseThresholdPercent = $noiseThreshold
        workloadCount = $rows.Count
        aotFasterCount = @($rows | Where-Object { $_.verdict -eq "aot-faster" }).Count
        hybridClrFasterCount = @($rows | Where-Object { $_.verdict -eq "hybridclr-faster" }).Count
        withinNoiseCount = @($rows | Where-Object { $_.verdict -eq "within-noise" }).Count
        allWorkloadsGeometricMeanAotSpeedup = Get-GeometricMean ([double[]]@($rows | ForEach-Object { $_.aotSpeedup }))
        interpreterAndBoundaryCoreGeometricMeanAotSpeedup = Get-GeometricMean ([double[]]@($coreRows | ForEach-Object { $_.aotSpeedup }))
        workloads = $rows
    }
}

$hybridSteady = Read-Report $HybridClrSteady
$aotSteadyResult = Read-Report $AotSteady
$hybridCold = Read-Report $HybridClrCold
$aotColdResult = Read-Report $AotCold
$comparison = [ordered]@{
    schemaVersion = 1
    suiteId = "managed-performance-v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    buildManifestSha256 = $hybridSteady.buildManifestSha256
    policySha256 = $hybridSteady.policySha256
    steady = Compare-Mode $hybridSteady $aotSteadyResult
    cold = Compare-Mode $hybridCold $aotColdResult
    interpretation = [ordered]@{
        primaryMetric = "steady median nanoseconds per logical iteration"
        coldMetric = "first-use batch normalized by workload-specific cold iteration count"
        exclusionsFromCoreGeometricMean = @("interp_boxing", "interp_boxing_escape", "interp_boxing_mixed", "interp_string_allocation", "interp_exception")
        note = "AOT steady repetitions only extend the timed window; reported ns/iteration is normalized and checksum-stable."
    }
}
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $LabRoot $Output }
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$comparison | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Runtime comparison: $outputPath"
Write-Host ("Steady core geometric mean AOT speedup: {0:N2}x" -f $comparison.steady.interpreterAndBoundaryCoreGeometricMeanAotSpeedup)
