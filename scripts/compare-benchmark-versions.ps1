param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)]
    [string]$PreviousSteady,
    [Parameter(Mandatory = $true)]
    [string]$CurrentSteady,
    [Parameter(Mandatory = $true)]
    [string]$PreviousCold,
    [Parameter(Mandatory = $true)]
    [string]$CurrentCold,
    [Parameter(Mandatory = $true)]
    [string]$PreviousBuildManifest,
    [Parameter(Mandatory = $true)]
    [string]$CurrentBuildManifest,
    [string]$PreviousPlayerResult = "",
    [string]$CurrentPlayerResult = "",
    [string]$PreviousDifferentialResult = "",
    [string]$CurrentDifferentialResult = "",
    [string]$Output = "reports/benchmark-version-comparison.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Read-Report([string]$Path) {
    $resolved = Resolve-LabPath $Path
    if (-not (Test-Path -LiteralPath $resolved)) {
        throw "Report was not found: $resolved"
    }
    return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
}

function Get-GeometricMean([double[]]$Values) {
    if ($Values.Count -eq 0) { return 0.0 }
    foreach ($value in $Values) {
        if ($value -le 0) { throw "Geometric mean inputs must be positive." }
    }
    $averageLog = ($Values | ForEach-Object { [Math]::Log($_) } | Measure-Object -Average).Average
    return [Math]::Exp($averageLog)
}

function New-GroupSummary([string]$Name, [object[]]$Rows) {
    $speedup = Get-GeometricMean ([double[]]@($Rows | ForEach-Object { $_.currentVsPreviousSpeedup }))
    return [ordered]@{
        id = $Name
        workloadCount = $Rows.Count
        currentVsPreviousSpeedupGeometricMean = $speedup
        currentTimeReductionPercentGeometricMean = (1.0 - 1.0 / $speedup) * 100.0
        currentFasterCount = @($Rows | Where-Object { $_.verdict -eq "current-faster" }).Count
        previousFasterCount = @($Rows | Where-Object { $_.verdict -eq "previous-faster" }).Count
        withinNoiseCount = @($Rows | Where-Object { $_.verdict -eq "within-noise" }).Count
    }
}

function Compare-Startup($Previous, $Current) {
    $rows = @()
    foreach ($property in @("aotMetadataLoadNanoseconds", "hotUpdateAssemblyLoadNanoseconds", "workloadDiscoveryNanoseconds")) {
        $previousMedian = [double]$Previous.startup.$property.median
        $currentMedian = [double]$Current.startup.$property.median
        $rows += [ordered]@{
            id = $property
            previousMedianNanoseconds = $previousMedian
            currentMedianNanoseconds = $currentMedian
            currentVsPreviousSpeedup = $previousMedian / $currentMedian
            currentTimeReductionPercent = (1.0 - $currentMedian / $previousMedian) * 100.0
        }
    }
    return $rows
}

