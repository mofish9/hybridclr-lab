[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$OutputRoot = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) { Split-Path -Parent $PSScriptRoot } else { [IO.Path]::GetFullPath($LabRoot) }
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) { Join-Path $LabRoot "artifacts/dhe-toolchain-fixture-gate" } else { [IO.Path]::GetFullPath($OutputRoot) }
Assert-DheSafeOutputRoot -Path $OutputRoot
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
$null = Initialize-DheOutputRoot -Path $OutputRoot -Force:$ForceOutput

$errors = New-Object System.Collections.Generic.List[string]
$packageRoot = Join-Path $OutputRoot "package"
$packageGatePath = Join-Path $OutputRoot "package.gate.json"
$consumerRoot = Join-Path $OutputRoot "consumer-repository"
$installedRoot = Join-Path $consumerRoot "Tools/HybridCLRDhe"
$projectRoot = Join-Path $consumerRoot "Project"
$adapterPath = Join-Path $consumerRoot "Build/dhe-adapter.ps1"
$installReport = Join-Path $OutputRoot "install.json"
$upgradeReport = Join-Path $OutputRoot "upgrade.json"
$corruptUpgradeReport = Join-Path $OutputRoot "corrupt-upgrade.json"
$doctorReport = Join-Path $OutputRoot "doctor.json"
$cleanReportRoot = Join-Path $OutputRoot "installed-clean-gate"
$releaseSourceRoot = Join-Path $OutputRoot "release-source-repository"
$releasePackageRoot = Join-Path $OutputRoot "release-package"
$releasePackageRepeatRoot = Join-Path $OutputRoot "release-package-repeat"
$autocrlfTrueSourceRoot = Join-Path $OutputRoot "release-source-autocrlf-true"
$autocrlfFalseSourceRoot = Join-Path $OutputRoot "release-source-autocrlf-false"
$autocrlfTruePackageRoot = Join-Path $OutputRoot "release-package-autocrlf-true"
$autocrlfFalsePackageRoot = Join-Path $OutputRoot "release-package-autocrlf-false"
$releasePackageGatePath = Join-Path $OutputRoot "release-package.gate.json"
$releaseConsumerRoot = Join-Path $OutputRoot "release-consumer-repository"
$releaseInstalledRoot = Join-Path $releaseConsumerRoot "Tools/HybridCLRDhe"
$releaseInstallReport = Join-Path $OutputRoot "release-install.json"
$releaseDoctorReport = Join-Path $OutputRoot "release-doctor.json"
$releaseUnpinnedInstallReport = Join-Path $OutputRoot "release-unpinned-install.json"
$releaseUnpinnedDoctorReport = Join-Path $OutputRoot "release-unpinned-doctor.json"
$releaseUnpinnedWorkflowRoot = Join-Path $OutputRoot "release-unpinned-workflow"
$downgradeSourceRoot = Join-Path $OutputRoot "downgrade-source-repository"
$downgradePackageRoot = Join-Path $OutputRoot "downgrade-package"
$downgradeRejectReport = Join-Path $OutputRoot "downgrade-rejected.json"
$downgradeInstallReport = Join-Path $OutputRoot "downgrade-installed.json"
$downgradeRecoveryReport = Join-Path $OutputRoot "downgrade-recovery.json"
$untrustedCodeRoot = Join-Path $OutputRoot "untrusted-code-package"
$untrustedCodeSentinel = Join-Path $OutputRoot "untrusted-code-executed.marker"
$reparsePackageRoot = Join-Path $OutputRoot "reparse-package"

