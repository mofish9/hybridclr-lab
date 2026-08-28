[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$LayoutPath = "",
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [ValidateSet("Release", "Exploratory")]
    [string]$Mode = "Release",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else { [IO.Path]::GetFullPath($LabRoot) }
$LayoutPath = if ([string]::IsNullOrWhiteSpace($LayoutPath)) {
    Join-Path $LabRoot "manifests/dhe-toolchain-layout.json"
} else { [IO.Path]::GetFullPath($LayoutPath) }
$OutputRoot = [IO.Path]::GetFullPath($OutputRoot)

Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
if (-not (Test-Path -LiteralPath $LayoutPath -PathType Leaf)) {
    throw "DHE toolchain layout was not found: $LayoutPath"
}

$existingOutputItems = @(if (Test-Path -LiteralPath $OutputRoot -PathType Container) {
    Get-ChildItem -LiteralPath $OutputRoot -Force -ErrorAction SilentlyContinue
})
if ($existingOutputItems.Count -gt 0 -and $ForceOutput) {
    $existingManifestPath = Join-Path $OutputRoot "dhe-toolchain-manifest.json"
    if (-not (Test-Path -LiteralPath $existingManifestPath -PathType Leaf)) {
        throw "DHE publisher refuses to replace a non-empty directory that is not a verified toolchain package: $OutputRoot"
    }
    $replacementGate = New-DheTemporaryReportPath "publisher-replacement-gate"
    try {
        & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") `
            -PackageRoot $OutputRoot -Output $replacementGate | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "DHE publisher refuses to replace an invalid existing toolchain package: $OutputRoot"
        }
    } finally {
        if (Test-Path -LiteralPath $replacementGate -PathType Leaf) { Remove-Item -LiteralPath $replacementGate -Force }
    }
    Assert-DheSafeVerifiedReplacementRoot -Path $OutputRoot -ProtectedPaths @($LayoutPath)
    $containingGitRoot = Find-DheContainingGitRoot $OutputRoot
    if (-not [string]::IsNullOrWhiteSpace([string]$containingGitRoot)) {
        $relativeOutput = $OutputRoot.Substring($containingGitRoot.Length).TrimStart('\', '/').Replace('\', '/')
        $oldErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            [object[]]$trackedOutput = @(& git -C $containingGitRoot ls-files --cached -- "$relativeOutput" 2>&1)
            $trackedOutputExitCode = [int]$LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldErrorActionPreference
        }
        if ($trackedOutputExitCode -ne 0 -or @($trackedOutput).Count -gt 0) {
            throw "DHE publisher refuses to replace a Git-tracked package directory: $OutputRoot"
        }
    }
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
    $null = New-Item -ItemType Directory -Force -Path $OutputRoot
} else {
    Assert-DheSafeOutputRoot -Path $OutputRoot -ProtectedPaths @($LayoutPath)
    $null = Initialize-DheOutputRoot -Path $OutputRoot -Force:$ForceOutput -ProtectedPaths @($LayoutPath)
}

$layout = Get-Content -Raw -LiteralPath $LayoutPath | ConvertFrom-Json
if ([int]$layout.schemaVersion -ne 1 -or
    [string]$layout.format -ne "hybridclr.dhe-toolchain-layout.json" -or
    [string]$layout.toolchainVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
    [int]$layout.contractVersion -ne 1) {
    throw "DHE toolchain layout has an unsupported contract: $LayoutPath"
}

