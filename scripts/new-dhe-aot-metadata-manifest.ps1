[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$Root,
    [Parameter(Mandatory = $true)][string]$SettingsFile,
    [Parameter(Mandatory = $true)][string]$RuntimeManifestPath,
    [Parameter(Mandatory = $true)][ValidatePattern("^[A-Za-z0-9._-]+$")][string]$Target,
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$PackageLockPath = "",
    [string]$ReleaseId = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$rootPath = [IO.Path]::GetFullPath($Root)
$settingsPath = [IO.Path]::GetFullPath($SettingsFile)
$runtimePath = [IO.Path]::GetFullPath($RuntimeManifestPath)
$outputPath = [IO.Path]::GetFullPath($Output)
if (-not [IO.Directory]::Exists($rootPath)) { throw "DHE AOT metadata root was not found: $rootPath" }
if (-not [IO.File]::Exists($settingsPath)) { throw "DHE settings file was not found: $settingsPath" }
if (-not [IO.File]::Exists($runtimePath)) { throw "DHE runtime manifest was not found: $runtimePath" }
Assert-DheSafeReportPath -Path $outputPath -ProtectedPaths @($rootPath, $settingsPath, $runtimePath) | Out-Null
if ([IO.File]::Exists($outputPath)) {
    if (-not $ForceOutput) { throw "DHE AOT metadata manifest already exists: $outputPath" }
    Remove-Item -LiteralPath $outputPath -Force
}
if ([IO.Directory]::Exists($outputPath)) { throw "DHE AOT metadata manifest output must be a file: $outputPath" }

try { $runtime = Get-Content -Raw -LiteralPath $runtimePath | ConvertFrom-Json }
catch { throw "DHE runtime manifest is not valid JSON: $runtimePath ($($_.Exception.Message))" }
if ([int]$runtime.schemaVersion -ne 1 -or $null -eq $runtime.engine -or $null -eq $runtime.source) {
    throw "DHE runtime manifest has an unsupported schema: $runtimePath"
}
if ([string]$runtime.engine.unityVersion -ne [string]$runtime.engine.version -and
    [string]::IsNullOrWhiteSpace([string]$runtime.engine.unityVersion)) {
    throw "DHE runtime manifest does not carry a usable Unity engine version: $runtimePath"
}

$assemblyNames = @(Get-DheYamlList $settingsPath "patchAOTAssemblies" |
    ForEach-Object {
        $value = ([string]$_).Trim()
        if ($value.EndsWith(".dll", [StringComparison]::OrdinalIgnoreCase)) {
            [IO.Path]::GetFileNameWithoutExtension($value)
        } else { $value }
    } | Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
if ($assemblyNames.Count -eq 0) {
    throw "HybridCLR settings contain no patchAOTAssemblies: $settingsPath"
}

$records = New-Object System.Collections.Generic.List[object]
foreach ($name in $assemblyNames) {
    $assemblyPath = Join-Path $rootPath ($name + ".dll")
    if (-not [IO.File]::Exists($assemblyPath)) {
        throw "DHE AOT metadata assembly was not found: $assemblyPath"
    }
    $records.Add([ordered]@{
        assemblyName = $name
        sha256 = Get-DheSha256 $assemblyPath
    })
}

$package = $null
if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
    $lockPath = [IO.Path]::GetFullPath($PackageLockPath)
    if (-not [IO.File]::Exists($lockPath)) { throw "DHE package lock was not found: $lockPath" }
    try { $lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json }
    catch { throw "DHE package lock is not valid JSON: $lockPath ($($_.Exception.Message))" }
    $package = [ordered]@{
        repository = [string]$lock.repository
        baseCommit = [string]$lock.baseCommit
        integratedCommit = [string]$lock.integratedCommit
        treeSha256 = [string]$lock.treeSha256
    }
}

$manifest = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-aot-metadata-manifest.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    pathSemantics = "workspace-absolute-v1"
    kind = "patch-aot-metadata"
    releaseId = if ([string]::IsNullOrWhiteSpace($ReleaseId)) { $null } else { $ReleaseId }
    target = $Target
    sourceRoot = $rootPath
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
        packageTreeSha256 = if ($null -eq $package) { $null } else { [string]$package.treeSha256 }
    }
    package = $package
    assemblies = $records.ToArray()
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null
[IO.File]::WriteAllText($outputPath, ($manifest | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE AOT metadata manifest: $outputPath"
exit 0