function Invoke-Child([string[]]$Arguments) {
    $hostPath = Resolve-DhePowerShellHost
    $old = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & $hostPath -NoProfile -ExecutionPolicy Bypass @Arguments 2>&1 | Out-Null
        return [int]$LASTEXITCODE
    } finally { $ErrorActionPreference = $old }
}
function Copy-Tree([string]$Source, [string]$Destination) {
    $null = New-Item -ItemType Directory -Force -Path $Destination
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

& (Join-Path $PSScriptRoot "publish-dhe-toolchain.ps1") `
    -LabRoot $LabRoot -OutputRoot $packageRoot -Mode Exploratory -ForceOutput | Out-Null
$packagePublished = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $packageRoot "dhe-toolchain-manifest.json") -PathType Leaf)
if (-not $packagePublished) { $errors.Add("Exploratory toolchain package was not published.") }
$publishedAttributes = if (Test-Path -LiteralPath (Join-Path $packageRoot ".gitattributes") -PathType Leaf) {
    Get-Content -Raw -LiteralPath (Join-Path $packageRoot ".gitattributes")
} else { "" }
$portableAttributesSanitized = $publishedAttributes -notmatch 'unity2021-dhe-demo' -and
    $publishedAttributes -match '/patches/dhe-lite/\*\.patch -text'
if (-not $portableAttributesSanitized) { $errors.Add("Published .gitattributes retained a Demo path or omitted the package patch byte policy.") }

& (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") `
    -PackageRoot $packageRoot -Output $packageGatePath | Out-Null
$packageValidated = $LASTEXITCODE -eq 0
if (-not $packageValidated) { $errors.Add("Published toolchain package did not validate.") }

$firstPackageId = [string](Get-Content -Raw -LiteralPath (Join-Path $packageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json).packageId
& (Join-Path $PSScriptRoot "publish-dhe-toolchain.ps1") `
    -LabRoot $LabRoot -OutputRoot $packageRoot -Mode Exploratory -ForceOutput | Out-Null
$replacementManifest = if (Test-Path -LiteralPath (Join-Path $packageRoot "dhe-toolchain-manifest.json") -PathType Leaf) {
    Get-Content -Raw -LiteralPath (Join-Path $packageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
} else { $null }
$publishedPackageReplaceValidated = $LASTEXITCODE -eq 0 -and $null -ne $replacementManifest -and
    [string]$replacementManifest.packageId -eq $firstPackageId
if (-not $publishedPackageReplaceValidated) { $errors.Add("Publisher could not safely replace a prior verified untracked package.") }

$tamperRoot = Join-Path $OutputRoot "tampered-package"
Copy-Tree $packageRoot $tamperRoot
[IO.File]::AppendAllText((Join-Path $tamperRoot "dhe.ps1"), "`n# tampered", (New-Object Text.UTF8Encoding($false)))
$tamperExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
    "-PackageRoot", $tamperRoot,
    "-Output", (Join-Path $OutputRoot "tampered.gate.json")
)
$tamperRejected = $tamperExit -ne 0
if (-not $tamperRejected) { $errors.Add("Toolchain package gate accepted a modified payload.") }

$extraRoot = Join-Path $OutputRoot "extra-file-package"
Copy-Tree $packageRoot $extraRoot
[IO.File]::WriteAllText((Join-Path $extraRoot "extra.txt"), "extra", (New-Object Text.UTF8Encoding($false)))
$extraExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
    "-PackageRoot", $extraRoot,
    "-Output", (Join-Path $OutputRoot "extra-file.gate.json")
)
$extraFileRejected = $extraExit -ne 0
if (-not $extraFileRejected) { $errors.Add("Toolchain package gate accepted an undeclared payload file.") }

$missingRoot = Join-Path $OutputRoot "missing-file-package"
Copy-Tree $packageRoot $missingRoot
Remove-Item -LiteralPath (Join-Path $missingRoot "dhe.ps1") -Force
$missingExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
    "-PackageRoot", $missingRoot,
    "-Output", (Join-Path $OutputRoot "missing-file.gate.json")
)
$missingFileRejected = $missingExit -ne 0
if (-not $missingFileRejected) { $errors.Add("Toolchain package gate accepted a missing declared payload file.") }

Copy-Tree $packageRoot $reparsePackageRoot
$reparseEntry = Join-Path $reparsePackageRoot "dhe.ps1"
Remove-Item -LiteralPath $reparseEntry -Force
try {
    $null = New-Item -ItemType SymbolicLink -Path $reparseEntry -Target (Join-Path $packageRoot "dhe.ps1") -Force
    $reparseExit = Invoke-Child @(
        "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
        "-PackageRoot", $reparsePackageRoot,
        "-Output", (Join-Path $OutputRoot "reparse-package.gate.json")
    )
    $reparsePackageRejected = $reparseExit -ne 0
} finally {
    $reparseItem = Get-Item -LiteralPath $reparseEntry -Force -ErrorAction SilentlyContinue
    if ($null -ne $reparseItem) { Remove-Item -LiteralPath $reparseEntry -Force }
}
if (-not $reparsePackageRejected) { $errors.Add("Toolchain package gate accepted a symbolic-link payload.") }

$forbiddenGateOutput = Join-Path $packageRoot "forbidden-package-gate.json"
$reportCollisionExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
    "-PackageRoot", $packageRoot,
    "-Output", $forbiddenGateOutput
)
$packageReportCollisionRejected = $reportCollisionExit -ne 0 -and
    -not (Test-Path -LiteralPath $forbiddenGateOutput)
if (-not $packageReportCollisionRejected) { $errors.Add("Toolchain package gate allowed its report to modify the package.") }

$packageManifest = Get-Content -Raw -LiteralPath (Join-Path $packageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
$packageId = [string]$packageManifest.packageId
$wrongPackageId = if ($packageId -eq ("0" * 64)) { "1" * 64 } else { "0" * 64 }
$packageIdPinExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
    "-PackageRoot", $packageRoot,
    "-Output", (Join-Path $OutputRoot "wrong-package-id.gate.json"),
    "-ExpectedPackageId", $wrongPackageId
)
$packageIdPinRejected = $packageIdPinExit -ne 0
if (-not $packageIdPinRejected) { $errors.Add("Toolchain package gate accepted the wrong expected packageId.") }

