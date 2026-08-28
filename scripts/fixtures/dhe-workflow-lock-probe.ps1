param(
    [Parameter(Mandatory = $true)]
    [string]$LabRoot,
    [ValidateRange(0, 30)]
    [int]$TimeoutSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path (Split-Path -Parent $PSScriptRoot) "dhe-workflow-common.ps1")

$lock = $null
try {
    $lock = Enter-DheWorkflowLock -LabRoot $LabRoot -TimeoutSeconds $TimeoutSeconds
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
} finally {
    Exit-DheWorkflowLock -Lock $lock
}
