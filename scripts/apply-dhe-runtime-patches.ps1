[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$NativeRoot,
    [string]$PackageRoot = "",
    [string]$HybridClrSource = "",
    [string]$Il2CppPlusSource = "",
    [string]$HybridClrUnitySource = "",
    [string]$LabRoot = "",
    [switch]$VerifyOnly,
    [switch]$RequireApplied
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$NativeRoot = [IO.Path]::GetFullPath($NativeRoot)
$lockPath = Join-Path $LabRoot "manifests/dhe-runtime-lock.json"
if (-not (Test-Path -LiteralPath $lockPath -PathType Leaf)) {
    throw "DHE runtime lock was not found: $lockPath"
}
$lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
$lockSourceMode = if ($null -ne $lock.PSObject.Properties["sourceMode"]) {
    [string]$lock.sourceMode
} else {
    "overlay"
}
if ($lockSourceMode -notin @("overlay", "integrated")) {
    throw "DHE runtime lock sourceMode must be 'overlay' or 'integrated'."
}

function Invoke-Git([string]$WorkingDirectory, [string[]]$Arguments) {
    $output = & git -C $WorkingDirectory @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$WorkingDirectory': $($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Assert-Commit([string]$RepositoryPath, [string]$Expected, [string]$Name) {
    if ([string]::IsNullOrWhiteSpace($RepositoryPath)) { return }
    if (-not (Test-Path -LiteralPath $RepositoryPath -PathType Container)) {
        throw "$Name source is not a Git repository: $RepositoryPath"
    }
    $null = & git -C $RepositoryPath rev-parse --git-dir 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "$Name source is not a Git repository: $RepositoryPath"
    }
    $actual = ((Invoke-Git $RepositoryPath @("rev-parse", "HEAD")) -join "`n").Trim()
    if ($actual -ne $Expected) {
        throw "$Name source is at '$actual'; DHE patch requires base commit '$Expected'."
    }
}

function Assert-Patch([object]$Entry) {
    $patchPath = [IO.Path]::GetFullPath((Join-Path $LabRoot ([string]$Entry.path)))
    if (-not (Test-Path -LiteralPath $patchPath -PathType Leaf)) {
        throw "DHE patch '$($Entry.id)' was not found: $patchPath"
    }
    $actualHash = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actualHash -ne ([string]$Entry.sha256).ToUpperInvariant()) {
        throw "DHE patch '$($Entry.id)' hash mismatch. Expected $($Entry.sha256), got $actualHash."
    }
    return $patchPath
}

function Get-EntrySourceMode([object]$Entry) {
    if ($null -ne $Entry.PSObject.Properties["sourceMode"]) {
        return [string]$Entry.sourceMode
    }
    return $lockSourceMode
}

function Assert-IntegratedSource([string]$RepositoryPath, [object]$Entry, [string]$Name, [string]$TreeRoot,
    [switch]$UsePackageHashPolicy) {
    $integratedCommitProperty = $Entry.PSObject.Properties["integratedCommit"]
    $expectedTreeProperty = $Entry.PSObject.Properties["expectedTreeSha256"]
    if ($null -eq $integratedCommitProperty -or
        ([string]$integratedCommitProperty.Value) -notmatch '^[0-9a-fA-F]{40}$' -or
        $null -eq $expectedTreeProperty -or
        ([string]$expectedTreeProperty.Value) -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Integrated DHE entry '$($Entry.id)' is missing integratedCommit or expectedTreeSha256."
    }
    Assert-Commit $RepositoryPath ([string]$integratedCommitProperty.Value) $Name
    $actualTree = if ($UsePackageHashPolicy) {
        Get-DhePackageSourceTreeHash $TreeRoot
    } else {
        Get-TreeHashExcludingGit $TreeRoot
    }
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualTree, [string]$expectedTreeProperty.Value)) {
        throw "$Name integrated tree hash mismatch. Expected $($expectedTreeProperty.Value), got $actualTree."
    }
    return $actualTree
}

