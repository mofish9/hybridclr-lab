param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$OptimizeSpeedSupplemental = "reports/fgs-candidate-optimizespeed-interp-generic-supplemental-10.json",
    [string]$OptimizeSpeedNone = "reports/fgs-candidate-optimizespeed-interp-generic-none-10.json",
    [string]$OptimizeSizeSupplemental = "reports/fgs-candidate-optimizesize-interp-generic-supplemental-10.json",
    [string]$OptimizeSizeNone = "reports/fgs-candidate-optimizesize-interp-generic-none-10.json",
    [string]$Output = "reports/fgs-candidate-interp-generic-comparison.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Read-Report([string]$Path) {
    $resolved = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $LabRoot $Path }
    if (-not (Test-Path -LiteralPath $resolved)) { throw "Benchmark report was not found: $resolved" }
    return [pscustomobject]@{
        path = [IO.Path]::GetRelativePath($LabRoot, $resolved).Replace('\', '/')
        report = Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
    }
}

function Assert-CommonContract($Expected, $Actual, [string]$Label) {
    foreach ($field in @(
        "schemaVersion", "suiteId", "benchmarkMode", "runner", "runtime", "platform",
        "architecture", "policySha256", "managedAssemblySha256")) {
        if ($Actual.$field -ne $Expected.$field) {
            throw "$Label does not match the common benchmark contract for '$field'."
        }
    }
}

function Get-AuditedWorkload($ReportInput, [string]$ExpectedMode, [string]$Label) {
    $report = $ReportInput.report
    if ($report.aotMetadataMode -ne $ExpectedMode) {
        throw "$Label metadata mode is '$($report.aotMetadataMode)', expected '$ExpectedMode'."
    }
    if (-not $report.minimumProcessCountMet -or
        [int]$report.sourceResultCount -lt [int]$report.minimumIndependentProcesses) {
        throw "$Label does not meet the independent-process requirement."
    }
    $workloads = @($report.workloads)
    if ($workloads.Count -ne 1 -or $workloads[0].id -ne "interp_generic") {
        throw "$Label must contain only the 'interp_generic' workload."
    }
    $workload = $workloads[0]
    if ([int]$workload.processCount -ne [int]$report.sourceResultCount) {
        throw "$Label process count does not match its source result count."
    }
    $uniquePids = @($workload.processSamples.processId | Sort-Object -Unique)
    if ($uniquePids.Count -ne [int]$workload.processCount) {
        throw "$Label reused one or more Player process IDs."
    }
    if ([string]::IsNullOrWhiteSpace([string]$workload.checksum)) {
        throw "$Label did not record a workload checksum."
    }
    if ($ExpectedMode -eq "none") {
        $startup = $report.startup.aotMetadataLoadNanoseconds
        if ([double]$startup.minimum -ne 0 -or [double]$startup.median -ne 0 -or
            [double]$startup.p95 -ne 0 -or [double]$startup.maximum -ne 0) {
            throw "$Label recorded AOT metadata loading time while metadata mode was none."
        }
    }
    return $workload
}

function Compare-Workloads(
    $BaselineReport, $Baseline, $CandidateReport, $Candidate,
    [string]$BaselineLabel, [string]$CandidateLabel) {
    if ($Baseline.iterations -ne $Candidate.iterations -or
        $Baseline.checksum -ne $Candidate.checksum -or
        (@($Baseline.features) -join "`n") -ne (@($Candidate.features) -join "`n")) {
        throw "Workload contract mismatch between $BaselineLabel and $CandidateLabel."
    }
    $baselineMedian = [double]$Baseline.aggregate.median
    $candidateMedian = [double]$Candidate.aggregate.median
    $deltaPercent = ($candidateMedian / $baselineMedian - 1.0) * 100.0
    $noiseThreshold = [Math]::Max(
        [double]$BaselineReport.calibration.recommendedNoiseThresholdPercent,
        [double]$CandidateReport.calibration.recommendedNoiseThresholdPercent)
    $verdict = if ($deltaPercent -gt $noiseThreshold) {
        "candidate-slower"
    } elseif ($deltaPercent -lt -$noiseThreshold) {
        "candidate-faster"
    } else {
        "within-noise"
    }
    return [ordered]@{
        baseline = $BaselineLabel
        candidate = $CandidateLabel
        baselineMedianNanosecondsPerIteration = $baselineMedian
        candidateMedianNanosecondsPerIteration = $candidateMedian
        deltaPercent = $deltaPercent
        noiseThresholdPercent = $noiseThreshold
        verdict = $verdict
    }
}