# A candidate can be internally self-consistent while still failing the
# external package ID pin. Verification must never execute its payload before
# that trust decision has passed.
Copy-Tree $packageRoot $untrustedCodeRoot
$untrustedSchemaScript = Join-Path $untrustedCodeRoot "scripts/run-dhe-schema-gate.ps1"
$untrustedScriptText = @'
if (-not [string]::IsNullOrWhiteSpace($env:DHE_PACKAGE_SCHEMA_SENTINEL)) {
    [IO.File]::WriteAllText($env:DHE_PACKAGE_SCHEMA_SENTINEL, "executed")
}
exit 0
'@
[IO.File]::WriteAllText($untrustedSchemaScript, $untrustedScriptText, (New-Object Text.UTF8Encoding($false)))
$untrustedManifestPath = Join-Path $untrustedCodeRoot "dhe-toolchain-manifest.json"
$untrustedManifest = Get-Content -Raw -LiteralPath $untrustedManifestPath | ConvertFrom-Json
[object[]]$untrustedRecords = @($untrustedManifest.files | Where-Object {
    [string]$_.path -eq "scripts/run-dhe-schema-gate.ps1"
})
if ($untrustedRecords.Count -ne 1) { throw "Unable to locate the candidate schema script payload record." }
$untrustedScriptFile = Get-Item -LiteralPath $untrustedSchemaScript -Force
$untrustedRecords[0].size = [int64]$untrustedScriptFile.Length
$untrustedRecords[0].sha256 = (Get-FileHash -LiteralPath $untrustedSchemaScript -Algorithm SHA256).Hash.ToLowerInvariant()
$untrustedManifest.packageId = Get-DheToolchainPackageId `
    -ToolchainVersion ([string]$untrustedManifest.toolchainVersion) `
    -ContractVersion ([int]$untrustedManifest.contractVersion) `
    -Mode ([string]$untrustedManifest.mode) `
    -SourceHead ([string]$untrustedManifest.sourceIdentity.head) `
    -SourceTree ([string]$untrustedManifest.sourceIdentity.tree) `
    -LayoutSha256 ([string]$untrustedManifest.layoutSha256) `
    -Files @($untrustedManifest.files)
[IO.File]::WriteAllText($untrustedManifestPath, ($untrustedManifest | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
$untrustedWrongPackageId = if ([string]$untrustedManifest.packageId -eq ("0" * 64)) { "1" * 64 } else { "0" * 64 }
$oldSentinel = [Environment]::GetEnvironmentVariable("DHE_PACKAGE_SCHEMA_SENTINEL", "Process")
try {
    [Environment]::SetEnvironmentVariable("DHE_PACKAGE_SCHEMA_SENTINEL", $untrustedCodeSentinel, "Process")
    $untrustedCodeExit = Invoke-Child @(
        "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
        "-PackageRoot", $untrustedCodeRoot,
        "-Output", (Join-Path $OutputRoot "untrusted-code.gate.json"),
        "-ExpectedPackageId", $untrustedWrongPackageId
    )
} finally {
    [Environment]::SetEnvironmentVariable("DHE_PACKAGE_SCHEMA_SENTINEL", $oldSentinel, "Process")
}
$untrustedPackageCodeInert = $untrustedCodeExit -ne 0 -and
    -not (Test-Path -LiteralPath $untrustedCodeSentinel)
if (-not $untrustedPackageCodeInert) { $errors.Add("Package verification executed untrusted candidate code before the external pin passed.") }

$null = New-Item -ItemType Directory -Force -Path $consumerRoot
& git -C $consumerRoot init -q
if ($LASTEXITCODE -ne 0) { throw "Unable to initialize the DHE toolchain consumer fixture repository." }
& git -C $consumerRoot config user.email "dhe-fixture@example.invalid"
& git -C $consumerRoot config user.name "DHE Fixture"

& (Join-Path $PSScriptRoot "install-dhe-toolchain.ps1") `
    -PackageRoot $packageRoot -Destination $installedRoot -Output $installReport -AllowExploratory | Out-Null
$installed = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath (Join-Path $installedRoot "dhe.ps1") -PathType Leaf)
if (-not $installed) { $errors.Add("Toolchain installer did not create a valid destination.") }

$null = New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot "ProjectSettings")
Copy-Item -LiteralPath (Join-Path $LabRoot "scripts/fixtures/dhe-settings-parser.asset") `
    -Destination (Join-Path $projectRoot "ProjectSettings/HybridCLRSettings.asset") -Force
[IO.File]::WriteAllText((Join-Path $projectRoot "ProjectSettings/ProjectVersion.txt"), "m_EditorVersion: 2022.3.62t12`n", (New-Object Text.UTF8Encoding($false)))

