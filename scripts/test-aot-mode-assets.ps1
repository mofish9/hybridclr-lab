param(
    [string]$PackageRoot='C:/hybridclr_optimize/worktrees/hybridclr-unity-aot-select-v1',
    [string]$OutputRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/aot-assets-v2'),
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3',
    [switch]$AllowDirty
)
$ErrorActionPreference='Stop'
$labRoot=Split-Path -Parent $PSScriptRoot
$OutputRoot=[IO.Path]::GetFullPath($OutputRoot)
if(Test-Path -LiteralPath $OutputRoot){throw 'Use a new output directory'}
$dirty=@(& git -C $PackageRoot status --porcelain).Count -ne 0
if($dirty -and -not $AllowDirty){throw 'Commit package or use AllowDirty for exploratory checks'}
$packageCommit=(& git -C $PackageRoot rev-parse HEAD).Trim()
$project=Join-Path $OutputRoot 'project'
New-Item -ItemType Directory -Path "$project/Assets","$project/Packages","$project/ProjectSettings","$OutputRoot/package"|Out-Null
# Copy tracked source to a standalone package snapshot, preserving the live checkout.
foreach($file in & git -C $PackageRoot ls-files){
    $destination=Join-Path "$OutputRoot/package" $file
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination)|Out-Null
    Copy-Item -LiteralPath (Join-Path $PackageRoot $file) -Destination $destination
}
Copy-Item -Path "$labRoot/fixtures/aot-mode-assets/*" -Destination "$project/Assets" -Recurse
@{dependencies=@{'com.code-philosophy.hybridclr'="file:$($OutputRoot.Replace('\','/'))/package";'com.unity.modules.jsonserialize'='1.0.0'}}|ConvertTo-Json|Set-Content -LiteralPath "$project/Packages/manifest.json" -Encoding utf8
'm_EditorVersion: 2022.3.62f3'|Set-Content -LiteralPath "$project/ProjectSettings/ProjectVersion.txt" -Encoding utf8
$report=Join-Path $OutputRoot 'result.json'
$process=Start-Process -FilePath "$EditorRoot/Editor/Unity.exe" -ArgumentList @('-batchmode','-nographics','-quit','-projectPath',('"'+$project+'"'),'-executeMethod','AssetGateTests.Run','-assetGateReport',('"'+$report+'"'),'-logFile',('"'+(Join-Path $OutputRoot 'editor.log')+'"')) -WindowStyle Hidden -PassThru
$process.Id|Set-Content -LiteralPath "$OutputRoot/pid.txt"
$watch=[Diagnostics.Stopwatch]::StartNew()
while(-not $process.WaitForExit(1000)){
    if($watch.Elapsed.TotalSeconds -gt 180){Stop-Process -Id $process.Id;throw 'Asset validator timed out (possible cyclic traversal)'}
}
$process.Refresh()
if(-not(Test-Path -LiteralPath $report)){throw "Editor failed before report: $($process.ExitCode)"}
$result=Get-Content $report -Raw|ConvertFrom-Json
$identity=@{packageCommit=$packageCommit;packageDirty=$dirty;editor=$EditorRoot;labCommit=(& git -C $labRoot rev-parse HEAD).Trim();validatorSha256=(Get-FileHash "$OutputRoot/package/Editor/BuildProcessors/AotModeAssetValidator.cs").Hash.ToLowerInvariant();passed=$result.passed;cases=$result.cases;fixture=@{}}
Get-ChildItem "$labRoot/fixtures/aot-mode-assets" -Recurse -File|ForEach-Object{$identity.fixture[$_.FullName.Substring($labRoot.Length+1)]=(Get-FileHash $_.FullName).Hash.ToLowerInvariant()}
$identity|ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$OutputRoot/gate.json" -Encoding utf8
if($process.ExitCode -ne 0 -or -not $result.passed){throw "Asset regression failed: $($result.error)"}
Write-Host "Asset regression passed: $($result.cases.Count) cases, package=$packageCommit, dirty=$dirty"
