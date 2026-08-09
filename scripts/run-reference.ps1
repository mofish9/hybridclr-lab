param(
    [string]$Output = "reports/reference-result.json"
)

$ErrorActionPreference = "Stop"
$labRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $labRoot "runners/dotnet-reference/HybridCLR.ReferenceRunner.csproj"
$manifest = Join-Path $labRoot "manifests/test-manifest.json"
$outputPath = Join-Path $labRoot $Output

dotnet run --project $project --configuration Release -- --manifest $manifest --output $outputPath
if ($LASTEXITCODE -ne 0)
{
    exit $LASTEXITCODE
}