& (Join-Path $installedRoot "dhe.ps1") new-adapter -Output $adapterPath -Force
$adapterCreated = $LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $adapterPath -PathType Leaf)
$tokens = $null
$parseErrors = $null
if ($adapterCreated) {
    [Management.Automation.Language.Parser]::ParseFile($adapterPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
}
$adapterParsed = $adapterCreated -and $parseErrors.Count -eq 0
if (-not $adapterParsed) { $errors.Add("Generated project adapter did not parse.") }

& git -C $consumerRoot add -- Tools Project Build
& git -C $consumerRoot commit -q -m "test: install DHE toolchain"
if ($LASTEXITCODE -ne 0) { throw "Unable to commit the installed DHE fixture package." }

& (Join-Path $PSScriptRoot "install-dhe-toolchain.ps1") `
    -PackageRoot $packageRoot -Destination $installedRoot -Output $upgradeReport -Upgrade -AllowExploratory `
    -ExpectedPackageId $packageId | Out-Null
$upgradeExitCode = [int]$LASTEXITCODE
$upgradeDocument = if (Test-Path -LiteralPath $upgradeReport -PathType Leaf) { Get-Content -Raw -LiteralPath $upgradeReport | ConvertFrom-Json } else { $null }
$postUpgradeStatus = @(& git -C $consumerRoot status --porcelain=v1 --untracked-files=all)
$upgradeValidated = $upgradeExitCode -eq 0 -and $null -ne $upgradeDocument -and
    [string]$upgradeDocument.operation -eq "upgrade" -and [bool]$upgradeDocument.passed -and
    [string]$upgradeDocument.packageId -eq $packageId -and $postUpgradeStatus.Count -eq 0
if (-not $upgradeValidated) { $errors.Add("Verified committed toolchain upgrade did not pass cleanly.") }

$heldInstallLock = Enter-DheWorkflowLock -LabRoot $installedRoot -TimeoutSeconds 0
try {
    $lockedUpgradeExit = Invoke-Child @(
        "-File", (Join-Path $PSScriptRoot "install-dhe-toolchain.ps1"),
        "-PackageRoot", $packageRoot,
        "-Destination", $installedRoot,
        "-Output", (Join-Path $OutputRoot "locked-upgrade.json"),
        "-Upgrade",
        "-AllowExploratory"
    )
} finally {
    Exit-DheWorkflowLock $heldInstallLock
}
$installLockRejected = $lockedUpgradeExit -ne 0 -and
    [string](Get-Content -Raw -LiteralPath (Join-Path $installedRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json).packageId -eq $packageId
if (-not $installLockRejected) { $errors.Add("Installer did not reject an upgrade while the destination workflow mutex was held.") }

& (Join-Path $installedRoot "dhe.ps1") new-adapter -Output $adapterPath -Force
$trackedAdapterExitCode = [int]$LASTEXITCODE
$trackedAdapterStatus = @(& git -C $consumerRoot status --porcelain=v1 --untracked-files=all)
$trackedAdapterUpdated = $trackedAdapterExitCode -eq 0 -and (Test-Path -LiteralPath $adapterPath -PathType Leaf) -and
    $trackedAdapterStatus.Count -eq 0
if (-not $trackedAdapterUpdated) { $errors.Add("Generated adapter could not safely update an existing tracked adapter.") }

$installedEntryPoint = Join-Path $installedRoot "dhe.ps1"
$installedEntryPointBytes = [IO.File]::ReadAllBytes($installedEntryPoint)
[IO.File]::AppendAllText($installedEntryPoint, "`n# corrupt installed fixture", (New-Object Text.UTF8Encoding($false)))
$corruptInstalledHash = (Get-FileHash -LiteralPath $installedEntryPoint -Algorithm SHA256).Hash
$corruptUpgradeExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "install-dhe-toolchain.ps1"),
    "-PackageRoot", $packageRoot,
    "-Destination", $installedRoot,
    "-Output", $corruptUpgradeReport,
    "-Upgrade",
    "-AllowExploratory",
    "-ExpectedPackageId", $packageId
)
$corruptInstallRejected = $corruptUpgradeExit -ne 0 -and
    (Test-Path -LiteralPath $installedEntryPoint -PathType Leaf) -and
    (Get-FileHash -LiteralPath $installedEntryPoint -Algorithm SHA256).Hash -eq $corruptInstalledHash
[IO.File]::WriteAllBytes($installedEntryPoint, $installedEntryPointBytes)
$restoredConsumerStatus = @(& git -C $consumerRoot status --porcelain=v1 --untracked-files=all)
$corruptInstallRejected = $corruptInstallRejected -and $restoredConsumerStatus.Count -eq 0
if (-not $corruptInstallRejected) { $errors.Add("Installer did not reject and preserve a corrupt committed installation.") }

$doctorCollisionPath = Join-Path $installedRoot "forbidden-doctor-report.json"
$doctorCollisionExit = Invoke-Child @(
    "-File", (Join-Path $installedRoot "dhe.ps1"),
    "doctor",
    "-Output", $doctorCollisionPath
)
$doctorReportCollisionRejected = $doctorCollisionExit -ne 0 -and -not (Test-Path -LiteralPath $doctorCollisionPath)
if (-not $doctorReportCollisionRejected) { $errors.Add("Doctor allowed its report to modify the installed package.") }

$installCollisionDestination = Join-Path $consumerRoot "Tools/CollisionInstall"
$installCollisionPath = Join-Path $packageRoot "forbidden-install-report.json"
$installCollisionExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "install-dhe-toolchain.ps1"),
    "-PackageRoot", $packageRoot,
    "-Destination", $installCollisionDestination,
    "-Output", $installCollisionPath,
    "-AllowExploratory"
)
$installReportCollisionRejected = $installCollisionExit -ne 0 -and
    -not (Test-Path -LiteralPath $installCollisionPath) -and
    -not (Test-Path -LiteralPath $installCollisionDestination)
if (-not $installReportCollisionRejected) { $errors.Add("Installer allowed its report to modify a package or created a rejected destination.") }

& (Join-Path $installedRoot "dhe.ps1") doctor -Output $doctorReport -ExpectedPackageId $packageId
$doctorDocument = if (Test-Path -LiteralPath $doctorReport -PathType Leaf) { Get-Content -Raw -LiteralPath $doctorReport | ConvertFrom-Json } else { $null }
$doctorPassed = $LASTEXITCODE -eq 0 -and $null -ne $doctorDocument -and [bool]$doctorDocument.passed
if (-not $doctorPassed) { $errors.Add("Installed toolchain doctor did not pass.") }

