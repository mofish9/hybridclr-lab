param(
    [Parameter(Mandatory=$true)][string]$BaselineProof,
    [Parameter(Mandatory=$true)][string]$CandidateProof,
    [Parameter(Mandatory=$true)][string]$OutputRoot,
    [int]$Pairs = 10
)
$ErrorActionPreference = 'Stop'
if ($Pairs -lt 3 -or (Test-Path -LiteralPath $OutputRoot)) { throw 'Use at least 3 pairs and a new output directory.' }
function Hash-File([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Fixture-Identity([string]$Proof) {
    $fixture = Join-Path $Proof 'project/Assets/Plugins/ValueLayout'
    @((Get-ChildItem -LiteralPath $fixture -Filter '*.dll' -File | Sort-Object Name) | ForEach-Object { $_.Name + ':' + (Hash-File $_.FullName) }) -join ','
}
if ((Fixture-Identity $BaselineProof) -ne (Fixture-Identity $CandidateProof)) { throw 'Workload DLLs differ.' }
$benchmark = 'project/Assets/DhePerformanceProbe.cs'
if ((Hash-File (Join-Path $BaselineProof $benchmark)) -ne (Hash-File (Join-Path $CandidateProof $benchmark))) { throw 'Benchmark sources differ.' }
$baselineCurrent = @(Get-ChildItem (Join-Path $BaselineProof 'base/current') -Filter '*.dll' -File | Sort-Object Name | ForEach-Object { $_.Name+':'+(Hash-File $_.FullName) }) -join ','
$candidateCurrent = @(Get-ChildItem (Join-Path $CandidateProof 'base/current') -Filter '*.dll' -File | Sort-Object Name | ForEach-Object { $_.Name+':'+(Hash-File $_.FullName) }) -join ','
if ($baselineCurrent -ne $candidateCurrent) { throw 'Current DLL payloads differ.' }
New-Item -ItemType Directory -Path $OutputRoot | Out-Null
$rows = [Collections.Generic.List[object]]::new()
$processIds = [Collections.Generic.HashSet[int]]::new()
foreach ($pair in 0..($Pairs-1)) {
    $order = if ($pair % 2 -eq 0) { @('baseline','candidate') } else { @('candidate','baseline') }
    foreach ($variant in $order) {
        $proof = if ($variant -eq 'baseline') { $BaselineProof } else { $CandidateProof }
        $prefix = Join-Path $OutputRoot ($variant+'-'+$pair)
        $identity = Get-Content (Join-Path $proof 'base/build-identity.json') -Raw | ConvertFrom-Json
        $start = [Diagnostics.ProcessStartInfo]::new((Join-Path $proof 'base/player/Snapshot.exe'))
        $start.UseShellExecute = $false; $start.CreateNoWindow = $true; $start.WindowStyle = 'Hidden'
        foreach ($arg in @('-batchmode','-nographics','-snapshotResult',($prefix+'.correctness.json'),
            '-expectedRevision','41','-expectedAssemblies',([string]$identity.assemblies.Count),
            '-dhePerformanceResult',$prefix,'-logFile',($prefix+'.log'))) { $start.ArgumentList.Add($arg) }
        $process = [Diagnostics.Process]::Start($start)
        $runId = $process.Id
        if (!$process.WaitForExit(60000)) { $process.Kill(); throw 'Performance Player timeout.' }
        if ($process.ExitCode -ne 0 -or !$processIds.Add($runId)) { throw 'Failed Player or duplicate PID.' }
        $correctness = Get-Content ($prefix+'.correctness.json') -Raw | ConvertFrom-Json
        if (!$correctness.passed) { throw 'Player correctness failed.' }
        foreach ($phase in @('before-load','after-load')) {
            $sample = Get-Content ($prefix+'.'+$phase+'.json') -Raw | ConvertFrom-Json
            if (!$sample.passed -or $sample.pid -ne $runId -or $sample.baseId -ne $identity.baseId) { throw 'Sample identity failed.' }
            if ($variant -eq 'candidate' -and ($sample.diagnostics -or $sample.smokeIncluded)) { throw 'Production candidate contains diagnostics.' }
            foreach ($case in $sample.samples) {
                $rows.Add([PSCustomObject]@{ pair=$pair; variant=$variant; phase=$phase; pid=$runId; name=$case.name;
                    iterations=$case.iterations; checksum=$case.checksum; milliseconds=1000.0*$case.ticks/$sample.frequency })
            }
        }
        Write-Output ($variant+' pair '+$pair+' passed, PID '+$runId)
        $process.Dispose()
    }
}
function Quantile($values, [double]$p) { $sorted=@($values|Sort-Object); $sorted[[Math]::Min($sorted.Count-1,[Math]::Max(0,[Math]::Ceiling($p*$sorted.Count)-1))] }
$summary = foreach ($phase in @('before-load','after-load')) {
    foreach ($name in @($rows.name | Sort-Object -Unique)) {
        $left = @($rows | Where-Object { $_.phase -eq $phase -and $_.name -eq $name -and $_.variant -eq 'baseline' } | Sort-Object pair)
        $right = @($rows | Where-Object { $_.phase -eq $phase -and $_.name -eq $name -and $_.variant -eq 'candidate' } | Sort-Object pair)
        $ratios = for ($i=0; $i -lt $Pairs; ++$i) {
            if ($left[$i].iterations -ne $right[$i].iterations -or $left[$i].checksum -ne $right[$i].checksum) { throw 'Measured workloads differ.' }
            $right[$i].milliseconds / $left[$i].milliseconds
        }
        $base50=Quantile $left.milliseconds .5; $new50=Quantile $right.milliseconds .5
        [PSCustomObject]@{phase=$phase; name=$name; baselineP50Ms=$base50; candidateP50Ms=$new50;
            baselineP95Ms=(Quantile $left.milliseconds .95); candidateP95Ms=(Quantile $right.milliseconds .95);
            pairedMedianRatio=(Quantile $ratios .5); unpairedRatio=$new50/$base50;
            baselineMadMs=(Quantile @($left.milliseconds|ForEach-Object {[Math]::Abs($_-$base50)}) .5);
            candidateMadMs=(Quantile @($right.milliseconds|ForEach-Object {[Math]::Abs($_-$new50)}) .5)}
    }
}
$utf8=[Text.UTF8Encoding]::new($false)
[IO.File]::WriteAllText((Join-Path $OutputRoot 'result.json'),([PSCustomObject]@{
    passed=$true; scope='Exploratory Windows opt7 shipped defaults vs production candidate; includes removal of opt7 counters. Not Android/P99 release evidence.';
    pairs=$Pairs; uniqueProcesses=$processIds.Count; p99ReleaseGatePassed=$false; summary=@($summary); samples=$rows;
    workloadDllIdentity=(Fixture-Identity $CandidateProof); currentDllIdentity=$candidateCurrent;
    benchmarkSha256=(Hash-File (Join-Path $CandidateProof $benchmark)); baselineProof=$BaselineProof; candidateProof=$CandidateProof;
    baselineGameAssemblySha256=(Hash-File (Join-Path $BaselineProof 'base/player/GameAssembly.dll'));
    candidateGameAssemblySha256=(Hash-File (Join-Path $CandidateProof 'base/player/GameAssembly.dll'))
}|ConvertTo-Json -Depth 30),$utf8)
$summary | Format-Table phase,name,baselineP50Ms,candidateP50Ms,pairedMedianRatio -AutoSize