function Assert-SafeRelativePath([string]$Value, [string]$Description) {
    $normalized = $Value.Replace('\', '/')
    if ([string]::IsNullOrWhiteSpace($normalized) -or
        [IO.Path]::IsPathRooted($normalized) -or
        $normalized -match '(^|/)\.\.(/|$)') {
        throw "$Description contains an unsafe path: $Value"
    }
    return $normalized.TrimStart('/')
}

$sourceFiles = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
foreach ($layoutEntry in @($layout.exactPaths)) {
    $relative = Assert-SafeRelativePath ([string]$layoutEntry) "DHE toolchain layout exactPaths"
    $source = Join-Path $LabRoot $relative
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "DHE toolchain source file was not found: $source"
    }
    $null = @(Get-DheRegularTreeFiles -Root $source)
    $null = $sourceFiles.Add($relative)
}
foreach ($layoutPrefix in @($layout.prefixes)) {
    $prefix = Assert-SafeRelativePath ([string]$layoutPrefix) "DHE toolchain layout prefixes"
    $wildcardIndex = $prefix.IndexOf('*')
    $scanRelative = if ($wildcardIndex -ge 0) {
        $beforeWildcard = $prefix.Substring(0, $wildcardIndex)
        $slashIndex = $beforeWildcard.LastIndexOf('/')
        if ($slashIndex -lt 0) { "" } else { $beforeWildcard.Substring(0, $slashIndex) }
    } else { $prefix.TrimEnd('/') }
    $scanRoot = if ([string]::IsNullOrWhiteSpace($scanRelative)) { $LabRoot } else { Join-Path $LabRoot $scanRelative }
    if (-not (Test-Path -LiteralPath $scanRoot -PathType Container)) {
        throw "DHE toolchain source prefix root was not found: $scanRoot"
    }
    $matches = @()
    foreach ($item in @(Get-DheRegularTreeFiles -Root $scanRoot)) {
        $relative = $item.FullName.Substring($LabRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        $matched = if ($prefix.Contains('*')) { $relative -like $prefix } else {
            $relative.Equals($prefix.TrimEnd('/'), [StringComparison]::OrdinalIgnoreCase) -or
                $relative.StartsWith($prefix.TrimEnd('/') + '/', [StringComparison]::OrdinalIgnoreCase)
        }
        if ($matched) {
            $matches += $relative
            $null = $sourceFiles.Add($relative)
        }
    }
    if ($matches.Count -eq 0) { throw "DHE toolchain source prefix matched no files: $prefix" }
}

$generatedPaths = @($layout.generatedPaths | ForEach-Object {
    Assert-SafeRelativePath ([string]$_) "DHE toolchain layout generatedPaths"
})
if (@($sourceFiles | Where-Object { $_ -in $generatedPaths }).Count -gt 0) {
    throw "DHE toolchain layout may not list a path as both source and generated output."
}

foreach ($relative in @($sourceFiles | Sort-Object)) {
    $source = Join-Path $LabRoot $relative
    $destination = Join-Path $OutputRoot $relative
    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destination))
    Copy-Item -LiteralPath $source -Destination $destination -Force
}

# The lab attributes also protect raw Demo package bytes. The distributed
# toolchain has no Demo payload, so publish a package-only attributes file.
$portableAttributes = @(
    "# Canonical byte policy for the distributed DHE toolchain.",
    "/.gitattributes text eol=lf",
    "/dhe.ps1 text eol=lf",
    "/dhe-source-boundary.json text eol=lf",
    "/dhe-toolchain-manifest.json text eol=lf",
    "/docs/HybridCLR-DHE-Toolchain.md text eol=lf",
    "/docs/THIRD-PARTY-NOTICES.md text eol=lf",
    "/manifests/*.json text eol=lf",
    "/schemas/dhe-*.json text eol=lf",
    "/scripts/*.ps1 text eol=lf",
    "/templates/*.ps1 text eol=lf",
    "/patches/dhe-lite/*.patch -text"
) -join "`n"
[IO.File]::WriteAllText((Join-Path $OutputRoot ".gitattributes"), $portableAttributes + "`n", (New-Object Text.UTF8Encoding($false)))

function Remove-JsonProperty($Object, [string]$Name) {
    if ($null -ne $Object -and $null -ne $Object.PSObject.Properties[$Name]) {
        $Object.PSObject.Properties.Remove($Name)
    }
}
function Write-PortableJson($Document, [string]$Destination) {
    $null = New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($Destination))
    $json = ($Document | ConvertTo-Json -Depth 20).Replace("`r`n", "`n").Replace("`r", "`n")
    [IO.File]::WriteAllText($Destination, $json, (New-Object Text.UTF8Encoding($false)))
}

$repoLockSource = Join-Path $LabRoot "manifests/repo-lock.json"
$workflowSource = Join-Path $LabRoot "manifests/runtime-workflows.json"
foreach ($requiredInput in @($repoLockSource, $workflowSource)) {
    if (-not (Test-Path -LiteralPath $requiredInput -PathType Leaf)) {
        throw "DHE portable lock input was not found: $requiredInput"
    }
}
$repoLock = Get-Content -Raw -LiteralPath $repoLockSource | ConvertFrom-Json
Remove-JsonProperty $repoLock.engine "editorPath"
Remove-JsonProperty $repoLock.engine "executablePath"
Write-PortableJson $repoLock (Join-Path $OutputRoot "manifests/repo-lock.json")

$workflows = Get-Content -Raw -LiteralPath $workflowSource | ConvertFrom-Json
foreach ($workflow in @($workflows.workflows)) {
    Remove-JsonProperty $workflow.engine "executablePath"
    Remove-JsonProperty $workflow.engine "nativeTestExternalPath"
    Remove-JsonProperty $workflow.il2cppPlus "path"
}
Write-PortableJson $workflows (Join-Path $OutputRoot "manifests/runtime-workflows.json")

$packageBoundary = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-source-boundary.json"
    pathBase = "manifest-directory-v1"
    exactPaths = @(".gitattributes", "dhe.ps1", "dhe-toolchain-manifest.json")
    prefixes = @("docs/", "manifests/", "patches/dhe-lite/", "schemas/", "scripts/", "templates/")
    generatedPrefixes = @("artifacts/", "staging/", "reports/")
}
Write-PortableJson $packageBoundary (Join-Path $OutputRoot "dhe-source-boundary.json")

