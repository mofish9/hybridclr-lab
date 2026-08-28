param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)]
    [string]$RawDirectory,
    [string]$Output = "reports/metadata-paired-diagnostics.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Get-Median([double[]]$Values) {
    if ($Values.Count -eq 0) { throw "Cannot calculate a median for an empty set." }
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if (($sorted.Count % 2) -eq 1) { return [double]$sorted[$middle] }
    return ([double]$sorted[$middle - 1] + [double]$sorted[$middle]) / 2.0
}

function Get-Snapshot([object]$Run, [string]$Name) {
    $snapshot = @($Run.snapshots | Where-Object name -eq $Name)
    if ($snapshot.Count -ne 1) { throw "Run $($Run.processId) does not contain exactly one '$Name' snapshot." }
    return $snapshot[0]
}

function Get-TimeValue([object]$Run, [string]$Name) {
    switch ($Name) {
        "loadAndEntry" {
            $value = (Get-TimeValue $Run "assemblyLoad") + (Get-TimeValue $Run "entryResolve") + (Get-TimeValue $Run "entryExecute")
            if ((Get-MetadataScenario $Run) -eq "reflection-first") {
                $value += Get-TimeValue $Run "reflectionTouch"
            }
            return $value
        }
        "loadAndReflection" {
            return (Get-TimeValue $Run "assemblyLoad") + (Get-TimeValue $Run "reflectionTouch")
        }
        "throughReflection" {
            if ((Get-MetadataScenario $Run) -eq "reflection-first") {
                return Get-TimeValue $Run "loadAndReflection"
            }
            return (Get-TimeValue $Run "loadAndEntry") + (Get-TimeValue $Run "reflectionTouch")
        }
        default {
            $property = $Run.durationsNanoseconds.PSObject.Properties[$Name]
            if ($null -eq $property) { return 0.0 }
            return [double]$property.Value
        }
    }
}

function Get-MemoryValue([object]$Run, [string]$SnapshotName, [string]$Metric = "privateBytes") {
    $baseline = Get-Snapshot $Run "baseline"
    $snapshot = Get-Snapshot $Run $SnapshotName
    return [double]$snapshot.$Metric - [double]$baseline.$Metric
}

function Has-AndroidPss([object[]]$Runs) {
    foreach ($run in $Runs) {
        foreach ($snapshot in $run.snapshots) {
            if ([double]$snapshot.androidPssBytes -lt 0) { return $false }
        }
    }
    return $true
}

function Assert-Equal([string]$Label, [object]$Expected, [object]$Actual) {
    if ([string]$Expected -cne [string]$Actual) {
        throw "$Label mismatch: expected '$Expected', actual '$Actual'."
    }
}

function Get-ReflectionContract([object]$Run) {
    if ($null -eq $Run.PSObject.Properties["reflectionContract"]) {
        return [pscustomobject]@{ profile = "exhaustive"; requestedTypeCount = 0 }
    }
    return [pscustomobject]@{
        profile = [string]$Run.reflectionContract.profile
        requestedTypeCount = [int]$Run.reflectionContract.requestedTypeCount
    }
}

function Get-MetadataScenario([object]$Run) {
    if ($null -eq $Run.PSObject.Properties["metadataScenario"]) { return "entry-first" }
    return [string]$Run.metadataScenario
}

function Assert-SideIdentity([string]$Side, [object]$Reference, [object]$Run) {
    foreach ($field in @("suiteId", "runner", "metadataMode", "runtime", "platform", "architecture")) {
        Assert-Equal "$Side $field" $Reference.$field $Run.$field
    }
    Assert-Equal "$Side build identity" $Reference.buildIdentity.sha256 $Run.buildIdentity.sha256
    Assert-Equal "$Side metadata scenario" (Get-MetadataScenario $Reference) (Get-MetadataScenario $Run)
    Assert-Equal "$Side stress assembly" $Reference.stressAssembly.sha256 $Run.stressAssembly.sha256
    $referenceReflection = Get-ReflectionContract $Reference
    $runReflection = Get-ReflectionContract $Run
    Assert-Equal "$Side reflection profile" $referenceReflection.profile $runReflection.profile
    Assert-Equal "$Side reflection type limit" $referenceReflection.requestedTypeCount $runReflection.requestedTypeCount
}

