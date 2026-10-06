param(
    [Parameter(Mandatory=$true)][string]$BuildRoot,
    [ValidateSet('Prepare','Build')][string]$Stage='Prepare',
    [string]$WorkspaceRoot='C:/hybridclr_optimize',
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$BuildRoot=[IO.Path]::GetFullPath($BuildRoot)
$labRoot=Split-Path -Parent $PSScriptRoot
$fixture=Join-Path $labRoot 'fixtures/aot-mode-release'
$helper=Join-Path $PSScriptRoot 'build-dhe-startup-windows.ps1'
$refs=(Get-Content "$BuildRoot/candidate/source-identities.json" -Raw|ConvertFrom-Json).DHE
if($Stage -eq 'Prepare') {
    & $helper -WorkspaceRoot $WorkspaceRoot -OutputRoot "$BuildRoot/fixed" -EditorRoot $EditorRoot -Stage Prepare -Profiles DHE -FixtureRootOverride $fixture -DheHybridClrRef $refs.hybridclr -DheIl2CppRef $refs.il2cpp_plus -DhePackageRef $refs.hybridclr_unity
    New-Item -ItemType Directory -Path "$BuildRoot/fixed/projects/DHE/Assets/Plugins/x86_64" -Force | Out-Null
    Copy-Item -LiteralPath "$BuildRoot/native-image-probe/Release/AotImageProbe.dll" -Destination "$BuildRoot/fixed/projects/DHE/Assets/Plugins/x86_64/AotImageProbe.dll"
    foreach($name in @('StartupHotfix','StartupUnityHotfix','StartupAotSupport')) {
        if((Get-FileHash "$BuildRoot/candidate/shared/$name.dll").Hash -ne (Get-FileHash "$BuildRoot/fixed/shared/$name.dll").Hash){throw "Fixed-mode workload drift: $name"}
    }
    Copy-Item -LiteralPath "$BuildRoot/candidate/shared/StartupConsumer.dll" -Destination "$BuildRoot/fixed/shared/StartupConsumer.dll"
    Copy-Item -LiteralPath "$BuildRoot/candidate/shared/regression" -Destination "$BuildRoot/fixed/shared/regression" -Recurse
    foreach($name in @('HybridCLR.ManagedCases','HybridCLR.CrossAssemblyDerived','HybridCLR.BoundaryContracts','AotModeRegression')) {
        Copy-Item -LiteralPath "$BuildRoot/fixed/shared/regression/$name.dll" -Destination "$BuildRoot/fixed/projects/DHE/Assets/Plugins/$name.dll"
    }
} else {
    & $helper -WorkspaceRoot $WorkspaceRoot -OutputRoot "$BuildRoot/fixed" -EditorRoot $EditorRoot -Stage DHE -FixtureRootOverride $fixture
}
