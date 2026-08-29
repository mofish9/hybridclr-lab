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
    [ValidateSet("StandaloneWindows64")]
    [string]$Target = "StandaloneWindows64",
    [Parameter(Mandatory = $true)]
    [ValidateRange(1, 1)]
    [int]$ToolchainContractVersion,
    [ValidateSet("Release", "Exploratory")]
    [string]$Mode = "Release",
    [string]$BaselineAotRoot = "",
    [string]$ProjectPlan = "",
    [string]$ProjectPlanValidation = "",
    [string]$BatchReport = "",
    [string]$SourcePreflight = "",
    [string]$CleanCheckoutGate = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$labRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
. (Join-Path $labRoot "scripts/dhe-workflow-common.ps1")

if ($Target -ne "StandaloneWindows64") {
    throw "The checked-in DHE demo adapter only supports StandaloneWindows64."
}
$runtimePath = [IO.Path]::GetFullPath($RuntimeSource)
$runtimeManifestPath = Join-Path ([IO.Path]::GetDirectoryName($runtimePath)) "runtime-manifest.json"
if (-not (Test-Path -LiteralPath $runtimeManifestPath -PathType Leaf)) {
    throw "DHE runtime manifest was not found: $runtimeManifestPath"
}
$runtimeManifest = Get-Content -Raw -LiteralPath $runtimeManifestPath | ConvertFrom-Json
$unityExe = if ($null -ne $runtimeManifest.PSObject.Properties["engine"] -and
    $null -ne $runtimeManifest.engine -and
    $null -ne $runtimeManifest.engine.PSObject.Properties["executablePath"]) {
    [IO.Path]::GetFullPath([string]$runtimeManifest.engine.executablePath)
} else { "" }
if ([string]::IsNullOrWhiteSpace($unityExe) -or -not (Test-Path -LiteralPath $unityExe -PathType Leaf)) {
    throw "The runtime manifest does not reference an available matching Editor: $unityExe"
}

$arguments = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
    (Join-Path $labRoot "scripts/run-dhe-demo-workflow.ps1"),
    "-UnityExe", $unityExe,
    "-LabRoot", $labRoot,
    "-ProjectPath", ([IO.Path]::GetFullPath($ProjectPath)),
    "-RuntimeSource", $runtimePath,
    "-OutputRoot", ([IO.Path]::GetFullPath($OutputRoot)),
    "-Mode", $Mode,
    "-ToolchainContractVersion", $ToolchainContractVersion,
    "-Invocation", $(if ($Action -eq "Prepare") { "AdapterPrepare" } else { "AdapterPlayer" }),
    "-BaselineAotRoot", $BaselineAotRoot,
    "-WorkflowLockAlreadyHeld"
)
if ($Action -eq "Player") {
    foreach ($required in @{
            ProjectPlan = $ProjectPlan
            ProjectPlanValidation = $ProjectPlanValidation
            BatchReport = $BatchReport
            SourcePreflight = $SourcePreflight
            CleanCheckoutGate = $CleanCheckoutGate
        }.GetEnumerator()) {
        if ([string]::IsNullOrWhiteSpace([string]$required.Value)) {
            throw "DHE demo adapter Player is missing $($required.Key)."
        }
        $arguments += @("-$($required.Key)", [IO.Path]::GetFullPath([string]$required.Value))
    }
}

& (Resolve-DhePowerShellHost) @arguments
exit [int]$LASTEXITCODE