function Assert-PairContract([object]$Baseline, [object]$Candidate, [string]$PairName) {
    foreach ($field in @("suiteId", "runner", "metadataMode", "runtime", "platform", "architecture")) {
        Assert-Equal "Pair '$PairName' $field" $Baseline.$field $Candidate.$field
    }
    foreach ($field in @("target", "architecture", "il2cppCodeGeneration", "aotMetadataPackaging")) {
        Assert-Equal "Pair '$PairName' buildIdentity.$field" $Baseline.buildIdentity.$field $Candidate.buildIdentity.$field
    }
    foreach ($field in @("name", "bytes", "sha256")) {
        Assert-Equal "Pair '$PairName' stressAssembly.$field" $Baseline.stressAssembly.$field $Candidate.stressAssembly.$field
    }
    foreach ($field in @("types", "members", "attributes", "entryChecksum")) {
        Assert-Equal "Pair '$PairName' touchCounts.$field" $Baseline.touchCounts.$field $Candidate.touchCounts.$field
    }
    foreach ($field in @("totalBytes", "fileCount")) {
        Assert-Equal "Pair '$PairName' aotMetadata.$field" $Baseline.aotMetadata.$field $Candidate.aotMetadata.$field
    }
    Assert-Equal "Pair '$PairName' metadata scenario" (Get-MetadataScenario $Baseline) (Get-MetadataScenario $Candidate)
    $baselineReflection = Get-ReflectionContract $Baseline
    $candidateReflection = Get-ReflectionContract $Candidate
    Assert-Equal "Pair '$PairName' reflection profile" $baselineReflection.profile $candidateReflection.profile
    Assert-Equal "Pair '$PairName' reflection type limit" $baselineReflection.requestedTypeCount $candidateReflection.requestedTypeCount
}

function New-PairedMetric([string]$Name, [string]$Unit, [double[]]$Baseline, [double[]]$Candidate) {
    if ($Baseline.Count -ne $Candidate.Count -or $Baseline.Count -eq 0) {
        throw "Paired metric '$Name' has mismatched or empty inputs."
    }
    $deltas = [double[]]@()
    $percentDeltas = [double[]]@()
    for ($index = 0; $index -lt $Baseline.Count; ++$index) {
        if ($Baseline[$index] -eq 0) { throw "Paired metric '$Name' has a zero baseline at pair $($index + 1)." }
        $delta = $Candidate[$index] - $Baseline[$index]
        $deltas += $delta
        $percentDeltas += 100.0 * $delta / $Baseline[$index]
    }
    return [ordered]@{
        name = $Name
        unit = $Unit
        pairedMedianDelta = Get-Median $deltas
        pairedMedianDeltaPercent = Get-Median $percentDeltas
        minimumDeltaPercent = [double]($percentDeltas | Measure-Object -Minimum).Minimum
        maximumDeltaPercent = [double]($percentDeltas | Measure-Object -Maximum).Maximum
    }
}

$rawPath = Resolve-LabPath $RawDirectory
$baselineFiles = @(Get-ChildItem -LiteralPath (Join-Path $rawPath "baseline") -Filter "sample-*.json" -File | Sort-Object Name)
$candidateFiles = @(Get-ChildItem -LiteralPath (Join-Path $rawPath "candidate") -Filter "sample-*.json" -File | Sort-Object Name)
if ($baselineFiles.Count -ne $candidateFiles.Count -or $baselineFiles.Count -eq 0) {
    throw "Raw directory does not contain matching baseline and candidate samples."
}

