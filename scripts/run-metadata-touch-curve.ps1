param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$BaselineProfile = "Baseline-Clean",
    [string]$CandidateProfile = "Metadata-Candidate",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "supplemental",
    [ValidateSet("entry-first", "reflection-first")]
    [string]$MetadataScenario = "entry-first",
    [int[]]$TypeCounts = @(1, 10, 100, 500, 1024),
    [bool]$IncludeExhaustive = $true,
    [int]$Pairs = 0,
    [string]$Output = "reports/metadata-touch-curve.json",
    [int]$PlayerTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$policy = Get-Content -Raw -LiteralPath (Join-Path $LabRoot "manifests/metadata-benchmark-policy.json") | ConvertFrom-Json
if ($Pairs -le 0) { $Pairs = [int]$policy.minimumIndependentProcesses }
if ($Pairs -lt [int]$policy.minimumIndependentProcesses) {
    throw "Pairs must be at least $($policy.minimumIndependentProcesses)."
}
$maximumTypeCount = [int]$policy.stressAssembly.typeCount
$normalizedTypeCounts = @($TypeCounts | Sort-Object -Unique)
if ($normalizedTypeCounts.Count -eq 0 -or @($normalizedTypeCounts | Where-Object { $_ -lt 1 -or $_ -gt $maximumTypeCount }).Count -ne 0) {
    throw "TypeCounts must contain unique values between 1 and $maximumTypeCount."
}

function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

function Get-NamedItem([object[]]$Items, [string]$Name, [string]$Label) {
    $matches = @($Items | Where-Object name -eq $Name)
    if ($matches.Count -ne 1) { throw "$Label does not contain exactly one '$Name' metric." }
    return $matches[0]
}

function New-PairedCurveMetric([object[]]$Metrics, [string]$Name, [string]$Label) {
    $metric = Get-NamedItem $Metrics $Name $Label
    return [ordered]@{
        medianDeltaPercent = [double]$metric.pairedMedianDeltaPercent
        minimumDeltaPercent = [double]$metric.minimumDeltaPercent
        maximumDeltaPercent = [double]$metric.maximumDeltaPercent
    }
}

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$evidenceRoot = "reports/touch-curve-$runId"
$points = @()
$baselineBuildIdentity = $null
$candidateBuildIdentity = $null

