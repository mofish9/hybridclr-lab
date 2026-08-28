param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("cold", "steady")]
    [string]$Mode = "steady",
    [int]$Processes = 0,
    [string]$Workload = "",
    [string]$Output = ""
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$policyPath = Join-Path $LabRoot "manifests/benchmark-policy.json"
$goldenPath = Join-Path $LabRoot "manifests/benchmark-golden.json"
$policy = Get-Content -Raw $policyPath | ConvertFrom-Json
if ($Processes -le 0) { $Processes = [int]$policy.minimumIndependentProcesses }
if ($Processes -lt 1) { throw "Processes must be at least 1." }
$project = Join-Path $LabRoot "runners/benchmark-reference/HybridCLR.BenchmarkReference.csproj"
$runner = Join-Path $LabRoot "runners/benchmark-reference/bin/Release/net6.0/HybridCLR.BenchmarkReference.dll"
if (-not (Test-Path $goldenPath)) { throw "Benchmark golden contract was not found: $goldenPath" }
dotnet build $project --configuration Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$workloads = @($Workload)
if ($Mode -eq "steady" -and [string]::IsNullOrWhiteSpace($Workload)) {
    $workloads = @($null)
}
elseif ($Mode -eq "cold" -and [string]::IsNullOrWhiteSpace($Workload)) {
    $workloads = @(dotnet $runner --list)
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
elseif ([string]::IsNullOrWhiteSpace($Workload)) {
    $workloads = @($null)
}

$runId = [DateTimeOffset]::UtcNow.ToString("yyyyMMddTHHmmssfffZ")
$rawDirectory = Join-Path $LabRoot "reports/raw/reference-$Mode-$runId"
New-Item -ItemType Directory -Force -Path $rawDirectory | Out-Null
$resultPaths = @()
foreach ($workloadId in $workloads) {
    for ($index = 1; $index -le $Processes; $index++) {
        $slug = if ([string]::IsNullOrWhiteSpace($workloadId)) { "all" } else { $workloadId }
        $resultPath = Join-Path $rawDirectory ("{0}-{1:D3}.json" -f $slug, $index)
        $arguments = @($runner, "--policy", $policyPath, "--golden", $goldenPath, "--mode", $Mode, "--output", $resultPath)
        if (-not [string]::IsNullOrWhiteSpace($workloadId)) { $arguments += @("--workload", $workloadId) }
        dotnet @arguments
        if ($LASTEXITCODE -ne 0) { throw "Reference benchmark process failed for '$slug' sample $index." }
        $resultPaths += $resultPath
    }
}

if ([string]::IsNullOrWhiteSpace($Output)) {
    $Output = "reports/reference-$Mode-benchmark.json"
}
& (Join-Path $PSScriptRoot "summarize-benchmark.ps1") -LabRoot $LabRoot -InputPath $resultPaths -Output $Output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
