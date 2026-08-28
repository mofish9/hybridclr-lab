param(
    [Parameter(Mandatory = $true)]
    [string[]]$InputPath,
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = "reports/benchmark-summary.json",
    [string]$BuildManifest = ""
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$policyPath = Join-Path $LabRoot "manifests/benchmark-policy.json"
$policySha256 = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    if ($Values.Count -eq 0) { throw "Cannot calculate a percentile for an empty sample." }
    $sorted = @($Values | Sort-Object)
    if ($Percentile -eq 0.5 -and ($sorted.Count % 2) -eq 0) {
        $upper = [int]($sorted.Count / 2)
        return ([double]$sorted[$upper - 1] + [double]$sorted[$upper]) / 2.0
    }
    $index = [Math]::Max(0, [Math]::Ceiling($Percentile * $sorted.Count) - 1)
    return [double]$sorted[$index]
}

function Get-Statistics([double[]]$Values) {
    $minimum = [double]($Values | Measure-Object -Minimum).Minimum
    $maximum = [double]($Values | Measure-Object -Maximum).Maximum
    $median = Get-Percentile $Values 0.5
    $p95 = Get-Percentile $Values 0.95
    $deviations = @($Values | ForEach-Object { [Math]::Abs([double]$_ - $median) })
    $mad = Get-Percentile $deviations 0.5
    $relativeMad = if ($median -eq 0) { 0.0 } else { $mad * 100.0 / $median }
    $p95Deviation = if ($median -eq 0) { 0.0 } else { [Math]::Max(0.0, ($p95 - $median) * 100.0 / $median) }
    return [ordered]@{
        minimum = $minimum
        median = $median
        p95 = $p95
        maximum = $maximum
        medianAbsoluteDeviation = $mad
        relativeMadPercent = $relativeMad
        p95DeviationPercent = $p95Deviation
    }
}

$resolvedInputs = @($InputPath | ForEach-Object {
    $path = if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $LabRoot $_ }
    Get-Item -LiteralPath ([IO.Path]::GetFullPath($path))
})
if ($resolvedInputs.Count -eq 0) { throw "No benchmark result files were supplied." }
$runs = @($resolvedInputs | ForEach-Object { Get-Content -Raw $_.FullName | ConvertFrom-Json })
$first = $runs[0]
$hasBuildIdentity = $null -ne $first.PSObject.Properties["buildIdentity"]
foreach ($run in $runs) {
    foreach ($field in @("schemaVersion", "suiteId", "benchmarkMode", "runner", "aotMetadataMode", "runtime", "platform", "architecture", "policySha256", "managedAssemblySha256")) {
        if ($run.$field -ne $first.$field) { throw "Benchmark result mismatch for '$field'." }
    }
    if ($run.schemaVersion -ne 1) { throw "Unsupported benchmark result schema: $($run.schemaVersion)" }
    if (($null -ne $run.PSObject.Properties["buildIdentity"]) -ne $hasBuildIdentity) {
        throw "Benchmark results mix runs with and without a build identity."
    }
    if ($hasBuildIdentity -and $run.buildIdentity.sha256 -ne $first.buildIdentity.sha256) {
        throw "Benchmark result mismatch for 'buildIdentity.sha256'."
    }
}

$workloadIds = @($runs | ForEach-Object { $_.workloads | ForEach-Object { $_.id } } | Sort-Object -Unique)
$workloadSummaries = @()
$allRelativeMad = @()
$allP95Deviation = @()
$minimumProcessCountMet = $true
foreach ($id in $workloadIds) {
    $samples = @()
    $definitions = @()
    foreach ($run in $runs) {
        $workload = @($run.workloads | Where-Object { $_.id -eq $id })
        if ($workload.Count -gt 1) { throw "Duplicate workload '$id' in one process result." }
        if ($workload.Count -eq 0) { continue }
        $item = $workload[0]
        $definitions += $item
        $values = [double[]]@($item.nanosecondsPerIteration)
        $sampleStats = Get-Statistics $values
        $samples += [ordered]@{
            startedAtUtc = $run.startedAtUtc
            processId = [int]$run.processId
            repetitions = if ($null -ne $item.repetitions) { [int]$item.repetitions } else { 1 }
            minimumNanosecondsPerIteration = $sampleStats.minimum
            medianNanosecondsPerIteration = $sampleStats.median
            p95NanosecondsPerIteration = $sampleStats.p95
            maximumNanosecondsPerIteration = $sampleStats.maximum
            batchNanoseconds = @($item.batchNanoseconds)
            nanosecondsPerIteration = @($item.nanosecondsPerIteration)
            gcCollections = [ordered]@{
                generation0 = [int]$item.gcCollections.generation0
                generation1 = [int]$item.gcCollections.generation1
                generation2 = [int]$item.gcCollections.generation2
            }
        }
    }

    $definition = $definitions[0]
    foreach ($candidate in $definitions) {
        if ($candidate.category -ne $definition.category -or
            $candidate.iterations -ne $definition.iterations -or
            $candidate.checksum -ne $definition.checksum -or
            (@($candidate.features) -join "`n") -ne (@($definition.features) -join "`n")) {
            throw "Workload contract mismatch for '$id'."
        }
    }
    if ($samples.Count -lt $policy.minimumIndependentProcesses) { $minimumProcessCountMet = $false }
    $processMedians = [double[]]@($samples | ForEach-Object { $_.medianNanosecondsPerIteration })
    $aggregate = Get-Statistics $processMedians
    $allRelativeMad += $aggregate.relativeMadPercent
    $allP95Deviation += $aggregate.p95DeviationPercent
    $workloadSummaries += [ordered]@{
        id = $id
        category = $definition.category
        features = @($definition.features)
        iterations = [int]$definition.iterations
        checksum = [string]$definition.checksum
        processCount = $samples.Count
        processSamples = $samples
        aggregate = $aggregate
        gcCollectionsTotal = [ordered]@{
			generation0 = [int](($samples | ForEach-Object { $_.gcCollections.generation0 } | Measure-Object -Sum).Sum)
			generation1 = [int](($samples | ForEach-Object { $_.gcCollections.generation1 } | Measure-Object -Sum).Sum)
			generation2 = [int](($samples | ForEach-Object { $_.gcCollections.generation2 } | Measure-Object -Sum).Sum)
        }
    }
}

