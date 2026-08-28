param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Baseline = "reports/baseline-clean-metadata-supplemental-summary.json",
    [string]$Candidate = "reports/candidate-metadata-supplemental-summary.json",
    [string]$BaselineBuildManifest = "reports/baseline-clean-build-manifest.json",
    [string]$CandidateBuildManifest = "reports/candidate-build-manifest.json",
    [string]$Output = "reports/candidate-metadata-comparison.json",
    [switch]$DiagnosticOnly
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Read-Json([string]$Path, [string]$Label) {
    $resolved = Resolve-LabPath $Path
    if (-not (Test-Path -LiteralPath $resolved)) { throw "$Label was not found: $resolved" }
    return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
}

function Assert-Summary($Summary, [string]$Label) {
    if ($Summary.schemaVersion -ne 1 -or $Summary.suiteId -ne "hybridclr-metadata-load-v2") {
        throw "$Label is not a hybridclr-metadata-load-v2 summary."
    }
    if (-not $Summary.minimumProcessCountMet) { throw "$Label did not meet the independent process requirement." }
    if ([int]$Summary.sourceResultCount -lt [int]$Summary.minimumIndependentProcesses) {
        throw "$Label has fewer results than its declared minimum."
    }
}

function Assert-BuildBinding($Summary, [string]$ManifestPath, $Manifest, [string]$Label) {
    $resolvedManifest = Resolve-LabPath $ManifestPath
    $actualManifestHash = (Get-FileHash -LiteralPath $resolvedManifest -Algorithm SHA256).Hash
    if ($Summary.buildManifestSha256 -ne $actualManifestHash) {
        throw "$Label summary is not bound to the supplied build manifest."
    }
    if ($Manifest.metadataBenchmarkPolicySha256 -ne $Summary.policySha256) {
        throw "$Label build manifest and summary use different metadata benchmark policies."
    }
    if ($Manifest.metadataStressAssemblySha256 -ne $Summary.stressAssembly.sha256) {
        throw "$Label build manifest and summary use different metadata stress assemblies."
    }
}

function Get-ReflectionContract([object]$Summary) {
    if ($null -eq $Summary.PSObject.Properties["reflectionContract"]) {
        return [pscustomobject]@{ profile = "exhaustive"; requestedTypeCount = 0 }
    }
    return [pscustomobject]@{
        profile = [string]$Summary.reflectionContract.profile
        requestedTypeCount = [int]$Summary.reflectionContract.requestedTypeCount
    }
}

function Get-MetadataScenario([object]$Summary) {
    if ($null -eq $Summary.PSObject.Properties["metadataScenario"]) { return "entry-first" }
    return [string]$Summary.metadataScenario
}

function Get-MetadataWarmupMode([object]$Summary) {
    $property = $Summary.PSObject.Properties["metadataWarmup"]
    if ($null -eq $property) { return "none" }
    $mode = [string]$Summary.metadataWarmup.mode
    if ($mode -notin @("none", "entry", "entry-method", "entry-graph", "entry-method-graph")) { throw "Summary has an invalid metadata warmup mode." }
    return $mode
}

function Get-DurationStatistics([object]$Summary, [string]$Name) {
    $property = $Summary.durationsNanoseconds.PSObject.Properties[$Name]
    if ($null -eq $property) { return [pscustomobject]@{ median = 0.0; p95 = 0.0; p99 = 0.0 } }
    return $property.Value
}

function Get-OptionalPercentile([object]$Statistics, [string]$Name) {
    if ($null -eq $Statistics) { return $null }
    $property = $Statistics.PSObject.Properties[$Name]
    if ($null -ne $property) { return [double]$property.Value }
    return $null
}

