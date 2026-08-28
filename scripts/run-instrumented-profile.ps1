param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = "reports/baseline-instrumented-opcode-profile.json",
    [switch]$SkipExisting
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$referenceProject = Join-Path $LabRoot "runners/benchmark-reference/HybridCLR.BenchmarkReference.csproj"
$referenceRunner = Join-Path $LabRoot "runners/benchmark-reference/bin/Release/net6.0/HybridCLR.BenchmarkReference.dll"
$profileDirectory = Join-Path $LabRoot "reports/raw/baseline-instrumented-opcode"
$diagnosticDirectory = Join-Path $LabRoot "reports/raw/baseline-instrumented-benchmark"

dotnet build $referenceProject --configuration Release --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$workloads = @(dotnet $referenceRunner --list)
if ($LASTEXITCODE -ne 0 -or $workloads.Count -eq 0) { throw "Unable to enumerate benchmark workloads." }
New-Item -ItemType Directory -Force -Path $profileDirectory, $diagnosticDirectory | Out-Null

foreach ($workload in $workloads) {
    $profilePath = Join-Path $profileDirectory "$workload.json"
    if ($SkipExisting -and (Test-Path $profilePath)) {
        Write-Host "[instrumentation] existing $workload"
        continue
    }

    Write-Host "[instrumentation] $workload"
    & (Join-Path $PSScriptRoot "run-benchmark-player.ps1") `
        -LabRoot $LabRoot `
        -Profile Baseline-Instrumented `
        -Mode steady `
        -BenchmarkRuntime hybridclr `
        -Processes 1 `
        -Workload $workload `
        -Output (Join-Path $diagnosticDirectory "$workload.json") `
        -InstrumentationOutput $profilePath
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

& (Join-Path $PSScriptRoot "summarize-instrumentation.ps1") `
    -LabRoot $LabRoot `
    -InputDirectory $profileDirectory `
    -Output $Output
