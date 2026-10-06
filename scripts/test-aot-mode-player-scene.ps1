param(
    [Parameter(Mandatory=$true)][string]$BuildRoot,
    [string]$OutputName='rejected-scene',
    [string]$EditorRoot='C:/Program Files/Unity/Hub/Editor/2022.3.62f3'
)
$ErrorActionPreference='Stop'
$labRoot=Split-Path -Parent $PSScriptRoot
$BuildRoot=[IO.Path]::GetFullPath($BuildRoot)
$project=Join-Path $BuildRoot 'candidate/projects/DHE'
if($OutputName -notmatch '^[a-z0-9-]+$'){throw 'Invalid output name'}
$output=Join-Path $BuildRoot $OutputName
if(Test-Path -LiteralPath $output){throw 'Preserve prior evidence; use a new probe output'}
New-Item -ItemType Directory -Path $output|Out-Null
# FixtureBuild creates bundles beside its input base directory. Isolate these
# inputs/outputs so the negative build cannot overwrite the release resources.
Copy-Item -LiteralPath "$BuildRoot/candidate/base" -Destination "$output/base" -Recurse
if(Test-Path -LiteralPath "$BuildRoot/candidate/shared/regression") {
    New-Item -ItemType Directory -Path "$output/shared"|Out-Null
    Copy-Item -LiteralPath "$BuildRoot/candidate/shared/regression" -Destination "$output/shared/regression" -Recurse
}
$fixture=Join-Path $labRoot 'fixtures/aot-mode-assets/PlayerSceneProbe.cs.txt'
$staged=Join-Path $project 'Assets/Editor/AotModePlayerSceneProbe.cs'
if(Test-Path -LiteralPath $staged){throw 'Probe source already exists'}
Copy-Item -LiteralPath $fixture -Destination $staged
$log=Join-Path $output 'editor.log'
$start=[Diagnostics.ProcessStartInfo]::new("$EditorRoot/Editor/Unity.exe")
$start.UseShellExecute=$false;$start.CreateNoWindow=$true
foreach($arg in @('-batchmode','-nographics','-quit','-projectPath',$project,'-executeMethod','AotModePlayerSceneProbe.Run','-startupRuntime',"$BuildRoot/candidate/sources/DHE/il2cpp_plus/libil2cpp",'-startupBaseRoot',"$output/base",'-startupOutput',$output,'-logFile',$log)){$start.ArgumentList.Add($arg)}
$process=[Diagnostics.Process]::Start($start)
$process.Id|Set-Content -LiteralPath "$output/pid.txt"
try {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    while(-not $process.WaitForExit(1000)) {
        if($watch.Elapsed.TotalSeconds -gt 600){$process.Kill($true);throw 'Player scene gate timed out'}
    }
    $content=Get-Content $log -Raw
    $passed=$process.ExitCode -eq 0 -and $content.Contains('AOT_GATE_PLAYER_BUILD_REJECTED') -and
        $content.Contains("serializes deferred hotfix object 'AOT gate injected component'")
    @{passed=$passed;scope='Real Player scene callback must reject deferred objects; bundle callbacks remain permitted';fixtureSha256=(Get-FileHash $fixture).Hash.ToLowerInvariant();sources=(Get-Content "$BuildRoot/candidate/source-identities.json" -Raw|ConvertFrom-Json).DHE}|ConvertTo-Json -Depth 5|Set-Content -LiteralPath "$output/gate.json" -Encoding utf8
    if(-not $passed){throw 'Player scene rejection gate failed; inspect editor.log'}
    Write-Host 'Real Player scene rejection gate passed'
} finally {
    # Only remove the exact files created for this probe, after its Editor exits.
    if($process.HasExited){
        Remove-Item -LiteralPath $staged
        if(Test-Path -LiteralPath "$staged.meta"){Remove-Item -LiteralPath "$staged.meta"}
    }
    $process.Dispose()
}
