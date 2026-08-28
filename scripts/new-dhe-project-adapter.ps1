[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [Parameter(Mandatory = $true)]
    [string]$Output,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) { Split-Path -Parent $PSScriptRoot } else { [IO.Path]::GetFullPath($LabRoot) }
$Output = [IO.Path]::GetFullPath($Output)
$templatePath = Join-Path $LabRoot "templates/dhe-project-adapter.ps1"
$manifestPath = Join-Path $LabRoot "dhe-toolchain-manifest.json"
if (-not (Test-Path -LiteralPath $templatePath -PathType Leaf)) { throw "DHE adapter template was not found: $templatePath" }
if ((Test-Path -LiteralPath $Output) -and -not $Force) { throw "Adapter output already exists: $Output. Pass -Force to replace it." }
$outputParent = [IO.Path]::GetDirectoryName($Output)
if (Test-DhePathWithinRoot $Output $LabRoot) {
    throw "Adapter output must be outside the DHE toolchain root: $Output"
}
if (Test-Path -LiteralPath $Output) {
    $existingOutput = Get-Item -LiteralPath $Output -Force
    if (($existingOutput.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "Adapter output may not be a junction or symbolic link: $Output"
    }
}
Assert-DheBasicOutputRootSafety -Path $outputParent
Assert-DheOutputNotAncestor -Path $outputParent -Root $LabRoot
$version = if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    [string](Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json).toolchainVersion
} else {
    [string](Get-Content -Raw -LiteralPath (Join-Path $LabRoot "manifests/dhe-toolchain-layout.json") | ConvertFrom-Json).toolchainVersion
}
$text = [IO.File]::ReadAllText($templatePath).Replace("__DHE_TOOLCHAIN_VERSION__", $version)
$null = New-Item -ItemType Directory -Force -Path $outputParent
[IO.File]::WriteAllText($Output, $text, (New-Object Text.UTF8Encoding($false)))
$tokens = $null
$parseErrors = $null
[Management.Automation.Language.Parser]::ParseFile($Output, [ref]$tokens, [ref]$parseErrors) | Out-Null
if ($parseErrors.Count -gt 0) { throw "Generated DHE adapter did not parse: $Output" }
Write-Host "DHE project adapter template: $Output"
exit 0
