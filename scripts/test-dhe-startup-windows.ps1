param(
    [string]$PairRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/startup-windows-v1'),
    [string]$ReportRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'reports/startup-windows-v1')
)
$ErrorActionPreference = 'Stop'
$PairRoot = [IO.Path]::GetFullPath($PairRoot)
$ReportRoot = [IO.Path]::GetFullPath($ReportRoot)
if (Test-Path -LiteralPath $ReportRoot) { throw "ReportRoot must be new: $ReportRoot" }
New-Item -ItemType Directory -Path $ReportRoot | Out-Null
$launcher = Join-Path $PairRoot 'launcher/StartupLauncher.exe'
$storePath = Join-Path $ReportRoot 'selection.bin'
$pair = Get-Content -LiteralPath (Join-Path $PairRoot 'pair.json') -Raw | ConvertFrom-Json
$referencePath = Join-Path $ReportRoot 'reference.json'
& $launcher --root $PairRoot --reference --report $referencePath
if ($LASTEXITCODE -ne 0) { throw 'CLR reference failed.' }
$reference = Get-Content -LiteralPath $referencePath -Raw | ConvertFrom-Json
if ($reference.currentSha256 -ne $pair.currentSha256) { throw 'CLR reference Current identity differs from the pair.' }
$results = [Collections.Generic.List[object]]::new()
function Run-Case([string]$Name, [string]$ExpectedMode, [string[]]$Extra = @(), [bool]$ExpectedFailure = $false) {
    $report = Join-Path $ReportRoot "$Name.json"
    $arguments = @('--root',$PairRoot,'--store',$storePath,'--report',$report) + $Extra
    $console = @(& $launcher @arguments 2>&1 | ForEach-Object { [string]$_ })
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne $(if ($ExpectedFailure) {2} else {0})) { throw "$Name unexpected exit code $exitCode`: $($console -join "`n")" }
    $result = Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
    if ($result.effectiveMode -ne $ExpectedMode -or -not $result.selectionImmutable -or -not $result.resetRejected) {
        throw "$Name failed mode immutability checks."
    }
    if (-not $ExpectedFailure) {
        if (-not $result.passed -or $result.caseCount -ne $reference.caseCount -or
            (($result.actual -join ',') -ne ($reference.actual -join ',')) -or $result.currentSha256 -ne $pair.currentSha256) {
            throw "$Name failed workload/current identity checks."
        }
        if ($ExpectedMode -eq 'DHE' -and (-not $result.changedSelected -or $result.unchangedSelected -or
            -not $result.diagnosticsEnabled -or $result.interpreterEntries -le 0 -or $result.aotEntries -le 0)) {
            throw "$Name did not prove both changed-interpreter and unchanged-AOT dispatch."
        }
        if ($ExpectedMode -eq 'LegacyInterpreter' -and $result.hotfixPresentBeforeLoad) {
            throw "$Name retained hotfix AOT identity in legacy mode."
        }
    } elseif ($result.nativeLoadCode -eq 0 -or $result.passed) { throw "$Name did not reject the injected DHE failure." }
    $results.Add(@{name=$Name; mode=$ExpectedMode; pid=$result.pid; expectedFailure=$ExpectedFailure; currentSha256=$result.currentSha256; caseCount=$result.caseCount; passed=$true; playerReport=$report})
    Write-Output "$Name passed (PID $($result.pid), $ExpectedMode)"
    return $result
}
$first = Run-Case '01-default-dhe-request-legacy' 'DHE' @('--next','LegacyInterpreter')
if (-not $first[-1].restartRequired) { throw 'Requesting legacy did not require restart.' }
$null = Run-Case '02-restarted-legacy' 'LegacyInterpreter'
$null = Run-Case '03-legacy-choice-persists' 'LegacyInterpreter'
$null = Run-Case '04-legacy-request-dhe' 'LegacyInterpreter' @('--next','DHE')
$null = Run-Case '05-restarted-dhe-fault-request-legacy' 'DHE' @('--fault-dhe','--next','LegacyInterpreter') $true

# Corrupt the DHE-only MV file while keeping the same authenticated Current.
# A fresh legacy process must remain independent of that DHE input.
$baseMv = Join-Path $PairRoot 'shared/base.mv'
$original = [IO.File]::ReadAllBytes($baseMv)
try {
    $broken = [byte[]]$original.Clone(); $broken[0] = $broken[0] -bxor 1
    [IO.File]::WriteAllBytes($baseMv, $broken)
    $null = Run-Case '06-legacy-with-broken-dhe-mv' 'LegacyInterpreter'
    $null = Run-Case '07-legacy-clear-next-choice' 'LegacyInterpreter' @('--clear')
} finally { [IO.File]::WriteAllBytes($baseMv, $original) }
$null = Run-Case '08-cleared-default-dhe' 'DHE'

# A broken persisted request must fail before a Player is created.
[IO.File]::WriteAllBytes($storePath, [byte[]]@(1,2,3))
$badReport = Join-Path $ReportRoot '09-corrupt-selection.json'
$badOutput = @(& $launcher --root $PairRoot --store $storePath --report $badReport 2>&1 | ForEach-Object { [string]$_ })
if ($LASTEXITCODE -ne 10 -or (Test-Path -LiteralPath $badReport)) { throw 'Corrupt persisted selection did not reject before launch.' }
$pids = @($results | ForEach-Object { $_.pid })
if (@($pids | Sort-Object -Unique).Count -ne $pids.Count) { throw 'Player PIDs are not unique.' }
$summary = [ordered]@{
    schemaVersion=1; format='hybridclr.startup-windows-flow-report.json'; generatedAtUtc=[DateTime]::UtcNow.ToString('o')
    passed=$true; scope='two-independent-windows-il2cpp-players-with-pre-unity-launcher'; engine='Unity2022.3.62f3'; target='StandaloneWindows64'
    currentSha256=$pair.currentSha256; referenceCaseCount=$reference.caseCount; differential=0
    independentPlayerProcessCount=$pids.Count; corruptSelectionRejectedBeforeLaunch=$true
    validatesSingleExecutableBackendSwitch=$false; qualifiesAndroid=$false; qualifiesWebGl=$false; qualifiesIos=$false
    performanceClaim=$false; dheDispatchDiagnostics=$pair.dheDispatchDiagnostics; ordinaryAotGuards=$pair.ordinaryAotGuards
    fixtureCommit=$pair.fixtureCommit; fixtureSourceSha256=$pair.fixtureSourceSha256
    pairManifestSha256=(Get-FileHash (Join-Path $PairRoot 'pair.json')).Hash.ToLowerInvariant()
    pairManifest=$pair; sourceIdentities=$pair.sourceIdentities; cases=@($results.ToArray())
}
$summaryPath = Join-Path $ReportRoot 'summary.json'
[IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 8).Replace("`r`n","`n") + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Windows flow gate passed: $summaryPath"
