param(
    [string]$Output = "reports/reference-result.json"
)

$ErrorActionPreference = "Stop"
$labRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $labRoot "runners/dotnet-reference/HybridCLR.ReferenceRunner.csproj"
$manifest = Join-Path $labRoot "manifests/test-manifest.json"
$golden = Join-Path $labRoot "manifests/test-golden.json"
$outputPath = Join-Path $labRoot $Output

if (-not (Test-Path $manifest)) { throw "Test manifest was not found: $manifest" }
if (-not (Test-Path $golden)) { throw "Golden contract was not found: $golden" }

dotnet run --project $project --configuration Release -- --manifest $manifest --golden $golden --output $outputPath
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}
