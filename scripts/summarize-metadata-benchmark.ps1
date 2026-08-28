param(
    [Parameter(Mandatory = $true)]
    [string[]]$InputPath,
    [Parameter(Mandatory = $true)]
    [string]$BuildManifest,
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = "reports/metadata-benchmark-summary.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Get-Percentile([double[]]$Values, [double]$Percentile) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    if ($Percentile -eq 0.5 -and ($sorted.Count % 2) -eq 0) {
        $upper = [int]($sorted.Count / 2)
        return ([double]$sorted[$upper - 1] + [double]$sorted[$upper]) / 2.0
    }
    $index = [Math]::Max(0, [Math]::Ceiling($Percentile * $sorted.Count) - 1)
    return [double]$sorted[$index]
}

function Get-Statistics([double[]]$Values) {
    if ($Values.Count -eq 0) { return $null }
    $median = Get-Percentile $Values 0.5
    $deviations = [double[]]@($Values | ForEach-Object { [Math]::Abs([double]$_ - $median) })
    $mad = Get-Percentile $deviations 0.5
    $p95 = Get-Percentile $Values 0.95
    $p99 = Get-Percentile $Values 0.99
    return [ordered]@{
        minimum = [double](($Values | Measure-Object -Minimum).Minimum)
        median = $median
        p95 = $p95
        p99 = $p99
        maximum = [double](($Values | Measure-Object -Maximum).Maximum)
        medianAbsoluteDeviation = $mad
        relativeMadPercent = if ($median -eq 0) { 0.0 } else { 100.0 * $mad / $median }
        p95DeviationPercent = if ($median -eq 0) { 0.0 } else { 100.0 * ($p95 - $median) / $median }
        p99DeviationPercent = if ($median -eq 0) { 0.0 } else { 100.0 * ($p99 - $median) / $median }
    }
}

function Get-ReflectionContract([object]$Run) {
    $property = $Run.PSObject.Properties["reflectionContract"]
    if ($null -eq $property) {
        return [pscustomobject]@{ profile = "exhaustive"; requestedTypeCount = 0 }
    }
    $profile = [string]$Run.reflectionContract.profile
    $requestedTypeCount = [int]$Run.reflectionContract.requestedTypeCount
    if ($profile -notin @("exhaustive", "selective") -or
        ($profile -eq "exhaustive" -and $requestedTypeCount -ne 0) -or
        ($profile -eq "selective" -and $requestedTypeCount -lt 1)) {
        throw "Metadata benchmark has an invalid reflection contract."
    }
    return [pscustomobject]@{ profile = $profile; requestedTypeCount = $requestedTypeCount }
}

function Get-MetadataScenario([object]$Run) {
    $property = $Run.PSObject.Properties["metadataScenario"]
    if ($null -eq $property) { return "entry-first" }
    $scenario = [string]$Run.metadataScenario
    if ($scenario -notin @("entry-first", "reflection-first")) {
        throw "Metadata benchmark has an invalid metadata scenario."
    }
    return $scenario
}

