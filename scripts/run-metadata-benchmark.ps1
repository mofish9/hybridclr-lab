param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProjectRoot = "",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Metadata-Tuanjie2022", "Metadata-Instrumented", "Metadata-Unity2021", "Metadata-Unity2022", "Compatibility-Unity2021-Standard", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "supplemental",
    [ValidateSet("entry-first", "reflection-first")]
    [string]$MetadataScenario = "entry-first",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [ValidateSet("exhaustive", "selective")]
    [string]$ReflectionProfile = "exhaustive",
    [int]$ReflectionTypeLimit = 0,
    [ValidateSet("none", "entry", "entry-method", "entry-graph", "entry-method-graph")]
    [string]$MetadataWarmup = "none",
    [int]$MetadataWarmupMaxItems = 8,
    [int]$Processes = 0,
    [string]$Output = "",
    [int]$PlayerTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    Join-Path $LabRoot "unity-test-project"
} else {
    [IO.Path]::GetFullPath($ProjectRoot)
}
$policyPath = Join-Path $LabRoot "manifests/metadata-benchmark-policy.json"
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json
if ($Processes -le 0) { $Processes = [int]$policy.minimumIndependentProcesses }
if ($Processes -lt 1) { throw "Processes must be at least 1." }
if ($ReflectionProfile -eq "selective") {
    if ($ReflectionTypeLimit -lt 1 -or $ReflectionTypeLimit -gt [int]$policy.stressAssembly.typeCount) {
        throw "ReflectionTypeLimit must be between 1 and $($policy.stressAssembly.typeCount) for selective reflection."
    }
} elseif ($ReflectionTypeLimit -ne 0) {
    throw "ReflectionTypeLimit must be zero for exhaustive reflection."
}
if ($MetadataWarmupMaxItems -lt 1) {
    throw "MetadataWarmupMaxItems must be at least 1."
}

