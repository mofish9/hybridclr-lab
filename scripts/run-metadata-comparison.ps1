param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProjectRoot = "",
    [string]$BaselineProfile = "Baseline-Clean",
    [string]$CandidateProfile = "Metadata-Candidate",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "supplemental",
    [ValidateSet("entry-first", "reflection-first")]
    [string]$MetadataScenario = "entry-first",
    [ValidateSet("exhaustive", "selective")]
    [string]$ReflectionProfile = "exhaustive",
    [int]$ReflectionTypeLimit = 0,
    [ValidateSet("none", "entry", "entry-method", "entry-graph", "entry-method-graph")]
    [string]$MetadataWarmup = "none",
    [int]$Pairs = 0,
    [string]$BaselineOutput = "",
    [string]$CandidateOutput = "",
    [string]$ComparisonOutput = "",
    [string]$PairedOutput = "",
    [int]$PlayerTimeoutSeconds = 180,
    [switch]$DiagnosticOnly
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    Join-Path $LabRoot "unity-test-project"
} else {
    [IO.Path]::GetFullPath($ProjectRoot)
}
$policyPath = Join-Path $LabRoot "manifests/metadata-benchmark-policy.json"
$policy = Get-Content -Raw -LiteralPath $policyPath | ConvertFrom-Json
if ($Pairs -le 0) { $Pairs = [int]$policy.minimumIndependentProcesses }
if ($Pairs -lt [int]$policy.minimumIndependentProcesses) {
    throw "Pairs must be at least $($policy.minimumIndependentProcesses)."
}
if ($ReflectionProfile -eq "selective") {
    if ($ReflectionTypeLimit -lt 1 -or $ReflectionTypeLimit -gt [int]$policy.stressAssembly.typeCount) {
        throw "ReflectionTypeLimit must be between 1 and $($policy.stressAssembly.typeCount) for selective reflection."
    }
} elseif ($ReflectionTypeLimit -ne 0) {
    throw "ReflectionTypeLimit must be zero for exhaustive reflection."
}

function Get-Variant([string]$Profile) {
    if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { return $Profile }
    return "$Profile-$Il2CppCodeGeneration"
}

function Get-Slug([string]$Value) {
    return $Value.ToLowerInvariant()
}

function Invoke-Sample([string]$Player, [string]$Result, [string]$Label) {
    $arguments = @(
        "-batchmode", "-nographics",
        "-labMode", "metadata",
        "-labAotMetadataMode", $AotMetadataMode,
        "-labMetadataScenario", $MetadataScenario,
        "-labReflectionProfile", $ReflectionProfile,
        "-labSettleMilliseconds", [string]$policy.settleMilliseconds,
        "-labMetadataResult", $Result
    )
    if ($ReflectionProfile -eq "selective") {
        $arguments += @("-labReflectionTypeLimit", [string]$ReflectionTypeLimit)
    }
    if ($MetadataWarmup -ne "none") {
        $arguments += @("-labMetadataWarmup", $MetadataWarmup)
    }
    $process = Start-Process -FilePath $Player -ArgumentList $arguments -PassThru -WindowStyle Hidden
    $exited = $process.WaitForExit($PlayerTimeoutSeconds * 1000)
    if (-not $exited) {
        try { $process.Kill() } catch { }
        throw "$Label timed out after $PlayerTimeoutSeconds seconds."
    }
    if ($process.ExitCode -ne 0) { throw "$Label exited with code $($process.ExitCode)." }
    if (-not (Test-Path -LiteralPath $Result)) { throw "$Label did not produce: $Result" }
    $sample = Get-Content -Raw -LiteralPath $Result | ConvertFrom-Json
    $warmupProperty = $sample.PSObject.Properties["metadataWarmup"]
    $sampleWarmupMode = if ($null -eq $warmupProperty) { "none" } else { [string]$sample.metadataWarmup.mode }
    $expectedSnapshotCount = if ($MetadataWarmup -eq "none") { 7 } else { 8 }
    if ($sample.metadataMode -ne $AotMetadataMode -or
        $sample.metadataScenario -ne $MetadataScenario -or
        $sampleWarmupMode -ne $MetadataWarmup -or
        $sample.reflectionContract.profile -ne $ReflectionProfile -or
        [int]$sample.reflectionContract.requestedTypeCount -ne $ReflectionTypeLimit -or
        ($ReflectionProfile -eq "selective" -and [int]$sample.touchCounts.types -ne $ReflectionTypeLimit) -or
        $sample.snapshots.Count -ne $expectedSnapshotCount) {
        throw "$Label produced an invalid metadata result."
    }
}