function Compare-Mode($Previous, $Current) {
    foreach ($field in @("schemaVersion", "suiteId", "benchmarkMode", "runner", "runtime", "platform", "architecture", "policySha256", "managedAssemblySha256", "aotMetadataMode")) {
        if ($Previous.$field -ne $Current.$field) {
            throw "Benchmark report mismatch for '$field': '$($Previous.$field)' versus '$($Current.$field)'."
        }
    }

    $previousIds = @($Previous.workloads | ForEach-Object { $_.id } | Sort-Object)
    $currentIds = @($Current.workloads | ForEach-Object { $_.id } | Sort-Object)
    if (($previousIds -join "`n") -ne ($currentIds -join "`n")) {
        throw "Benchmark workload sets do not match."
    }

    $rows = @()
    foreach ($previousWorkload in $Previous.workloads) {
        $matches = @($Current.workloads | Where-Object { $_.id -eq $previousWorkload.id })
        if ($matches.Count -ne 1) {
            throw "Current report does not contain exactly one '$($previousWorkload.id)' workload."
        }
        $currentWorkload = $matches[0]
        if ($previousWorkload.category -ne $currentWorkload.category -or
            $previousWorkload.iterations -ne $currentWorkload.iterations -or
            $previousWorkload.checksum -ne $currentWorkload.checksum -or
            (@($previousWorkload.features) -join "`n") -ne (@($currentWorkload.features) -join "`n")) {
            throw "Workload contract mismatch for '$($previousWorkload.id)'."
        }

        $previousMedian = [double]$previousWorkload.aggregate.median
        $currentMedian = [double]$currentWorkload.aggregate.median
        if ($previousMedian -le 0 -or $currentMedian -le 0) {
            throw "Workload '$($previousWorkload.id)' has a non-positive median."
        }
        $timeReductionPercent = (1.0 - $currentMedian / $previousMedian) * 100.0
        $noiseThreshold = [Math]::Max(5.0, [Math]::Max(
            3.0 * [Math]::Max(
                [double]$previousWorkload.aggregate.relativeMadPercent,
                [double]$currentWorkload.aggregate.relativeMadPercent),
            [Math]::Max(
                [double]$previousWorkload.aggregate.p95DeviationPercent,
                [double]$currentWorkload.aggregate.p95DeviationPercent)))
        $verdict = if ($timeReductionPercent -gt $noiseThreshold) {
            "current-faster"
        } elseif ($timeReductionPercent -lt -$noiseThreshold) {
            "previous-faster"
        } else {
            "within-noise"
        }
        $rows += [ordered]@{
            id = $previousWorkload.id
            category = $previousWorkload.category
            features = @($previousWorkload.features)
            iterations = [int]$previousWorkload.iterations
            checksum = [string]$previousWorkload.checksum
            previousMedianNanosecondsPerIteration = $previousMedian
            currentMedianNanosecondsPerIteration = $currentMedian
            currentVsPreviousSpeedup = $previousMedian / $currentMedian
            currentTimeReductionPercent = $timeReductionPercent
            noiseThresholdPercent = $noiseThreshold
            verdict = $verdict
            previousProcessCount = [int]$previousWorkload.processCount
            currentProcessCount = [int]$currentWorkload.processCount
            previousRelativeMadPercent = [double]$previousWorkload.aggregate.relativeMadPercent
            currentRelativeMadPercent = [double]$currentWorkload.aggregate.relativeMadPercent
        }
    }

    $groups = @()
    $groups += New-GroupSummary "all-workloads" @($rows)
    $groups += New-GroupSummary "non-fgs" @($rows | Where-Object { $_.category -ne "full-generic-sharing" })
    $groups += New-GroupSummary "interpreter-category" @($rows | Where-Object { $_.category -eq "interpreter" })
    $groups += New-GroupSummary "core-interpreter-excluding-exception" @($rows | Where-Object {
        $_.category -eq "interpreter" -and $_.id -ne "interp_exception"
    })
    foreach ($category in @($rows | ForEach-Object { $_.category } | Sort-Object -Unique)) {
        $groups += New-GroupSummary "category:$category" @($rows | Where-Object { $_.category -eq $category })
    }

    return [ordered]@{
        mode = $Previous.benchmarkMode
        contractValidation = [ordered]@{
            matched = $true
            workloadCount = $rows.Count
            suiteId = $Previous.suiteId
            policySha256 = $Previous.policySha256
            managedAssemblySha256 = $Previous.managedAssemblySha256
        }
        sampling = [ordered]@{
            previousSourceResultCount = [int]$Previous.sourceResultCount
            currentSourceResultCount = [int]$Current.sourceResultCount
            requiredIndependentProcesses = [int]$Previous.minimumIndependentProcesses
            previousMinimumProcessCountMet = [bool]$Previous.minimumProcessCountMet
            currentMinimumProcessCountMet = [bool]$Current.minimumProcessCountMet
            status = if ($Previous.minimumProcessCountMet -and $Current.minimumProcessCountMet) { "policy-complete" } else { "exploratory" }
        }
        startup = Compare-Startup $Previous $Current
        groups = $groups
        largestImprovements = @($rows | Sort-Object currentTimeReductionPercent -Descending | Select-Object -First 5)
        largestRegressions = @($rows | Sort-Object currentTimeReductionPercent | Select-Object -First 5)
        workloads = $rows
    }
}

function Get-BuildSummary($Build) {
    return [ordered]@{
        profile = $Build.profile
        createdAtUtc = $Build.createdAtUtc
        hybridclr = $Build.repositories.hybridclr
        il2cpp_plus = $Build.repositories.il2cpp_plus
        engine = $Build.engine
        target = $Build.target
        architecture = "x64"
        configuration = $Build.configuration
        il2cppCodeGeneration = $Build.il2cppCodeGeneration
        aotMetadataPackaging = $Build.aotMetadataPackaging
        fullGenericSharingDiagnostics = [bool]$Build.fullGenericSharingDiagnostics
        buildIdentitySha256 = $Build.buildIdentitySha256
        stagedRuntimeSha256 = $Build.stagedRuntimeSha256
        managedAssemblySha256 = $Build.managedAssemblySha256
        performanceWorkloadSourceSha256 = $Build.performanceWorkloadSourceSha256
        benchmarkPolicySha256 = $Build.benchmarkPolicySha256
        benchmarkGoldenSha256 = $Build.benchmarkGoldenSha256
        playerSize = $Build.playerSize
    }
}

