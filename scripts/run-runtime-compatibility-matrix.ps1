param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$HybridClrSource = "../repos/hybridclr",
    [ValidateSet("Workflow", "Metadata")]
    [string]$CompatibilityScope = "Workflow",
    [string]$Output = "",
    [switch]$AllowDirty,
    [switch]$AllowSurrogateExternalHeaders
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$hybridClrPath = if ([IO.Path]::IsPathRooted($HybridClrSource)) {
    [IO.Path]::GetFullPath($HybridClrSource)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $HybridClrSource))
}

function Invoke-Git([string]$RepoPath, [string[]]$Arguments) {
    $output = & git -C $RepoPath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$RepoPath': $($output -join [Environment]::NewLine)"
    }
    return $output
}

$requestedHybridClrCommit = (Invoke-Git $hybridClrPath @("rev-parse", "HEAD")).Trim()
$requestedHybridClrTreeSha256 = Get-TreeHash (Join-Path $hybridClrPath "hybridclr")
$requestedHybridClrDirty = @(Invoke-Git $hybridClrPath @("status", "--porcelain")).Count -gt 0
if ($requestedHybridClrDirty -and -not $AllowDirty) {
    throw "HybridCLR source is dirty; pass -AllowDirty only for an explicit local matrix."
}
$manifestPath = Join-Path $LabRoot "manifests/runtime-workflows.json"
$manifest = Get-Content -Raw $manifestPath | ConvertFrom-Json
$results = @()
$hasSurrogateExternalHeaders = $false

foreach ($workflow in $manifest.workflows) {
    $il2cppSource = $workflow.il2cppPlus
    if ($CompatibilityScope -eq "Metadata") {
        if ($null -eq $workflow.PSObject.Properties["metadataIl2cppPlus"]) {
            throw "Metadata il2cpp_plus source is not declared for '$($workflow.id)'."
        }
        $il2cppSource = $workflow.metadataIl2cppPlus
    }
    $il2cppPath = [IO.Path]::GetFullPath((Join-Path $LabRoot $il2cppSource.path))
    if (-not (Test-Path $il2cppPath)) {
        throw "il2cpp_plus worktree for '$($workflow.id)' was not found: $il2cppPath"
    }
    $actualCommit = (& git -C $il2cppPath rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $actualCommit -ne $il2cppSource.commit) {
        throw "il2cpp_plus for '$($workflow.id)' is at $actualCommit, expected $($il2cppSource.commit)."
    }
    $requestedIl2CppTreeSha256 = Get-TreeHash (Join-Path $il2cppPath "libil2cpp")

    $nativeTestProfile = if ($CompatibilityScope -eq "Metadata") {
        switch ($workflow.id) {
            "Tuanjie2022Fgs" { "Metadata-Tuanjie2022" }
            "Unity2022Fgs" { "Metadata-Unity2022" }
            "Unity2021Standard" { "Metadata-Unity2021" }
            default { [string]$workflow.nativeTestProfile }
        }
    } else {
        [string]$workflow.nativeTestProfile
    }

    & (Join-Path $PSScriptRoot "assemble-runtime.ps1") `
        -LabRoot $LabRoot `
        -Profile $nativeTestProfile `
        -EngineWorkflow $workflow.id `
        -HybridClrSource $hybridClrPath `
        -Il2CppPlusSource $il2cppPath `
        -AllowDirty:$AllowDirty `
        -AllowSurrogateExternalHeaders:$AllowSurrogateExternalHeaders

    & (Join-Path $PSScriptRoot "run-native-tests.ps1") -LabRoot $LabRoot -Profile $nativeTestProfile
    if ($LASTEXITCODE -ne 0) { throw "Native compatibility tests failed for '$($workflow.id)'." }

    $runtime = Get-Content -Raw (Join-Path $LabRoot "staging/runtime/$nativeTestProfile/runtime-manifest.json") | ConvertFrom-Json
    $runtimeHybridClrPath = [IO.Path]::GetFullPath([string]$runtime.source.hybridclr.path)
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($runtimeHybridClrPath, $hybridClrPath) -or
        [string]$runtime.source.hybridclr.commit -ne $requestedHybridClrCommit -or
        [string]$runtime.source.hybridclr.treeSha256 -ne $requestedHybridClrTreeSha256 -or
        -not [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath([string]$runtime.source.il2cpp_plus.path), $il2cppPath) -or
        [string]$runtime.source.il2cpp_plus.commit -ne $actualCommit -or
        [string]$runtime.source.il2cpp_plus.treeSha256 -ne $requestedIl2CppTreeSha256) {
        throw "Runtime provenance for '$($workflow.id)' does not match the requested HybridCLR source."
    }
    $usesSurrogateExternalHeaders = [bool]$runtime.externalHeaders.surrogate
    $hasSurrogateExternalHeaders = $hasSurrogateExternalHeaders -or $usesSurrogateExternalHeaders
    $results += [ordered]@{
        workflow = $workflow.id
        nativeTestProfile = $nativeTestProfile
        fullGenericSharingTests = [bool]$runtime.fullGenericSharingDiagnostics
        engine = $workflow.engine
        fullGenericSharing = $workflow.fullGenericSharing
        productionWorkflow = $workflow.productionWorkflow
        hybridclrCommit = $runtime.source.hybridclr.commit
        hybridclrDirty = $runtime.source.hybridclr.dirty
        hybridclrTreeSha256 = $runtime.source.hybridclr.treeSha256
        il2cppPlusCommit = $runtime.source.il2cpp_plus.commit
        il2cppPlusTreeSha256 = $runtime.source.il2cpp_plus.treeSha256
        stagedRuntimeSha256 = $runtime.stagedRuntimeSha256
        externalHeaders = $runtime.externalHeaders
        nativeTestsPassed = $true
    }
}

$report = [ordered]@{
    schemaVersion = 1
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    evidenceScope = "nativeCompatibility"
    compatibilityScope = $CompatibilityScope
    hybridclrSource = $hybridClrPath
    hybridclrCommit = $requestedHybridClrCommit
    hybridclrDirty = $requestedHybridClrDirty
    hybridclrTreeSha256 = $requestedHybridClrTreeSha256
    passed = $true
    surrogateExternalHeadersUsed = $hasSurrogateExternalHeaders
    mergeReady = -not $hasSurrogateExternalHeaders
    workflows = $results
}
$reportRelativePath = if ([string]::IsNullOrWhiteSpace($Output)) {
    "reports/runtime-compatibility-matrix-$($CompatibilityScope.ToLowerInvariant()).json"
} else {
    $Output
}
$reportPath = if ([IO.Path]::IsPathRooted($reportRelativePath)) {
    [IO.Path]::GetFullPath($reportRelativePath)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $reportRelativePath))
}
New-Item -ItemType Directory -Force -Path (Split-Path $reportPath) | Out-Null
$report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $reportPath -Encoding UTF8
if ($report.mergeReady) {
    Write-Host "Runtime compatibility matrix passed and is merge-ready."
} else {
    Write-Warning "Runtime compatibility matrix passed its native tests but is not merge-ready because surrogate external headers were used."
}
Write-Host "Report: $reportPath"