$baselineVariant = Get-Variant $BaselineProfile
$candidateVariant = Get-Variant $CandidateProfile
$baselineSlug = Get-Slug $baselineVariant
$candidateSlug = Get-Slug $candidateVariant
$baselinePlayer = Join-Path $projectRoot "Builds/$baselineVariant/HybridCLRLab.exe"
$candidatePlayer = Join-Path $projectRoot "Builds/$candidateVariant/HybridCLRLab.exe"
$baselineManifest = "reports/$baselineSlug-build-manifest.json"
$candidateManifest = "reports/$candidateSlug-build-manifest.json"
foreach ($path in @($baselinePlayer, $candidatePlayer, (Join-Path $LabRoot $baselineManifest), (Join-Path $LabRoot $candidateManifest))) {
    if (-not (Test-Path -LiteralPath $path)) { throw "Required comparison input was not found: $path" }
}

$warmupSuffix = if ($MetadataWarmup -eq "none") { "" } else { "-warmup-$MetadataWarmup" }
if ([string]::IsNullOrWhiteSpace($BaselineOutput)) {
    $BaselineOutput = "reports/baseline-clean-metadata-$AotMetadataMode$warmupSuffix-summary.json"
}
if ([string]::IsNullOrWhiteSpace($CandidateOutput)) {
    $CandidateOutput = "reports/metadata-candidate-metadata-$AotMetadataMode$warmupSuffix-summary.json"
}
if ([string]::IsNullOrWhiteSpace($ComparisonOutput)) {
    $ComparisonOutput = "reports/metadata-candidate-comparison$warmupSuffix.json"
}

$runId = "{0}-{1}" -f [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ"), [Guid]::NewGuid().ToString("N").Substring(0, 8)
$reflectionSlug = if ($ReflectionProfile -eq "selective") { "selective-$ReflectionTypeLimit" } else { "exhaustive" }
$rawRoot = Join-Path $LabRoot "reports/raw/metadata-comparison-$AotMetadataMode-$MetadataScenario-$reflectionSlug$warmupSuffix-$runId"
$baselineRaw = Join-Path $rawRoot "baseline"
$candidateRaw = Join-Path $rawRoot "candidate"
New-Item -ItemType Directory -Force -Path $baselineRaw, $candidateRaw | Out-Null
$baselineResults = @()
$candidateResults = @()

for ($index = 1; $index -le $Pairs; $index++) {
    $baselineResult = Join-Path $baselineRaw ("sample-{0:D3}.json" -f $index)
    $candidateResult = Join-Path $candidateRaw ("sample-{0:D3}.json" -f $index)
    if (($index % 2) -eq 1) {
        Invoke-Sample $baselinePlayer $baselineResult "Baseline sample $index"
        Invoke-Sample $candidatePlayer $candidateResult "Candidate sample $index"
        $order = "baseline,candidate"
    } else {
        Invoke-Sample $candidatePlayer $candidateResult "Candidate sample $index"
        Invoke-Sample $baselinePlayer $baselineResult "Baseline sample $index"
        $order = "candidate,baseline"
    }
    $baselineResults += $baselineResult
    $candidateResults += $candidateResult
    Write-Host "[metadata-comparison] pair $index of $Pairs ($order)"
}

& (Join-Path $PSScriptRoot "summarize-metadata-benchmark.ps1") `
    -LabRoot $LabRoot -InputPath $baselineResults -BuildManifest $baselineManifest -Output $BaselineOutput
& (Join-Path $PSScriptRoot "summarize-metadata-benchmark.ps1") `
    -LabRoot $LabRoot -InputPath $candidateResults -BuildManifest $candidateManifest -Output $CandidateOutput
& (Join-Path $PSScriptRoot "compare-metadata-benchmarks.ps1") `
    -LabRoot $LabRoot `
    -Baseline $BaselineOutput `
    -Candidate $CandidateOutput `
    -BaselineBuildManifest $baselineManifest `
    -CandidateBuildManifest $candidateManifest `
    -Output $ComparisonOutput `
    -DiagnosticOnly:$DiagnosticOnly
if (-not [string]::IsNullOrWhiteSpace($PairedOutput)) {
    & (Join-Path $PSScriptRoot "summarize-metadata-pairs.ps1") `
        -LabRoot $LabRoot `
        -RawDirectory $rawRoot `
        -Output $PairedOutput
}
$comparisonPath = if ([IO.Path]::IsPathRooted($ComparisonOutput)) { $ComparisonOutput } else { Join-Path $LabRoot $ComparisonOutput }
$comparison = Get-Content -Raw -LiteralPath $comparisonPath | ConvertFrom-Json
if (-not $DiagnosticOnly -and -not $comparison.summary.hardGatePassed) { exit 2 }