function New-TimeComparison([string]$Name, [double]$BaselineValue, [double]$CandidateValue, $Acceptance) {
    $delta = $CandidateValue - $BaselineValue
    $deltaPercent = if ($BaselineValue -eq 0.0) {
        if ($CandidateValue -eq 0.0) { 0.0 } else { $null }
    } else {
        100.0 * $delta / $BaselineValue
    }
    $isRegression = $null -ne $deltaPercent -and
        $deltaPercent -gt [double]$Acceptance.maximumTimeRegressionPercent -and
        $delta -gt [double]$Acceptance.maximumTimeRegressionNanoseconds
    $isImprovement = $null -ne $deltaPercent -and
        (-$deltaPercent) -ge [double]$Acceptance.minimumTimeImprovementPercent -and
        (-$delta) -ge [double]$Acceptance.minimumTimeImprovementNanoseconds
    $verdict = if ($null -eq $deltaPercent) {
        "not-comparable"
    } elseif ($isRegression) {
        "regression"
    } elseif ($isImprovement) {
        "improvement"
    } else {
        "within-threshold"
    }
    return [ordered]@{
        name = $Name
        baselineMedian = $BaselineValue
        candidateMedian = $CandidateValue
        delta = $delta
        deltaPercent = $deltaPercent
        verdict = $verdict
    }
}

function New-MemoryComparison([string]$Name, [double]$BaselineValue, [double]$CandidateValue, $Acceptance) {
    $delta = $CandidateValue - $BaselineValue
    $deltaPercent = if ($BaselineValue -eq 0.0) {
        if ($CandidateValue -eq 0.0) { 0.0 } else { $null }
    } else {
        100.0 * $delta / [Math]::Abs($BaselineValue)
    }
    $isRegression = $null -ne $deltaPercent -and
        $deltaPercent -gt [double]$Acceptance.maximumMemoryRegressionPercent -and
        $delta -gt [double]$Acceptance.maximumMemoryRegressionBytes
    $improvementBytes = -$delta
    $isImprovement = $null -ne $deltaPercent -and
        (-$deltaPercent) -ge [double]$Acceptance.minimumMemoryImprovementPercent -and
        $improvementBytes -ge [double]$Acceptance.minimumMemoryImprovementBytes
    $verdict = if ($null -eq $deltaPercent) {
        "not-comparable"
    } elseif ($isRegression) {
        "regression"
    } elseif ($isImprovement) {
        "improvement"
    } else {
        "within-threshold"
    }
    return [ordered]@{
        name = $Name
        baselineMedianBytes = $BaselineValue
        candidateMedianBytes = $CandidateValue
        deltaBytes = $delta
        deltaPercent = $deltaPercent
        verdict = $verdict
    }
}

function New-StageMetric([string]$Name, [double]$BaselineValue, [double]$CandidateValue, [string]$Requirement, [double]$ThresholdPercent) {
    $deltaPercent = if ($BaselineValue -eq 0.0) {
        if ($CandidateValue -eq 0.0) { 0.0 } else { $null }
    } else {
        100.0 * ($CandidateValue - $BaselineValue) / [Math]::Abs($BaselineValue)
    }
    $achievedPercent = if ($Requirement -eq "minimum-improvement") { -$deltaPercent } else { $deltaPercent }
    $passed = $null -ne $deltaPercent -and $(if ($Requirement -eq "minimum-improvement") {
        $achievedPercent -ge $ThresholdPercent
    } else {
        $achievedPercent -le $ThresholdPercent
    })
    return [ordered]@{
        name = $Name
        baselineMedian = $BaselineValue
        candidateMedian = $CandidateValue
        deltaPercent = $deltaPercent
        requirement = $Requirement
        thresholdPercent = $ThresholdPercent
        achievedPercent = $achievedPercent
        passed = $passed
    }
}

