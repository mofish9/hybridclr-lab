[CmdletBinding()]
param(
    [string]$SourceRoot = "",
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeManifest,
    [Parameter(Mandatory = $true)]
    [string]$InstalledConsumerGate,
    [string]$OutputPrefix = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")

if ($PSVersionTable.PSEdition -ne "Core") {
    throw "DHE transport release publishing requires PowerShell 7 (pwsh)."
}

$SourceRoot = if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($SourceRoot)
}
$PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
$RuntimeManifest = [IO.Path]::GetFullPath($RuntimeManifest)
$InstalledConsumerGate = [IO.Path]::GetFullPath($InstalledConsumerGate)
$OutputPrefix = if ([string]::IsNullOrWhiteSpace($OutputPrefix)) {
    $PackageRoot
} else {
    [IO.Path]::GetFullPath($OutputPrefix)
}

Assert-DheOutputNotAncestor -Path $PackageRoot -Root $SourceRoot
Assert-DheOutputNotAncestor -Path $OutputPrefix -Root $SourceRoot
foreach ($required in @($RuntimeManifest, $InstalledConsumerGate)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
        throw "DHE transport release input was not found: $required"
    }
}

$outputParent = [IO.Path]::GetDirectoryName($OutputPrefix)
if ([string]::IsNullOrWhiteSpace($outputParent)) {
    throw "DHE transport release output prefix has no parent: $OutputPrefix"
}
$packageParent = [IO.Path]::GetDirectoryName($PackageRoot)
if (-not $packageParent.Equals($outputParent, [StringComparison]::OrdinalIgnoreCase)) {
    throw "PackageRoot and OutputPrefix must use the same release directory."
}
$null = New-Item -ItemType Directory -Force -Path $outputParent
$archivePath = $OutputPrefix + ".zip"
$bundlePath = $OutputPrefix + ".source.bundle"
$runtimeRecordPath = $OutputPrefix + ".runtime-manifest.json"
$consumerRecordPath = $OutputPrefix + ".installed-consumer-gate.json"
$packageGatePath = $OutputPrefix + ".gate.json"
$archiveGatePath = $OutputPrefix + ".archive.gate.json"
$releaseManifestPath = $OutputPrefix + ".release.json"
$sidecars = @(
    $archivePath,
    $bundlePath,
    $runtimeRecordPath,
    $consumerRecordPath,
    $packageGatePath,
    $archiveGatePath,
    $releaseManifestPath
)
foreach ($sidecar in $sidecars) {
    Assert-DheSafeReportPath -Path $sidecar `
        -ProtectedPaths @($PackageRoot, $RuntimeManifest, $InstalledConsumerGate) | Out-Null
    if (Test-Path -LiteralPath $sidecar) {
        if (-not $ForceOutput) {
            throw "DHE transport release output already exists: $sidecar"
        }
        if (-not (Test-Path -LiteralPath $sidecar -PathType Leaf)) {
            throw "DHE transport release refuses to replace a non-file sidecar: $sidecar"
        }
    }
}

function Invoke-Git([string[]]$Arguments) {
    $output = @(& git -C $SourceRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git -C '$SourceRoot' $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Read-And-ValidateJson([string]$Path, [string]$SchemaName, [string]$Description) {
    $schemaPath = Join-Path $SourceRoot ("schemas/" + $SchemaName)
    $schemaErrors = @()
    if (-not (Test-Json -LiteralPath $Path -SchemaFile $schemaPath -ErrorVariable schemaErrors 2>$null)) {
        $detail = @($schemaErrors | ForEach-Object { $_.Exception.Message }) -join " | "
        throw "$Description failed JSON Schema validation: $Path ($detail)"
    }
    return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
}

function New-FileRecord([string]$Path) {
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        path = [IO.Path]::GetFileName($Path)
        size = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
    }
}

function Publish-StagedFile([string]$StagedPath, [string]$Destination) {
    if (Test-Path -LiteralPath $Destination -PathType Leaf) {
        Remove-Item -LiteralPath $Destination -Force
    }
    Move-Item -LiteralPath $StagedPath -Destination $Destination
}

$sourceStatus = @(Invoke-Git @("status", "--porcelain=v1", "--untracked-files=all"))
if ($sourceStatus.Count -ne 0) {
    throw "DHE transport Release requires a clean source repository."
}
$sourceHead = [string](Invoke-Git @("rev-parse", "HEAD") | Select-Object -First 1)
$sourceTree = [string](Invoke-Git @("rev-parse", "HEAD^{tree}") | Select-Object -First 1)

$consumerGate = Read-And-ValidateJson $InstalledConsumerGate `
    "dhe-installed-consumer-gate.schema.json" "Installed-consumer gate"
if (-not [bool]$consumerGate.passed -or [bool]$consumerGate.consumerRetained) {
    throw "DHE transport Release requires a passing installed-consumer gate with successful cleanup."
}
if ([string]$consumerGate.sourceHead -ne $sourceHead -or
    [string]$consumerGate.sourceTree -ne $sourceTree) {
    throw "Installed-consumer evidence does not bind the current source HEAD/tree."
}

$runtime = Read-And-ValidateJson $RuntimeManifest `
    "dhe-runtime-manifest.schema.json" "DHE runtime manifest"
if (-not [bool]$runtime.dheEnabled -or
    [string]$runtime.pathSemantics -ne "workspace-absolute-v1") {
    throw "DHE transport Release requires a workspace DHE runtime manifest."
}
$runtimeRoot = [IO.Path]::GetFullPath([string]$runtime.stagedLibil2cpp)
if (-not (Test-Path -LiteralPath $runtimeRoot -PathType Container)) {
    throw "DHE runtime tree was not found: $runtimeRoot"
}
$actualRuntimeHash = Get-TreeHash $runtimeRoot
if ($actualRuntimeHash -ne [string]$runtime.stagedRuntimeSha256 -or
    $actualRuntimeHash -ne [string]$consumerGate.runtimeTreeSha256) {
    throw "DHE runtime tree, manifest, and installed-consumer evidence are not identical."
}

$null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($PackageRoot))
& (Join-Path $SourceRoot "scripts/publish-dhe-toolchain.ps1") `
    -LabRoot $SourceRoot -OutputRoot $PackageRoot -Mode Release -ForceOutput:$ForceOutput
if ($LASTEXITCODE -ne 0) { throw "DHE toolchain package publishing failed." }
$packageManifestPath = Join-Path $PackageRoot "dhe-toolchain-manifest.json"
$packageManifest = Read-And-ValidateJson $packageManifestPath `
    "dhe-toolchain-manifest.schema.json" "DHE toolchain package manifest"
$packageId = [string]$packageManifest.packageId
if ($packageId -ne [string]$consumerGate.packageId -or
    [string]$packageManifest.sourceIdentity.head -ne $sourceHead -or
    [string]$packageManifest.sourceIdentity.tree -ne $sourceTree) {
    throw "Published package identity does not match installed-consumer or source evidence."
}
& (Join-Path $SourceRoot "scripts/test-dhe-toolchain-package.ps1") `
    -PackageRoot $PackageRoot -Output $packageGatePath -RequireRelease `
    -ExpectedPackageId $packageId
if ($LASTEXITCODE -ne 0) { throw "Published DHE toolchain package verification failed." }

$temporaryRoot = Join-Path $outputParent (".dhe-release-" + [Guid]::NewGuid().ToString("N"))
$null = New-Item -ItemType Directory -Path $temporaryRoot
try {
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $stagedArchive = Join-Path $temporaryRoot "package.zip"
    $archiveStream = [IO.File]::Open($stagedArchive, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $zip = [IO.Compression.ZipArchive]::new(
            $archiveStream,
            [IO.Compression.ZipArchiveMode]::Create,
            $false)
        try {
            $packageName = [IO.Path]::GetFileName($PackageRoot.TrimEnd('\', '/'))
            $packageFiles = @{}
            $packageRelativePaths = New-Object 'System.Collections.Generic.List[string]'
            foreach ($file in @(Get-DheRegularTreeFiles -Root $PackageRoot)) {
                $relative = $file.FullName.Substring($PackageRoot.Length).TrimStart('\', '/').Replace('\', '/')
                $packageFiles[$relative] = $file.FullName
                $packageRelativePaths.Add($relative)
            }
            $packageRelativePaths.Sort([StringComparer]::Ordinal)
            foreach ($relative in $packageRelativePaths) {
                $entry = $zip.CreateEntry(
                    ($packageName + "/" + $relative),
                    [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
                $entryStream = $entry.Open()
                $sourceStream = [IO.File]::OpenRead([string]$packageFiles[$relative])
                try { $sourceStream.CopyTo($entryStream) } finally {
                    $sourceStream.Dispose()
                    $entryStream.Dispose()
                }
            }
        } finally {
            $zip.Dispose()
        }
    } finally {
        $archiveStream.Dispose()
    }

    $extractRoot = Join-Path $temporaryRoot "extract"
    [IO.Compression.ZipFile]::ExtractToDirectory($stagedArchive, $extractRoot)
    $extractedPackage = Join-Path $extractRoot ([IO.Path]::GetFileName($PackageRoot.TrimEnd('\', '/')))
    $stagedArchiveGate = Join-Path $temporaryRoot "archive-package-gate.json"
    & (Join-Path $SourceRoot "scripts/test-dhe-toolchain-package.ps1") `
        -PackageRoot $extractedPackage -Output $stagedArchiveGate -RequireRelease `
        -ExpectedPackageId $packageId
    if ($LASTEXITCODE -ne 0) { throw "Transport archive package verification failed." }

    $stagedBundle = Join-Path $temporaryRoot "source.bundle"
    $bundleOutput = @(& git -C $SourceRoot bundle create $stagedBundle HEAD 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "DHE source bundle creation failed: $($bundleOutput -join [Environment]::NewLine)"
    }
    $bundleHeads = @(& git bundle list-heads $stagedBundle 2>&1)
    if ($LASTEXITCODE -ne 0 -or
        @($bundleHeads | Where-Object { $_ -eq "$sourceHead HEAD" }).Count -ne 1) {
        throw "DHE source bundle does not expose the exact source HEAD."
    }
    $null = @(& git -C $SourceRoot bundle verify $stagedBundle 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "DHE source bundle verification failed." }

    $stagedRuntimeRecord = Join-Path $temporaryRoot "runtime-manifest.json"
    $stagedConsumerRecord = Join-Path $temporaryRoot "installed-consumer-gate.json"
    Copy-Item -LiteralPath $RuntimeManifest -Destination $stagedRuntimeRecord
    Copy-Item -LiteralPath $InstalledConsumerGate -Destination $stagedConsumerRecord

    Publish-StagedFile $stagedArchive $archivePath
    Publish-StagedFile $stagedBundle $bundlePath
    Publish-StagedFile $stagedRuntimeRecord $runtimeRecordPath
    Publish-StagedFile $stagedConsumerRecord $consumerRecordPath
    Publish-StagedFile $stagedArchiveGate $archiveGatePath

    $release = [ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-toolchain-release.json"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        passed = $true
        pathSemantics = "release-sibling-v1"
        toolchainVersion = [string]$packageManifest.toolchainVersion
        contractVersion = [int]$packageManifest.contractVersion
        packageId = $packageId
        source = [ordered]@{
            head = $sourceHead
            tree = $sourceTree
            bundleVerified = $true
            bundle = New-FileRecord $bundlePath
        }
        runtime = [ordered]@{
            treeSha256 = $actualRuntimeHash.ToLowerInvariant()
            engineWorkflow = [string]$runtime.engineWorkflow
            manifest = New-FileRecord $runtimeRecordPath
        }
        package = [ordered]@{
            directory = [IO.Path]::GetFileName($PackageRoot.TrimEnd('\', '/'))
            manifest = ([IO.Path]::GetFileName($PackageRoot.TrimEnd('\', '/')) + "/dhe-toolchain-manifest.json")
            archiveVerified = $true
            archive = New-FileRecord $archivePath
            gate = New-FileRecord $packageGatePath
            archiveGate = New-FileRecord $archiveGatePath
        }
        installedConsumer = [ordered]@{
            sourceHead = [string]$consumerGate.sourceHead
            sourceTree = [string]$consumerGate.sourceTree
            consumerHead = [string]$consumerGate.consumerHead
            consumerTree = [string]$consumerGate.consumerTree
            report = New-FileRecord $consumerRecordPath
        }
    }
    $stagedReleaseManifest = Join-Path $temporaryRoot "release.json"
    [IO.File]::WriteAllText(
        $stagedReleaseManifest,
        ($release | ConvertTo-Json -Depth 10),
        (New-Object Text.UTF8Encoding($false)))
    $schemaErrors = @()
    $schemaPath = Join-Path $SourceRoot "schemas/dhe-toolchain-release.schema.json"
    if (-not (Test-Json -LiteralPath $stagedReleaseManifest -SchemaFile $schemaPath -ErrorVariable schemaErrors 2>$null)) {
        throw "DHE transport release manifest failed schema validation: $(@($schemaErrors | ForEach-Object { $_.Exception.Message }) -join ' | ')"
    }
    Publish-StagedFile $stagedReleaseManifest $releaseManifestPath
} finally {
    if (Test-Path -LiteralPath $temporaryRoot -PathType Container) {
        try {
            Remove-Item -LiteralPath $temporaryRoot -Recurse -Force
        } catch {
            Write-Warning "Unable to clean temporary DHE release directory '$temporaryRoot': $($_.Exception.Message)"
        }
    }
}

Write-Host "DHE toolchain transport release: $releaseManifestPath"
Write-Host "PackageId=$packageId; source=$sourceHead; runtime=$actualRuntimeHash"
exit 0
