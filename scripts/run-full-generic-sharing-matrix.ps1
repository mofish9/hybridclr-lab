param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProjectRoot = "",
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs")]
    [string]$EngineWorkflow = "Tuanjie2022Fgs",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic")]
    [string]$Profile = "Fgs-Candidate",
    [string]$HybridClrSource = "../worktrees/hybridclr-fgs-compatibility-v8.13.0",
    [string]$Il2CppPlusSource = "",
    [switch]$SkipBuild,
    [switch]$SkipAssembly,
    [switch]$ProductionOnly,
    [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "runtime-provenance.ps1")
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$workflowManifest = Get-Content -Raw (Join-Path $LabRoot "manifests/runtime-workflows.json") | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found." }
$workflow = $workflow[0]
$unity2022Profiles = @("Unity2022-Candidate", "Unity2022-Fgs-Diagnostic")
if ($EngineWorkflow -eq "Unity2022Fgs" -and $Profile -notin $unity2022Profiles) {
    throw "Unity2022Fgs requires a Unity2022-owned FGS profile; received '$Profile'."
}
if ($EngineWorkflow -eq "Tuanjie2022Fgs" -and $Profile -in $unity2022Profiles) {
    throw "Profile '$Profile' is reserved for Unity2022Fgs."
}
$projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    Join-Path $LabRoot "unity-test-project"
} elseif ([IO.Path]::IsPathRooted($ProjectRoot)) {
    [IO.Path]::GetFullPath($ProjectRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $ProjectRoot))
}
$hybridClrPath = if ([IO.Path]::IsPathRooted($HybridClrSource)) {
    [IO.Path]::GetFullPath($HybridClrSource)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $HybridClrSource))
}
$effectiveIl2CppPlusSource = if ([string]::IsNullOrWhiteSpace($Il2CppPlusSource)) {
    [string]$workflow.il2cppPlus.path
} else {
    $Il2CppPlusSource
}
$il2cppPlusPath = if ([IO.Path]::IsPathRooted($effectiveIl2CppPlusSource)) {
    [IO.Path]::GetFullPath($effectiveIl2CppPlusSource)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $effectiveIl2CppPlusSource))
}

function Invoke-Git([string]$RepoPath, [string[]]$Arguments) {
    $output = & git -C $RepoPath @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in '$RepoPath': $($output -join [Environment]::NewLine)"
    }
    return $output
}

$requestedHybridClrCommit = (Invoke-Git $hybridClrPath @("rev-parse", "HEAD")).Trim()
$requestedHybridClrDirty = @(Invoke-Git $hybridClrPath @("status", "--porcelain")).Count -gt 0
if ($requestedHybridClrDirty -and -not $AllowDirty) {
    throw "HybridCLR source is dirty; pass -AllowDirty only for an explicit local FGS matrix."
}
$requestedHybridClrTreeSha256 = Get-TreeHash (Join-Path $hybridClrPath "hybridclr")
$requestedIl2CppPlusCommit = (Invoke-Git $il2cppPlusPath @("rev-parse", "HEAD")).Trim()
if ($requestedIl2CppPlusCommit -ne [string]$workflow.il2cppPlus.commit) {
    throw "il2cpp_plus for '$EngineWorkflow' is at $requestedIl2CppPlusCommit, expected $($workflow.il2cppPlus.commit)."
}
$requestedIl2CppPlusTreeSha256 = Get-TreeHash (Join-Path $il2cppPlusPath "libil2cpp")
$configurations = if ($ProductionOnly) {
    @([pscustomobject]@{ CodeGeneration = "OptimizeSize"; Packaging = "exclude" })
} else {
    foreach ($packaging in @("include", "exclude")) {
        [pscustomobject]@{ CodeGeneration = "OptimizeSize"; Packaging = $packaging }
    }
}
$skipAssemblyForBuild = $SkipAssembly
foreach ($configuration in $configurations) {
        $codeGeneration = $configuration.CodeGeneration
        $packaging = $configuration.Packaging
        if (-not $SkipBuild) {
            & (Join-Path $PSScriptRoot "build-clean-baseline.ps1") `
                -LabRoot $LabRoot `
                -ProjectRoot $projectRoot `
                -Profile $Profile `
                -EngineWorkflow $EngineWorkflow `
                -HybridClrSource $hybridClrPath `
                -Il2CppPlusSource $il2cppPlusPath `
                -Il2CppCodeGeneration $codeGeneration `
                -AotMetadataPackaging $packaging `
                -SkipAssembly:$skipAssemblyForBuild `
                -SkipPlayerRun `
                -AllowDirty:$AllowDirty
            $skipAssemblyForBuild = $true
        }

        $codeGenerationVariant = if ($codeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$codeGeneration" }
        $buildVariant = if ($packaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
        $buildManifestPath = Join-Path $LabRoot "reports/$($buildVariant.ToLowerInvariant())-build-manifest.json"
        if (-not (Test-Path $buildManifestPath)) { throw "FGS build manifest was not found: $buildManifestPath" }
        $buildManifest = Get-Content -Raw $buildManifestPath | ConvertFrom-Json
        $buildHybridClrPath = [IO.Path]::GetFullPath([string]$buildManifest.repositories.hybridclr.path)
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($buildHybridClrPath, $hybridClrPath) -or
            [string]$buildManifest.repositories.hybridclr.commit -ne $requestedHybridClrCommit -or
            [string]$buildManifest.repositories.hybridclr.treeSha256 -ne $requestedHybridClrTreeSha256 -or
            -not [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath([string]$buildManifest.repositories.il2cpp_plus.path), $il2cppPlusPath) -or
            [string]$buildManifest.repositories.il2cpp_plus.commit -ne $requestedIl2CppPlusCommit -or
            [string]$buildManifest.repositories.il2cpp_plus.treeSha256 -ne $requestedIl2CppPlusTreeSha256) {
            throw "FGS build '$buildVariant' does not match the requested HybridCLR and il2cpp_plus source trees."
        }

        $metadataModes = if ($packaging -eq "include") { @("supplemental", "none") } else { @("none") }
        foreach ($metadataMode in $metadataModes) {
            & (Join-Path $PSScriptRoot "run-full-generic-sharing-gate.ps1") `
                -LabRoot $LabRoot `
                -ProjectRoot $projectRoot `
                -EngineWorkflow $EngineWorkflow `
                -Profile $Profile `
                -Il2CppCodeGeneration $codeGeneration `
                -AotMetadataMode $metadataMode `
                -AotMetadataPackaging $packaging
        }
}

Write-Host "Full generic sharing matrix passed for $Profile."