$baselineReport = Read-Json $Baseline "Baseline metadata summary"
$candidateReport = Read-Json $Candidate "Candidate metadata summary"
$baselineManifest = Read-Json $BaselineBuildManifest "Baseline build manifest"
$candidateManifest = Read-Json $CandidateBuildManifest "Candidate build manifest"
$policyPath = Resolve-LabPath "manifests/metadata-benchmark-policy.json"
$policy = Get-Content -Raw -LiteralPath $policyPath | ConvertFrom-Json
$policyHash = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
$stageTargetPath = Resolve-LabPath "manifests/metadata-stage-targets.json"
$stageTargetPolicy = Get-Content -Raw -LiteralPath $stageTargetPath | ConvertFrom-Json
$stageTargetHash = (Get-FileHash -LiteralPath $stageTargetPath -Algorithm SHA256).Hash

Assert-Summary $baselineReport "Baseline"
Assert-Summary $candidateReport "Candidate"
Assert-BuildBinding $baselineReport $BaselineBuildManifest $baselineManifest "Baseline"
Assert-BuildBinding $candidateReport $CandidateBuildManifest $candidateManifest "Candidate"
$baselineReflectionContract = Get-ReflectionContract $baselineReport
$candidateReflectionContract = Get-ReflectionContract $candidateReport
$baselineMetadataScenario = Get-MetadataScenario $baselineReport
$candidateMetadataScenario = Get-MetadataScenario $candidateReport
$baselineMetadataWarmup = Get-MetadataWarmupMode $baselineReport
$candidateMetadataWarmup = Get-MetadataWarmupMode $candidateReport

foreach ($field in @("schemaVersion", "suiteId", "metadataMode", "runtime", "platform", "architecture", "policySha256")) {
    if ($baselineReport.$field -ne $candidateReport.$field) { throw "Summary mismatch for '$field'." }
}
if ($baselineReport.policySha256 -ne $policyHash) { throw "Reports do not use the current metadata benchmark policy." }
if ($baselineReflectionContract.profile -ne $candidateReflectionContract.profile -or
    $baselineReflectionContract.requestedTypeCount -ne $candidateReflectionContract.requestedTypeCount) {
    throw "Baseline and Candidate reflection contracts do not match."
}
if ($baselineMetadataScenario -ne $candidateMetadataScenario) {
    throw "Baseline and Candidate metadata scenarios do not match."
}
if ($baselineMetadataWarmup -ne $candidateMetadataWarmup) {
    throw "Baseline and Candidate metadata warmup modes do not match."
}
if ($baselineManifest.target -ne $candidateManifest.target -or
    $baselineManifest.configuration -ne $candidateManifest.configuration -or
    $baselineManifest.il2cppCodeGeneration -ne $candidateManifest.il2cppCodeGeneration) {
    throw "Baseline and Candidate build configurations do not match."
}
if ($baselineReport.stressAssembly.sha256 -ne $candidateReport.stressAssembly.sha256 -or
    $baselineReport.stressAssembly.bytes -ne $candidateReport.stressAssembly.bytes -or
    $baselineReport.touchCounts.types -ne $candidateReport.touchCounts.types -or
    $baselineReport.touchCounts.members -ne $candidateReport.touchCounts.members -or
    $baselineReport.touchCounts.attributes -ne $candidateReport.touchCounts.attributes -or
    $baselineReport.touchCounts.entryChecksum -ne $candidateReport.touchCounts.entryChecksum) {
    throw "Metadata stress input or touch contract mismatch."
}

