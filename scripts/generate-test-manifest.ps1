param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Output = "manifests/test-manifest.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$project = Join-Path $LabRoot "runners/manifest-generator/HybridCLR.ManifestGenerator.csproj"
$outputPath = if ([IO.Path]::IsPathRooted($Output)) { $Output } else { Join-Path $LabRoot $Output }

dotnet run --project $project --configuration Release --no-restore -- $outputPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$manifest = Get-Content -Raw $outputPath | ConvertFrom-Json
$idsPath = [IO.Path]::ChangeExtension($outputPath, ".ids")
$lines = @("suiteId=$($manifest.suiteId)") + @($manifest.cases | ForEach-Object { $_.id })
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($idsPath, ([string]::Join("`n", $lines) + "`n"), $utf8NoBom)
Write-Host "Generated manifest index: $idsPath"
$contractsPath = [IO.Path]::ChangeExtension($outputPath, ".contracts")
$contractLines = @("suiteId=$($manifest.suiteId)") + @($manifest.cases | ForEach-Object {
    "$($_.id)`t$($_.category)`t$($_.layer)`t$([string]::Join(',', @($_.features)))"
})
[IO.File]::WriteAllText($contractsPath, ([string]::Join("`n", $contractLines) + "`n"), $utf8NoBom)
Write-Host "Generated manifest contracts: $contractsPath"
