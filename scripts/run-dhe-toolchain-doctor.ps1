[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$ProjectPath = "",
    [string]$DnlibPath = "",
    [string]$Output = "",
    [switch]$RequireRelease,
    [ValidatePattern("^$|^[0-9a-fA-F]{64}$")]
    [string]$ExpectedPackageId = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) { Split-Path -Parent $PSScriptRoot } else { [IO.Path]::GetFullPath($LabRoot) }
$Output = if ([string]::IsNullOrWhiteSpace($Output)) { New-DheTemporaryReportPath "doctor" } else { [IO.Path]::GetFullPath($Output) }
Assert-DheSafeReportPath -Path $Output -ProtectedPaths @($LabRoot) | Out-Null
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$gateOutput = $Output + ".package-gate.json"
Assert-DheSafeReportPath -Path $gateOutput -ProtectedPaths @($LabRoot) | Out-Null
$packageGatePassed = $false
$manifest = $null
$gitCommand = Get-Command git -ErrorAction SilentlyContinue
$gitAvailable = $null -ne $gitCommand
$pwshCommand = Get-Command pwsh -ErrorAction SilentlyContinue
$pwshAvailable = $null -ne $pwshCommand
$projectTested = -not [string]::IsNullOrWhiteSpace($ProjectPath)
$projectReady = $null
$projectRoot = $null
$projectGitRoot = $null
$resolvedDnlib = $null

try {
    if ($RequireRelease -and [string]::IsNullOrWhiteSpace($ExpectedPackageId)) {
        throw "A Release DHE toolchain doctor check requires -ExpectedPackageId from a trusted external release record."
    }
    $gateArgs = @{ PackageRoot = $LabRoot; Output = $gateOutput }
    if ($RequireRelease) { $gateArgs.RequireRelease = $true }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $gateArgs.ExpectedPackageId = $ExpectedPackageId }
    & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") @gateArgs | Out-Null
    $packageGatePassed = $LASTEXITCODE -eq 0
    if (-not $packageGatePassed) { $errors.Add("DHE toolchain package gate failed.") }
} catch {
    $errors.Add($_.Exception.Message)
}
if (Test-Path -LiteralPath (Join-Path $LabRoot "dhe-toolchain-manifest.json") -PathType Leaf) {
    try { $manifest = Get-Content -Raw -LiteralPath (Join-Path $LabRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json }
    catch { $errors.Add("DHE toolchain manifest could not be read by doctor.") }
}
if (-not $pwshAvailable) { $errors.Add("PowerShell 7 (pwsh) was not found on PATH.") }
if (-not $gitAvailable) { $errors.Add("git was not found on PATH.") }

if ($projectTested) {
    $projectRoot = [IO.Path]::GetFullPath($ProjectPath)
    $projectReady = $true
    foreach ($requiredProjectFile in @(
        (Join-Path $projectRoot "ProjectSettings/HybridCLRSettings.asset"),
        (Join-Path $projectRoot "ProjectSettings/ProjectVersion.txt")
    )) {
        if (-not (Test-Path -LiteralPath $requiredProjectFile -PathType Leaf)) {
            $errors.Add("Required Unity project file was not found: $requiredProjectFile")
            $projectReady = $false
        }
    }
    try { $resolvedDnlib = Resolve-DheDnlibPath -RequestedPath $DnlibPath -ProjectRoot $projectRoot }
    catch {
        $errors.Add($_.Exception.Message)
        $projectReady = $false
    }
    if ($gitAvailable) {
        $gitRootOutput = @(& git -C $projectRoot rev-parse --show-toplevel 2>$null)
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($gitRootOutput -join "").Trim())) {
            $projectGitRoot = [IO.Path]::GetFullPath(($gitRootOutput -join "").Trim())
        } else {
            $warnings.Add("Project is not currently inside a Git repository; Release mode will reject it.")
        }
    }
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-toolchain-doctor.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    requireRelease = [bool]$RequireRelease
    toolchainVersion = if ($null -eq $manifest) { $null } else { [string]$manifest.toolchainVersion }
    contractVersion = if ($null -eq $manifest) { $null } else { [int]$manifest.contractVersion }
    packageId = if ($null -eq $manifest) { $null } else { [string]$manifest.packageId }
    expectedPackageId = if ([string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $null } else { $ExpectedPackageId.ToLowerInvariant() }
    packageGatePassed = $packageGatePassed
    powershellVersion = $PSVersionTable.PSVersion.ToString()
    pwshAvailable = $pwshAvailable
    gitAvailable = $gitAvailable
    projectTested = $projectTested
    projectReady = $projectReady
    projectPath = $projectRoot
    projectGitRoot = $projectGitRoot
    dnlibPath = $resolvedDnlib
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
}
$outputParent = [IO.Path]::GetDirectoryName($Output)
if (-not [string]::IsNullOrWhiteSpace($outputParent)) { $null = New-Item -ItemType Directory -Force -Path $outputParent }
[IO.File]::WriteAllText($Output, ($report | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath $gateOutput -PathType Leaf) { Remove-Item -LiteralPath $gateOutput -Force }
Write-Host "DHE toolchain doctor: $Output"
if (-not $report.passed) {
    Write-Error ("DHE toolchain doctor failed: " + ($errors -join "; "))
    exit 1
}
Write-Host ("DHE toolchain doctor passed: version={0}; projectTested={1}" -f $report.toolchainVersion, $projectTested)
exit 0
