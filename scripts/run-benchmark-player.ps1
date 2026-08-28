param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProjectRoot = "",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("cold", "steady")]
    [string]$Mode = "steady",
    [ValidateSet("hybridclr", "aot")]
    [string]$BenchmarkRuntime = "hybridclr",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "supplemental",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [int]$Processes = 0,
    [string]$Workload = "",
    [string]$Output = "",
    [string]$InstrumentationOutput = "",
    [int]$PlayerTimeoutSeconds = 300
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    Join-Path $LabRoot "unity-test-project"
} elseif ([IO.Path]::IsPathRooted($ProjectRoot)) {
    [IO.Path]::GetFullPath($ProjectRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $ProjectRoot))
}
$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$profileSlug = $buildVariant.ToLowerInvariant()
$player = Join-Path $projectRoot "Builds/$buildVariant/HybridCLRLab.exe"
$buildManifest = Join-Path $LabRoot "reports/$profileSlug-build-manifest.json"
$policyPath = Join-Path $LabRoot "manifests/benchmark-policy.json"
if (-not (Test-Path $player)) { throw "Player was not found. Build it first: $player" }
if (-not (Test-Path $buildManifest)) { throw "Build manifest was not found: $buildManifest" }
$build = Get-Content -Raw $buildManifest | ConvertFrom-Json
$buildManifestSha256 = (Get-FileHash -LiteralPath $buildManifest -Algorithm SHA256).Hash
$policySha256 = (Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") { throw "A no-metadata build can only run with metadata mode none." }
if ($build.aotMetadataPackaging -ne $AotMetadataPackaging) { throw "Build manifest metadata packaging does not match the benchmark request." }
if ($build.benchmarkPolicySha256 -ne $policySha256) { throw "Build manifest benchmark policy does not match the current policy." }
$expectedManagedHash = (Get-FileHash -LiteralPath (Join-Path $LabRoot "artifacts/managed-cases/StandaloneWindows64/HybridCLR.ManagedCases.dll") -Algorithm SHA256).Hash
if ($build.managedAssemblySha256 -ne $expectedManagedHash) { throw "Build manifest managed assembly hash does not match current artifact." }
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
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json
if ($Processes -le 0) { $Processes = [int]$policy.minimumIndependentProcesses }
if ($Processes -lt 1) { throw "Processes must be at least 1." }

$referenceProject = Join-Path $LabRoot "runners/benchmark-reference/HybridCLR.BenchmarkReference.csproj"
$referenceRunner = Join-Path $LabRoot "runners/benchmark-reference/bin/Release/net6.0/HybridCLR.BenchmarkReference.dll"
dotnet build $referenceProject --configuration Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$workloads = @($Workload)
if (-not [string]::IsNullOrWhiteSpace($InstrumentationOutput) -and [string]::IsNullOrWhiteSpace($Workload)) {
    throw "InstrumentationOutput requires exactly one -Workload so the profile is attributable."
}
if ($Mode -eq "steady" -and $BenchmarkRuntime -eq "aot" -and [string]::IsNullOrWhiteSpace($Workload)) {
    $workloads = @(dotnet $referenceRunner --list)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
elseif ($Mode -eq "steady" -and [string]::IsNullOrWhiteSpace($Workload)) {
    $workloads = @($null)
}
elseif ($Mode -eq "cold" -and [string]::IsNullOrWhiteSpace($Workload)) {
    $workloads = @(dotnet $referenceRunner --list)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$rawDirectory = Join-Path $LabRoot "reports/raw/$profileSlug-player-$Mode-$runId"
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
$resultPaths = @()
foreach ($workloadId in $workloads) {
    for ($index = 1; $index -le $Processes; $index++) {
        $slug = if ([string]::IsNullOrWhiteSpace($workloadId)) { "all" } else { $workloadId }
        $resultPath = Join-Path $rawDirectory ("{0}-{1:D3}.json" -f $slug, $index)
        $repeat = 1
        if ($BenchmarkRuntime -eq "aot" -and $Mode -eq "steady") {
            $repeat = switch ($workloadId) {
                "aot_to_interp_boundary" { 24; break }
                "interp_arithmetic" { 24; break }
                "interp_array" { 32; break }
                "interp_boxing" { 1; break }
                "interp_boxing_escape" { 1; break }
                "interp_boxing_mixed" { 1; break }
                "interp_branch" { 20; break }
                "interp_call" { 64; break }
                "interp_delegate" { 16; break }
                "interp_exception" { 1; break }
                "interp_field" { 24; break }
                "interp_float" { 8; break }
                "interp_generic" { 20; break }
                "interp_string_allocation" { 1; break }
                "interp_struct" { 8; break }
                "interp_to_aot_boundary" { 24; break }
                "interp_virtual" { 8; break }
                default { 1; break }
            }
        }
        $playerArgs = @(
            "-batchmode", "-nographics",
            "-labTarget", "StandaloneWindows64",
            "-labMode", "benchmark",
            "-labBenchmarkRuntime", $BenchmarkRuntime,
            "-labAotMetadataMode", $AotMetadataMode,
            "-labBenchmarkMode", $Mode,
            "-labWarmupBatches", [string]$policy.warmupBatches,
            "-labMeasurementBatches", [string]$policy.measurementBatches,
            "-labBenchmarkRepeat", [string]$repeat,
            "-labBenchmarkResult", $resultPath
        )
        if ($BenchmarkRuntime -eq "aot") {
            $playerArgs += @("-labAotAssemblySha256", [string]$build.aotBenchmarkAssemblySha256)
        }
        if (-not [string]::IsNullOrWhiteSpace($workloadId)) {
            $playerArgs += @("-labBenchmarkWorkload", $workloadId)
        }
        if (-not [string]::IsNullOrWhiteSpace($InstrumentationOutput)) {
            $profilePath = if ([IO.Path]::IsPathRooted($InstrumentationOutput)) {
                [IO.Path]::GetFullPath($InstrumentationOutput)
            } else {
                [IO.Path]::GetFullPath((Join-Path $LabRoot $InstrumentationOutput))
            }
            $playerArgs += @("-labInstrumentationResult", $profilePath)
        }

        $process = Start-Process -FilePath $player -ArgumentList $playerArgs -PassThru -WindowStyle Hidden
        $exited = $process.WaitForExit($PlayerTimeoutSeconds * 1000)
        if (-not $exited) {
            try { $process.Kill() } catch { }
            throw "Player benchmark timed out for '$slug' sample $index after $PlayerTimeoutSeconds seconds."
        }
        if ($process.ExitCode -ne 0) {
            throw "Player benchmark failed for '$slug' sample $index with exit code $($process.ExitCode)."
        }
        if (-not (Test-Path $resultPath)) { throw "Player benchmark did not produce: $resultPath" }
        $result = Get-Content -Raw $resultPath | ConvertFrom-Json
        if ($result.benchmarkMode -ne $Mode -or $result.aotMetadataMode -ne $AotMetadataMode -or
            $result.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
            $result.buildIdentity.hybridclrUnityTreeSha256 -ne $build.hybridclrUnityTreeSha256 -or
            $result.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
            $result.buildIdentity.managedAssemblySha256 -ne $build.managedAssemblySha256 -or
            $result.goldenContractSha256 -ne $build.benchmarkGoldenSha256 -or $result.workloads.Count -lt 1) {
            throw "Player benchmark produced an invalid result: $resultPath"
        }
        $resultPaths += $resultPath
        Write-Host ("[player-benchmark] {0}/{1} {2} {3} repeat={4}" -f $BenchmarkRuntime, $slug, $index, $Processes, $repeat)
    }
}

if ((Get-FileHash -LiteralPath $buildManifest -Algorithm SHA256).Hash -ne $buildManifestSha256) {
    throw "Build manifest changed while the benchmark was running; refusing to summarize mixed identities."
}
if ((Get-FileHash -LiteralPath $player -Algorithm SHA256).Hash -ne $actualPlayerHash -or
    (Get-FileHash -LiteralPath $gameAssemblies[0].FullName -Algorithm SHA256).Hash -ne $actualGameAssemblyHash) {
    throw "Player artifacts changed while the benchmark was running."
}
if ((Get-FileHash -LiteralPath $policyPath -Algorithm SHA256).Hash -ne $policySha256) {
    throw "Benchmark policy changed while the benchmark was running."
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $metadataSuffix = if ($AotMetadataMode -eq "none") { "-none" } else { "" }
    $Output = "reports/$profileSlug-player-$Mode$metadataSuffix-benchmark.json"
}
& (Join-Path $PSScriptRoot "summarize-benchmark.ps1") `
    -LabRoot $LabRoot `
    -InputPath $resultPaths `
    -Output $Output `
    -BuildManifest $buildManifest
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
