param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$ProjectRoot = "",
    [ValidateSet("Tuanjie2022Fgs", "Unity2022Fgs")]
    [string]$EngineWorkflow = "Tuanjie2022Fgs",
    [ValidateSet("Baseline-Clean", "Baseline-Instrumented", "Candidate", "Metadata-Candidate", "Fgs-Diagnostic", "Fgs-Candidate", "Unity2022-Candidate", "Unity2022-Fgs-Diagnostic")]
    [string]$Profile = "Baseline-Clean",
    [ValidateSet("OptimizeSpeed", "OptimizeSize")]
    [string]$Il2CppCodeGeneration = "OptimizeSpeed",
    [ValidateSet("supplemental", "none")]
    [string]$AotMetadataMode = "none",
    [ValidateSet("include", "exclude")]
    [string]$AotMetadataPackaging = "include",
    [int]$PlayerTimeoutSeconds = 180
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$projectRoot = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
    Join-Path $LabRoot "unity-test-project"
} elseif ([IO.Path]::IsPathRooted($ProjectRoot)) {
    [IO.Path]::GetFullPath($ProjectRoot)
} else {
    [IO.Path]::GetFullPath((Join-Path $LabRoot $ProjectRoot))
}
$codeGenerationVariant = if ($Il2CppCodeGeneration -eq "OptimizeSpeed") { $Profile } else { "$Profile-$Il2CppCodeGeneration" }
$buildVariant = if ($AotMetadataPackaging -eq "exclude") { "$codeGenerationVariant-NoMetadata" } else { $codeGenerationVariant }
$slug = $buildVariant.ToLowerInvariant()
$player = Join-Path $projectRoot "Builds/$buildVariant/HybridCLRLab.exe"
$result = Join-Path $LabRoot "reports/$slug-$AotMetadataMode-player-result.json"
$differential = "reports/$slug-$AotMetadataMode-differential-result.json"
$buildManifestPath = Join-Path $LabRoot "reports/$slug-build-manifest.json"

if (-not (Test-Path $player)) { throw "Player was not found: $player" }
if (-not (Test-Path $buildManifestPath)) { throw "Build manifest was not found: $buildManifestPath" }
if ($AotMetadataPackaging -eq "exclude" -and $AotMetadataMode -ne "none") {
    throw "A no-metadata build can only run the none gate."
}
$build = Get-Content -Raw $buildManifestPath | ConvertFrom-Json
if ($build.profile -ne $Profile -or $build.il2cppCodeGeneration -ne $Il2CppCodeGeneration -or
    $build.aotMetadataPackaging -ne $AotMetadataPackaging) {
    throw "Build manifest does not match the requested FGS gate configuration."
}
if ($build.engine.workflow -ne $EngineWorkflow) {
    throw "Full generic sharing gate requires a $EngineWorkflow build manifest."
}
$diagnosticsEnabled = [bool]$build.fullGenericSharingDiagnostics
if (Test-Path $result) { Remove-Item -LiteralPath $result -Force }

$arguments = @(
    "-batchmode", "-nographics",
    "-labTarget", "StandaloneWindows64",
    "-labAotMetadataMode", $AotMetadataMode,
    "-labResult", $result
)
$process = Start-Process -FilePath $player -ArgumentList $arguments -PassThru -WindowStyle Hidden
$exited = $process.WaitForExit($PlayerTimeoutSeconds * 1000)
if (-not $exited) {
    try { $process.Kill() } catch { }
    throw "Full generic sharing gate timed out after $PlayerTimeoutSeconds seconds."
}
if ($process.ExitCode -ne 0) { throw "Player exited with code $($process.ExitCode). See $result" }
if (-not (Test-Path $result)) { throw "Player did not produce: $result" }

