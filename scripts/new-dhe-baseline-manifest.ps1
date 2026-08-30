[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaselineRoot,
    [Parameter(Mandatory = $true)][string]$RuntimeManifestPath,
    [Parameter(Mandatory = $true)][string]$SettingsFile,
    [Parameter(Mandatory = $true)][ValidatePattern("^[A-Za-z0-9._-]+$")][string]$Target,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$PackageLockPath = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$baselinePath = [IO.Path]::GetFullPath($BaselineRoot)
$runtimePath = [IO.Path]::GetFullPath($RuntimeManifestPath)
$settingsPath = [IO.Path]::GetFullPath($SettingsFile)
$outputPath = [IO.Path]::GetFullPath($Output)
if (-not [IO.Directory]::Exists($baselinePath)) { throw "DHE baseline root was not found: $baselinePath" }
if (-not [IO.File]::Exists($runtimePath)) { throw "DHE runtime manifest was not found: $runtimePath" }
if (-not [IO.File]::Exists($settingsPath)) { throw "DHE settings file was not found: $settingsPath" }
if ((Normalize-DhePath $outputPath) -eq (Normalize-DhePath $baselinePath)) {
    throw "DHE baseline manifest output must not replace the baseline root."
}
$protectedManifestInputs = @($runtimePath, $settingsPath)
if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
    $protectedManifestInputs += [IO.Path]::GetFullPath($PackageLockPath)
}
# Audit an existing output before -ForceOutput can remove it. This preserves
# the common report-path safety rule that rejects tracked files and reparse
# points rather than discovering them after deletion.
Assert-DheSafeReportPath -Path $outputPath -ProtectedPaths $protectedManifestInputs | Out-Null
if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
    if (-not $ForceOutput) { throw "DHE baseline manifest output already exists: $outputPath" }
    Remove-Item -LiteralPath $outputPath -Force
}
if (Test-Path -LiteralPath $outputPath -PathType Container) {
    throw "DHE baseline manifest output must be a file path: $outputPath"
}

$runtime = Get-Content -Raw -LiteralPath $runtimePath | ConvertFrom-Json
# Runtime manifests intentionally use the runtime-manifest schema, which does
# not carry a `format` discriminator. Keep this check aligned with the
# checked-in schema instead of treating an absent optional property as an
# invalid manifest under StrictMode.
$dheEnabledProperty = $runtime.PSObject.Properties["dheEnabled"]
if ([int]$runtime.schemaVersion -ne 1 -or
    $null -eq $dheEnabledProperty -or $dheEnabledProperty.Value -isnot [bool] -or
    -not [bool]$dheEnabledProperty.Value -or
    [string]::IsNullOrWhiteSpace([string]$runtime.engineWorkflow) -or
    $null -eq $runtime.engine -or
    $null -eq $runtime.source) {
    throw "DHE runtime manifest has an unsupported schema: $runtimePath"
}
$sets = Resolve-DheSettingsAssemblySets -SettingsFile $settingsPath
$assemblyNames = @($sets.dheAotAssemblies)
if ($assemblyNames.Count -eq 0) { throw "DHE settings contain no AOT assembly names." }
$records = New-Object System.Collections.Generic.List[object]
foreach ($assemblyName in $assemblyNames) {
    $assemblyPath = Join-Path $baselinePath ($assemblyName + ".dll")
    if (-not [IO.File]::Exists($assemblyPath)) { throw "DHE baseline assembly was not found: $assemblyPath" }
    $records.Add([ordered]@{
        assemblyName = $assemblyName
        sha256 = Get-DheSha256 $assemblyPath
    })
}

$package = $null
if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
    $lockPath = [IO.Path]::GetFullPath($PackageLockPath)
    if (-not [IO.File]::Exists($lockPath)) { throw "DHE package lock was not found: $lockPath" }
    $lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
    $package = [ordered]@{
        repository = [string]$lock.repository
        baseCommit = [string]$lock.baseCommit
        integratedCommit = [string]$lock.integratedCommit
        treeSha256 = [string]$lock.treeSha256
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-baseline-manifest.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    pathSemantics = "workspace-absolute-v1"
    baselineKind = "stripped-aot"
    target = $Target
    sourceRoot = $baselinePath
    engineWorkflow = [string]$runtime.engineWorkflow
    engine = [ordered]@{
        family = [string]$runtime.engine.family
        version = [string]$runtime.engine.version
        unityVersion = [string]$runtime.engine.unityVersion
        unityVersionNumber = [int]$runtime.engine.unityVersionNumber
        tuanjieVersionNumber = [int]$runtime.engine.tuanjieVersionNumber
    }
    runtime = [ordered]@{
        profile = [string]$runtime.profile
        stagedRuntimeSha256 = [string]$runtime.stagedRuntimeSha256
        runtimeManifestSha256 = Get-DheSha256 $runtimePath
        packageTreeSha256 = [string]$runtime.source.hybridclr_unity.treeSha256
    }
    package = $package
    assemblies = $records.ToArray()
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
[IO.File]::WriteAllText($outputPath, ($manifest | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE baseline manifest: $outputPath"