function Apply-Patch([string]$Root, [object]$Entry, [string]$PatchPath) {
    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        throw "DHE patch root was not found: $Root"
    }

    $stripComponents = if ($null -ne $Entry.PSObject.Properties["stripComponents"]) { [int]$Entry.stripComponents } else { 1 }
    if ($stripComponents -lt 0 -or $stripComponents -gt 8) { throw "Invalid stripComponents for DHE patch '$($Entry.id)'." }
    $check = Invoke-DheGitApplyAtRoot -Root $Root -PatchPath $PatchPath -StripComponents $stripComponents -Check
    if ($check.exitCode -ne 0) {
        $reverse = Invoke-DheGitApplyAtRoot -Root $Root -PatchPath $PatchPath -StripComponents $stripComponents -Check -Reverse
        if ($reverse.exitCode -eq 0) {
            Write-Host "DHE patch already applied: $($Entry.id)"
            return "already-applied"
        }
        throw "DHE patch '$($Entry.id)' does not apply cleanly to '$Root': $($check.output -join [Environment]::NewLine)"
    }
    if ($VerifyOnly) {
        if ($RequireApplied) {
            throw "DHE patch '$($Entry.id)' is not applied to '$Root'."
        }
        Write-Host "DHE patch verified: $($Entry.id)"
        return "verified"
    }
    $apply = Invoke-DheGitApplyAtRoot -Root $Root -PatchPath $PatchPath -StripComponents $stripComponents
    if ($apply.exitCode -ne 0) {
        throw "Failed to apply DHE patch '$($Entry.id)' to '$Root': $($apply.output -join [Environment]::NewLine)"
    }
    Write-Host "Applied DHE patch: $($Entry.id)"
    return "applied"
}

function Undo-Patch([object]$Item) {
    $root = [string]$Item.root
    $entry = $Item.entry
    $patchPath = [string]$Item.patchPath
    $stripComponents = if ($null -ne $entry.PSObject.Properties["stripComponents"]) { [int]$entry.stripComponents } else { 1 }
    $reverse = Invoke-DheGitApplyAtRoot -Root $root -PatchPath $patchPath -StripComponents $stripComponents -Reverse
    if ($reverse.exitCode -ne 0) {
        Write-Warning "Unable to roll back DHE patch '$($entry.id)' from '$root'."
    }
}

$nativeEntries = @($lock.patches | Where-Object { $_.applyRoot -eq "libil2cpp" })
if ($nativeEntries.Count -eq 0) {
    throw "DHE runtime lock contains no libil2cpp patches."
}
$nativeResults = New-Object System.Collections.Generic.List[object]
$appliedForRollback = New-Object System.Collections.Generic.List[object]
try {
foreach ($nativeEntry in $nativeEntries) {
    $nativeSource = switch ([string]$nativeEntry.repository) {
        "hybridclr" { $HybridClrSource; break }
        "il2cpp_plus" { $Il2CppPlusSource; break }
        default { throw "Unsupported native DHE patch repository '$($nativeEntry.repository)'." }
    }
    if ([string]::IsNullOrWhiteSpace($nativeSource)) {
        throw "A source path is required to verify native DHE patch '$($nativeEntry.id)'."
    }
    $entrySourceMode = Get-EntrySourceMode $nativeEntry
    if ($entrySourceMode -ne $lockSourceMode) {
        throw "DHE runtime lock mixes source modes: lock=$lockSourceMode, entry=$($nativeEntry.id) mode=$entrySourceMode."
    }
    if ($entrySourceMode -eq "integrated") {
        $sourceTreeRoot = switch ([string]$nativeEntry.repository) {
            "hybridclr" { Join-Path $nativeSource "hybridclr"; break }
            "il2cpp_plus" { Join-Path $nativeSource "libil2cpp"; break }
            default { throw "Unsupported native DHE patch repository '$($nativeEntry.repository)'." }
        }
        $nativeTree = Assert-IntegratedSource $nativeSource $nativeEntry ([string]$nativeEntry.repository) $sourceTreeRoot
        $stagedTreeRoot = if ([string]$nativeEntry.repository -eq "hybridclr") {
            Join-Path $NativeRoot "hybridclr"
        } else {
            $NativeRoot
        }
        if (-not (Test-Path -LiteralPath $stagedTreeRoot -PathType Container)) {
            throw "Integrated DHE staged tree was not found: $stagedTreeRoot"
        }
        $stagedTree = Get-TreeHash $stagedTreeRoot
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($stagedTree, $nativeTree)) {
            throw "Integrated DHE staged tree differs from '$($nativeEntry.id)': expected $nativeTree, got $stagedTree."
        }
        $nativePatchResult = "integrated-verified"
        $nativePatch = $null
    } else {
        Assert-Commit $nativeSource $nativeEntry.baseCommit ([string]$nativeEntry.repository)
        $nativePatch = Assert-Patch $nativeEntry
        $nativePatchResult = Apply-Patch $NativeRoot $nativeEntry $nativePatch
    }
    if ($nativePatchResult -eq "applied") {
        $appliedForRollback.Add([ordered]@{ root = $NativeRoot; entry = $nativeEntry; patchPath = $nativePatch })
    }
    $nativeResults.Add([ordered]@{
        id = [string]$nativeEntry.id
        result = $nativePatchResult
    })
}

