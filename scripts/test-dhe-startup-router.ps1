param(
    [string]$PairRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-router-player'),
    [string]$ReportRoot=(Join-Path (Split-Path -Parent $PSScriptRoot) 'reports/startup-router-v1'),
    [string]$ReferenceLauncher=(Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-windows-v1/launcher/StartupLauncher.exe')
)
$ErrorActionPreference='Stop'
$PairRoot=[IO.Path]::GetFullPath($PairRoot); $ReportRoot=[IO.Path]::GetFullPath($ReportRoot)
if(Test-Path -LiteralPath $ReportRoot){throw 'Report directory must be new.'}
New-Item -ItemType Directory -Path $ReportRoot | Out-Null
$store=Join-Path $ReportRoot 'selection.bin'
$pair=Get-Content (Join-Path $PairRoot 'startup-pair.json') -Raw | ConvertFrom-Json
$referencePath=Join-Path $ReportRoot 'reference.json'
& $ReferenceLauncher --root $PairRoot --reference --report $referencePath
if($LASTEXITCODE -ne 0){throw 'CLR reference failed.'}
$reference=Get-Content $referencePath -Raw | ConvertFrom-Json
if($reference.currentSha256 -ne $pair.currentSha256){throw 'Reference identity differs.'}
$cases=[Collections.Generic.List[object]]::new()
function Run([string]$Name,[string]$Mode,[string[]]$Extra=@(),[bool]$ExpectedFailure=$false) {
    $report=Join-Path $ReportRoot "$Name.json"
    $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $PairRoot 'StartupPlayer.exe'))
    $start.UseShellExecute=$false; $start.CreateNoWindow=$true; $start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($argument in @('-batchmode','-nographics','-startupStore',$store,'-startupPairRoot',$PairRoot,'-startupReport',$report,'-logFile',(Join-Path $ReportRoot "$Name.log"))+$Extra){$start.ArgumentList.Add($argument)}
    $process=[Diagnostics.Process]::Start($start)
    try{if(-not $process.WaitForExit(60000)){$process.Kill($true);throw "$Name timed out."};$exitCode=$process.ExitCode;$pidValue=$process.Id}finally{$process.Dispose()}
    if($exitCode -ne $(if($ExpectedFailure){2}else{0})){throw "$Name exit=$exitCode; inspect $Name.log"}
    $result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if($result.pid -ne $pidValue -or $result.effectiveMode -ne $Mode -or -not $result.profileMatchesBackend -or -not $result.selectionImmutable -or -not $result.nativePrepareRejected){throw "$Name selection/backend identity mismatch."}
    if(-not $ExpectedFailure){
        if(-not $result.passed -or $result.caseCount -ne $reference.caseCount -or ($result.actual -join ',') -ne ($reference.actual -join ',') -or $result.currentSha256 -ne $pair.currentSha256){throw "$Name correctness mismatch."}
        if($Mode -eq 'LegacyInterpreter' -and $result.hotfixPresentBeforeLoad){throw 'Legacy retained DHE hotfix AOT identity.'}
        if($Mode -eq 'DHE' -and (-not $result.changedSelected -or $result.unchangedSelected -or $result.aotEntries -le 0 -or $result.interpreterEntries -le 0)){throw 'DHE dispatch evidence missing.'}
    }elseif($result.passed -or $result.nativeLoadCode -eq 0){throw 'Injected failure did not occur.'}
    $cases.Add(@{name=$Name;mode=$Mode;pid=$pidValue;caseCount=$result.caseCount;backendMask=$result.nativeBackendMask;expectedFailure=$ExpectedFailure;selectedGeneration=$result.selectedGeneration;committedGeneration=$result.committedGeneration})
    Write-Host "$Name passed: PID $pidValue, $Mode, backend mask $($result.nativeBackendMask)"
    return $result
}
$first=Run '01-default-save-legacy' 'DHE' @('-startupNextMode','LegacyInterpreter'); if(-not $first.restartRequired -or $first.committedGeneration -ne 1){throw 'Commit did not acknowledge.'}
$null=Run '02-restarted-legacy' 'LegacyInterpreter'
$null=Run '03-sticky-legacy' 'LegacyInterpreter'
$null=Run '04-save-dhe' 'LegacyInterpreter' @('-startupNextMode','DHE')
$null=Run '05-dhe-fault-save-legacy' 'DHE' @('-startupFaultDhe','-startupNextMode','LegacyInterpreter') $true
$baseMv=Join-Path $PairRoot 'shared/base.mv'; $saved=[IO.File]::ReadAllBytes($baseMv)
try{$broken=[byte[]]$saved.Clone();$broken[0]=$broken[0] -bxor 1;[IO.File]::WriteAllBytes($baseMv,$broken);$null=Run '06-legacy-with-bad-dhe-input' 'LegacyInterpreter';$cleared=Run '07-clear-tombstone' 'LegacyInterpreter' @('-startupClear');if($cleared.committedGeneration -ne 4 -or -not $cleared.restartRequired){throw 'Clear lost generation.'}}finally{[IO.File]::WriteAllBytes($baseMv,$saved)}
$last=Run '08-restarted-default' 'DHE';if($last.selectedGeneration -ne 4){throw 'Tombstone not applied.'}
if(@($cases.pid | Sort-Object -Unique).Count -ne $cases.Count){throw 'PID reuse.'}
# Damage selected native metadata: package fails before UnityMain, no other backend retry.
$metadata=Join-Path $PairRoot $pair.profile[0].metadata; $metadataBytes=[IO.File]::ReadAllBytes($metadata)
function ExpectPreinitReject([string]$Name){
    $report=Join-Path $ReportRoot "$Name.json"; $start=[Diagnostics.ProcessStartInfo]::new((Join-Path $PairRoot 'StartupPlayer.exe'));$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    foreach($arg in @('-batchmode','-nographics','-startupStore',$store,'-startupPairRoot',$PairRoot,'-startupReport',$report)){$start.ArgumentList.Add($arg)}
    $process=[Diagnostics.Process]::Start($start);try{if(-not $process.WaitForExit(10000)){$process.Kill($true);throw 'Reject timeout.'};if($process.ExitCode -ne 10 -or (Test-Path $report)){throw 'Failed to reject before Unity.'}}finally{$process.Dispose()}
}
try{$changed=[byte[]]$metadataBytes.Clone();$changed[0]=$changed[0] -bxor 1;[IO.File]::WriteAllBytes($metadata,$changed);ExpectPreinitReject '09-bad-selected-metadata'}finally{[IO.File]::WriteAllBytes($metadata,$metadataBytes)}
[IO.File]::WriteAllBytes($store,[byte[]]@(1,2,3));ExpectPreinitReject '10-corrupt-selection'
$summary=[ordered]@{
    format='hybridclr.startup-windows-router-report.v1';passed=$true;generatedAtUtc=[DateTime]::UtcNow.ToString('o')
    scope='one-native-executable-one-unityplayer-startup-bound-backend';engine=$pair.engine;currentSha256=$pair.currentSha256
    referenceCaseCount=$reference.caseCount;differential=0;independentPlayerProcesses=$cases.Count;singleExecutable=$true;unselectedBackendLoaded=$false
    selectedMetadataMismatchRejectedBeforeUnity=$true;corruptSelectionRejectedBeforeUnity=$true
    windowsManagedOnlyConditionalPass=$true;productionReady=$false;androidQualified=$false;iosQualified=$false;webglPlayerQualified=$false;otherEngineMatrixQualified=$false;performanceClaim=$false
    fixtureCommit=$pair.fixtureCommit;fixtureSha256=$pair.fixtureSha256;managedApiSha256=$pair.managedApiSha256
    pairManifestSha256=(Get-FileHash (Join-Path $PairRoot 'startup-pair.json')).Hash.ToLowerInvariant();pairManifest=$pair;cases=@($cases.ToArray())
}
[IO.File]::WriteAllText((Join-Path $ReportRoot 'summary.json'),($summary | ConvertTo-Json -Depth 12).Replace("`r`n","`n"),[Text.UTF8Encoding]::new($false))
Write-Output 'Single executable Windows gate passed.'
