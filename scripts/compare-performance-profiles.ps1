param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BaselineSteady = "reports/baseline-clean-player-hybridclr-steady-benchmark.json",
    [string]$CandidateSteady = "reports/candidate-player-hybridclr-steady-benchmark.json",
    [string]$CandidateAotSteady = "reports/candidate-player-aot-steady-benchmark.json",
    [string]$BaselineCold = "reports/baseline-clean-player-hybridclr-cold-benchmark.json",
    [string]$CandidateCold = "reports/candidate-player-hybridclr-cold-benchmark.json",
    [string]$CandidateAotCold = "reports/candidate-player-aot-cold-benchmark.json",
    [string]$Output = "reports/candidate-triple-performance-comparison.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Resolve-ReportPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Read-Report([string]$Path) {
    $resolved = Resolve-ReportPath $Path
    if (-not (Test-Path -LiteralPath $resolved)) { throw "Benchmark report was not found: $resolved" }
    return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
}

function Assert-Report($Report, [string]$Label, [string]$Mode) {
    if ($Report.suiteId -ne "managed-performance-v1" -or $Report.benchmarkMode -ne $Mode) {
        throw "$Label is not a managed-performance-v1 $Mode report."
    }
    if (-not $Report.minimumProcessCountMet) { throw "$Label did not meet the minimum independent process count." }
    if ([int]$Report.sourceResultCount -lt 10) { throw "$Label has fewer than 10 independent process results." }
}

function Assert-Contracts($Reports, [string]$Mode) {
    $first = $Reports[0]
    $expectedIds = @($first.workloads | ForEach-Object { $_.id })
    foreach ($report in $Reports[1..($Reports.Count - 1)]) {
        $ids = @($report.workloads | ForEach-Object { $_.id })
        if (($ids -join "`n") -ne ($expectedIds -join "`n")) {
            throw "Workload id mismatch in $Mode reports."
        }
    }
    foreach ($id in $expectedIds) {
        $reference = @($first.workloads | Where-Object { $_.id -eq $id })[0]
        $referenceFeatures = @($reference.features) -join "`n"
        foreach ($report in $Reports[1..($Reports.Count - 1)]) {
            $workload = @($report.workloads | Where-Object { $_.id -eq $id })
            if ($workload.Count -ne 1) { throw "$Mode report is missing or duplicating workload '$id'." }
            $workload = $workload[0]
            if ([int]$workload.iterations -ne [int]$reference.iterations -or
                [string]$workload.checksum -ne [string]$reference.checksum -or
                ((@($workload.features) -join "`n") -ne $referenceFeatures)) {
                throw "Workload contract mismatch for '$id' in $Mode reports."
            }
        }
    }
}

function Get-GeometricMean([double[]]$Values) {
    if ($Values.Count -eq 0) { return 0.0 }
    $logSum = 0.0
    foreach ($value in $Values) { $logSum += [Math]::Log($value) }
    return [Math]::Exp($logSum / $Values.Count)
}

function Get-ProfileMetadata($Report, [string]$Path) {
    return [ordered]@{
        report = $Path
        runner = $Report.runner
        runtime = $Report.runtime
        buildManifestSha256 = $Report.buildManifestSha256
        managedAssemblySha256 = $Report.managedAssemblySha256
        policySha256 = $Report.policySha256
        processCount = [int]$Report.sourceResultCount
        recommendedNoiseThresholdPercent = [double]$Report.calibration.recommendedNoiseThresholdPercent
    }
}