if (-not [string]::IsNullOrWhiteSpace($PackageRoot)) {
    if ([string]::IsNullOrWhiteSpace($HybridClrUnitySource)) {
        throw "-HybridClrUnitySource is required whenever -PackageRoot is provided."
    }
    $PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
    $packageEntries = @($lock.patches | Where-Object { $_.applyRoot -eq "package" })
    if ($packageEntries.Count -eq 0) {
        throw "DHE runtime lock contains no package patches."
    }
    $packageResults = New-Object System.Collections.Generic.List[object]
    foreach ($entry in $packageEntries) {
        $entrySourceMode = Get-EntrySourceMode $entry
        if ($entrySourceMode -ne $lockSourceMode) {
            throw "DHE runtime lock mixes source modes: lock=$lockSourceMode, entry=$($entry.id) mode=$entrySourceMode."
        }
        if ($entrySourceMode -eq "integrated") {
            $sourceTree = Assert-IntegratedSource $HybridClrUnitySource $entry "hybridclr_unity" $HybridClrUnitySource `
                -UsePackageHashPolicy
            $packageTree = Get-DhePackageTreeHash $PackageRoot
            if (-not [StringComparer]::OrdinalIgnoreCase.Equals($packageTree, $sourceTree)) {
                throw "Integrated DHE package tree differs from hybridclr_unity: expected $sourceTree, got $packageTree."
            }
            $packagePatchResult = "integrated-verified"
            $patchPath = $null
        } else {
            Assert-Commit $HybridClrUnitySource $entry.baseCommit "hybridclr_unity"
            $patchPath = Assert-Patch $entry
            $packagePatchResult = Apply-Patch $PackageRoot $entry $patchPath
        }
        if ($packagePatchResult -eq "applied") {
            $appliedForRollback.Add([ordered]@{ root = $PackageRoot; entry = $entry; patchPath = $patchPath })
        }
        $packageResults.Add([ordered]@{
            id = [string]$entry.id
            result = $packagePatchResult
        })
    }
}
}
catch {
    for ($index = $appliedForRollback.Count - 1; $index -ge 0; --$index) {
        Undo-Patch $appliedForRollback[$index]
    }
    throw
}

$result = [ordered]@{
    schemaVersion = 1
    lock = [IO.Path]::GetFullPath($lockPath)
    sourceMode = $lockSourceMode
    nativeRoot = $NativeRoot
    nativePatches = $nativeResults.ToArray()
    verifyOnly = [bool]$VerifyOnly
    requireApplied = [bool]$RequireApplied
}
if (-not [string]::IsNullOrWhiteSpace($PackageRoot)) {
    $result.packageRoot = $PackageRoot
    $result.packagePatches = @($lock.patches | Where-Object { $_.applyRoot -eq "package" } | ForEach-Object id)
    $result.packagePatchResults = $packageResults.ToArray()
}
$result | ConvertTo-Json -Depth 8 | Write-Host
exit 0
