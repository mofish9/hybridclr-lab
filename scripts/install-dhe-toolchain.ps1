[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [Parameter(Mandatory = $true)]
    [string]$Destination,
    [string]$Output = "",
    [switch]$Upgrade,
    [switch]$AllowDowngrade,
    [switch]$AllowExploratory,
    [ValidatePattern("^$|^[0-9a-fA-F]{64}$")]
    [string]$ExpectedPackageId = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$PackageRoot = [IO.Path]::GetFullPath($PackageRoot).TrimEnd('\', '/')
$Destination = [IO.Path]::GetFullPath($Destination).TrimEnd('\', '/')
$Output = if ([string]::IsNullOrWhiteSpace($Output)) { New-DheTemporaryReportPath "install" } else { [IO.Path]::GetFullPath($Output) }
$destinationExists = Test-Path -LiteralPath $Destination
if ($destinationExists) {
    Assert-DheSafeVerifiedReplacementRoot -Path $Destination -ProtectedPaths @($PackageRoot)
} else {
    Assert-DheSafeOutputRoot -Path $Destination -ProtectedPaths @($PackageRoot)
}
Assert-DheOutputNotAncestor -Path $Destination -Root $PackageRoot
Assert-DheSafeReportPath -Path $Output -ProtectedPaths @($PackageRoot, $Destination) | Out-Null

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$operation = if (Test-Path -LiteralPath $Destination) { "upgrade" } else { "install" }
$previousVersion = $null
$installedVersion = $null
$contractVersion = $null
$packageId = $null
$packageGatePassed = $false
$parent = [IO.Path]::GetDirectoryName($Destination)
$name = [IO.Path]::GetFileName($Destination)
$stage = Join-Path $parent (".$name.dhe-stage-" + [Guid]::NewGuid().ToString("N"))
$backup = Join-Path $parent (".$name.dhe-backup-" + [Guid]::NewGuid().ToString("N"))
$sourceGate = Join-Path $parent (".$name.dhe-source-gate-" + [Guid]::NewGuid().ToString("N") + ".json")
$stageGate = Join-Path $parent (".$name.dhe-stage-gate-" + [Guid]::NewGuid().ToString("N") + ".json")
$backupCreated = $false
$destinationInstalled = $false
$installLock = $null

function Write-InstallReport {
    $report = [ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-toolchain-install.json"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        passed = $errors.Count -eq 0 -and $destinationInstalled
        sourcePackage = $PackageRoot
        destination = $Destination
        operation = $operation
        previousVersion = $previousVersion
        installedVersion = $installedVersion
        contractVersion = $contractVersion
        packageId = $packageId
        expectedPackageId = if ([string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $null } else { $ExpectedPackageId.ToLowerInvariant() }
        packageGatePassed = $packageGatePassed
        errors = $errors.ToArray()
        warnings = $warnings.ToArray()
    }
    $outputParent = [IO.Path]::GetDirectoryName($Output)
    if (-not [string]::IsNullOrWhiteSpace($outputParent)) { $null = New-Item -ItemType Directory -Force -Path $outputParent }
    [IO.File]::WriteAllText($Output, ($report | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
    return $report
}

try {
    # Use the same destination-scoped mutex as an installed workflow. An
    # upgrade must not replace scripts while a build is executing them.
    $installLock = Enter-DheWorkflowLock -LabRoot $Destination -TimeoutSeconds 0
    $null = New-Item -ItemType Directory -Force -Path $parent
    if (-not $AllowExploratory -and [string]::IsNullOrWhiteSpace($ExpectedPackageId)) {
        throw "A Release DHE toolchain install requires -ExpectedPackageId from a trusted external release record."
    }
    $verifyArgs = @{
        PackageRoot = $PackageRoot
        Output = $sourceGate
    }
    if (-not $AllowExploratory) { $verifyArgs.RequireRelease = $true }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $verifyArgs.ExpectedPackageId = $ExpectedPackageId }
    & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") @verifyArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Source DHE toolchain package verification failed." }
    $packageGatePassed = $true
    $sourceManifest = Get-Content -Raw -LiteralPath (Join-Path $PackageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
    $installedVersion = [string]$sourceManifest.toolchainVersion
    $contractVersion = [int]$sourceManifest.contractVersion
    $packageId = [string]$sourceManifest.packageId

    if (Test-Path -LiteralPath $Destination) {
        if (-not $Upgrade) { throw "Destination already exists; pass -Upgrade for a verified replacement: $Destination" }
        $existingManifestPath = Join-Path $Destination "dhe-toolchain-manifest.json"
        if (-not (Test-Path -LiteralPath $existingManifestPath -PathType Leaf)) {
            throw "Existing destination is not an installed DHE toolchain: $Destination"
        }
        $existingManifest = Get-Content -Raw -LiteralPath $existingManifestPath | ConvertFrom-Json
        $previousVersion = [string]$existingManifest.toolchainVersion
        if ([int]$existingManifest.contractVersion -ne $contractVersion) {
            throw "DHE toolchain contract change requires an explicit migration; installed=$($existingManifest.contractVersion), incoming=$contractVersion."
        }
        if (-not $AllowDowngrade -and ([version]$installedVersion) -lt ([version]$previousVersion)) {
            throw "DHE toolchain downgrade is refused by default: $previousVersion -> $installedVersion"
        }
        $existingGate = Join-Path $parent (".$name.dhe-existing-gate-" + [Guid]::NewGuid().ToString("N") + ".json")
        try {
            & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") `
                -PackageRoot $Destination -Output $existingGate | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Existing DHE toolchain is corrupt; refusing an in-place upgrade." }
        } finally {
            if (Test-Path -LiteralPath $existingGate -PathType Leaf) { Remove-Item -LiteralPath $existingGate -Force }
        }
    }

    $null = New-Item -ItemType Directory -Force -Path $stage
    Get-ChildItem -LiteralPath $PackageRoot -Force | Copy-Item -Destination $stage -Recurse -Force
    $stageVerifyArgs = @{ PackageRoot = $stage; Output = $stageGate }
    if (-not $AllowExploratory) { $stageVerifyArgs.RequireRelease = $true }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $stageVerifyArgs.ExpectedPackageId = $ExpectedPackageId }
    & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") @stageVerifyArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Staged DHE toolchain package verification failed." }

    if (Test-Path -LiteralPath $Destination) {
        Move-Item -LiteralPath $Destination -Destination $backup
        $backupCreated = $true
    }
    Move-Item -LiteralPath $stage -Destination $Destination
    $destinationInstalled = $true

    $finalGate = Join-Path $parent (".$name.dhe-final-gate-" + [Guid]::NewGuid().ToString("N") + ".json")
    try {
        $finalArgs = @{ PackageRoot = $Destination; Output = $finalGate }
        if (-not $AllowExploratory) { $finalArgs.RequireRelease = $true }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $finalArgs.ExpectedPackageId = $ExpectedPackageId }
        & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") @finalArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Installed DHE toolchain package verification failed." }
    } finally {
        if (Test-Path -LiteralPath $finalGate -PathType Leaf) { Remove-Item -LiteralPath $finalGate -Force }
    }
    if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
        Remove-Item -LiteralPath $backup -Recurse -Force
        $backupCreated = $false
    }
} catch {
    $errors.Add($_.Exception.Message)
    if ($destinationInstalled -and (Test-Path -LiteralPath $Destination)) {
        Remove-Item -LiteralPath $Destination -Recurse -Force
        $destinationInstalled = $false
    }
    if ($backupCreated -and (Test-Path -LiteralPath $backup)) {
        Move-Item -LiteralPath $backup -Destination $Destination
        $backupCreated = $false
    }
} finally {
    foreach ($temporaryPath in @($stage, $sourceGate, $stageGate)) {
        if (Test-Path -LiteralPath $temporaryPath) {
            if (Test-Path -LiteralPath $temporaryPath -PathType Container) { Remove-Item -LiteralPath $temporaryPath -Recurse -Force }
            else { Remove-Item -LiteralPath $temporaryPath -Force }
        }
    }
    Exit-DheWorkflowLock $installLock
    $installLock = $null
}

$finalReport = Write-InstallReport
Write-Host "DHE toolchain install report: $Output"
if (-not $finalReport.passed) {
    Write-Error ("DHE toolchain install failed: " + ($errors -join "; "))
    exit 1
}
Write-Host ("DHE toolchain {0}: {1} ({2})" -f $operation, $Destination, $installedVersion)
exit 0