& (Join-Path $installedRoot "dhe.ps1") doctor -ExpectedPackageId $packageId
$defaultDoctorExitCode = [int]$LASTEXITCODE
& (Join-Path $installedRoot "dhe.ps1") verify-package -PackageRoot $installedRoot -ExpectedPackageId $packageId
$defaultVerifyExitCode = [int]$LASTEXITCODE
$defaultReportConsumerStatus = @(& git -C $consumerRoot status --porcelain=v1 --untracked-files=all)
$defaultReportsExternal = $defaultDoctorExitCode -eq 0 -and $defaultVerifyExitCode -eq 0 -and
    $defaultReportConsumerStatus.Count -eq 0
if (-not $defaultReportsExternal) { $errors.Add("Default doctor or package-gate reports changed the consumer repository.") }

$workflowProbeRoot = Join-Path $OutputRoot "installed-workflow-probe"
$workflowProbeExit = Invoke-Child @(
    "-File", (Join-Path $installedRoot "dhe.ps1"),
    "workflow",
    "-AdapterScript", $adapterPath,
    "-ProjectPath", $projectRoot,
    "-SettingsFile", (Join-Path $projectRoot "ProjectSettings/HybridCLRSettings.asset"),
    "-RuntimeSource", $projectRoot,
    "-OutputRoot", $workflowProbeRoot,
    "-ExpectedToolchainPackageId", $packageId,
    "-Mode", "Exploratory",
    "-ForceOutput"
)
$workflowProbeFailurePath = Join-Path $workflowProbeRoot "project-workflow-failure.json"
$workflowProbeFailure = if (Test-Path -LiteralPath $workflowProbeFailurePath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $workflowProbeFailurePath | ConvertFrom-Json
} else { $null }
$workflowPackageGatePassed = $workflowProbeExit -ne 0 -and $null -ne $workflowProbeFailure -and
    [bool]$workflowProbeFailure.stages.toolchain.passed -and
    -not [bool]$workflowProbeFailure.stages.prepare.passed -and
    (Test-Path -LiteralPath ([string]$workflowProbeFailure.stages.toolchain.report) -PathType Leaf)
if (-not $workflowPackageGatePassed) { $errors.Add("Installed workflow did not pass package verification before invoking its adapter.") }

& (Join-Path $installedRoot "scripts/run-dhe-clean-checkout-gate.ps1") `
    -LabRoot $installedRoot `
    -ProjectPath $projectRoot `
    -OutputRoot $cleanReportRoot `
    -ToolGitRoot $consumerRoot `
    -ToolSourceBoundaryPath (Join-Path $installedRoot "dhe-source-boundary.json") `
    -RequireToolGitClean -RequireToolTrackedSources -ForceOutput | Out-Null
$cleanDocumentPath = Join-Path $cleanReportRoot "clean-checkout-gate-report.json"
$cleanDocument = if (Test-Path -LiteralPath $cleanDocumentPath -PathType Leaf) { Get-Content -Raw -LiteralPath $cleanDocumentPath | ConvertFrom-Json } else { $null }
$manifestBoundaryTracked = $LASTEXITCODE -eq 0 -and $null -ne $cleanDocument -and
    [bool]$cleanDocument.toolGit.passed -and [bool]$cleanDocument.toolGit.trackedSourcesComplete -and
    [string]$cleanDocument.toolGit.sourceBoundaryPathBase -eq "manifest-directory-v1"
if (-not $manifestBoundaryTracked) { $errors.Add("Installed manifest-directory source boundary did not pass tracked-source verification.") }

# Build a real Release package from an isolated clean source repository. This
# proves the Release-only Git identity path without requiring the development
# worktree itself to be clean.
Copy-Tree $packageRoot $releaseSourceRoot
& git -C $releaseSourceRoot init -q
& git -C $releaseSourceRoot config user.email "dhe-release-fixture@example.invalid"
& git -C $releaseSourceRoot config user.name "DHE Release Fixture"
& git -C $releaseSourceRoot add -- .
& git -C $releaseSourceRoot commit -q -m "test: clean DHE release source"
if ($LASTEXITCODE -ne 0) { throw "Unable to commit the clean DHE Release source fixture." }
$releaseSourceHead = (@(& git -C $releaseSourceRoot rev-parse HEAD) -join "").Trim().ToLowerInvariant()

& (Join-Path $releaseSourceRoot "scripts/publish-dhe-toolchain.ps1") `
    -LabRoot $releaseSourceRoot -OutputRoot $releasePackageRoot -Mode Release -ForceOutput | Out-Null
$releaseManifestPath = Join-Path $releasePackageRoot "dhe-toolchain-manifest.json"
$releaseManifest = if (Test-Path -LiteralPath $releaseManifestPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $releaseManifestPath | ConvertFrom-Json
} else { $null }
$releasePackagePublished = $LASTEXITCODE -eq 0 -and $null -ne $releaseManifest -and
    [bool]$releaseManifest.releaseReady -and [string]$releaseManifest.mode -eq "Release" -and
    [string]$releaseManifest.sourceIdentity.head -eq $releaseSourceHead
if (-not $releasePackagePublished) { $errors.Add("Clean Release toolchain package was not published with the expected source identity.") }
$releasePackageId = if ($null -eq $releaseManifest) { "" } else { [string]$releaseManifest.packageId }
$releasePackageVersion = if ($null -eq $releaseManifest) { "" } else { [string]$releaseManifest.toolchainVersion }

& (Join-Path $releaseSourceRoot "scripts/publish-dhe-toolchain.ps1") `
    -LabRoot $releaseSourceRoot -OutputRoot $releasePackageRepeatRoot -Mode Release -ForceOutput | Out-Null