$baselineRuns = @()
$candidateRuns = @()
$baselineReference = $null
$candidateReference = $null
for ($index = 0; $index -lt $baselineFiles.Count; ++$index) {
    if ($baselineFiles[$index].Name -ne $candidateFiles[$index].Name) {
        throw "Pair filenames do not match at index $index."
    }
    $baseline = Get-Content -Raw -LiteralPath $baselineFiles[$index].FullName | ConvertFrom-Json
    $candidate = Get-Content -Raw -LiteralPath $candidateFiles[$index].FullName | ConvertFrom-Json
    if ($baseline.suiteId -ne "hybridclr-metadata-load-v2" -or
        $candidate.suiteId -ne $baseline.suiteId -or
        $candidate.metadataMode -ne $baseline.metadataMode -or
        [string]::IsNullOrWhiteSpace([string]$baseline.buildIdentity.sha256) -or
        [string]::IsNullOrWhiteSpace([string]$candidate.buildIdentity.sha256) -or
        $candidate.stressAssembly.sha256 -ne $baseline.stressAssembly.sha256) {
        throw "Pair '$($baselineFiles[$index].Name)' does not share the metadata benchmark contract."
    }
    if ($null -eq $baselineReference) {
        $baselineReference = $baseline
        $candidateReference = $candidate
    } else {
        Assert-SideIdentity "Baseline" $baselineReference $baseline
        Assert-SideIdentity "Candidate" $candidateReference $candidate
    }
    Assert-PairContract $baseline $candidate $baselineFiles[$index].Name
    $baselineRuns += $baseline
    $candidateRuns += $candidate
}

$metrics = @()
foreach ($name in @("aotMetadataLoad", "assemblyLoad", "entryResolve", "entryExecute", "reflectionTouch", "loadAndEntry", "loadAndReflection", "throughReflection")) {
    $baselineValues = [double[]]@($baselineRuns | ForEach-Object { Get-TimeValue $_ $name })
    $candidateValues = [double[]]@($candidateRuns | ForEach-Object { Get-TimeValue $_ $name })
    $metrics += New-PairedMetric $name "nanoseconds" $baselineValues $candidateValues
}
foreach ($spec in @(
    @("loadOnlyPrivateBytes", "hot-update-bytes-released"),
    @("entryExecutedPrivateBytes", "entry-executed"),
    @("reflectionTouchedPrivateBytes", "reflection-touched")
)) {
    $baselineValues = [double[]]@($baselineRuns | ForEach-Object { Get-MemoryValue $_ $spec[1] })
    $candidateValues = [double[]]@($candidateRuns | ForEach-Object { Get-MemoryValue $_ $spec[1] })
    $metrics += New-PairedMetric $spec[0] "bytes" $baselineValues $candidateValues
}
if ((Has-AndroidPss $baselineRuns) -and (Has-AndroidPss $candidateRuns)) {
    foreach ($spec in @(
        @("loadOnlyAndroidPssBytes", "hot-update-bytes-released"),
        @("entryExecutedAndroidPssBytes", "entry-executed"),
        @("reflectionTouchedAndroidPssBytes", "reflection-touched")
    )) {
        $baselineValues = [double[]]@($baselineRuns | ForEach-Object { Get-MemoryValue $_ $spec[1] "androidPssBytes" })
        $candidateValues = [double[]]@($candidateRuns | ForEach-Object { Get-MemoryValue $_ $spec[1] "androidPssBytes" })
        $metrics += New-PairedMetric $spec[0] "bytes" $baselineValues $candidateValues
    }
}

$report = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    rawDirectory = $RawDirectory
    pairCount = $baselineFiles.Count
    contract = [ordered]@{
        runtime = $baselineReference.runtime
        platform = $baselineReference.platform
        architecture = $baselineReference.architecture
        metadataMode = $baselineReference.metadataMode
        metadataScenario = Get-MetadataScenario $baselineReference
        reflectionContract = (Get-ReflectionContract $baselineReference)
        baselineBuildIdentity = $baselineReference.buildIdentity
        candidateBuildIdentity = $candidateReference.buildIdentity
        stressAssembly = $baselineReference.stressAssembly
        touchCounts = $baselineReference.touchCounts
    }
    metrics = $metrics
}
$outputPath = Resolve-LabPath $Output
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Metadata paired diagnostics: $outputPath"
Write-Host "Pairs: $($baselineFiles.Count)"
