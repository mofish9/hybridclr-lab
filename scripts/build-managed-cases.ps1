param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$UnityProjectRoot = "",
    [string]$Target = "StandaloneWindows64",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$UnityProjectRoot = if ([string]::IsNullOrWhiteSpace($UnityProjectRoot)) {
    Join-Path $LabRoot "unity-test-project"
} else {
    [IO.Path]::GetFullPath($UnityProjectRoot)
}
$manifestScript = Join-Path $LabRoot "scripts/generate-test-manifest.ps1"
& $manifestScript -LabRoot $LabRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$stressSourceScript = Join-Path $LabRoot "scripts/generate-metadata-stress-source.ps1"
& $stressSourceScript -LabRoot $LabRoot
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$project = Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCases/HybridCLR.ManagedCases.csproj"
$crossAssemblyProject = Join-Path $LabRoot "managed-cases/HybridCLR.CrossAssemblyDerived/HybridCLR.CrossAssemblyDerived.csproj"
$aotProject = Join-Path $LabRoot "managed-cases/HybridCLR.ManagedCasesAot/HybridCLR.ManagedCasesAot.csproj"
$stressProject = Join-Path $LabRoot "managed-cases/HybridCLR.MetadataStress/HybridCLR.MetadataStress.csproj"
$output = Join-Path $LabRoot "artifacts/managed-cases/$Target"
$targetDefine = switch ($Target) {
    "StandaloneWindows64" { "HYBRIDCLR_TARGET_WINDOWS"; break }
    "Android" { "HYBRIDCLR_TARGET_ANDROID"; break }
    default { ""; break }
}
if (Test-Path $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Force -Path $output | Out-Null

$managedBuildArguments = @("build", $project, "--configuration", $Configuration, "--output", $output, "--nologo", "-v:minimal")
if (-not [string]::IsNullOrWhiteSpace($targetDefine)) {
    $managedBuildArguments += "-p:DefineConstants=$targetDefine"
}
dotnet @managedBuildArguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $crossAssemblyProject --configuration $Configuration --output $output --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $stressProject --configuration $Configuration --output $output --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = Join-Path $output "HybridCLR.ManagedCases.dll"
if (-not (Test-Path $dll)) { throw "Managed cases build did not produce $dll" }
$contractDll = Join-Path $output "HybridCLR.BoundaryContracts.dll"
if (-not (Test-Path $contractDll)) { throw "Boundary contracts build did not produce $contractDll" }
$stressDll = Join-Path $output "HybridCLR.MetadataStress.dll"
if (-not (Test-Path $stressDll)) { throw "Metadata stress build did not produce $stressDll" }
$crossAssemblyDll = Join-Path $output "HybridCLR.CrossAssemblyDerived.dll"
if (-not (Test-Path $crossAssemblyDll)) { throw "Cross-assembly probe build did not produce $crossAssemblyDll" }
$pluginDirectory = Join-Path $UnityProjectRoot "Assets/Plugins/HybridCLRLab"
New-Item -ItemType Directory -Force -Path $pluginDirectory | Out-Null
Copy-Item -LiteralPath $contractDll -Destination (Join-Path $pluginDirectory "HybridCLR.BoundaryContracts.dll") -Force
$aotBuildOutput = Join-Path $LabRoot "artifacts/managed-cases-aot/$Target"
if (Test-Path $aotBuildOutput) { Remove-Item -LiteralPath $aotBuildOutput -Recurse -Force }
New-Item -ItemType Directory -Force -Path $aotBuildOutput | Out-Null
dotnet build $aotProject --configuration $Configuration --output $aotBuildOutput --nologo -v:minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$aotBuildDll = Join-Path $aotBuildOutput "HybridCLR.ManagedCasesAot.dll"
if (-not (Test-Path $aotBuildDll)) { throw "AOT benchmark assembly build did not produce $aotBuildDll" }
$aotOutput = Join-Path $output "Aot"
New-Item -ItemType Directory -Force -Path $aotOutput | Out-Null
Get-ChildItem -LiteralPath $aotBuildOutput -File | Copy-Item -Destination $aotOutput -Force
$aotDll = Join-Path $aotOutput "HybridCLR.ManagedCasesAot.dll"
Copy-Item -LiteralPath $aotDll -Destination (Join-Path $pluginDirectory "HybridCLR.ManagedCasesAot.dll") -Force
Write-Host "Managed cases: $dll"
Write-Host "Cross-assembly probe: $crossAssemblyDll"
Write-Host "Metadata stress assembly: $stressDll"
Write-Host "AOT boundary contract: $contractDll"
Write-Host "AOT benchmark assembly: $aotDll"