$gitTop = @(& git -C $LabRoot rev-parse --show-toplevel 2>$null)
$gitRoot = if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace(($gitTop -join "").Trim())) {
    [IO.Path]::GetFullPath(($gitTop -join "").Trim())
} else { $null }
$gitHead = $null
$gitTree = $null
$gitClean = $false
$sourcesTracked = $false
if ($null -ne $gitRoot) {
    $gitHeadOutput = @(& git -C $gitRoot rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -eq 0) { $gitHead = ($gitHeadOutput -join "").Trim().ToLowerInvariant() }
    $gitTreeOutput = @(& git -C $gitRoot rev-parse 'HEAD^{tree}' 2>$null)
    if ($LASTEXITCODE -eq 0) { $gitTree = ($gitTreeOutput -join "").Trim().ToLowerInvariant() }
    $gitStatus = @(& git -C $gitRoot status --porcelain=v1 --untracked-files=all 2>$null)
    $gitClean = $LASTEXITCODE -eq 0 -and $gitStatus.Count -eq 0
    $tracked = @(& git -C $gitRoot ls-files 2>$null)
    if ($LASTEXITCODE -eq 0) {
        $trackedSet = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
        foreach ($item in @($tracked)) { $null = $trackedSet.Add(([string]$item).Replace('\', '/')) }
        $requiredSourceInputs = @($sourceFiles) + @(
            $repoLockSource.Substring($gitRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/'),
            $workflowSource.Substring($gitRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
        )
        $sourcesTracked = @($requiredSourceInputs | Where-Object { -not $trackedSet.Contains([string]$_) }).Count -eq 0
    }
}
if ($Mode -eq "Release" -and (-not $gitClean -or -not $sourcesTracked -or
    $gitHead -notmatch '^[0-9a-fA-F]{40,64}$' -or $gitTree -notmatch '^[0-9a-fA-F]{40,64}$')) {
    throw "A Release DHE toolchain package requires a clean Git checkout with every package source tracked."
}

$manifestPath = Join-Path $OutputRoot "dhe-toolchain-manifest.json"
$fileRecords = New-Object System.Collections.Generic.List[object]
foreach ($file in @(Get-DheRegularTreeFiles -Root $OutputRoot | Sort-Object FullName)) {
    if ($file.FullName.Equals($manifestPath, [StringComparison]::OrdinalIgnoreCase)) { continue }
    $relative = $file.FullName.Substring($OutputRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
    $fileRecords.Add([ordered]@{
        path = $relative
        size = [int64]$file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}
$releaseReady = $Mode -eq "Release" -and $gitClean -and $sourcesTracked
$layoutSha256 = (Get-FileHash -LiteralPath $LayoutPath -Algorithm SHA256).Hash.ToLowerInvariant()
$packageId = Get-DheToolchainPackageId `
    -ToolchainVersion ([string]$layout.toolchainVersion) `
    -ContractVersion ([int]$layout.contractVersion) `
    -Mode $Mode `
    -SourceHead ([string]$gitHead) `
    -SourceTree ([string]$gitTree) `
    -LayoutSha256 $layoutSha256 `
    -Files $fileRecords.ToArray()
$manifest = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-toolchain-manifest.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    toolchainVersion = [string]$layout.toolchainVersion
    contractVersion = [int]$layout.contractVersion
    mode = $Mode
    releaseReady = $releaseReady
    pathSemantics = "package-relative-v1"
    packageId = $packageId
    entryPoint = "dhe.ps1"
    commands = @($layout.commands)
    layoutSha256 = $layoutSha256
    sourceIdentity = [ordered]@{
        head = $gitHead
        tree = $gitTree
        clean = $gitClean
        tracked = $sourcesTracked
    }
    fileCount = $fileRecords.Count
    files = $fileRecords.ToArray()
}
$manifestJson = ($manifest | ConvertTo-Json -Depth 12).Replace("`r`n", "`n").Replace("`r", "`n")
[IO.File]::WriteAllText($manifestPath, $manifestJson, (New-Object Text.UTF8Encoding($false)))
$publicationGate = New-DheTemporaryReportPath "publisher-final-gate"
try {
    $publicationGateArgs = @{ PackageRoot = $OutputRoot; Output = $publicationGate }
    if ($Mode -eq "Release") { $publicationGateArgs.RequireRelease = $true }
    & (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") @publicationGateArgs | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Published DHE toolchain package failed final verification: $OutputRoot"
    }
} finally {
    if (Test-Path -LiteralPath $publicationGate -PathType Leaf) { Remove-Item -LiteralPath $publicationGate -Force }
}
Write-Host "DHE toolchain package: $manifestPath"
Write-Host ("Version={0}; files={1}; mode={2}; releaseReady={3}" -f `
    $manifest.toolchainVersion, $manifest.fileCount, $Mode, $releaseReady)
exit 0