$baselineEntryResolve = Get-DurationStatistics $baselineReport "entryResolve"
$candidateEntryResolve = Get-DurationStatistics $candidateReport "entryResolve"
$baselineAotP99 = Get-OptionalPercentile $baselineReport.durationsNanoseconds.aotMetadataLoad "p99"
$candidateAotP99 = Get-OptionalPercentile $candidateReport.durationsNanoseconds.aotMetadataLoad "p99"
$baselineLoadP99 = Get-OptionalPercentile $baselineReport.durationsNanoseconds.assemblyLoad "p99"
$candidateLoadP99 = Get-OptionalPercentile $candidateReport.durationsNanoseconds.assemblyLoad "p99"
$baselineEntryResolveP99 = Get-OptionalPercentile $baselineEntryResolve "p99"
$candidateEntryResolveP99 = Get-OptionalPercentile $candidateEntryResolve "p99"
$baselineReflectionP99 = Get-OptionalPercentile $baselineReport.durationsNanoseconds.reflectionTouch "p99"
$candidateReflectionP99 = Get-OptionalPercentile $candidateReport.durationsNanoseconds.reflectionTouch "p99"
$baselineEntryExecuteP99 = Get-OptionalPercentile $baselineReport.durationsNanoseconds.entryExecute "p99"
$candidateEntryExecuteP99 = Get-OptionalPercentile $candidateReport.durationsNanoseconds.entryExecute "p99"
$hasP99 = $null -ne $baselineAotP99 -and $null -ne $candidateAotP99 -and
    $null -ne $baselineLoadP99 -and $null -ne $candidateLoadP99 -and
    $null -ne $baselineEntryResolveP99 -and $null -ne $candidateEntryResolveP99 -and
    $null -ne $baselineReflectionP99 -and $null -ne $candidateReflectionP99 -and
    $null -ne $baselineEntryExecuteP99 -and $null -ne $candidateEntryExecuteP99
$p99Acceptance = [pscustomobject]@{
    maximumTimeRegressionPercent = [double]$policy.acceptance.maximumP99TimeRegressionPercent
    maximumTimeRegressionNanoseconds = [double]$policy.acceptance.maximumTimeRegressionNanoseconds
    minimumTimeImprovementPercent = [double]$policy.acceptance.minimumTimeImprovementPercent
    minimumTimeImprovementNanoseconds = [double]$policy.acceptance.minimumTimeImprovementNanoseconds
}
$timeComparisons = @(
    New-TimeComparison "aotMetadataLoad" $baselineReport.durationsNanoseconds.aotMetadataLoad.median $candidateReport.durationsNanoseconds.aotMetadataLoad.median $policy.acceptance
    New-TimeComparison "assemblyLoad" $baselineReport.durationsNanoseconds.assemblyLoad.median $candidateReport.durationsNanoseconds.assemblyLoad.median $policy.acceptance
    New-TimeComparison "entryResolve" $baselineEntryResolve.median $candidateEntryResolve.median $policy.acceptance
    New-TimeComparison "reflectionTouch" $baselineReport.durationsNanoseconds.reflectionTouch.median $candidateReport.durationsNanoseconds.reflectionTouch.median $policy.acceptance
    New-TimeComparison "entryExecute" $baselineReport.durationsNanoseconds.entryExecute.median $candidateReport.durationsNanoseconds.entryExecute.median $policy.acceptance
    New-TimeComparison "aotMetadataLoadP95" $baselineReport.durationsNanoseconds.aotMetadataLoad.p95 $candidateReport.durationsNanoseconds.aotMetadataLoad.p95 $policy.acceptance
    New-TimeComparison "assemblyLoadP95" $baselineReport.durationsNanoseconds.assemblyLoad.p95 $candidateReport.durationsNanoseconds.assemblyLoad.p95 $policy.acceptance
    New-TimeComparison "entryResolveP95" $baselineEntryResolve.p95 $candidateEntryResolve.p95 $policy.acceptance
    New-TimeComparison "reflectionTouchP95" $baselineReport.durationsNanoseconds.reflectionTouch.p95 $candidateReport.durationsNanoseconds.reflectionTouch.p95 $policy.acceptance
    New-TimeComparison "entryExecuteP95" $baselineReport.durationsNanoseconds.entryExecute.p95 $candidateReport.durationsNanoseconds.entryExecute.p95 $policy.acceptance
)
$tailTimeComparisons = @()
if ($hasP99) {
    $tailTimeComparisons = @(
        New-TimeComparison "aotMetadataLoadP99" $baselineAotP99 $candidateAotP99 $p99Acceptance
        New-TimeComparison "assemblyLoadP99" $baselineLoadP99 $candidateLoadP99 $p99Acceptance
        New-TimeComparison "entryResolveP99" $baselineEntryResolveP99 $candidateEntryResolveP99 $p99Acceptance
        New-TimeComparison "reflectionTouchP99" $baselineReflectionP99 $candidateReflectionP99 $p99Acceptance
        New-TimeComparison "entryExecuteP99" $baselineEntryExecuteP99 $candidateEntryExecuteP99 $p99Acceptance
    )
}
$memoryComparisons = @(
    New-MemoryComparison "loadOnlyPrivateBytes" $baselineReport.privateBytesDeltaFromBaseline.loadOnly.median $candidateReport.privateBytesDeltaFromBaseline.loadOnly.median $policy.acceptance
    New-MemoryComparison "reflectionTouchedPrivateBytes" $baselineReport.privateBytesDeltaFromBaseline.reflectionTouched.median $candidateReport.privateBytesDeltaFromBaseline.reflectionTouched.median $policy.acceptance
    New-MemoryComparison "entryExecutedPrivateBytes" $baselineReport.privateBytesDeltaFromBaseline.entryExecuted.median $candidateReport.privateBytesDeltaFromBaseline.entryExecuted.median $policy.acceptance
)
$hasAndroidPss = $null -ne $baselineReport.androidPssBytesDeltaFromBaseline.loadOnly -and
    $null -ne $candidateReport.androidPssBytesDeltaFromBaseline.loadOnly
