param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs", "Unity2021Standard")]
    [string]$EngineWorkflow,
    [string]$HybridClrSource = "../repos/hybridclr",
    [string]$Profile = "",
    [switch]$AllowDirty,
    [switch]$SkipBuild,
    [int]$PlayerTimeoutSeconds = 180
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
    throw "HybridCLR source is dirty; pass -AllowDirty only for an explicit local gate."
}
$workflowManifest = Get-Content -Raw (Join-Path $LabRoot "manifests/runtime-workflows.json") | ConvertFrom-Json
$workflow = @($workflowManifest.workflows | Where-Object id -eq $EngineWorkflow)
if ($workflow.Count -ne 1) { throw "Engine workflow '$EngineWorkflow' was not found." }
$workflow = $workflow[0]
$il2cppPath = [IO.Path]::GetFullPath((Join-Path $LabRoot $workflow.il2cppPlus.path))
if (-not (Test-Path $il2cppPath)) { throw "il2cpp_plus worktree for '$EngineWorkflow' was not found: $il2cppPath" }
$requestedIl2CppCommit = (Invoke-Git $il2cppPath @("rev-parse", "HEAD")).Trim()
if ($requestedIl2CppCommit -ne [string]$workflow.il2cppPlus.commit) {
    throw "il2cpp_plus for '$EngineWorkflow' is at $requestedIl2CppCommit, expected $($workflow.il2cppPlus.commit). Update the workflow manifest only after reviewing the new commit."
}
$requestedIl2CppTreeSha256 = Get-TreeHash (Join-Path $il2cppPath "libil2cpp")

