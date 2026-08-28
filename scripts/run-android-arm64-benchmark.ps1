param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("cold", "steady")]
    [string]$Mode = "steady",
    [ValidateSet("hybridclr", "aot")]
    [string]$BenchmarkRuntime = "hybridclr",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "none",
    [string]$DeviceSerial = "",
    [string]$Apk = "",
    [int]$Processes = 0,
    [string]$Workload = "",
    [string]$Output = "",
    [int]$PlayerTimeoutSeconds = 300,
    [int]$InterProcessCooldownSeconds = 5,
    [int]$WarmupBatches = -1,
    [int]$MeasurementBatches = -1,
    [switch]$SkipInstall
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
. (Join-Path $PSScriptRoot "android-arm64-common.ps1")
$tools = Get-AndroidLabTools -LabRoot $LabRoot
$device = Get-AndroidLabDevice -Adb $tools.Adb -Serial $DeviceSerial
$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$profileSlug = $buildVariant.ToLowerInvariant()
$manifestPath = Join-Path $LabRoot "reports/$profileSlug-android-arm64-build-manifest.json"
$policyPath = Join-Path $LabRoot "manifests/benchmark-policy.json"
if (-not (Test-Path -LiteralPath $manifestPath)) { throw "Android build manifest was not found: $manifestPath" }
$build = Get-Content -Raw $manifestPath | ConvertFrom-Json
if ($build.target -ne "Android" -or $build.architecture -ne "arm64-v8a") { throw "Build manifest is not Android arm64-v8a." }
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") { throw "A no-metadata APK can only run with metadata mode none." }
if ($build.il2cppCodeGeneration -ne $Il2CppCodeGeneration -or $build.aotMetadataPackaging -ne $AotMetadataPackaging) {
    throw "Android build manifest does not match the requested benchmark configuration."
}
$packageName = [string]$build.applicationIdentifier
if ([string]::IsNullOrWhiteSpace($Apk)) { $Apk = Join-Path $LabRoot ([string]$build.apk.path) }
elseif (-not [IO.Path]::IsPathRooted($Apk)) { $Apk = Join-Path $LabRoot $Apk }
$Apk = [IO.Path]::GetFullPath($Apk)
if (-not (Test-Path -LiteralPath $Apk)) { throw "Android APK was not found: $Apk" }
if ((Get-FileHash -LiteralPath $Apk -Algorithm SHA256).Hash -ne $build.apk.sha256) { throw "APK hash does not match its build manifest." }
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json
if ($Processes -le 0) { $Processes = [int]$policy.minimumIndependentProcesses }
if ($Processes -lt 1) { throw "Processes must be at least 1." }
if ($InterProcessCooldownSeconds -lt 0) { throw "InterProcessCooldownSeconds cannot be negative." }
if ($WarmupBatches -lt -1 -or $MeasurementBatches -lt -1) { throw "Batch overrides must be -1 or non-negative." }
$effectiveWarmupBatches = if ($WarmupBatches -ge 0) { $WarmupBatches } else { [int]$policy.warmupBatches }
$effectiveMeasurementBatches = if ($MeasurementBatches -ge 0) { $MeasurementBatches } else { [int]$policy.measurementBatches }
if ($effectiveMeasurementBatches -lt 1) { throw "MeasurementBatches must be at least 1." }

$referenceProject = Join-Path $LabRoot "runners/benchmark-reference/HybridCLR.BenchmarkReference.csproj"
$referenceRunner = Join-Path $LabRoot "runners/benchmark-reference/bin/Release/net6.0/HybridCLR.BenchmarkReference.dll"
dotnet build $referenceProject --configuration Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$workloads = @($Workload)
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

if (-not $SkipInstall) {
    Install-AndroidLabApk -Adb $tools.Adb -Serial $device.Serial -Apk $Apk -PackageName $packageName
}
$activity = Get-AndroidLabActivity -Adb $tools.Adb -Serial $device.Serial -PackageName $packageName
Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "pm", "clear", $packageName) | Out-Null

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$rawDirectory = Join-Path $LabRoot "reports/raw/$profileSlug-android-arm64-$BenchmarkRuntime-$Mode-$runId"
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory "environment-before.json")
$deviceResult = "/sdcard/Android/data/$packageName/files/hybridclr-lab-player-benchmark.json"
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
        $unityArgs = @(
            "-batchmode", "-nographics",
            "-labTarget", "Android",
            "-labMode", "benchmark",
            "-labBenchmarkRuntime", $BenchmarkRuntime,
            "-labAotMetadataMode", $AotMetadataMode,
            "-labBenchmarkMode", $Mode,
            "-labWarmupBatches", [string]$effectiveWarmupBatches,
            "-labMeasurementBatches", [string]$effectiveMeasurementBatches,
            "-labBenchmarkRepeat", [string]$repeat
        )
        if ($BenchmarkRuntime -eq "aot") { $unityArgs += @("-labAotAssemblySha256", [string]$build.aotBenchmarkAssemblySha256) }
        if (-not [string]::IsNullOrWhiteSpace($workloadId)) { $unityArgs += @("-labBenchmarkWorkload", $workloadId) }

        Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
        Remove-AndroidLabDeviceFile -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult
        Start-AndroidLabPlayer -Adb $tools.Adb -Serial $device.Serial -Component $activity -UnityArguments $unityArgs
        $failureLog = Join-Path $rawDirectory ("{0}-{1:D3}-failure.log" -f $slug, $index)
        try {
            Receive-AndroidLabResult -Adb $tools.Adb -Serial $device.Serial -DevicePath $deviceResult `
                -LocalPath $resultPath -TimeoutSeconds $PlayerTimeoutSeconds -FailureLogPath $failureLog
        }
        finally {
            Invoke-LabAdb -Adb $tools.Adb -Serial $device.Serial -CommandArguments @("shell", "am", "force-stop", $packageName) -AllowFailure | Out-Null
        }
        $result = Get-Content -Raw $resultPath | ConvertFrom-Json
        $expectedAssemblyHash = if ($BenchmarkRuntime -eq "aot") { [string]$build.aotBenchmarkAssemblySha256 } else { [string]$build.managedAssemblySha256 }
        if ($result.architecture -ne "arm64" -or [string]$result.platform -notmatch "Android" -or
            $result.benchmarkMode -ne $Mode -or $result.aotMetadataMode -ne $AotMetadataMode -or
            $result.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
            $result.managedAssemblySha256 -ne $expectedAssemblyHash -or
            $result.policySha256 -ne $build.benchmarkPolicySha256 -or
            $result.goldenContractSha256 -ne $build.benchmarkGoldenSha256 -or $result.workloads.Count -lt 1) {
            throw "Android benchmark produced an invalid or mismatched result: $resultPath"
        }
        $resultPaths += $resultPath
        Write-Host ("[android-arm64] {0}/{1} {2}/{3}, repeat={4}" -f $BenchmarkRuntime, $slug, $index, $Processes, $repeat)
        if ($InterProcessCooldownSeconds -gt 0) { Start-Sleep -Seconds $InterProcessCooldownSeconds }
    }
}
& (Join-Path $PSScriptRoot "capture-android-environment.ps1") -LabRoot $LabRoot -DeviceSerial $device.Serial -Output (Join-Path $rawDirectory "environment-after.json")
if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = "reports/$profileSlug-android-arm64-player-$BenchmarkRuntime-$Mode-benchmark.json"
}
& (Join-Path $PSScriptRoot "summarize-benchmark.ps1") -LabRoot $LabRoot -InputPath $resultPaths -Output $Output -BuildManifest $manifestPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Android ARM64 benchmark summary: $Output"