function Get-MetadataWarmup([object]$Run) {
    $property = $Run.PSObject.Properties["metadataWarmup"]
    if ($null -eq $property) {
        return [pscustomobject]@{ mode = "none"; nanoseconds = 0.0; acrossFrames = $false; frameCount = 0; batchCount = 0; processingNanoseconds = 0.0; maxFrameNanoseconds = 0.0 }
    }
    $mode = [string]$Run.metadataWarmup.mode
    if ($mode -notin @("none", "entry", "entry-method", "entry-graph", "entry-method-graph")) {
        throw "Metadata benchmark has an invalid metadata warmup mode."
    }
    $nanoseconds = [double]$Run.metadataWarmup.nanoseconds
    if ($nanoseconds -lt 0) {
        throw "Metadata benchmark has a negative metadata warmup duration."
    }
    $acrossFrames = [bool]$Run.metadataWarmup.acrossFrames
    $frameCount = [int]$Run.metadataWarmup.frameCount
    $batchCount = [int]$Run.metadataWarmup.batchCount
    $processingNanoseconds = [double]$Run.metadataWarmup.processingNanoseconds
    $maxFrameNanoseconds = [double]$Run.metadataWarmup.maxFrameNanoseconds
    if ($frameCount -lt 0 -or $batchCount -lt 0 -or $processingNanoseconds -lt 0 -or $maxFrameNanoseconds -lt 0 -or
        ($mode -ne "none" -and ($frameCount -lt 1 -or $batchCount -lt 1))) {
        throw "Metadata benchmark has invalid warmup frame statistics."
    }
    return [pscustomobject]@{ mode = $mode; nanoseconds = $nanoseconds; acrossFrames = $acrossFrames; frameCount = $frameCount; batchCount = $batchCount; processingNanoseconds = $processingNanoseconds; maxFrameNanoseconds = $maxFrameNanoseconds }
}

function Get-DurationValue([object]$Run, [string]$Name) {
    $property = $Run.durationsNanoseconds.PSObject.Properties[$Name]
    if ($null -eq $property) { return 0.0 }
    return [double]$property.Value
}

$resolvedInputs = @($InputPath | ForEach-Object {
    $path = if ([IO.Path]::IsPathRooted($_)) { $_ } else { Join-Path $LabRoot $_ }
    Get-Item -LiteralPath ([IO.Path]::GetFullPath($path))
})
$runs = @($resolvedInputs | ForEach-Object { Get-Content -Raw $_.FullName | ConvertFrom-Json })
if ($runs.Count -eq 0) { throw "No metadata benchmark results were supplied." }
$first = $runs[0]
$firstReflectionContract = Get-ReflectionContract $first
$firstMetadataScenario = Get-MetadataScenario $first
$firstMetadataWarmup = Get-MetadataWarmup $first
foreach ($run in $runs) {
    foreach ($field in @("schemaVersion", "suiteId", "runner", "metadataMode", "runtime", "platform", "architecture")) {
        if ($run.$field -ne $first.$field) { throw "Metadata benchmark mismatch for '$field'." }
    }
    if ($run.buildIdentity.sha256 -ne $first.buildIdentity.sha256) {
        throw "Metadata benchmark mismatch for 'buildIdentity.sha256'."
    }
    $reflectionContract = Get-ReflectionContract $run
    if ((Get-MetadataScenario $run) -ne $firstMetadataScenario) {
        throw "Metadata benchmark mismatch for 'metadataScenario'."
    }
    $metadataWarmup = Get-MetadataWarmup $run
    if ($metadataWarmup.mode -ne $firstMetadataWarmup.mode) {
        throw "Metadata benchmark mismatch for 'metadataWarmup.mode'."
    }
    if ($metadataWarmup.acrossFrames -ne $firstMetadataWarmup.acrossFrames) {
        throw "Metadata benchmark mismatch for 'metadataWarmup.acrossFrames'."
    }
    if ($reflectionContract.profile -ne $firstReflectionContract.profile -or
        $reflectionContract.requestedTypeCount -ne $firstReflectionContract.requestedTypeCount) {
        throw "Metadata benchmark mismatch for 'reflectionContract'."
    }
    if ($run.stressAssembly.sha256 -ne $first.stressAssembly.sha256 -or
        $run.stressAssembly.bytes -ne $first.stressAssembly.bytes -or
        $run.touchCounts.types -ne $first.touchCounts.types -or
        $run.touchCounts.members -ne $first.touchCounts.members -or
        $run.touchCounts.attributes -ne $first.touchCounts.attributes -or
        $run.touchCounts.entryChecksum -ne $first.touchCounts.entryChecksum) {
        throw "Metadata stress input or touch contract mismatch."
    }
}

