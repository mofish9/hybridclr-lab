param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$InputDirectory = "reports/raw/baseline-instrumented-opcode",
    [string]$Output = "reports/baseline-instrumented-opcode-profile.json",
    [int]$TopPerWorkload = 25
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
function Resolve-LabPath([string]$Path) {
    if ([IO.Path]::IsPathRooted($Path)) { return [IO.Path]::GetFullPath($Path) }
    return [IO.Path]::GetFullPath((Join-Path $LabRoot $Path))
}

$inputPath = Resolve-LabPath $InputDirectory
$outputPath = Resolve-LabPath $Output
$instructionPath = [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos/hybridclr/hybridclr/interpreter/Instruction.h"))

if (-not (Test-Path $inputPath)) { throw "Instrumentation directory was not found: $inputPath" }
if (-not (Test-Path $instructionPath)) { throw "Instruction.h was not found: $instructionPath" }
if ($TopPerWorkload -lt 1) { throw "TopPerWorkload must be at least 1." }

$instructionLines = Get-Content -LiteralPath $instructionPath
$start = [Array]::IndexOf($instructionLines, "`t`t//!!!{{OPCODE")
$end = [Array]::IndexOf($instructionLines, "`t`t//!!!}}OPCODE")
if ($start -lt 0 -or $end -le $start) { throw "Unable to parse HiOpcodeEnum from $instructionPath" }
$opcodeNames = @("None") + @(
    $instructionLines[($start + 1)..($end - 1)] |
        ForEach-Object { ($_ -replace '//.*$', '').Trim().TrimEnd(',') } |
        Where-Object { $_.Length -gt 0 }
)

function Get-OpcodeName([int]$Id) {
    if ($Id -lt 0 -or $Id -ge $opcodeNames.Count) { throw "Unknown opcode id: $Id" }
    return $opcodeNames[$Id]
}

$profileFiles = @(Get-ChildItem -LiteralPath $inputPath -Filter "*.json" -File | Sort-Object Name)
if ($profileFiles.Count -eq 0) { throw "No instrumentation profiles were found in $inputPath" }

$opcodeTotals = @{}
$opcodeEqualShares = @{}
$transitionTotals = @{}
$transitionEqualShares = @{}
$perWorkload = @()
$totalDispatches = [uint64]0
$totalEntries = [uint64]0
$totalTransforms = [uint64]0
$totalTransformNanoseconds = [uint64]0

foreach ($file in $profileFiles) {
    $profile = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json
    if ([int]$profile.schemaVersion -ne 1 -or $profile.dispatchCount -le 0) {
        throw "Invalid instrumentation profile: $($file.FullName)"
    }

    $workloadId = [IO.Path]::GetFileNameWithoutExtension($file.Name)
    $dispatches = [uint64]$profile.dispatchCount
    $totalDispatches += $dispatches
    $totalEntries += [uint64]$profile.interpreterEntryCount
    $totalTransforms += [uint64]$profile.transformCount
    $totalTransformNanoseconds += [uint64]$profile.transformNanoseconds

    $opcodes = @()
    foreach ($opcode in $profile.opcodes) {
        $id = [int]$opcode.id
        $count = [uint64]$opcode.count
        $share = $count * 100.0 / $dispatches
        if (-not $opcodeTotals.ContainsKey($id)) { $opcodeTotals[$id] = [uint64]0 }
        if (-not $opcodeEqualShares.ContainsKey($id)) { $opcodeEqualShares[$id] = 0.0 }
        $opcodeTotals[$id] = [uint64]$opcodeTotals[$id] + $count
        $opcodeEqualShares[$id] = [double]$opcodeEqualShares[$id] + $share
        $opcodes += [ordered]@{ id = $id; name = Get-OpcodeName $id; count = $count; sharePercent = $share }
    }

    $transitions = @()
    foreach ($transition in $profile.transitions) {
        $from = [int]$transition.from
        $to = [int]$transition.to
        $count = [uint64]$transition.count
        $key = "$from`:$to"
        $share = $count * 100.0 / $dispatches
        if (-not $transitionTotals.ContainsKey($key)) { $transitionTotals[$key] = [uint64]0 }
        if (-not $transitionEqualShares.ContainsKey($key)) { $transitionEqualShares[$key] = 0.0 }
        $transitionTotals[$key] = [uint64]$transitionTotals[$key] + $count
        $transitionEqualShares[$key] = [double]$transitionEqualShares[$key] + $share
        $transitions += [ordered]@{
            from = $from
            fromName = Get-OpcodeName $from
            to = $to
            toName = Get-OpcodeName $to
            count = $count
            sharePercent = $share
        }
    }

    $perWorkload += [ordered]@{
        id = $workloadId
        dispatchCount = $dispatches
        interpreterEntryCount = [uint64]$profile.interpreterEntryCount
        transformCount = [uint64]$profile.transformCount
        transformNanoseconds = [uint64]$profile.transformNanoseconds
        topOpcodes = @($opcodes | Sort-Object count -Descending | Select-Object -First $TopPerWorkload)
        topTransitions = @($transitions | Sort-Object count -Descending | Select-Object -First $TopPerWorkload)
    }
}

$workloadCount = $profileFiles.Count
$aggregateOpcodes = foreach ($entry in $opcodeTotals.GetEnumerator()) {
    $id = [int]$entry.Key
    [ordered]@{
        id = $id
        name = Get-OpcodeName $id
        count = [uint64]$entry.Value
        weightedDispatchPercent = [uint64]$entry.Value * 100.0 / $totalDispatches
        meanWorkloadSharePercent = [double]$opcodeEqualShares[$id] / $workloadCount
    }
}
$aggregateTransitions = foreach ($entry in $transitionTotals.GetEnumerator()) {
    $parts = $entry.Key.Split(':')
    $from = [int]$parts[0]
    $to = [int]$parts[1]
    [ordered]@{
        from = $from
        fromName = Get-OpcodeName $from
        to = $to
        toName = Get-OpcodeName $to
        count = [uint64]$entry.Value
        weightedDispatchPercent = [uint64]$entry.Value * 100.0 / $totalDispatches
        meanWorkloadSharePercent = [double]$transitionEqualShares[$entry.Key] / $workloadCount
    }
}

$report = [ordered]@{
    schemaVersion = 1
    profile = "Baseline-Instrumented"
    diagnosticOnly = $true
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    source = [ordered]@{
        hybridclrCommit = (git -C (Join-Path $LabRoot "../repos/hybridclr") rev-parse HEAD).Trim()
        instructionSha256 = (Get-FileHash -LiteralPath $instructionPath -Algorithm SHA256).Hash
        opcodeCount = $opcodeNames.Count
    }
    summary = [ordered]@{
        workloadCount = $workloadCount
        dispatchCount = $totalDispatches
        interpreterEntryCount = $totalEntries
        transformCount = $totalTransforms
        transformNanoseconds = $totalTransformNanoseconds
    }
    aggregateOpcodes = @($aggregateOpcodes | Sort-Object count -Descending)
    aggregateTransitions = @($aggregateTransitions | Sort-Object count -Descending)
    workloads = $perWorkload
}

New-Item -ItemType Directory -Force -Path (Split-Path $outputPath) | Out-Null
$report | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $outputPath -Encoding UTF8
Write-Host "Instrumentation summary: $outputPath"
Write-Host "Workloads: $workloadCount, dispatches: $totalDispatches"
Write-Host "Top opcodes:"
$report.aggregateOpcodes | Select-Object -First 10 name,count,weightedDispatchPercent,meanWorkloadSharePercent | Format-Table -AutoSize
Write-Host "Top transitions:"
$report.aggregateTransitions | Select-Object -First 10 fromName,toName,count,weightedDispatchPercent,meanWorkloadSharePercent | Format-Table -AutoSize
