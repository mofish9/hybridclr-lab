param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Profile = "Baseline-Clean",
    [string]$PlayerSteady = "reports/baseline-clean-player-steady-benchmark.json",
    [string]$PlayerCold = "reports/baseline-clean-player-cold-benchmark.json",
    [string]$AotSteady = "reports/baseline-clean-player-aot-steady-benchmark.json",
    [string]$AotCold = "reports/baseline-clean-player-aot-cold-benchmark.json",
    [string]$Comparison = "reports/baseline-clean-hybridclr-vs-aot.json",
    [string]$ReferenceSteady = "reports/baseline-clean-reference-steady-benchmark.json",
    [string]$ReferenceCold = "reports/baseline-clean-reference-cold-benchmark.json",
    [string]$Output = "reports/baseline-clean-performance-summary.json",
    [string]$Environment = "reports/benchmark-environment.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
function Read-Json([string]$Path) {
    $resolved = if ([IO.Path]::IsPathRooted($Path)) { $Path } else { Join-Path $LabRoot $Path }
    if (-not (Test-Path $resolved)) { throw "Required report was not found: $resolved" }
    return Get-Content -Raw $resolved | ConvertFrom-Json
}
function Get-RelativePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    return $full.Substring($LabRoot.Length).TrimStart('\', '/')
}

$playerSteadyResult = Read-Json $PlayerSteady
$playerColdResult = Read-Json $PlayerCold
$referenceSteadyResult = Read-Json $ReferenceSteady
$referenceColdResult = Read-Json $ReferenceCold
$aotSteadyResult = Read-Json $AotSteady
$aotColdResult = Read-Json $AotCold
$comparisonResult = Read-Json $Comparison
$buildManifest = Read-Json "reports/$($Profile.ToLowerInvariant())-build-manifest.json"
$playerResult = Read-Json "reports/$($Profile.ToLowerInvariant())-player-result.json"
$referenceResult = Read-Json "reports/reference-result.json"
$differential = Read-Json "reports/$($Profile.ToLowerInvariant())-differential-result.json"
$environmentResult = Read-Json $Environment
$environmentPath = if ([IO.Path]::IsPathRooted($Environment)) { $Environment } else { Join-Path $LabRoot $Environment }
$buildManifestPath = Join-Path $LabRoot "reports/$($Profile.ToLowerInvariant())-build-manifest.json"
$buildManifestSha256 = (Get-FileHash -LiteralPath $buildManifestPath -Algorithm SHA256).Hash
$policySha256 = (Get-FileHash -LiteralPath (Join-Path $LabRoot "manifests/benchmark-policy.json") -Algorithm SHA256).Hash
foreach ($repositoryName in @("hybridclr_unity", "hybridclr", "il2cpp_plus")) {
    $repository = $buildManifest.repositories.$repositoryName
    if ($null -eq $repository -or [bool]$repository.dirty -or [string]::IsNullOrWhiteSpace([string]$repository.treeSha256)) {
        throw "Release performance evidence requires a clean, tree-hashed '$repositoryName' repository."
    }
}
if ($buildManifest.hybridclrUnityTreeSha256 -ne $buildManifest.repositories.hybridclr_unity.treeSha256) {
    throw "hybridclr_unity source identity is inconsistent in the build manifest."
}

