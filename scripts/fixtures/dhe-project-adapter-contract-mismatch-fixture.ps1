[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Prepare", "Player")]
    [string]$Action,
    [Parameter(Mandatory = $true)][string]$ProjectPath,
    [Parameter(Mandatory = $true)][string]$SettingsFile,
    [Parameter(Mandatory = $true)][string]$RuntimeSource,
    [Parameter(Mandatory = $true)][string]$OutputRoot,
    [Parameter(Mandatory = $true)][int]$ToolchainContractVersion,
    [string]$Target = "StandaloneWindows64",
    [string]$Mode = "Exploratory",
    [switch]$RequireCompleteCoverage
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$preparePath = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "adapter/prepare.json"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($preparePath)) | Out-Null
$prepare = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-project-adapter-prepare.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $true
    toolchainContractVersion = 999
    target = $Target
    pathSemantics = "workspace-absolute-v1"
    projectPath = [IO.Path]::GetFullPath($ProjectPath)
    settingsFile = [IO.Path]::GetFullPath($SettingsFile)
    baselineRoot = [IO.Path]::GetFullPath($ProjectPath)
    currentRoot = [IO.Path]::GetFullPath($ProjectPath)
    baselineGeneratedFromCurrent = $true
    baselineSourceRoot = [IO.Path]::GetFullPath($ProjectPath)
    errors = @()
}
[IO.File]::WriteAllText($preparePath, ($prepare | ConvertTo-Json -Depth 6), (New-Object Text.UTF8Encoding($false)))
exit 0