if ($hasAndroidPss) {
    $memoryComparisons += @(
        New-MemoryComparison "loadOnlyAndroidPssBytes" $baselineReport.androidPssBytesDeltaFromBaseline.loadOnly.median $candidateReport.androidPssBytesDeltaFromBaseline.loadOnly.median $policy.acceptance
        New-MemoryComparison "reflectionTouchedAndroidPssBytes" $baselineReport.androidPssBytesDeltaFromBaseline.reflectionTouched.median $candidateReport.androidPssBytesDeltaFromBaseline.reflectionTouched.median $policy.acceptance
        New-MemoryComparison "entryExecutedAndroidPssBytes" $baselineReport.androidPssBytesDeltaFromBaseline.entryExecuted.median $candidateReport.androidPssBytesDeltaFromBaseline.entryExecuted.median $policy.acceptance
    )
}
$p99MinimumProcessCount = 100
$p99Qualified = $hasP99 -and
    [int]$baselineReport.sourceResultCount -ge $p99MinimumProcessCount -and
    [int]$candidateReport.sourceResultCount -ge $p99MinimumProcessCount
$gateTimeComparisons = @($timeComparisons)
if ($p99Qualified) { $gateTimeComparisons += $tailTimeComparisons }
$regressions = @($gateTimeComparisons + $memoryComparisons | Where-Object { $_.verdict -eq "regression" })
$improvements = @($gateTimeComparisons + $memoryComparisons | Where-Object { $_.verdict -eq "improvement" })
$baselineLoadAndReflection = [double]$baselineReport.durationsNanoseconds.assemblyLoad.median + [double]$baselineReport.durationsNanoseconds.reflectionTouch.median
$candidateLoadAndReflection = [double]$candidateReport.durationsNanoseconds.assemblyLoad.median + [double]$candidateReport.durationsNanoseconds.reflectionTouch.median
$baselineLoadAndEntry = [double]$baselineReport.durationsNanoseconds.assemblyLoad.median + [double]$baselineEntryResolve.median + [double]$baselineReport.durationsNanoseconds.entryExecute.median
$candidateLoadAndEntry = [double]$candidateReport.durationsNanoseconds.assemblyLoad.median + [double]$candidateEntryResolve.median + [double]$candidateReport.durationsNanoseconds.entryExecute.median
if ($baselineMetadataScenario -eq "reflection-first") {
    $baselineLoadAndEntry += [double]$baselineReport.durationsNanoseconds.reflectionTouch.median
    $candidateLoadAndEntry += [double]$candidateReport.durationsNanoseconds.reflectionTouch.median
    $baselineThroughReflection = $baselineLoadAndReflection
    $candidateThroughReflection = $candidateLoadAndReflection
} else {
    $baselineThroughReflection = $baselineLoadAndEntry + [double]$baselineReport.durationsNanoseconds.reflectionTouch.median
    $candidateThroughReflection = $candidateLoadAndEntry + [double]$candidateReport.durationsNanoseconds.reflectionTouch.median
}
$targets = $stageTargetPolicy.targets
$loadAndEntryTarget = if ($baselineMetadataScenario -eq "reflection-first") { $targets.throughReflectionMinimumImprovementPercent } else { $targets.loadAndEntryMinimumImprovementPercent }
$entryMemoryTargetKind = if ($baselineMetadataScenario -eq "reflection-first") { "maximum-regression" } else { "minimum-improvement" }
$entryMemoryTarget = if ($baselineMetadataScenario -eq "reflection-first") { $targets.reflectionTouchedPrivateBytesMaximumRegressionPercent } else { $targets.entryExecutedPrivateBytesMinimumImprovementPercent }
$stageMetrics = @(
    New-StageMetric "assemblyLoad" $baselineReport.durationsNanoseconds.assemblyLoad.median $candidateReport.durationsNanoseconds.assemblyLoad.median "minimum-improvement" $targets.assemblyLoadMinimumImprovementPercent
    New-StageMetric "loadAndEntry" $baselineLoadAndEntry $candidateLoadAndEntry "minimum-improvement" $loadAndEntryTarget
    New-StageMetric "loadAndReflection" $baselineLoadAndReflection $candidateLoadAndReflection "minimum-improvement" $targets.loadAndReflectionMinimumImprovementPercent
    New-StageMetric "throughReflection" $baselineThroughReflection $candidateThroughReflection "minimum-improvement" $targets.throughReflectionMinimumImprovementPercent
    New-StageMetric "loadOnlyPrivateBytes" $baselineReport.privateBytesDeltaFromBaseline.loadOnly.median $candidateReport.privateBytesDeltaFromBaseline.loadOnly.median "minimum-improvement" $targets.loadOnlyPrivateBytesMinimumImprovementPercent
    New-StageMetric "entryExecutedPrivateBytes" $baselineReport.privateBytesDeltaFromBaseline.entryExecuted.median $candidateReport.privateBytesDeltaFromBaseline.entryExecuted.median $entryMemoryTargetKind $entryMemoryTarget
    New-StageMetric "entryExecute" $baselineReport.durationsNanoseconds.entryExecute.median $candidateReport.durationsNanoseconds.entryExecute.median "maximum-regression" $targets.entryExecuteMaximumRegressionPercent
    New-StageMetric "entryResolve" $baselineEntryResolve.median $candidateEntryResolve.median "maximum-regression" $targets.entryResolveMaximumRegressionPercent
    New-StageMetric "reflectionTouch" $baselineReport.durationsNanoseconds.reflectionTouch.median $candidateReport.durationsNanoseconds.reflectionTouch.median "maximum-regression" $targets.reflectionTouchMaximumRegressionPercent
    New-StageMetric "entryExecuteP95" $baselineReport.durationsNanoseconds.entryExecute.p95 $candidateReport.durationsNanoseconds.entryExecute.p95 "maximum-regression" $targets.entryExecuteMaximumRegressionPercent
    New-StageMetric "entryResolveP95" $baselineEntryResolve.p95 $candidateEntryResolve.p95 "maximum-regression" $targets.entryResolveMaximumRegressionPercent
    New-StageMetric "reflectionTouchP95" $baselineReport.durationsNanoseconds.reflectionTouch.p95 $candidateReport.durationsNanoseconds.reflectionTouch.p95 "maximum-regression" $targets.reflectionTouchMaximumRegressionPercent
    New-StageMetric "reflectionTouchedPrivateBytes" $baselineReport.privateBytesDeltaFromBaseline.reflectionTouched.median $candidateReport.privateBytesDeltaFromBaseline.reflectionTouched.median "maximum-regression" $targets.reflectionTouchedPrivateBytesMaximumRegressionPercent
)
if ($hasAndroidPss) {
    $stageMetrics += @(
        New-StageMetric "loadOnlyAndroidPssBytes" $baselineReport.androidPssBytesDeltaFromBaseline.loadOnly.median $candidateReport.androidPssBytesDeltaFromBaseline.loadOnly.median "minimum-improvement" $targets.loadOnlyPrivateBytesMinimumImprovementPercent
        New-StageMetric "entryExecutedAndroidPssBytes" $baselineReport.androidPssBytesDeltaFromBaseline.entryExecuted.median $candidateReport.androidPssBytesDeltaFromBaseline.entryExecuted.median $entryMemoryTargetKind $entryMemoryTarget
        New-StageMetric "reflectionTouchedAndroidPssBytes" $baselineReport.androidPssBytesDeltaFromBaseline.reflectionTouched.median $candidateReport.androidPssBytesDeltaFromBaseline.reflectionTouched.median "maximum-regression" $targets.reflectionTouchedPrivateBytesMaximumRegressionPercent
    )
}
if ($p99Qualified) {
    $p99StageThreshold = [double]$policy.acceptance.maximumP99TimeRegressionPercent
    $stageMetrics += @(
        New-StageMetric "entryExecuteP99" $baselineEntryExecuteP99 $candidateEntryExecuteP99 "maximum-regression" $p99StageThreshold
        New-StageMetric "entryResolveP99" $baselineEntryResolveP99 $candidateEntryResolveP99 "maximum-regression" $p99StageThreshold
        New-StageMetric "reflectionTouchP99" $baselineReflectionP99 $candidateReflectionP99 "maximum-regression" $p99StageThreshold
    )
}
$stageFailures = @($stageMetrics | Where-Object { -not $_.passed })