foreach ($result in @($playerSteadyResult, $playerColdResult, $aotSteadyResult, $aotColdResult, $referenceSteadyResult, $referenceColdResult)) {
    if ($result.suiteId -ne "managed-performance-v1" -or -not $result.minimumProcessCountMet) {
        throw "Performance input is not a complete managed-performance-v1 summary."
    }
    $expectedAssemblyHash = if ($result.runner -like "*-aot-v1") {
        $buildManifest.aotBenchmarkAssemblySha256
    } else {
        $buildManifest.managedAssemblySha256
    }
    if ($result.managedAssemblySha256 -ne $expectedAssemblyHash) {
        throw "Performance result assembly does not match build manifest for runner $($result.runner)."
    }
    if ($null -ne $result.PSObject.Properties["buildIdentity"] -and
        ($result.policySha256 -ne $policySha256 -or
         $result.buildManifestSha256 -ne $buildManifestSha256 -or
         $result.buildIdentity.sha256 -ne $buildManifest.buildIdentitySha256 -or
         $result.buildIdentity.hybridclrUnityTreeSha256 -ne $buildManifest.hybridclrUnityTreeSha256 -or
         $result.stagedRuntimeSha256 -ne $buildManifest.stagedRuntimeSha256 -or
         $result.playerSha256 -ne $buildManifest.playerSha256 -or
         $result.gameAssemblySha256 -ne $buildManifest.gameAssemblySha256 -or
         $result.benchmarkGoldenSha256 -ne $buildManifest.benchmarkGoldenSha256)) {
        throw "Player performance result identity does not match the current frozen build inputs."
    }
}
if ($playerResult.summary.failed -ne 0 -or $referenceResult.summary.failed -ne 0 -or $differential.summary.differences -ne 0) {
    throw "Correctness gate is not clean; refusing to compose performance summary."
}

$outputPath = Join-Path $LabRoot $Output
$summary = [ordered]@{
    schemaVersion = 1
    suiteId = "managed-performance-v1"
    profile = $Profile
    status = "clean-baseline-measured"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    runtime = $buildManifest
    environment = [ordered]@{
        report = Get-RelativePath $environmentPath
        sha256 = (Get-FileHash -LiteralPath $environmentPath -Algorithm SHA256).Hash
        details = $environmentResult
    }
    correctness = [ordered]@{
        native = "CTest passed"
        reference = [ordered]@{ passed = $referenceResult.summary.passed; total = $referenceResult.summary.total }
        player = [ordered]@{ passed = $playerResult.summary.passed; total = $playerResult.summary.total }
        differentialDifferences = $differential.summary.differences
    }
    policy = [ordered]@{
        report = "manifests/benchmark-policy.json"
        sha256 = $policySha256
        status = "uncalibrated-policy-with-baseline-recommendation"
        hybridClrSteadyRecommendedNoiseThresholdPercent = $playerSteadyResult.calibration.recommendedNoiseThresholdPercent
        hybridClrColdRecommendedNoiseThresholdPercent = $playerColdResult.calibration.recommendedNoiseThresholdPercent
        aotSteadyRecommendedNoiseThresholdPercent = $aotSteadyResult.calibration.recommendedNoiseThresholdPercent
        aotColdRecommendedNoiseThresholdPercent = $aotColdResult.calibration.recommendedNoiseThresholdPercent
    }
    player = [ordered]@{
        hybridclr = [ordered]@{ steady = $playerSteadyResult; cold = $playerColdResult }
        aot = [ordered]@{ steady = $aotSteadyResult; cold = $aotColdResult }
        comparison = $comparisonResult
    }
    reference = [ordered]@{
        steady = $referenceSteadyResult
        cold = $referenceColdResult
    }
    inputs = [ordered]@{
        buildManifest = Get-RelativePath $buildManifestPath
        buildManifestSha256 = $buildManifestSha256
        buildIdentitySha256 = $buildManifest.buildIdentitySha256
        stagedRuntimeSha256 = $buildManifest.stagedRuntimeSha256
        playerSha256 = $buildManifest.playerSha256
        gameAssemblySha256 = $buildManifest.gameAssemblySha256
        playerCorrectness = Get-RelativePath (Join-Path $LabRoot "reports/$($Profile.ToLowerInvariant())-player-result.json")
        referenceCorrectness = "reports/reference-result.json"
        differential = Get-RelativePath (Join-Path $LabRoot "reports/$($Profile.ToLowerInvariant())-differential-result.json")
    }
}
New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$summary | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Performance summary: $outputPath"