$durationSummary = [ordered]@{}
foreach ($name in @("aotMetadataLoad", "assemblyLoad", "entryResolve", "reflectionTouch", "entryExecute")) {
    $durationSummary[$name] = Get-Statistics ([double[]]@($runs | ForEach-Object { Get-DurationValue $_ $name }))
}
$metadataWarmupSummary = [ordered]@{
    mode = $firstMetadataWarmup.mode
    acrossFrames = $firstMetadataWarmup.acrossFrames
    nanoseconds = Get-Statistics ([double[]]@($runs | ForEach-Object { (Get-MetadataWarmup $_).nanoseconds }))
    frameCount = Get-Statistics ([double[]]@($runs | ForEach-Object { (Get-MetadataWarmup $_).frameCount }))
    batchCount = Get-Statistics ([double[]]@($runs | ForEach-Object { (Get-MetadataWarmup $_).batchCount }))
    processingNanoseconds = Get-Statistics ([double[]]@($runs | ForEach-Object { (Get-MetadataWarmup $_).processingNanoseconds }))
    maxFrameNanoseconds = Get-Statistics ([double[]]@($runs | ForEach-Object { (Get-MetadataWarmup $_).maxFrameNanoseconds }))
}

$snapshotNames = @($first.snapshots | ForEach-Object { $_.name })
$snapshotSummary = [ordered]@{}
foreach ($snapshotName in $snapshotNames) {
    $items = @($runs | ForEach-Object { $_.snapshots | Where-Object { $_.name -eq $snapshotName } })
    if ($items.Count -ne $runs.Count) { throw "Snapshot '$snapshotName' is missing from one or more runs." }
    $metricSummary = [ordered]@{}
    foreach ($metric in @("privateBytes", "workingSetBytes", "peakPrivateBytes", "peakWorkingSetBytes", "managedHeapBytes", "unityAllocatedBytes", "unityReservedBytes", "androidPssBytes")) {
        $values = [double[]]@($items | ForEach-Object { [double]$_.$metric } | Where-Object { $_ -ge 0 })
        $metricSummary[$metric] = Get-Statistics $values
    }
    $snapshotSummary[$snapshotName] = $metricSummary
}