if ($EngineWorkflow -in @("Tuanjie2022Fgs", "Unity2022Fgs")) {
    $productionProfile = if ($EngineWorkflow -eq "Tuanjie2022Fgs") { "Candidate" } else { "Unity2022-Candidate" }
    $diagnosticProfile = if ($EngineWorkflow -eq "Tuanjie2022Fgs") { "Fgs-Diagnostic" } else { "Unity2022-Fgs-Diagnostic" }
    $projectRoot = if ($SkipBuild) {
        Join-Path $LabRoot "artifacts/engine-projects/$EngineWorkflow"
    } else {
        (& (Join-Path $PSScriptRoot "prepare-engine-test-project.ps1") -LabRoot $LabRoot -EngineWorkflow $EngineWorkflow | Select-Object -Last 1)
    }
    if ([string]::IsNullOrWhiteSpace($Profile)) {
        & (Join-Path $PSScriptRoot "run-full-generic-sharing-matrix.ps1") `
            -LabRoot $LabRoot `
            -ProjectRoot $projectRoot `
            -EngineWorkflow $EngineWorkflow `
            -Profile $productionProfile `
            -HybridClrSource $hybridClrPath `
            -Il2CppPlusSource $il2cppPath `
            -SkipBuild:$SkipBuild `
            -AllowDirty:$AllowDirty
        & (Join-Path $PSScriptRoot "run-full-generic-sharing-matrix.ps1") `
            -LabRoot $LabRoot `
            -ProjectRoot $projectRoot `
            -EngineWorkflow $EngineWorkflow `
            -Profile $diagnosticProfile `
            -HybridClrSource $hybridClrPath `
            -Il2CppPlusSource $il2cppPath `
            -SkipBuild:$SkipBuild `
            -ProductionOnly `
            -AllowDirty:$AllowDirty
    } else {
        & (Join-Path $PSScriptRoot "run-full-generic-sharing-matrix.ps1") `
            -LabRoot $LabRoot `
            -ProjectRoot $projectRoot `
            -EngineWorkflow $EngineWorkflow `
            -Profile $Profile `
            -HybridClrSource $hybridClrPath `
            -Il2CppPlusSource $il2cppPath `
            -SkipBuild:$SkipBuild `
            -AllowDirty:$AllowDirty
    }
    return
}

$projectRoot = if ($SkipBuild) {
    Join-Path $LabRoot "artifacts/engine-projects/Unity2021Standard"
} else {
    (& (Join-Path $PSScriptRoot "prepare-engine-test-project.ps1") -LabRoot $LabRoot -EngineWorkflow Unity2021Standard | Select-Object -Last 1)
}
if (-not $SkipBuild) {
    & (Join-Path $PSScriptRoot "build-clean-baseline.ps1") `
        -LabRoot $LabRoot `
        -Profile Compatibility-Unity2021-Standard `
        -EngineWorkflow Unity2021Standard `
        -ProjectRoot $projectRoot `
        -HybridClrSource $hybridClrPath `
        -Il2CppPlusSource $il2cppPath `
        -Il2CppCodeGeneration $workflow.productionWorkflow.il2cppCodeGeneration `
        -AotMetadataPackaging $workflow.productionWorkflow.aotMetadataPackaging `
        -AllowDirty:$AllowDirty
}

$resultPath = Join-Path $LabRoot "reports/compatibility-unity2021-standard-player-result.json"
$buildPath = Join-Path $LabRoot "reports/compatibility-unity2021-standard-build-manifest.json"
$diffPath = Join-Path $LabRoot "reports/compatibility-unity2021-standard-differential-result.json"
if (-not (Test-Path $buildPath)) { throw "Unity2021Standard build manifest was not found: $buildPath" }
$build = Get-Content -Raw $buildPath | ConvertFrom-Json
$buildHybridClrPath = [IO.Path]::GetFullPath([string]$build.repositories.hybridclr.path)
$sourceMatches = [StringComparer]::OrdinalIgnoreCase.Equals($buildHybridClrPath, $hybridClrPath) -and
    [string]$build.repositories.hybridclr.commit -eq $requestedHybridClrCommit -and
    [string]$build.repositories.hybridclr.treeSha256 -eq $requestedHybridClrTreeSha256 -and
    [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath([string]$build.repositories.il2cpp_plus.path), $il2cppPath) -and
    [string]$build.repositories.il2cpp_plus.commit -eq $requestedIl2CppCommit -and
    [string]$build.repositories.il2cpp_plus.treeSha256 -eq $requestedIl2CppTreeSha256
if (-not $sourceMatches) {
    throw "Unity2021Standard build provenance does not match HybridCLR source '$hybridClrPath' at $requestedHybridClrCommit."
}

if ($SkipBuild) {
    $playerPath = Join-Path $projectRoot "Builds/Compatibility-Unity2021-Standard/HybridCLRLab.exe"
    if (-not (Test-Path $playerPath)) { throw "Unity2021Standard Player was not found: $playerPath" }
    if (Test-Path $resultPath) { Remove-Item -LiteralPath $resultPath -Force }
    $playerArguments = @(
        "-batchmode", "-nographics",
        "-labTarget", "StandaloneWindows64",
        "-labAotMetadataMode", $workflow.productionWorkflow.aotMetadataMode,
        "-labResult", $resultPath
    )
    $playerProcess = Start-Process -FilePath $playerPath -ArgumentList $playerArguments -PassThru -WindowStyle Hidden
    $playerExited = $playerProcess.WaitForExit($PlayerTimeoutSeconds * 1000)
    if (-not $playerExited) {
        try { $playerProcess.Kill() } catch { }
        throw "Unity2021Standard Player timed out after $PlayerTimeoutSeconds seconds."
    }
    if ($playerProcess.ExitCode -ne 0) {
        throw "Unity2021Standard Player exited with code $($playerProcess.ExitCode). See $resultPath"
    }
    if (-not (Test-Path $resultPath)) { throw "Unity2021Standard Player did not produce: $resultPath" }
    & (Join-Path $PSScriptRoot "compare-results.ps1") -LabRoot $LabRoot -Actual $resultPath -Output $diffPath
}

foreach ($path in @($resultPath, $diffPath)) {
    if (-not (Test-Path $path)) { throw "Unity2021Standard gate evidence was not found: $path" }
}
$result = Get-Content -Raw $resultPath | ConvertFrom-Json
$diff = Get-Content -Raw $diffPath | ConvertFrom-Json
$diagnostics = $result.PSObject.Properties["fullGenericSharingDiagnostics"]
if ($null -eq $diagnostics -or
    $null -eq $result.fullGenericSharingDiagnostics.PSObject.Properties["dispatchCount"] -or
    $null -eq $result.fullGenericSharingDiagnostics.PSObject.Properties["interpreterInvokerCount"]) {
    throw "Unity2021Standard result is missing full generic sharing diagnostic counters."
}
$casesMissingDiagnostics = @($result.cases | Where-Object {
    $null -eq $_.PSObject.Properties["fullGenericSharingAotBridgeCount"] -or
    $null -eq $_.PSObject.Properties["fullGenericSharingInterpreterInvokerCount"]
})
if ($casesMissingDiagnostics.Count -ne 0) {
    $caseIds = @($casesMissingDiagnostics | ForEach-Object { [string]$_.id }) -join ", "
    throw "Unity2021Standard result cases are missing full generic sharing diagnostic counters: $caseIds"
}
if ($result.aotMetadataMode -ne "supplemental" -or
    [int]$result.aotMetadata.fileCount -le 0 -or [int64]$result.aotMetadata.totalBytes -le 0 -or
    [int64]$result.aotMetadata.loadNanoseconds -le 0 -or
    $build.profile -ne "Compatibility-Unity2021-Standard" -or
    $build.engine.workflow -ne "Unity2021Standard" -or
    $build.il2cppCodeGeneration -ne $workflow.productionWorkflow.il2cppCodeGeneration -or
    $build.aotMetadataPackaging -ne $workflow.productionWorkflow.aotMetadataPackaging -or
    [bool]$build.fullGenericSharingDiagnostics -or
    $result.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
    $result.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
    $result.buildIdentity.managedAssemblySha256 -ne $build.managedAssemblySha256 -or
    $result.managedAssemblySha256 -ne $build.managedAssemblySha256 -or
    $result.buildIdentity.il2cppCodeGeneration -ne $build.il2cppCodeGeneration -or
    $result.buildIdentity.aotMetadataPackaging -ne $build.aotMetadataPackaging -or
    [bool]$result.buildIdentity.fullGenericSharingDiagnostics -or
    [bool]$result.fullGenericSharingDiagnostics.enabled -or
    [int64]$result.fullGenericSharingDiagnostics.dispatchCount -ne 0 -or
    [int64]$result.fullGenericSharingDiagnostics.interpreterInvokerCount -ne 0 -or
    @($result.cases | Where-Object {
        [int64]$_.fullGenericSharingAotBridgeCount -ne 0 -or
        [int64]$_.fullGenericSharingInterpreterInvokerCount -ne 0
    }).Count -ne 0 -or
    [int]$result.summary.total -le 0 -or [int]$result.summary.failed -ne 0 -or
    [int]$result.summary.passed -ne [int]$result.summary.total -or
    [int]$diff.summary.differences -ne 0) {
    throw "Unity2021Standard evidence does not satisfy its merge gate contract."
}
Write-Host "Unity2021Standard merge gate passed: $($result.summary.passed)/$($result.summary.total), differential 0."
