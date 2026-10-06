param(
    [Parameter(Mandatory=$true)][string]$BuildRoot,
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$BuildRoot=[IO.Path]::GetFullPath($BuildRoot)
$labRoot=Split-Path -Parent $PSScriptRoot
$project=Join-Path $BuildRoot 'current-assets-project'
if(Test-Path -LiteralPath $project){throw 'Preserve prior bundle evidence; use a new build root'}
$bundleRoot=Join-Path $BuildRoot 'candidate/shared/bundles'
$archive=Join-Path $BuildRoot 'candidate/shared/base-bundles'
if(Test-Path -LiteralPath $archive){throw 'Base bundle archive already exists'}
# Move only this build's fixture output; never modify the source or frozen DLLs.
if(-not $bundleRoot.StartsWith($BuildRoot+[IO.Path]::DirectorySeparatorChar) -or
   -not $archive.StartsWith($BuildRoot+[IO.Path]::DirectorySeparatorChar)){throw 'Bundle paths outside build root'}
if(Test-Path -LiteralPath $bundleRoot){Move-Item -LiteralPath $bundleRoot -Destination $archive}
New-Item -ItemType Directory -Path "$project/Assets/Plugins","$project/Assets/Editor","$project/Packages","$project/ProjectSettings" | Out-Null
Copy-Item -LiteralPath "$BuildRoot/candidate/shared/StartupUnityHotfix.dll" -Destination "$project/Assets/Plugins/StartupUnityHotfix.dll"
Copy-Item -LiteralPath "$labRoot/fixtures/aot-mode-current-assets/BuildCurrentAssets.cs" -Destination "$project/Assets/Editor/BuildCurrentAssets.cs"
'{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}' | Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
'm_EditorVersion: 2022.3.62f3' | Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Encoding utf8
$start=[Diagnostics.ProcessStartInfo]::new("$EditorRoot/Editor/Unity.exe")
$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
foreach($arg in @('-batchmode','-nographics','-quit','-projectPath',$project,'-executeMethod','BuildCurrentAssets.Run','-bundleOutput',$bundleRoot,'-logFile',"$BuildRoot/current-assets.log")){$start.ArgumentList.Add($arg)}
$process=[Diagnostics.Process]::Start($start)
try {
    if(-not $process.WaitForExit(300000)){$process.Kill($true);throw 'Current bundle build timed out'}
    if($process.ExitCode -ne 0 -or -not(Test-Path -LiteralPath "$bundleRoot/current-assets.json")){throw 'Current bundle build failed'}
} finally {$process.Dispose()}
if((Get-FileHash "$project/Assets/Plugins/StartupUnityHotfix.dll").Hash -ne (Get-FileHash "$BuildRoot/candidate/shared/StartupUnityHotfix.dll").Hash){throw 'Current bundle DLL identity drift'}
Write-Host 'Current-only prefab, ScriptableObject and scene bundles built from the frozen Current DLL'