$baselinePrivate = [double[]]@($runs | ForEach-Object { [double](($_.snapshots | Where-Object name -eq "baseline").privateBytes) })
$loadOnlyPrivate = [double[]]@()
$reflectionPrivate = [double[]]@()
$executePrivate = [double[]]@()
$aotPrivate = [double[]]@()
$hotUpdatePrivate = [double[]]@()
$reflectionIncrementPrivate = [double[]]@()
$executeIncrementPrivate = [double[]]@()
$warmupPrivate = [double[]]@()
$warmupIncrementPrivate = [double[]]@()
$aotUnityAllocated = [double[]]@()
$hotUpdateUnityAllocated = [double[]]@()
$reflectionUnityAllocated = [double[]]@()
$executeUnityAllocated = [double[]]@()
$warmupUnityAllocated = [double[]]@()
$warmupIncrementUnityAllocated = [double[]]@()
$aotUnityReserved = [double[]]@()
$hotUpdateUnityReserved = [double[]]@()
$loadOnlyAndroidPss = [double[]]@()
$reflectionAndroidPss = [double[]]@()
$executeAndroidPss = [double[]]@()
$aotAndroidPss = [double[]]@()
$hotUpdateAndroidPss = [double[]]@()
$reflectionIncrementAndroidPss = [double[]]@()
$executeIncrementAndroidPss = [double[]]@()
$warmupAndroidPss = [double[]]@()
$warmupIncrementAndroidPss = [double[]]@()
for ($index = 0; $index -lt $runs.Count; $index++) {
    $run = $runs[$index]
    $baseline = $baselinePrivate[$index]
    $baselineSnapshot = $run.snapshots | Where-Object name -eq "baseline"
    $aotSnapshot = $run.snapshots | Where-Object name -eq "aot-metadata-loaded"
    $loadSnapshot = $run.snapshots | Where-Object name -eq "hot-update-bytes-released"
    $warmupSnapshot = $run.snapshots | Where-Object name -eq "metadata-warmed"
    $reflectionSnapshot = $run.snapshots | Where-Object name -eq "reflection-touched"
    $executeSnapshot = $run.snapshots | Where-Object name -eq "entry-executed"
    $loadOnlyPrivate += [double]$loadSnapshot.privateBytes - $baseline
    $reflectionPrivate += [double]$reflectionSnapshot.privateBytes - $baseline
    $executePrivate += [double]$executeSnapshot.privateBytes - $baseline
    $aotPrivate += [double]$aotSnapshot.privateBytes - $baseline
    $hotUpdatePrivate += [double]$loadSnapshot.privateBytes - [double]$aotSnapshot.privateBytes
    if ($null -ne $warmupSnapshot) {
        $warmupPrivate += [double]$warmupSnapshot.privateBytes - $baseline
        $warmupIncrementPrivate += [double]$warmupSnapshot.privateBytes - [double]$loadSnapshot.privateBytes
    }
    if ($firstMetadataScenario -eq "entry-first") {
        $executeIncrementPrivate += [double]$executeSnapshot.privateBytes - [double]$loadSnapshot.privateBytes
        $reflectionIncrementPrivate += [double]$reflectionSnapshot.privateBytes - [double]$executeSnapshot.privateBytes
    } else {
        $reflectionIncrementPrivate += [double]$reflectionSnapshot.privateBytes - [double]$loadSnapshot.privateBytes
        $executeIncrementPrivate += [double]$executeSnapshot.privateBytes - [double]$reflectionSnapshot.privateBytes
    }
    $aotUnityAllocated += [double]$aotSnapshot.unityAllocatedBytes - [double]$baselineSnapshot.unityAllocatedBytes
    $hotUpdateUnityAllocated += [double]$loadSnapshot.unityAllocatedBytes - [double]$aotSnapshot.unityAllocatedBytes
    if ($null -ne $warmupSnapshot) {
        $warmupUnityAllocated += [double]$warmupSnapshot.unityAllocatedBytes - [double]$baselineSnapshot.unityAllocatedBytes
        $warmupIncrementUnityAllocated += [double]$warmupSnapshot.unityAllocatedBytes - [double]$loadSnapshot.unityAllocatedBytes
    }
    if ($firstMetadataScenario -eq "entry-first") {
        $executeUnityAllocated += [double]$executeSnapshot.unityAllocatedBytes - [double]$loadSnapshot.unityAllocatedBytes
        $reflectionUnityAllocated += [double]$reflectionSnapshot.unityAllocatedBytes - [double]$executeSnapshot.unityAllocatedBytes
    } else {
        $reflectionUnityAllocated += [double]$reflectionSnapshot.unityAllocatedBytes - [double]$loadSnapshot.unityAllocatedBytes
        $executeUnityAllocated += [double]$executeSnapshot.unityAllocatedBytes - [double]$reflectionSnapshot.unityAllocatedBytes
    }
    $aotUnityReserved += [double]$aotSnapshot.unityReservedBytes - [double]$baselineSnapshot.unityReservedBytes
    $hotUpdateUnityReserved += [double]$loadSnapshot.unityReservedBytes - [double]$aotSnapshot.unityReservedBytes
    if ([double]$baselineSnapshot.androidPssBytes -ge 0 -and
        [double]$aotSnapshot.androidPssBytes -ge 0 -and
        [double]$loadSnapshot.androidPssBytes -ge 0 -and
        [double]$executeSnapshot.androidPssBytes -ge 0 -and
        [double]$reflectionSnapshot.androidPssBytes -ge 0) {
        $loadOnlyAndroidPss += [double]$loadSnapshot.androidPssBytes - [double]$baselineSnapshot.androidPssBytes
        $executeAndroidPss += [double]$executeSnapshot.androidPssBytes - [double]$baselineSnapshot.androidPssBytes
        $reflectionAndroidPss += [double]$reflectionSnapshot.androidPssBytes - [double]$baselineSnapshot.androidPssBytes
        $aotAndroidPss += [double]$aotSnapshot.androidPssBytes - [double]$baselineSnapshot.androidPssBytes
        $hotUpdateAndroidPss += [double]$loadSnapshot.androidPssBytes - [double]$aotSnapshot.androidPssBytes
        if ($null -ne $warmupSnapshot) {
            $warmupAndroidPss += [double]$warmupSnapshot.androidPssBytes - [double]$baselineSnapshot.androidPssBytes
            $warmupIncrementAndroidPss += [double]$warmupSnapshot.androidPssBytes - [double]$loadSnapshot.androidPssBytes
        }
        if ($firstMetadataScenario -eq "entry-first") {
            $executeIncrementAndroidPss += [double]$executeSnapshot.androidPssBytes - [double]$loadSnapshot.androidPssBytes
            $reflectionIncrementAndroidPss += [double]$reflectionSnapshot.androidPssBytes - [double]$executeSnapshot.androidPssBytes
        } else {
            $reflectionIncrementAndroidPss += [double]$reflectionSnapshot.androidPssBytes - [double]$loadSnapshot.androidPssBytes
            $executeIncrementAndroidPss += [double]$executeSnapshot.androidPssBytes - [double]$reflectionSnapshot.androidPssBytes
        }
    }
}