function Invoke-TouchPoint([string]$Profile, [int]$RequestedTypeCount, [string]$Slug) {
    $baselineOutput = "$evidenceRoot/baseline-$Slug-summary.json"
    $candidateOutput = "$evidenceRoot/candidate-$Slug-summary.json"
    $comparisonOutput = "$evidenceRoot/$Slug-comparison.json"
    $pairedOutput = "$evidenceRoot/$Slug-paired.json"
    & (Join-Path $PSScriptRoot "run-metadata-comparison.ps1") `
        -LabRoot $LabRoot `
        -BaselineProfile $BaselineProfile `
        -CandidateProfile $CandidateProfile `
        -Il2CppCodeGeneration $Il2CppCodeGeneration `
        -AotMetadataMode $AotMetadataMode `
        -MetadataScenario $MetadataScenario `
        -ReflectionProfile $Profile `
        -ReflectionTypeLimit $RequestedTypeCount `
        -Pairs $Pairs `
        -BaselineOutput $baselineOutput `
        -CandidateOutput $candidateOutput `
        -ComparisonOutput $comparisonOutput `
        -PairedOutput $pairedOutput `
        -PlayerTimeoutSeconds $PlayerTimeoutSeconds `
        -DiagnosticOnly

    $baseline = Get-Content -Raw -LiteralPath (Resolve-LabPath $baselineOutput) | ConvertFrom-Json
    $candidate = Get-Content -Raw -LiteralPath (Resolve-LabPath $candidateOutput) | ConvertFrom-Json
    $comparison = Get-Content -Raw -LiteralPath (Resolve-LabPath $comparisonOutput) | ConvertFrom-Json
    $paired = Get-Content -Raw -LiteralPath (Resolve-LabPath $pairedOutput) | ConvertFrom-Json
    if ($null -eq $script:baselineBuildIdentity) {
        $script:baselineBuildIdentity = $baseline.buildIdentity
        $script:candidateBuildIdentity = $candidate.buildIdentity
    } elseif ($baseline.buildIdentity.sha256 -ne $script:baselineBuildIdentity.sha256 -or
        $candidate.buildIdentity.sha256 -ne $script:candidateBuildIdentity.sha256) {
        throw "Touch curve points do not share stable build identities."
    }

    $reflectionMedian = Get-NamedItem $comparison.time "reflectionTouch" "Comparison '$Slug'"
    $reflectionP95 = Get-NamedItem $comparison.time "reflectionTouchP95" "Comparison '$Slug'"
    $loadAndEntry = Get-NamedItem $comparison.stageTarget.metrics "loadAndEntry" "Comparison '$Slug'"
    $loadAndReflection = Get-NamedItem $comparison.stageTarget.metrics "loadAndReflection" "Comparison '$Slug'"
    $throughReflection = Get-NamedItem $comparison.stageTarget.metrics "throughReflection" "Comparison '$Slug'"
    $reflectionMemory = Get-NamedItem $comparison.memory "reflectionTouchedPrivateBytes" "Comparison '$Slug'"
    return [ordered]@{
        profile = $Profile
        requestedTypeCount = $RequestedTypeCount
        actualTouchCounts = $baseline.touchCounts
        reflectionMedian = [ordered]@{
            baselineNanoseconds = [double]$reflectionMedian.baselineMedian
            candidateNanoseconds = [double]$reflectionMedian.candidateMedian
            deltaPercent = [double]$reflectionMedian.deltaPercent
        }
        reflectionP95 = [ordered]@{
            baselineNanoseconds = [double]$reflectionP95.baselineMedian
            candidateNanoseconds = [double]$reflectionP95.candidateMedian
            deltaPercent = [double]$reflectionP95.deltaPercent
        }
        loadAndEntry = [ordered]@{
            baselineNanoseconds = [double]$loadAndEntry.baselineMedian
            candidateNanoseconds = [double]$loadAndEntry.candidateMedian
            deltaPercent = [double]$loadAndEntry.deltaPercent
        }
        loadAndReflection = [ordered]@{
            baselineNanoseconds = [double]$loadAndReflection.baselineMedian
            candidateNanoseconds = [double]$loadAndReflection.candidateMedian
            deltaPercent = [double]$loadAndReflection.deltaPercent
        }
        throughReflection = [ordered]@{
            baselineNanoseconds = [double]$throughReflection.baselineMedian
            candidateNanoseconds = [double]$throughReflection.candidateMedian
            deltaPercent = [double]$throughReflection.deltaPercent
        }
        reflectionTouchedPrivateBytes = [ordered]@{
            baselineBytes = [double]$reflectionMemory.baselineMedianBytes
            candidateBytes = [double]$reflectionMemory.candidateMedianBytes
            deltaPercent = [double]$reflectionMemory.deltaPercent
        }
        pairedDeltaPercent = [ordered]@{
            assemblyLoad = New-PairedCurveMetric $paired.metrics "assemblyLoad" "Paired diagnostics '$Slug'"
            entryResolve = New-PairedCurveMetric $paired.metrics "entryResolve" "Paired diagnostics '$Slug'"
            entryExecute = New-PairedCurveMetric $paired.metrics "entryExecute" "Paired diagnostics '$Slug'"
            reflectionTouch = New-PairedCurveMetric $paired.metrics "reflectionTouch" "Paired diagnostics '$Slug'"
            loadAndEntry = New-PairedCurveMetric $paired.metrics "loadAndEntry" "Paired diagnostics '$Slug'"
            loadAndReflection = New-PairedCurveMetric $paired.metrics "loadAndReflection" "Paired diagnostics '$Slug'"
            throughReflection = New-PairedCurveMetric $paired.metrics "throughReflection" "Paired diagnostics '$Slug'"
            reflectionTouchedPrivateBytes = New-PairedCurveMetric $paired.metrics "reflectionTouchedPrivateBytes" "Paired diagnostics '$Slug'"
        }
        evidence = [ordered]@{
            baselineSummary = $baselineOutput
            candidateSummary = $candidateOutput
            comparison = $comparisonOutput
            pairedDiagnostics = $pairedOutput
        }
    }
}

foreach ($typeCount in $normalizedTypeCounts) {
    Write-Host "[metadata-touch-curve] selective $typeCount types"
    $points += Invoke-TouchPoint -Profile "selective" -RequestedTypeCount $typeCount -Slug "selective-$typeCount"
}
if ($IncludeExhaustive) {
    Write-Host "[metadata-touch-curve] exhaustive"
    $points += Invoke-TouchPoint -Profile "exhaustive" -RequestedTypeCount 0 -Slug "exhaustive"
}

$report = [ordered]@{
    schemaVersion = 1
    suiteId = "hybridclr-metadata-touch-curve-v1"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    metadataMode = $AotMetadataMode
    metadataScenario = $MetadataScenario
    pairsPerPoint = $Pairs
    baselineProfile = $BaselineProfile
    candidateProfile = $CandidateProfile
    baselineBuildIdentity = $baselineBuildIdentity
    candidateBuildIdentity = $candidateBuildIdentity
    selectiveTypeCounts = $normalizedTypeCounts
    includesExhaustive = $IncludeExhaustive
    points = $points
}
$outputPath = Resolve-LabPath $Output
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Metadata touch curve: $outputPath"