function Compare-PlayerSize($Previous, $Current) {
    $rows = @()
    foreach ($property in @("totalFilesBytes", "executableBytes", "gameAssemblyBytes", "dataDirectoryBytes", "supplementalAotMetadataBytes")) {
        $previousBytes = [long]$Previous.playerSize.$property
        $currentBytes = [long]$Current.playerSize.$property
        $rows += [ordered]@{
            id = $property
            previousBytes = $previousBytes
            currentBytes = $currentBytes
            deltaBytes = $currentBytes - $previousBytes
            deltaPercent = if ($previousBytes -eq 0) { 0.0 } else { ($currentBytes - $previousBytes) * 100.0 / $previousBytes }
        }
    }
    return $rows
}

function Get-CorrectnessSummary([string]$PlayerPath, [string]$DifferentialPath) {
    if ([string]::IsNullOrWhiteSpace($PlayerPath) -or [string]::IsNullOrWhiteSpace($DifferentialPath)) {
        return $null
    }
    $player = Read-Report $PlayerPath
    $differential = Read-Report $DifferentialPath
    return [ordered]@{
        total = [int]$player.summary.total
        passed = [int]$player.summary.passed
        failed = [int]$player.summary.failed
        differences = [int]$differential.summary.differences
        differentialPassed = [bool]$differential.summary.passed
    }
}

$previousSteadyReport = Read-Report $PreviousSteady
$currentSteadyReport = Read-Report $CurrentSteady
$previousColdReport = Read-Report $PreviousCold
$currentColdReport = Read-Report $CurrentCold
$previousBuild = Read-Report $PreviousBuildManifest
$currentBuild = Read-Report $CurrentBuildManifest

foreach ($field in @("engine", "target", "configuration", "il2cppCodeGeneration", "aotMetadataPackaging", "managedAssemblySha256", "performanceWorkloadSourceSha256", "benchmarkPolicySha256", "benchmarkGoldenSha256")) {
    $previousValue = if ($field -eq "engine") { $previousBuild.engine.compatibilityVersion } else { $previousBuild.$field }
    $currentValue = if ($field -eq "engine") { $currentBuild.engine.compatibilityVersion } else { $currentBuild.$field }
    if ($previousValue -ne $currentValue) {
        throw "Build manifest mismatch for '$field': '$previousValue' versus '$currentValue'."
    }
}

$comparison = [ordered]@{
    schemaVersion = 1
    suiteId = "hybridclr-version-performance-comparison-v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    direction = [ordered]@{
        baseline = "previous"
        candidate = "current"
        speedup = "previous median / current median; values above 1 mean current is faster"
        timeReductionPercent = "(1 - current median / previous median) * 100; positive values mean current is faster"
    }
    scope = [ordered]@{
        primary = "non-FGS workloads"
        fgsCategoryTreatment = "Executed for suite completeness and reported separately; excluded from the primary non-FGS aggregate."
    }
    previous = Get-BuildSummary $previousBuild
    current = Get-BuildSummary $currentBuild
    contractParity = [ordered]@{
        engine = $previousBuild.engine.compatibilityVersion
        managedAssemblySha256 = $previousBuild.managedAssemblySha256
        performanceWorkloadSourceSha256 = $previousBuild.performanceWorkloadSourceSha256
        benchmarkPolicySha256 = $previousBuild.benchmarkPolicySha256
        benchmarkGoldenSha256 = $previousBuild.benchmarkGoldenSha256
        fullGenericSharingDiagnosticsDisabled = (-not $previousBuild.fullGenericSharingDiagnostics -and -not $currentBuild.fullGenericSharingDiagnostics)
    }
    correctness = [ordered]@{
        previous = Get-CorrectnessSummary $PreviousPlayerResult $PreviousDifferentialResult
        current = Get-CorrectnessSummary $CurrentPlayerResult $CurrentDifferentialResult
    }
    playerSize = Compare-PlayerSize $previousBuild $currentBuild
    steady = Compare-Mode $previousSteadyReport $currentSteadyReport
    cold = Compare-Mode $previousColdReport $currentColdReport
    interpretation = [ordered]@{
        primaryMetric = "steady median nanoseconds per logical iteration"
        aggregateMethod = "geometric mean of per-workload current-vs-previous speedups"
        coldMetric = "first-use batch normalized by workload-specific cold iteration count"
        samplingNote = "Three steady processes and one cold process per workload are an exploratory comparison below the policy minimum of ten independent processes."
    }
}

$outputPath = Resolve-LabPath $Output
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$comparison | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Benchmark version comparison: $outputPath"
$primary = @($comparison.steady.groups | Where-Object { $_.id -eq "non-fgs" })[0]
Write-Host ("Steady non-FGS geometric mean: {0:N3}x, {1:N2}% time reduction" -f
    $primary.currentVsPreviousSpeedupGeometricMean, $primary.currentTimeReductionPercentGeometricMean)