$releaseRepeatManifest = Get-Content -Raw -LiteralPath (Join-Path $releasePackageRepeatRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
$releasePackageDeterministic = $LASTEXITCODE -eq 0 -and
    [string]$releaseRepeatManifest.packageId -eq $releasePackageId -and
    ((@($releaseRepeatManifest.files | ForEach-Object { "$($_.path)|$($_.size)|$($_.sha256)" }) -join "`n") -eq
        (@($releaseManifest.files | ForEach-Object { "$($_.path)|$($_.size)|$($_.sha256)" }) -join "`n"))
if (-not $releasePackageDeterministic) { $errors.Add("Repeated clean Release publication changed packageId or payload records.") }

& git -c core.autocrlf=true clone -q --no-hardlinks $releaseSourceRoot $autocrlfTrueSourceRoot
if ($LASTEXITCODE -ne 0) { throw "Unable to create the core.autocrlf=true release fixture checkout." }
& git -c core.autocrlf=false clone -q --no-hardlinks $releaseSourceRoot $autocrlfFalseSourceRoot
if ($LASTEXITCODE -ne 0) { throw "Unable to create the core.autocrlf=false release fixture checkout." }
& (Join-Path $autocrlfTrueSourceRoot "scripts/publish-dhe-toolchain.ps1") `
    -LabRoot $autocrlfTrueSourceRoot -OutputRoot $autocrlfTruePackageRoot -Mode Release -ForceOutput | Out-Null
$autocrlfTrueManifest = Get-Content -Raw -LiteralPath (Join-Path $autocrlfTruePackageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
& (Join-Path $autocrlfFalseSourceRoot "scripts/publish-dhe-toolchain.ps1") `
    -LabRoot $autocrlfFalseSourceRoot -OutputRoot $autocrlfFalsePackageRoot -Mode Release -ForceOutput | Out-Null
$autocrlfFalseManifest = Get-Content -Raw -LiteralPath (Join-Path $autocrlfFalsePackageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
$crossAutocrlfDeterministic = $LASTEXITCODE -eq 0 -and
    [string]$autocrlfTrueManifest.packageId -eq [string]$autocrlfFalseManifest.packageId -and
    ((@($autocrlfTrueManifest.files | ForEach-Object { "$($_.path)|$($_.size)|$($_.sha256)" }) -join "`n") -eq
        (@($autocrlfFalseManifest.files | ForEach-Object { "$($_.path)|$($_.size)|$($_.sha256)" }) -join "`n"))
if (-not $crossAutocrlfDeterministic) { $errors.Add("Release package identity changed across core.autocrlf checkout policies.") }

& (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1") `
    -PackageRoot $releasePackageRoot -Output $releasePackageGatePath -RequireRelease `
    -ExpectedPackageId $releasePackageId | Out-Null
$releaseGateDocument = if (Test-Path -LiteralPath $releasePackageGatePath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $releasePackageGatePath | ConvertFrom-Json
} else { $null }
$releasePackageValidated = $LASTEXITCODE -eq 0 -and $null -ne $releaseGateDocument -and
    [bool]$releaseGateDocument.passed -and [bool]$releaseGateDocument.releaseIdentityValid -and
    [bool]$releaseGateDocument.schemaValid -and [bool]$releaseGateDocument.layoutValid
if (-not $releasePackageValidated) { $errors.Add("Clean Release toolchain package did not pass the full package gate.") }

$releaseIdentityTamperRoot = Join-Path $OutputRoot "release-identity-tamper-package"
Copy-Tree $releasePackageRoot $releaseIdentityTamperRoot
$releaseIdentityTamperManifestPath = Join-Path $releaseIdentityTamperRoot "dhe-toolchain-manifest.json"
$releaseIdentityTamperManifest = Get-Content -Raw -LiteralPath $releaseIdentityTamperManifestPath | ConvertFrom-Json
$releaseIdentityTamperManifest.sourceIdentity.clean = $false
[IO.File]::WriteAllText($releaseIdentityTamperManifestPath, ($releaseIdentityTamperManifest | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
$releaseIdentityTamperExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "test-dhe-toolchain-package.ps1"),
    "-PackageRoot", $releaseIdentityTamperRoot,
    "-Output", (Join-Path $OutputRoot "release-identity-tamper.gate.json"),
    "-RequireRelease"
)
$releaseIdentityTamperRejected = $releaseIdentityTamperExit -ne 0
if (-not $releaseIdentityTamperRejected) { $errors.Add("Release package gate accepted a dirty source identity.") }

$null = New-Item -ItemType Directory -Force -Path $releaseConsumerRoot
& git -C $releaseConsumerRoot init -q
& git -C $releaseConsumerRoot config user.email "dhe-release-consumer@example.invalid"
& git -C $releaseConsumerRoot config user.name "DHE Release Consumer"
$releaseUnpinnedDestination = Join-Path $releaseConsumerRoot "Tools/UnpinnedHybridCLRDhe"
$releaseUnpinnedInstallExit = Invoke-Child @(
    "-File", (Join-Path $PSScriptRoot "install-dhe-toolchain.ps1"),
    "-PackageRoot", $releasePackageRoot,
    "-Destination", $releaseUnpinnedDestination,
    "-Output", $releaseUnpinnedInstallReport
)
$releaseInstallPinRequired = $releaseUnpinnedInstallExit -ne 0 -and
    -not (Test-Path -LiteralPath $releaseUnpinnedDestination)
if (-not $releaseInstallPinRequired) { $errors.Add("Release installer accepted an unpinned package.") }

& (Join-Path $releasePackageRoot "scripts/install-dhe-toolchain.ps1") `
    -PackageRoot $releasePackageRoot -Destination $releaseInstalledRoot -Output $releaseInstallReport `
    -ExpectedPackageId $releasePackageId | Out-Null
$releaseInstalled = $LASTEXITCODE -eq 0 -and
    (Test-Path -LiteralPath (Join-Path $releaseInstalledRoot "dhe.ps1") -PathType Leaf)
if (-not $releaseInstalled) { $errors.Add("Release package did not install into the independent consumer repository.") }
& git -C $releaseConsumerRoot add -- Tools
& git -C $releaseConsumerRoot commit -q -m "test: install Release DHE toolchain"
if ($LASTEXITCODE -ne 0) { throw "Unable to commit the installed Release DHE package." }

$releaseUnpinnedDoctorExit = Invoke-Child @(
    "-File", (Join-Path $releaseInstalledRoot "dhe.ps1"),
    "doctor",
    "-Output", $releaseUnpinnedDoctorReport,
    "-RequireRelease"
)
$releaseUnpinnedDoctorDocument = if (Test-Path -LiteralPath $releaseUnpinnedDoctorReport -PathType Leaf) {
    Get-Content -Raw -LiteralPath $releaseUnpinnedDoctorReport | ConvertFrom-Json
} else { $null }
$releaseDoctorPinRequired = $releaseUnpinnedDoctorExit -ne 0 -and
    $null -ne $releaseUnpinnedDoctorDocument -and
    -not [bool]$releaseUnpinnedDoctorDocument.passed -and
    [bool]$releaseUnpinnedDoctorDocument.requireRelease -and
    $null -eq $releaseUnpinnedDoctorDocument.expectedPackageId
if (-not $releaseDoctorPinRequired) { $errors.Add("Release doctor accepted an installed package without an external packageId pin.") }

$releaseUnpinnedWorkflowExit = Invoke-Child @(
    "-File", (Join-Path $releaseInstalledRoot "dhe.ps1"),
    "workflow",
    "-AdapterScript", $adapterPath,
    "-ProjectPath", $projectRoot,
    "-SettingsFile", (Join-Path $projectRoot "ProjectSettings/HybridCLRSettings.asset"),
    "-RuntimeSource", $projectRoot,
    "-OutputRoot", $releaseUnpinnedWorkflowRoot,
    "-BaselineAotRoot", $projectRoot,
    "-Mode", "Release",
    "-ForceOutput"
)
$releaseUnpinnedWorkflowFailurePath = Join-Path $releaseUnpinnedWorkflowRoot "project-workflow-failure.json"
$releaseUnpinnedWorkflowFailure = if (Test-Path -LiteralPath $releaseUnpinnedWorkflowFailurePath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $releaseUnpinnedWorkflowFailurePath | ConvertFrom-Json
} else { $null }
$releaseWorkflowPinRequired = $releaseUnpinnedWorkflowExit -ne 0 -and
    $null -ne $releaseUnpinnedWorkflowFailure -and
    [string]$releaseUnpinnedWorkflowFailure.error -like "*requires -ExpectedToolchainPackageId*" -and
    -not [bool]$releaseUnpinnedWorkflowFailure.stages.prepare.passed
if (-not $releaseWorkflowPinRequired) { $errors.Add("Installed Release workflow accepted a missing external packageId pin or invoked its adapter first.") }

& (Join-Path $releaseInstalledRoot "dhe.ps1") doctor -Output $releaseDoctorReport `
    -RequireRelease -ExpectedPackageId $releasePackageId
$releaseDoctorDocument = if (Test-Path -LiteralPath $releaseDoctorReport -PathType Leaf) {
    Get-Content -Raw -LiteralPath $releaseDoctorReport | ConvertFrom-Json
} else { $null }
$releaseDoctorPassed = $LASTEXITCODE -eq 0 -and $null -ne $releaseDoctorDocument -and
    [bool]$releaseDoctorDocument.passed -and [string]$releaseDoctorDocument.packageId -eq $releasePackageId
if (-not $releaseDoctorPassed) { $errors.Add("Installed Release toolchain doctor did not pass the pinned package identity.") }

Copy-Tree $packageRoot $downgradeSourceRoot
$downgradeLayoutPath = Join-Path $downgradeSourceRoot "manifests/dhe-toolchain-layout.json"
$downgradeLayout = Get-Content -Raw -LiteralPath $downgradeLayoutPath | ConvertFrom-Json
$downgradeLayout.toolchainVersion = "0.0.9"
[IO.File]::WriteAllText($downgradeLayoutPath, ($downgradeLayout | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
& git -C $downgradeSourceRoot init -q
& git -C $downgradeSourceRoot config user.email "dhe-downgrade-fixture@example.invalid"
& git -C $downgradeSourceRoot config user.name "DHE Downgrade Fixture"
& git -C $downgradeSourceRoot add -- .
& git -C $downgradeSourceRoot commit -q -m "test: DHE 0.0.9 release source"
if ($LASTEXITCODE -ne 0) { throw "Unable to commit the DHE downgrade source fixture." }
& (Join-Path $downgradeSourceRoot "scripts/publish-dhe-toolchain.ps1") `
    -LabRoot $downgradeSourceRoot -OutputRoot $downgradePackageRoot -Mode Release -ForceOutput | Out-Null
$downgradeManifest = Get-Content -Raw -LiteralPath (Join-Path $downgradePackageRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json
$downgradePackageId = [string]$downgradeManifest.packageId

$downgradeRejectExit = Invoke-Child @(
    "-File", (Join-Path $downgradePackageRoot "scripts/install-dhe-toolchain.ps1"),
    "-PackageRoot", $downgradePackageRoot,
    "-Destination", $releaseInstalledRoot,
    "-Output", $downgradeRejectReport,
    "-Upgrade",
    "-ExpectedPackageId", $downgradePackageId
)
$installedVersionAfterReject = [string](Get-Content -Raw -LiteralPath (Join-Path $releaseInstalledRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json).toolchainVersion
$downgradeRejected = $downgradeRejectExit -ne 0 -and $installedVersionAfterReject -eq $releasePackageVersion
if (-not $downgradeRejected) { $errors.Add("Installer did not reject the Release toolchain downgrade by default.") }

& (Join-Path $downgradePackageRoot "scripts/install-dhe-toolchain.ps1") `
    -PackageRoot $downgradePackageRoot -Destination $releaseInstalledRoot -Output $downgradeInstallReport `
    -Upgrade -AllowDowngrade -ExpectedPackageId $downgradePackageId | Out-Null
$explicitDowngradeValidated = $LASTEXITCODE -eq 0 -and
    [string](Get-Content -Raw -LiteralPath (Join-Path $releaseInstalledRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json).toolchainVersion -eq "0.0.9"
if (-not $explicitDowngradeValidated) { $errors.Add("Explicit verified toolchain downgrade did not install 0.0.9.") }

& (Join-Path $releasePackageRoot "scripts/install-dhe-toolchain.ps1") `
    -PackageRoot $releasePackageRoot -Destination $releaseInstalledRoot -Output $downgradeRecoveryReport `
    -Upgrade -ExpectedPackageId $releasePackageId | Out-Null
$downgradeRecoveryExitCode = [int]$LASTEXITCODE
$releaseConsumerFinalStatus = @(& git -C $releaseConsumerRoot status --porcelain=v1 --untracked-files=all)
$downgradeRecoveryValidated = $downgradeRecoveryExitCode -eq 0 -and
    [string](Get-Content -Raw -LiteralPath (Join-Path $releaseInstalledRoot "dhe-toolchain-manifest.json") | ConvertFrom-Json).toolchainVersion -eq $releasePackageVersion -and
    $releaseConsumerFinalStatus.Count -eq 0
if (-not $downgradeRecoveryValidated) { $errors.Add("Toolchain did not recover from the explicit downgrade to the pinned clean Release installation.") }

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-toolchain-fixture-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    packagePublished = $packagePublished
    portableAttributesSanitized = $portableAttributesSanitized
    packageValidated = $packageValidated
    publishedPackageReplaceValidated = $publishedPackageReplaceValidated
    tamperRejected = $tamperRejected
    extraFileRejected = $extraFileRejected
    missingFileRejected = $missingFileRejected
    reparsePackageRejected = $reparsePackageRejected
    packageReportCollisionRejected = $packageReportCollisionRejected
    packageIdPinRejected = $packageIdPinRejected
    untrustedPackageCodeInert = $untrustedPackageCodeInert
    installed = $installed
    upgradeValidated = $upgradeValidated
    installLockRejected = $installLockRejected
    trackedAdapterUpdated = $trackedAdapterUpdated
    corruptInstallRejected = $corruptInstallRejected
    doctorReportCollisionRejected = $doctorReportCollisionRejected
    installReportCollisionRejected = $installReportCollisionRejected
    doctorPassed = $doctorPassed
    defaultReportsExternal = $defaultReportsExternal
    adapterCreated = $adapterCreated
    adapterParsed = $adapterParsed
    workflowPackageGatePassed = $workflowPackageGatePassed
    manifestBoundaryTracked = $manifestBoundaryTracked
    releasePackagePublished = $releasePackagePublished
    releasePackageDeterministic = $releasePackageDeterministic
    crossAutocrlfDeterministic = $crossAutocrlfDeterministic
    releasePackageValidated = $releasePackageValidated
    releaseIdentityTamperRejected = $releaseIdentityTamperRejected
    releaseInstallPinRequired = $releaseInstallPinRequired
    releaseInstalled = $releaseInstalled
    releaseDoctorPinRequired = $releaseDoctorPinRequired
    releaseWorkflowPinRequired = $releaseWorkflowPinRequired
    releaseDoctorPassed = $releaseDoctorPassed
    downgradeRejected = $downgradeRejected
    explicitDowngradeValidated = $explicitDowngradeValidated
    downgradeRecoveryValidated = $downgradeRecoveryValidated
    errors = $errors.ToArray()
}
$reportPath = Join-Path $OutputRoot "toolchain-fixture-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
if (-not $report.passed) {
    Write-Error ("DHE toolchain fixture gate failed: " + ($errors -join "; "))
    exit 1
}
Write-Host "DHE toolchain fixture gate passed: $reportPath"
exit 0