$manifestPath = if ([IO.Path]::IsPathRooted($BuildManifest)) { $BuildManifest } else { Join-Path $LabRoot $BuildManifest }
$manifestPath = [IO.Path]::GetFullPath($manifestPath)
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Build manifest was not found: $manifestPath" }
$build = Get-Content -Raw $manifestPath | ConvertFrom-Json
if ($first.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
    $first.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
    $first.buildIdentity.managedAssemblySha256 -ne $build.managedAssemblySha256 -or
    $first.buildIdentity.hybridclrUnityTreeSha256 -ne $build.hybridclrUnityTreeSha256) {
    throw "Metadata benchmark build identity does not match the build manifest."
}
$policyPath = Join-Path $LabRoot "manifests/metadata-benchmark-policy.json"
$policySha256 = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
if ($build.metadataBenchmarkPolicySha256 -ne $policySha256) {
    throw "Metadata benchmark policy does not match the build manifest."
}
foreach ($field in @("playerSha256", "gameAssemblySha256")) {
    if ([string]::IsNullOrWhiteSpace([string]$build.$field)) {
        throw "Build manifest is missing required release identity '$field'."
    }
}
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json
$summary = [ordered]@{
    schemaVersion = 1
    suiteId = $first.suiteId
    metadataMode = $first.metadataMode
    metadataScenario = $firstMetadataScenario
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runtime = $first.runtime
    platform = $first.platform
    architecture = $first.architecture
    reflectionContract = $firstReflectionContract
    buildIdentity = $first.buildIdentity
    sourceResultCount = $runs.Count
    minimumIndependentProcesses = [int]$policy.minimumIndependentProcesses
    minimumProcessCountMet = $runs.Count -ge [int]$policy.minimumIndependentProcesses
    buildManifestSha256 = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    policySha256 = $policySha256
    stagedRuntimeSha256 = $build.stagedRuntimeSha256
    playerSha256 = $build.playerSha256
    gameAssemblySha256 = $build.gameAssemblySha256
    metadataStressAssemblySha256 = $build.metadataStressAssemblySha256
    metadataStressPrewarmManifestSha256 = $build.metadataStressPrewarmManifestSha256
    stressAssembly = $first.stressAssembly
    aotMetadata = $first.aotMetadata
    metadataWarmup = $metadataWarmupSummary
    touchCounts = $first.touchCounts
    durationsNanoseconds = $durationSummary
    snapshots = $snapshotSummary
    privateBytesDeltaFromBaseline = [ordered]@{
        loadOnly = Get-Statistics $loadOnlyPrivate
        entryExecuted = Get-Statistics $executePrivate
        reflectionTouched = Get-Statistics $reflectionPrivate
        metadataWarmed = Get-Statistics $warmupPrivate
    }
    androidPssBytesDeltaFromBaseline = [ordered]@{
        loadOnly = Get-Statistics $loadOnlyAndroidPss
        entryExecuted = Get-Statistics $executeAndroidPss
        reflectionTouched = Get-Statistics $reflectionAndroidPss
        metadataWarmed = Get-Statistics $warmupAndroidPss
    }
    phaseDeltas = [ordered]@{
        privateBytes = [ordered]@{
            aotMetadata = Get-Statistics $aotPrivate
            hotUpdateLoad = Get-Statistics $hotUpdatePrivate
            metadataWarmup = Get-Statistics $warmupIncrementPrivate
            entryExecute = Get-Statistics $executeIncrementPrivate
            reflectionTouch = Get-Statistics $reflectionIncrementPrivate
            hotUpdateLoadMedianPerDllByte = (Get-Percentile $hotUpdatePrivate 0.5) / [double]$first.stressAssembly.bytes
            aotMetadataMedianPerPayloadByte = if ([double]$first.aotMetadata.totalBytes -gt 0) { (Get-Percentile $aotPrivate 0.5) / [double]$first.aotMetadata.totalBytes } else { 0.0 }
        }
        unityAllocatedBytes = [ordered]@{
            aotMetadata = Get-Statistics $aotUnityAllocated
            hotUpdateLoad = Get-Statistics $hotUpdateUnityAllocated
            metadataWarmup = Get-Statistics $warmupIncrementUnityAllocated
            entryExecute = Get-Statistics $executeUnityAllocated
            reflectionTouch = Get-Statistics $reflectionUnityAllocated
            hotUpdateLoadMedianPerDllByte = (Get-Percentile $hotUpdateUnityAllocated 0.5) / [double]$first.stressAssembly.bytes
            aotMetadataMedianPerPayloadByte = if ([double]$first.aotMetadata.totalBytes -gt 0) { (Get-Percentile $aotUnityAllocated 0.5) / [double]$first.aotMetadata.totalBytes } else { 0.0 }
        }
        unityReservedBytes = [ordered]@{
            aotMetadata = Get-Statistics $aotUnityReserved
            hotUpdateLoad = Get-Statistics $hotUpdateUnityReserved
        }
        androidPssBytes = [ordered]@{
            aotMetadata = Get-Statistics $aotAndroidPss
            hotUpdateLoad = Get-Statistics $hotUpdateAndroidPss
            metadataWarmup = Get-Statistics $warmupIncrementAndroidPss
            entryExecute = Get-Statistics $executeIncrementAndroidPss
            reflectionTouch = Get-Statistics $reflectionIncrementAndroidPss
            hotUpdateLoadMedianPerDllByte = if ($hotUpdateAndroidPss.Count -gt 0) { (Get-Percentile $hotUpdateAndroidPss 0.5) / [double]$first.stressAssembly.bytes } else { $null }
            aotMetadataMedianPerPayloadByte = if ($aotAndroidPss.Count -gt 0 -and [double]$first.aotMetadata.totalBytes -gt 0) { (Get-Percentile $aotAndroidPss 0.5) / [double]$first.aotMetadata.totalBytes } else { $null }
        }
    }
}

$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $LabRoot $Output }
$outputPath = [IO.Path]::GetFullPath($outputPath)
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$summary | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Metadata benchmark summary: $outputPath"
Write-Host "Processes: $($runs.Count), minimum met: $($summary.minimumProcessCountMet)"