$comparison = [ordered]@{
    schemaVersion = 1
    suiteId = "hybridclr-metadata-comparison-v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    metadataMode = $baselineReport.metadataMode
	policySha256 = $policyHash
	stageTargetSha256 = $stageTargetHash
	acceptance = $policy.acceptance
	stageTarget = [ordered]@{
		stage = $stageTargetPolicy.stage
		metrics = $stageMetrics
	}
    inputs = [ordered]@{
        baseline = [ordered]@{ summary = $Baseline; buildManifest = $BaselineBuildManifest; processCount = [int]$baselineReport.sourceResultCount }
        candidate = [ordered]@{ summary = $Candidate; buildManifest = $CandidateBuildManifest; processCount = [int]$candidateReport.sourceResultCount }
        stressAssembly = $baselineReport.stressAssembly
        metadataScenario = $baselineMetadataScenario
        reflectionContract = $baselineReflectionContract
        touchCounts = $baselineReport.touchCounts
    }
    time = $timeComparisons
    tail = [ordered]@{
        metric = "p99"
        minimumProcessCount = $p99MinimumProcessCount
        qualifiedForHardGate = $p99Qualified
        time = $tailTimeComparisons
    }
    memory = $memoryComparisons
    summary = [ordered]@{
		hardGatePassed = $regressions.Count -eq 0 -and $stageFailures.Count -eq 0
		stageTargetPassed = $stageFailures.Count -eq 0
		stageTargetFailures = @($stageFailures | ForEach-Object { $_.name })
        regressionCount = $regressions.Count
        improvementCount = $improvements.Count
        regressions = @($regressions | ForEach-Object { $_.name })
        improvements = @($improvements | ForEach-Object { $_.name })
    }
}

$outputPath = Resolve-LabPath $Output
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$comparison | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Metadata comparison: $outputPath"
Write-Host "Hard gate passed: $($comparison.summary.hardGatePassed); improvements: $($improvements.Count); regressions: $($regressions.Count)"
if (-not $DiagnosticOnly -and -not $comparison.summary.hardGatePassed) { exit 2 }
