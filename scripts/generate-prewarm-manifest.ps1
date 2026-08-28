param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)][string]$Assembly,
    [Parameter(Mandatory = $true)][string]$RootType,
    [Parameter(Mandatory = $true)][string]$RootMethod,
    [int]$RootParameterCount = -1,
    [int]$MaxDepth = 64,
    [int]$MaxMethods = 16384,
    [Parameter(Mandatory = $true)][string]$OutputJson,
    [Parameter(Mandatory = $true)][string]$OutputCSharp
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$assemblyPath = if ([IO.Path]::IsPathRooted($Assembly)) { $Assembly } else { Join-Path $LabRoot $Assembly }
$jsonPath = if ([IO.Path]::IsPathRooted($OutputJson)) { $OutputJson } else { Join-Path $LabRoot $OutputJson }
$csharpPath = if ([IO.Path]::IsPathRooted($OutputCSharp)) { $OutputCSharp } else { Join-Path $LabRoot $OutputCSharp }
$projectPath = Join-Path $LabRoot "runners/prewarm-manifest/PrewarmManifest.csproj"

dotnet run --project $projectPath -- `
    --assembly $assemblyPath `
    --root-type $RootType `
    --root-method $RootMethod `
    --root-parameter-count $RootParameterCount `
    --max-depth $MaxDepth `
    --max-methods $MaxMethods `
    --output-json $jsonPath `
    --output-cs $csharpPath
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
