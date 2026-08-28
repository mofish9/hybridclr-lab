[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Prepare", "Player")]
    [string]$Action,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$SettingsFile,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeSource,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [string]$Target = "StandaloneWindows64",
    [string]$Mode = "Exploratory"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$failurePath = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) "workflow-failure.json"
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($failurePath)) | Out-Null
$failure = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-demo-workflow-failure.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $false
    error = "fixture-prepare-root-cause:$Target"
    outputRoot = [IO.Path]::GetFullPath($OutputRoot)
}
[IO.File]::WriteAllText($failurePath, ($failure | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
exit 23
