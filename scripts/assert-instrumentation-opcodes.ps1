param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [Parameter(Mandatory = $true)]
    [string]$ProfilePath,
    [Parameter(Mandatory = $true)]
    [string[]]$RequiredOpcode
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$inputPath = if ([IO.Path]::IsPathRooted($ProfilePath)) {
    [IO.Path]::GetFullPath($ProfilePath)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $ProfilePath))
}
$instructionPath = [IO.Path]::GetFullPath((Join-Path $LabRoot "../repos/hybridclr/hybridclr/interpreter/Instruction.h"))
if (-not (Test-Path $inputPath)) { throw "Instrumentation snapshot was not found: $inputPath" }
if (-not (Test-Path $instructionPath)) { throw "Instruction.h was not found: $instructionPath" }

$instructionLines = Get-Content -LiteralPath $instructionPath
$start = [Array]::IndexOf($instructionLines, "`t`t//!!!{{OPCODE")
$end = [Array]::IndexOf($instructionLines, "`t`t//!!!}}OPCODE")
if ($start -lt 0 -or $end -le $start) { throw "Unable to parse HiOpcodeEnum from $instructionPath" }
$opcodeNames = @("None") + @(
    $instructionLines[($start + 1)..($end - 1)] |
        ForEach-Object { ($_ -replace '//.*$', '').Trim().TrimEnd(',') } |
        Where-Object { $_.Length -gt 0 }
)
$profile = Get-Content -Raw -LiteralPath $inputPath | ConvertFrom-Json
if ([int]$profile.schemaVersion -ne 1 -or $profile.dispatchCount -le 0) {
    throw "Invalid instrumentation snapshot: $inputPath"
}
$counts = @{}
foreach ($entry in $profile.opcodes) {
    $id = [int]$entry.id
    if ($id -lt 0 -or $id -ge $opcodeNames.Count) { throw "Unknown opcode id in snapshot: $id" }
    $counts[$opcodeNames[$id]] = [uint64]$entry.count
}
foreach ($name in $RequiredOpcode) {
    if (-not $counts.ContainsKey($name) -or [uint64]$counts[$name] -eq 0) {
        throw "Required opcode was not executed: $name"
    }
    Write-Host "[instrumentation] $name count=$($counts[$name])"
}