$speedSupplementalInput = Read-Report $OptimizeSpeedSupplemental
$speedNoneInput = Read-Report $OptimizeSpeedNone
$sizeSupplementalInput = Read-Report $OptimizeSizeSupplemental
$sizeNoneInput = Read-Report $OptimizeSizeNone
$first = $speedSupplementalInput.report
foreach ($entry in @(
    @{ label = "OptimizeSpeed/none"; report = $speedNoneInput.report },
    @{ label = "OptimizeSize/supplemental"; report = $sizeSupplementalInput.report },
    @{ label = "OptimizeSize/none"; report = $sizeNoneInput.report })) {
    Assert-CommonContract $first $entry.report $entry.label
}
if ($speedSupplementalInput.report.buildManifestSha256 -ne $speedNoneInput.report.buildManifestSha256) {
    throw "OptimizeSpeed metadata modes were not measured from the same build manifest."
}
if ($sizeSupplementalInput.report.buildManifestSha256 -ne $sizeNoneInput.report.buildManifestSha256) {
    throw "OptimizeSize metadata modes were not measured from the same build manifest."
}

$speedSupplemental = Get-AuditedWorkload $speedSupplementalInput "supplemental" "OptimizeSpeed/supplemental"
$speedNone = Get-AuditedWorkload $speedNoneInput "none" "OptimizeSpeed/none"
$sizeSupplemental = Get-AuditedWorkload $sizeSupplementalInput "supplemental" "OptimizeSize/supplemental"
$sizeNone = Get-AuditedWorkload $sizeNoneInput "none" "OptimizeSize/none"

$comparison = [ordered]@{
    schemaVersion = 1
    suiteId = "full-generic-sharing-benchmark-comparison-v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    policySha256 = $first.policySha256
    managedAssemblySha256 = $first.managedAssemblySha256
    inputs = [ordered]@{
        optimizeSpeedSupplemental = $speedSupplementalInput.path
        optimizeSpeedNone = $speedNoneInput.path
        optimizeSizeSupplemental = $sizeSupplementalInput.path
        optimizeSizeNone = $sizeNoneInput.path
    }
    audit = [ordered]@{
        expectedWorkload = "interp_generic"
        expectedChecksum = [string]$speedSupplemental.checksum
        minimumIndependentProcesses = [int]$first.minimumIndependentProcesses
        allProcessCountsMet = $true
        allProcessIdsUnique = $true
        noneMetadataLoadNanosecondsZero = $true
    }
    comparisons = [ordered]@{
        optimizeSpeedNoneVsSupplemental = Compare-Workloads $speedSupplementalInput.report $speedSupplemental $speedNoneInput.report $speedNone "OptimizeSpeed/supplemental" "OptimizeSpeed/none"
        optimizeSizeNoneVsSupplemental = Compare-Workloads $sizeSupplementalInput.report $sizeSupplemental $sizeNoneInput.report $sizeNone "OptimizeSize/supplemental" "OptimizeSize/none"
        optimizeSizeVsSpeedSupplemental = Compare-Workloads $speedSupplementalInput.report $speedSupplemental $sizeSupplementalInput.report $sizeSupplemental "OptimizeSpeed/supplemental" "OptimizeSize/supplemental"
        optimizeSizeVsSpeedNone = Compare-Workloads $speedNoneInput.report $speedNone $sizeNoneInput.report $sizeNone "OptimizeSpeed/none" "OptimizeSize/none"
    }
}

$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $LabRoot $Output }
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$comparison | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "FGS benchmark comparison: $outputPath"
Write-Host "Raw-result semantic audit: passed"
