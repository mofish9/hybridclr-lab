param(
    [string]$PairRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/as'),
    [string]$ReportRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'reports/aot-selection-probe-v1')
)
$ErrorActionPreference='Stop'
$PairRoot=[IO.Path]::GetFullPath($PairRoot);$ReportRoot=[IO.Path]::GetFullPath($ReportRoot)
if(Test-Path -LiteralPath $ReportRoot){throw 'Report directory must be new'}
New-Item -ItemType Directory -Path $ReportRoot | Out-Null
$manifest=Get-Content (Join-Path $PairRoot 'probe-build.json') -Raw | ConvertFrom-Json
foreach($profile in @('DHE','LegacyInterpreter')){
    if((Get-FileHash (Join-Path $PairRoot "$profile/player/GameAssembly.dll")).Hash.ToLowerInvariant() -ne $manifest.$profile.gameAssemblySha256){throw "Stale $profile backend"}
    if((Get-FileHash (Join-Path $PairRoot "$profile/player/StartupPlayer_Data/il2cpp_data/Metadata/global-metadata.dat")).Hash.ToLowerInvariant() -ne $manifest.$profile.metadataSha256){throw "Stale $profile metadata"}
}
& (Join-Path $PairRoot 'reference/Reference.exe') $PairRoot (Join-Path $ReportRoot 'clr-reference.json')
if($LASTEXITCODE -ne 0){throw 'CLR reference failed'}
$reference=Get-Content (Join-Path $ReportRoot 'clr-reference.json') -Raw | ConvertFrom-Json
if($reference.currentSha256 -ne $manifest.currentSha256 -or $reference.consumerSha256 -ne $manifest.consumerSha256){throw 'Reference identity drift'}
$results=[Collections.Generic.List[object]]::new()
function Run([string]$Name,[string]$Profile,[string]$Scenario){
    $report=Join-Path $ReportRoot "$Name.json"
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $PairRoot "$Profile/player/StartupPlayer.exe"))
    $start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($argument in @('-batchmode','-nographics','-scenario',$Scenario,'-startupPairRoot',$PairRoot,'-startupReport',$report,'-logFile',(Join-Path $ReportRoot "$Name.log"))){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start)
    try{if(-not $process.WaitForExit(60000)){$process.Kill($true);throw "$Name timed out"};$code=$process.ExitCode;$actualPid=$process.Id}finally{$process.Dispose()}
    if(-not(Test-Path -LiteralPath $report)){throw "$Name exited $code without report"}
    $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if($code -ne 0 -or -not $result.testPassed -or $result.pid -ne $actualPid){throw "$Name diagnostic failed: $($result | ConvertTo-Json -Depth 4 -Compress)"}
    if($result.currentSha256 -ne $manifest.currentSha256 -or $result.consumerSha256 -ne $manifest.consumerSha256){throw 'Player payload identity drift'}
    if($Scenario -ne 'legacy-poison'){
        if($result.caseCount -ne $reference.caseCount -or ($result.actual -join ',') -ne ($reference.actual -join ',')){throw "$Name CLR differential"}
    }elseif(-not $result.faultObserved -or $result.workloadPassed){throw 'Legacy did not expose shared DHE hook dependency'}
    $results.Add(@{name=$Name;profile=$Profile;scenario=$Scenario;result=$result})
    Write-Host "${Name}: mode=$($result.mode), cases=$($result.caseCount), DHE-hook calls=$($result.reflectionHookCalls+$result.allocationHookCalls+$result.fieldHookCalls), fault=$($result.faultObserved)"
    return $result
}
$legacy=Run '01-reference-traditional' 'LegacyInterpreter' 'legacy'
$dhe=Run '02-aot-select-dhe' 'DHE' 'dhe'
$interpreted=Run '03-aot-select-interpreter' 'DHE' 'legacy'
$preaccess=Run '04-preselection-generated-reference' 'DHE' 'preaccess-legacy'
$poison=Run '05-shared-dhe-hook-fault' 'DHE' 'legacy-poison'
$race=Run '06-concurrent-single-selection' 'DHE' 'concurrent'
if($race.concurrentSuccesses -ne 1 -or @($race.concurrentReturns | Where-Object {$_ -ne 0 -and $_ -ne 1}).Count){throw 'Concurrent selector failed'}
$mv=Join-Path $PairRoot 'shared/base.mv';$saved=[IO.File]::ReadAllBytes($mv)
try{$broken=[byte[]]$saved.Clone();$broken[0]=$broken[0] -bxor 1;[IO.File]::WriteAllBytes($mv,$broken);$ignored=Run '07-interpreter-ignores-dhe-mv' 'DHE' 'legacy'}finally{[IO.File]::WriteAllBytes($mv,$saved)}
if(($interpreted.reflectionHookCalls+$interpreted.allocationHookCalls+$interpreted.fieldHookCalls) -le 0){throw 'DHE hook reachability not observed'}
if(@($results | ForEach-Object {$_.result.pid} | Sort-Object -Unique).Count -ne $results.Count){throw 'PID reuse'}
$summary=[ordered]@{
    format='hybridclr.aot-selection-validation.v1';generatedAtUtc=[DateTime]::UtcNow.ToString('o');diagnosticTestsPassed=$true
    aotManagedEntryWorks=$true;sameCandidatePlayerBothModes=$true;sameCurrentAndConsumer=$true;ordinaryLoadWorkloadDifferential=0;referenceCaseCount=$reference.caseCount
    delayedPublicationIsFullIsolation=$false;generatedBaseReferenceEscaped=$preaccess.preaccessEscaped
    legacyStillReachesDheHooks=$true;injectedDheHookFailureAffectsLegacy=$poison.faultObserved
    meetsFullFallbackRequirement=$false;generalAotModuleDesignDisproved=$false;productionReady=$false;mergeReady=$false
    qualifiesOtherEngines=$false;qualifiesAndroid=$false;qualifiesWebGl=$false;qualifiesIos=$false;performanceClaim=$false
    buildManifest=$manifest;buildManifestSha256=(Get-FileHash (Join-Path $PairRoot 'probe-build.json')).Hash.ToLowerInvariant();cases=@($results.ToArray())
}
[IO.File]::WriteAllText((Join-Path $ReportRoot 'summary.json'),($summary | ConvertTo-Json -Depth 12).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
Write-Output 'AOT selection mechanism validated; full isolation rejected by diagnostic counterexamples.'