$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$slug = $buildVariant.ToLowerInvariant()
$player = Join-Path $projectRoot "Builds/$buildVariant/HybridCLRLab.exe"
$buildManifest = Join-Path $LabRoot "reports/$slug-build-manifest.json"
if (-not (Test-Path $player)) { throw "Player was not found: $player" }
if (-not (Test-Path $buildManifest)) { throw "Build manifest was not found: $buildManifest" }
$build = Get-Content -Raw $buildManifest | ConvertFrom-Json
$buildManifestSha256 = (Get-FileHash -LiteralPath $buildManifest -Algorithm SHA256).Hash
$policySha256 = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") { throw "A no-metadata build can only run with metadata mode none." }
if ($build.aotMetadataPackaging -ne $AotMetadataPackaging) { throw "Build manifest metadata packaging does not match the metadata benchmark request." }
if ($build.metadataBenchmarkPolicySha256 -ne $policySha256) { throw "Build manifest metadata policy does not match the current policy." }
$metadataStressAssembly = Join-Path $LabRoot "artifacts/managed-cases/StandaloneWindows64/HybridCLR.MetadataStress.dll"
if ((Get-FileHash -LiteralPath $metadataStressAssembly -Algorithm SHA256).Hash -ne $build.metadataStressAssemblySha256) {
    throw "Metadata stress assembly does not match the build manifest."
}
$prewarmManifest = Join-Path $LabRoot "reports/prewarm-manifest-stress-StandaloneWindows64.json"
if ((Get-FileHash -LiteralPath $prewarmManifest -Algorithm SHA256).Hash -ne $build.metadataStressPrewarmManifestSha256) {
    throw "Prewarm manifest does not match the build manifest."
}
$actualPlayerHash = (Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash
if ([string]::IsNullOrWhiteSpace([string]$build.playerSha256) -or $build.playerSha256 -ne $actualPlayerHash) {
    throw "Player executable does not match the build manifest. Rebuild before benchmarking."
}
$gameAssemblies = @(Get-ChildItem -LiteralPath (Split-Path -Parent $player) -Recurse -File -Filter "GameAssembly.dll")
if ($gameAssemblies.Count -ne 1) { throw "Expected exactly one GameAssembly.dll beside the player build." }
$actualGameAssemblyHash = (Get-FileHash -LiteralPath $gameAssemblies[0].FullName -Algorithm SHA256).Hash
if ([string]::IsNullOrWhiteSpace([string]$build.gameAssemblySha256) -or $build.gameAssemblySha256 -ne $actualGameAssemblyHash) {
    throw "GameAssembly.dll does not match the build manifest. Rebuild before benchmarking."
}

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$reflectionSlug = if ($ReflectionProfile -eq "selective") { "selective-$ReflectionTypeLimit" } else { "exhaustive" }
$warmupSuffix = if ($MetadataWarmup -eq "none") { "" } else { "-warmup-$MetadataWarmup" }
$rawDirectory = Join-Path $LabRoot "reports/raw/$slug-metadata-$AotMetadataMode-$MetadataScenario-$reflectionSlug$warmupSuffix-$runId"
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
$results = @()
$requestedStageProfile = $env:HYBRIDCLR_METADATA_PROFILE
if (-not [string]::IsNullOrWhiteSpace($requestedStageProfile)) {
    $requestedStageProfile = if ([IO.Path]::IsPathRooted($requestedStageProfile)) {
        [IO.Path]::GetFullPath($requestedStageProfile)
    } else {
        [IO.Path]::GetFullPath((Join-Path $LabRoot $requestedStageProfile))
    }
}
$stageProfileFiles = @()
for ($index = 1; $index -le $Processes; $index++) {
    $result = Join-Path $rawDirectory ("sample-{0:D3}.json" -f $index)
    $arguments = @(
        "-batchmode", "-nographics",
        "-labTarget", "StandaloneWindows64",
        "-labMode", "metadata",
        "-labAotMetadataMode", $AotMetadataMode,
        "-labMetadataScenario", $MetadataScenario,
        "-labReflectionProfile", $ReflectionProfile,
        "-labSettleMilliseconds", [string]$policy.settleMilliseconds,
        "-labMetadataResult", $result
    )
    if ($ReflectionProfile -eq "selective") {
        $arguments += @("-labReflectionTypeLimit", [string]$ReflectionTypeLimit)
    }
    if ($MetadataWarmup -ne "none") {
        $arguments += @("-labMetadataWarmup", $MetadataWarmup)
        $arguments += @("-labMetadataWarmupAcrossFrames", "true")
        $warmupLimitArgument = if ($MetadataWarmup -eq "entry" -or $MetadataWarmup -eq "entry-graph") {
            "-labMetadataWarmupMaxTypes"
        } else {
            "-labMetadataWarmupMaxMethods"
        }
        $arguments += @($warmupLimitArgument, [string]$MetadataWarmupMaxItems)
    }

    $stageProfile = $null
    if (-not [string]::IsNullOrWhiteSpace($requestedStageProfile)) {
        $stageProfile = Join-Path $rawDirectory ("sample-{0:D3}-metadata-stages.csv" -f $index)
        if (Test-Path -LiteralPath $stageProfile) { Remove-Item -LiteralPath $stageProfile -Force }
        $env:HYBRIDCLR_METADATA_PROFILE = $stageProfile
        $stageProfileFiles += $stageProfile
    }
    try {
        $process = Start-Process -FilePath $player -ArgumentList $arguments -PassThru -WindowStyle Hidden
        $exited = $process.WaitForExit($PlayerTimeoutSeconds * 1000)
        if (-not $exited) {
            try { $process.Kill() } catch { }
            throw "Metadata benchmark sample $index timed out after $PlayerTimeoutSeconds seconds."
        }
    } finally {
        if ([string]::IsNullOrWhiteSpace($requestedStageProfile)) {
            Remove-Item Env:HYBRIDCLR_METADATA_PROFILE -ErrorAction SilentlyContinue
        } else {
            $env:HYBRIDCLR_METADATA_PROFILE = $requestedStageProfile
        }
    }
    if ($process.ExitCode -ne 0) { throw "Metadata benchmark sample $index exited with code $($process.ExitCode)." }
    if (-not (Test-Path $result)) { throw "Metadata benchmark did not produce: $result" }
    if ($stageProfile -and -not (Test-Path -LiteralPath $stageProfile)) {
        throw "Metadata benchmark did not produce stage profile: $stageProfile"
    }
    $sample = Get-Content -Raw $result | ConvertFrom-Json
    $warmupProperty = $sample.PSObject.Properties["metadataWarmup"]
    $sampleWarmupMode = if ($null -eq $warmupProperty) { "none" } else { [string]$sample.metadataWarmup.mode }
    $sampleWarmupNanoseconds = if ($null -eq $warmupProperty) { 0L } else { [long]$sample.metadataWarmup.nanoseconds }
    $sampleWarmupAcrossFrames = if ($null -eq $warmupProperty) { $false } else { [bool]$sample.metadataWarmup.acrossFrames }
    $sampleWarmupFrameCount = if ($null -eq $warmupProperty) { 0 } else { [int]$sample.metadataWarmup.frameCount }
    $expectedSnapshotCount = if ($MetadataWarmup -eq "none") { 7 } else { 8 }
    if ($sample.metadataMode -ne $AotMetadataMode -or $sample.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
        $sample.buildIdentity.hybridclrUnityTreeSha256 -ne $build.hybridclrUnityTreeSha256 -or
        $sample.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
        $sample.buildIdentity.managedAssemblySha256 -ne $build.managedAssemblySha256 -or
        $sample.metadataScenario -ne $MetadataScenario -or
        $sampleWarmupMode -ne $MetadataWarmup -or
        $sampleWarmupNanoseconds -lt 0 -or
        ($MetadataWarmup -ne "none" -and (-not $sampleWarmupAcrossFrames -or $sampleWarmupFrameCount -lt 1)) -or
        $sample.reflectionContract.profile -ne $ReflectionProfile -or
        [int]$sample.reflectionContract.requestedTypeCount -ne $ReflectionTypeLimit -or
        ($ReflectionProfile -eq "selective" -and [int]$sample.touchCounts.types -ne $ReflectionTypeLimit) -or
        $sample.snapshots.Count -ne $expectedSnapshotCount) {
        throw "Metadata benchmark produced an invalid result: $result"
    }
    $results += $result
    Write-Host "[metadata-benchmark] $buildVariant / $AotMetadataMode / $MetadataScenario / $index of $Processes"
}

if ((Get-FileHash -LiteralPath $buildManifest -Algorithm SHA256).Hash -ne $buildManifestSha256 -or
    (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash -ne $policySha256) {
    throw "Build manifest or metadata policy changed while the benchmark was running."
}
if ((Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash -ne $actualPlayerHash -or
    (Get-FileHash -LiteralPath $gameAssemblies[0].FullName -Algorithm SHA256).Hash -ne $actualGameAssemblyHash) {
    throw "Player artifacts changed while the metadata benchmark was running."
}

if ($stageProfileFiles.Count -gt 0) {
    $mergedStageLines = [Collections.Generic.List[string]]::new()
    foreach ($stageProfile in $stageProfileFiles) {
        foreach ($line in Get-Content -LiteralPath $stageProfile) {
            if ($line -notmatch '^[0-9]+,[A-Za-z0-9_]+,[0-9]+$') {
                throw "Malformed metadata stage profile line in '$stageProfile': $line"
            }
            $mergedStageLines.Add($line)
        }
    }
    if ($mergedStageLines.Count -eq 0) { throw "Metadata stage profiles were empty." }
    New-Item -ItemType Directory -Force -Path (Split-Path $requestedStageProfile) | Out-Null
    [IO.File]::WriteAllLines($requestedStageProfile, $mergedStageLines, [Text.UTF8Encoding]::new($false))
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = if ($ReflectionProfile -eq "exhaustive") {
        "reports/$slug-metadata-$AotMetadataMode-$MetadataScenario$warmupSuffix-summary.json"
    } else {
        "reports/$slug-metadata-$AotMetadataMode-$MetadataScenario-$reflectionSlug$warmupSuffix-summary.json"
    }
}
& (Join-Path $PSScriptRoot "summarize-metadata-benchmark.ps1") `
    -LabRoot $LabRoot `
    -InputPath $results `
    -BuildManifest $buildManifest `
    -Output $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