function Compare-Mode($Baseline, $Candidate, $Aot, [string]$Mode) {
    Assert-Contracts @($Baseline, $Candidate, $Aot) $Mode
    if ($Candidate.buildManifestSha256 -ne $Aot.buildManifestSha256) {
        throw "Candidate and AOT $Mode reports must use the same build manifest."
    }
    if ($Baseline.policySha256 -ne $Candidate.policySha256 -or $Candidate.policySha256 -ne $Aot.policySha256) {
        throw "$Mode reports do not share the same benchmark policy."
    }

    $candidateThreshold = [Math]::Max(
        [double]$Baseline.calibration.recommendedNoiseThresholdPercent,
        [double]$Candidate.calibration.recommendedNoiseThresholdPercent)
    $aotThreshold = [Math]::Max(
        [double]$Candidate.calibration.recommendedNoiseThresholdPercent,
        [double]$Aot.calibration.recommendedNoiseThresholdPercent)
    $rows = @()
    foreach ($baselineWorkload in $Baseline.workloads) {
        $id = $baselineWorkload.id
        $candidateWorkload = @($Candidate.workloads | Where-Object { $_.id -eq $id })[0]
        $aotWorkload = @($Aot.workloads | Where-Object { $_.id -eq $id })[0]
        $baselineMedian = [double]$baselineWorkload.aggregate.median
        $candidateMedian = [double]$candidateWorkload.aggregate.median
        $aotMedian = [double]$aotWorkload.aggregate.median
        $candidateDelta = ($candidateMedian - $baselineMedian) * 100.0 / $baselineMedian
        $aotDelta = ($aotMedian - $candidateMedian) * 100.0 / $candidateMedian
        $rows += [ordered]@{
            id = $id
            category = $baselineWorkload.category
            features = @($baselineWorkload.features)
            iterations = [int]$baselineWorkload.iterations
            checksum = $baselineWorkload.checksum
            baselineHybridClrMedianNanosecondsPerIteration = $baselineMedian
            candidateHybridClrMedianNanosecondsPerIteration = $candidateMedian
            candidateVsBaselineSpeedup = $baselineMedian / $candidateMedian
            candidateVsBaselineDeltaPercent = $candidateDelta
            candidateVerdict = if ($candidateDelta -le -$candidateThreshold) { "candidate-faster" } elseif ($candidateDelta -ge $candidateThreshold) { "candidate-regression" } else { "within-noise" }
            candidateAotMedianNanosecondsPerIteration = $aotMedian
            aotVsCandidateSpeedup = $candidateMedian / $aotMedian
            aotVsCandidateDeltaPercent = $aotDelta
            aotVerdict = if ($aotDelta -le -$aotThreshold) { "aot-faster" } elseif ($aotDelta -ge $aotThreshold) { "candidate-faster" } else { "within-noise" }
            candidateNoiseThresholdPercent = $candidateThreshold
            aotNoiseThresholdPercent = $aotThreshold
        }
    }
    $coreRows = @($rows | Where-Object { $_.id -notin @("interp_boxing", "interp_boxing_escape", "interp_boxing_mixed", "interp_string_allocation", "interp_exception") })
    return [ordered]@{
        mode = $Mode
        workloadCount = $rows.Count
        candidateFasterCount = @($rows | Where-Object { $_.candidateVerdict -eq "candidate-faster" }).Count
        candidateRegressionCount = @($rows | Where-Object { $_.candidateVerdict -eq "candidate-regression" }).Count
        candidateWithinNoiseCount = @($rows | Where-Object { $_.candidateVerdict -eq "within-noise" }).Count
        allWorkloadsCandidateVsBaselineGeometricMeanSpeedup = Get-GeometricMean ([double[]]@($rows | ForEach-Object { $_.candidateVsBaselineSpeedup }))
        interpreterAndBoundaryCoreCandidateVsBaselineGeometricMeanSpeedup = Get-GeometricMean ([double[]]@($coreRows | ForEach-Object { $_.candidateVsBaselineSpeedup }))
        allWorkloadsAotVsCandidateGeometricMeanSpeedup = Get-GeometricMean ([double[]]@($rows | ForEach-Object { $_.aotVsCandidateSpeedup }))
        interpreterAndBoundaryCoreAotVsCandidateGeometricMeanSpeedup = Get-GeometricMean ([double[]]@($coreRows | ForEach-Object { $_.aotVsCandidateSpeedup }))
        workloads = $rows
    }
}

$baselineSteadyReport = Read-Report $BaselineSteady
$candidateSteadyReport = Read-Report $CandidateSteady
$candidateAotSteadyReport = Read-Report $CandidateAotSteady
$baselineColdReport = Read-Report $BaselineCold
$candidateColdReport = Read-Report $CandidateCold
$candidateAotColdReport = Read-Report $CandidateAotCold
Assert-Report $baselineSteadyReport "Baseline steady" "steady"
Assert-Report $candidateSteadyReport "Candidate steady" "steady"
Assert-Report $candidateAotSteadyReport "Candidate AOT steady" "steady"
Assert-Report $baselineColdReport "Baseline cold" "cold"
Assert-Report $candidateColdReport "Candidate cold" "cold"
Assert-Report $candidateAotColdReport "Candidate AOT cold" "cold"

$outputPath = Resolve-ReportPath $Output
$comparison = [ordered]@{
    schemaVersion = 1
    suiteId = "managed-performance-v1"
    comparison = "community-hybridclr-vs-candidate-hybridclr-vs-aot"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    profiles = [ordered]@{
        baselineHybridClr = Get-ProfileMetadata $baselineSteadyReport $BaselineSteady
        candidateHybridClr = Get-ProfileMetadata $candidateSteadyReport $CandidateSteady
        candidateAot = Get-ProfileMetadata $candidateAotSteadyReport $CandidateAotSteady
    }
    steady = Compare-Mode $baselineSteadyReport $candidateSteadyReport $candidateAotSteadyReport "steady"
    cold = Compare-Mode $baselineColdReport $candidateColdReport $candidateAotColdReport "cold"
    interpretation = [ordered]@{
        primaryMetric = "steady median nanoseconds per logical iteration"
        candidateSpeedup = "baselineHybridClrMedian / candidateHybridClrMedian"
        aotSpeedup = "candidateHybridClrMedian / candidateAotMedian"
        coreExclusions = @("interp_boxing", "interp_boxing_escape", "interp_boxing_mixed", "interp_string_allocation", "interp_exception")
        note = "Candidate verdicts use the maximum recommended noise threshold of baseline and candidate. Cold results are diagnostic because cold-path variance is high."
    }
}
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$comparison | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Triple performance comparison: $outputPath"
Write-Host ("Steady core Candidate vs Baseline: {0:N2}x; AOT vs Candidate: {1:N2}x" -f `
    $comparison.steady.interpreterAndBoundaryCoreCandidateVsBaselineGeometricMeanSpeedup,
    $comparison.steady.interpreterAndBoundaryCoreAotVsCandidateGeometricMeanSpeedup)