function Get-StartupStatistics([string]$Property) {
    return Get-Statistics ([double[]]@($runs | ForEach-Object { $_.startup.$Property }))
}

$buildManifestSha256 = $null
$buildManifestData = $null
if (-not [string]::IsNullOrWhiteSpace($BuildManifest)) {
    $buildManifestPath = if ([IO.Path]::IsPathRooted($BuildManifest)) { $BuildManifest } else { Join-Path $LabRoot $BuildManifest }
    $buildManifestPath = [IO.Path]::GetFullPath($buildManifestPath)
    if (-not (Test-Path $buildManifestPath)) { throw "Build manifest was not found: $buildManifestPath" }
    $buildManifestSha256 = (Get-FileHash -LiteralPath $buildManifestPath -Algorithm SHA256).Hash
    $buildManifestData = Get-Content -Raw $buildManifestPath | ConvertFrom-Json
    if (-not $hasBuildIdentity) { throw "Player benchmark results must contain a build identity." }
    if ($first.buildIdentity.sha256 -ne $buildManifestData.buildIdentitySha256) {
        throw "Benchmark build identity does not match the build manifest."
    }
    if ($first.buildIdentity.stagedRuntimeSha256 -ne $buildManifestData.stagedRuntimeSha256 -or
        $first.buildIdentity.managedAssemblySha256 -ne $buildManifestData.managedAssemblySha256 -or
        $first.buildIdentity.hybridclrUnityTreeSha256 -ne $buildManifestData.hybridclrUnityTreeSha256) {
        throw "Benchmark runtime or managed assembly identity does not match the build manifest."
    }
    if ($first.policySha256 -ne $policySha256 -or $buildManifestData.benchmarkPolicySha256 -ne $policySha256) {
        throw "Benchmark policy identity does not match the current policy and build manifest."
    }
    foreach ($field in @("playerSha256", "gameAssemblySha256")) {
        if ([string]::IsNullOrWhiteSpace([string]$buildManifestData.$field)) {
            throw "Build manifest is missing required release identity '$field'."
        }
    }
    if ($null -ne $buildManifestData.PSObject.Properties["benchmarkGoldenSha256"] -and
        $first.goldenContractSha256 -ne $buildManifestData.benchmarkGoldenSha256) {
        throw "Benchmark golden contract does not match the build manifest."
    }
}
$maxRelativeMad = [double](($allRelativeMad | Measure-Object -Maximum).Maximum)
$maxP95Deviation = [double](($allP95Deviation | Measure-Object -Maximum).Maximum)
$recommendedThreshold = [Math]::Ceiling([Math]::Max(5.0, [Math]::Max(3.0 * $maxRelativeMad, $maxP95Deviation)))
$summary = [ordered]@{
    schemaVersion = 1
    suiteId = $first.suiteId
    benchmarkMode = $first.benchmarkMode
    runner = $first.runner
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runtime = $first.runtime
    platform = $first.platform
    architecture = $first.architecture
    policySha256 = $first.policySha256
    managedAssemblySha256 = $first.managedAssemblySha256
    buildManifestSha256 = $buildManifestSha256
    stagedRuntimeSha256 = if ($hasBuildIdentity) { $first.buildIdentity.stagedRuntimeSha256 } else { $null }
    playerSha256 = if ($null -ne $buildManifestData) { $buildManifestData.playerSha256 } else { $null }
    gameAssemblySha256 = if ($null -ne $buildManifestData) { $buildManifestData.gameAssemblySha256 } else { $null }
    benchmarkGoldenSha256 = if ($null -ne $buildManifestData) { $buildManifestData.benchmarkGoldenSha256 } else { $null }
    sourceResultCount = $runs.Count
    minimumIndependentProcesses = [int]$policy.minimumIndependentProcesses
    minimumProcessCountMet = $minimumProcessCountMet
    startup = [ordered]@{
        aotMetadataLoadNanoseconds = Get-StartupStatistics "aotMetadataLoadNanoseconds"
        hotUpdateAssemblyLoadNanoseconds = Get-StartupStatistics "hotUpdateAssemblyLoadNanoseconds"
        workloadDiscoveryNanoseconds = Get-StartupStatistics "workloadDiscoveryNanoseconds"
    }
    workloads = $workloadSummaries
    calibration = [ordered]@{
        maximumRelativeMadPercent = $maxRelativeMad
        maximumP95DeviationPercent = $maxP95Deviation
        recommendedNoiseThresholdPercent = $recommendedThreshold
    }
}
if ($hasBuildIdentity) {
    $summary["buildIdentity"] = $first.buildIdentity
}
if ($null -ne $first.PSObject.Properties["aotMetadataMode"]) {
    $summary["aotMetadataMode"] = $first.aotMetadataMode
}

$outputPath = if ([IO.Path]::IsPathRooted($Output)) {
    [IO.Path]::GetFullPath($Output)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $Output))
}
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Benchmark summary: $outputPath"
Write-Host "Minimum process count met: $minimumProcessCountMet"
Write-Host "Recommended noise threshold: $recommendedThreshold%"
