[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)]
    [ValidateSet("help", "archive", "doctor", "install", "new-adapter", "preflight", "release", "schema", "validate", "verify-package", "version", "workflow")]
    [string]$Command = "help",
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$CommandArguments = @()
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$toolRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$manifestPath = Join-Path $toolRoot "dhe-toolchain-manifest.json"

if ($Command -eq "help") {
    Write-Host "HybridCLR DHE toolchain commands:"
    Write-Host "  doctor, install, new-adapter, preflight, workflow"
    Write-Host "  validate, release, archive, schema, verify-package, version"
    exit 0
}

if ($Command -eq "version") {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "DHE toolchain manifest was not found: $manifestPath"
    }
    $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
    Write-Host ("HybridCLR DHE toolchain {0} (contract {1}, packageId={2}, releaseReady={3})" -f `
        [string]$manifest.toolchainVersion, [int]$manifest.contractVersion, `
        [string]$manifest.packageId, [bool]$manifest.releaseReady)
    exit 0
}

$scriptByCommand = @{
    "archive" = "run-dhe-archive-gate.ps1"
    "doctor" = "run-dhe-toolchain-doctor.ps1"
    "install" = "install-dhe-toolchain.ps1"
    "new-adapter" = "new-dhe-project-adapter.ps1"
    "preflight" = "run-dhe-project-preflight.ps1"
    "release" = "run-dhe-release-gate.ps1"
    "schema" = "run-dhe-schema-gate.ps1"
    "validate" = "validate-dhe-artifacts.ps1"
    "verify-package" = "test-dhe-toolchain-package.ps1"
    "workflow" = "run-dhe-project-workflow.ps1"
}
$scriptPath = Join-Path $toolRoot ("scripts/" + [string]$scriptByCommand[$Command])
if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
    throw "DHE command implementation was not found: $scriptPath"
}

$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
if ($null -eq $pwshCommand) {
    throw "HybridCLR DHE toolchain requires PowerShell 7 (pwsh) on PATH."
}
$hostPath = $pwshCommand.Source
& $hostPath -NoProfile -ExecutionPolicy Bypass -File $scriptPath @CommandArguments
exit [int]$LASTEXITCODE