$run = Get-Content -Raw $result | ConvertFrom-Json
if ($run.aotMetadataMode -ne $AotMetadataMode -or
    $run.buildIdentity.sha256 -ne $build.buildIdentitySha256 -or
    $run.buildIdentity.stagedRuntimeSha256 -ne $build.stagedRuntimeSha256 -or
    $run.buildIdentity.managedAssemblySha256 -ne $build.managedAssemblySha256 -or
    $run.buildIdentity.il2cppCodeGeneration -ne $Il2CppCodeGeneration -or
    $run.buildIdentity.aotMetadataPackaging -ne $AotMetadataPackaging -or
    [bool]$run.buildIdentity.fullGenericSharingDiagnostics -ne $diagnosticsEnabled -or
    [bool]$run.fullGenericSharingDiagnostics.enabled -ne $diagnosticsEnabled) {
    throw "Correctness result provenance does not match the requested build."
}
if ($AotMetadataMode -eq "none" -and
    ([int]$run.aotMetadata.fileCount -ne 0 -or [int64]$run.aotMetadata.totalBytes -ne 0 -or
     [int64]$run.aotMetadata.loadNanoseconds -ne 0)) {
    throw "None correctness result loaded supplemental AOT metadata."
}
if ($AotMetadataMode -eq "supplemental" -and
    ([int]$run.aotMetadata.fileCount -le 0 -or [int64]$run.aotMetadata.totalBytes -le 0 -or
     [int64]$run.aotMetadata.loadNanoseconds -le 0)) {
    throw "Supplemental correctness result did not load an AOT metadata payload."
}
if ([int]$run.summary.failed -ne 0 -or [int]$run.summary.passed -ne [int]$run.summary.total) {
    throw "Full generic sharing correctness failed: $($run.summary.passed)/$($run.summary.total)."
}
if ($run.correctnessProbes.crossAssemblyLazyVTable -ne $true -or
    $run.correctnessProbes.lazyMetadataConcurrentFirstTouch -ne $true) {
    throw "Full generic sharing lazy metadata correctness probes did not pass."
}
if ($null -eq $run.PSObject.Properties["fullGenericSharingDiagnostics"] -or
    $null -eq $run.fullGenericSharingDiagnostics.PSObject.Properties["dispatchCount"] -or
    $null -eq $run.fullGenericSharingDiagnostics.PSObject.Properties["interpreterInvokerCount"]) {
    throw "Correctness result is missing full generic sharing diagnostic counters."
}
$casesMissingDiagnostics = @($run.cases | Where-Object {
    $null -eq $_.PSObject.Properties["fullGenericSharingAotBridgeCount"] -or
    $null -eq $_.PSObject.Properties["fullGenericSharingInterpreterInvokerCount"]
})
if ($casesMissingDiagnostics.Count -ne 0) {
    $caseIds = @($casesMissingDiagnostics | ForEach-Object { [string]$_.id }) -join ", "
    throw "Correctness result cases are missing full generic sharing diagnostic counters: $caseIds"
}
if ($diagnosticsEnabled) {
    if ([int64]$run.fullGenericSharingDiagnostics.dispatchCount -le 0 -or
        [int64]$run.fullGenericSharingDiagnostics.interpreterInvokerCount -le 0) {
        throw "Diagnostic correctness passed without proving both FGS bridge selection and interpreter invocation."
    }
    $delegateCases = @($run.cases | Where-Object id -eq "boundary_fgs_aot_calls_hot_generic_delegate")
    if ($delegateCases.Count -ne 1 -or
        [int64]$delegateCases[0].fullGenericSharingInterpreterInvokerCount -ne 1) {
        throw "Diagnostic correctness did not prove that the AOT-to-hot-update generic delegate entered the interpreter invoker."
    }

	$expectedBridgeCounts = @(
		[pscustomobject]@{ Id = "boundary_fgs_calli_void"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_calli_large_return"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_calli_small_return"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_direct_virtual_void"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_direct_virtual_large_return"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_direct_virtual_small_return"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_delegate_closed_instance"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_delegate_open_instance"; Count = 1 },
		[pscustomobject]@{ Id = "boundary_fgs_delegate_multicast_void"; Count = 2 }
	)
	foreach ($expectation in $expectedBridgeCounts) {
		$matchingCases = @($run.cases | Where-Object id -eq $expectation.Id)
		if ($matchingCases.Count -ne 1 -or
			[int64]$matchingCases[0].fullGenericSharingAotBridgeCount -ne [int64]$expectation.Count) {
			throw "FGS bridge diagnostic '$($expectation.Id)' did not record exactly $($expectation.Count) actual dispatch(es)."
		}
	}
} elseif ([int64]$run.fullGenericSharingDiagnostics.dispatchCount -ne 0 -or
          [int64]$run.fullGenericSharingDiagnostics.interpreterInvokerCount -ne 0 -or
          @($run.cases | Where-Object {
              [int64]$_.fullGenericSharingAotBridgeCount -ne 0 -or
              [int64]$_.fullGenericSharingInterpreterInvokerCount -ne 0
          }).Count -ne 0) {
    throw "Production correctness result contains FGS diagnostic events even though instrumentation is disabled."
}

& (Join-Path $PSScriptRoot "compare-results.ps1") -LabRoot $LabRoot -Actual $result -Output $differential
$diff = Get-Content -Raw (Join-Path $LabRoot $differential) | ConvertFrom-Json
if ([int]$diff.summary.differences -ne 0) { throw "Differential result contains $($diff.summary.differences) differences." }

Write-Host "Full generic sharing gate passed: $buildVariant / $AotMetadataMode / payload=$AotMetadataPackaging"
Write-Host "Cases: $($run.summary.passed)/$($run.summary.total), differential differences: 0"
Write-Host "Result: $result"
