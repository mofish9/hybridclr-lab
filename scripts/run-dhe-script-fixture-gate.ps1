[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$OutputRoot = "",
    [switch]$WorkflowLockAlreadyHeld,
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")
# PowerShell does not create LASTEXITCODE for a successful script invocation;
# initialize it before the explicit child-process checks below.
$global:LASTEXITCODE = 0
$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-script-fixture-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
$OutputRoot = Initialize-DheOutputRoot -Path $OutputRoot -Force:$ForceOutput

function Require([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-ExpectedFailure([string[]]$Arguments) {
    $callerErrorActionPreference = $ErrorActionPreference
    try {
        # Windows PowerShell 5.1 promotes a child Write-Error/throw to a
        # terminating NativeCommandError under ErrorAction Stop. These are
        # intentional negative probes, so retain only the process exit code.
        $ErrorActionPreference = "Continue"
        & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File @Arguments 2>&1 | Out-Null
        return [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $callerErrorActionPreference
    }
}

$fixtures = Join-Path $LabRoot "scripts/fixtures"
$settingsFixture = Join-Path $fixtures "dhe-settings-parser.asset"
$settingsResult = Resolve-DheSettingsAssemblySets -SettingsFile $settingsFixture -ProjectRoot $LabRoot
Require ((@($settingsResult.hotUpdateAssemblies) -join "|") -eq "Feature,Core|Quoted#Name") `
    "Settings parser did not preserve quoted commas, quoted hashes, or inline comments."
Require ((@($settingsResult.dheAotAssemblies) -join "|") -eq "Feature,Core|Quoted#Name") `
    "Settings parser did not parse the explicit DHE assembly list."
Require ((@($settingsResult.externalHotUpdateAssemblyDirs) -join "|") -eq "relative/path") `
    "Settings parser did not remove a trailing YAML comment from a quoted path."

# Child-process list arguments must preserve legal Windows paths containing a
# semicolon; the legacy delimiter format cannot represent this case.
$pathListFixture = [string[]]@(
    "C:\dhe\assembly;one.dll",
    "C:\dhe\two.dll")
$encodedPathList = ConvertTo-DheStringListArgument -Values $pathListFixture
$decodedPathList = [string[]](ConvertFrom-DheStringListArgument -Value $encodedPathList)
Require ($decodedPathList.Count -eq 2 -and
    $decodedPathList[0] -eq $pathListFixture[0] -and
    $decodedPathList[1] -eq $pathListFixture[1]) `
    "JSON DHE string-list argument did not preserve a semicolon-containing path."

$externalProjectRoot = Join-Path $OutputRoot "external-project-without-embedded-package"
New-Item -ItemType Directory -Force -Path $externalProjectRoot | Out-Null
$externalDnlibFallbackRejected = $false
try {
    $null = Resolve-DheDnlibPath -ProjectRoot $externalProjectRoot
} catch {
    $externalDnlibFallbackRejected = $_.Exception.Message -like "dnlib.dll was not found in the project embedded HybridCLR package*"
}
Require $externalDnlibFallbackRejected "External project silently fell back to the demo dnlib.dll."
$demoDnlib = Resolve-DheDnlibPath -ProjectRoot (Join-Path $LabRoot "unity2021-dhe-demo")
Require ($demoDnlib.StartsWith((Join-Path $LabRoot "unity2021-dhe-demo"), [StringComparison]::OrdinalIgnoreCase)) `
    "Demo project did not resolve dnlib.dll from its own embedded package."

# Prove that an embedded-package lock is bound to ProjectPath, not to the lab
# or to the directory that happens to contain the lock file.
$externalEmbeddedRoot = Join-Path $OutputRoot "external-project-with-embedded-package"
$externalEmbeddedProject = Join-Path $externalEmbeddedRoot "consumer-project"
$externalEmbeddedLockRoot = Join-Path $externalEmbeddedRoot "release-locks-outside-project"
$externalEmbeddedPackages = Join-Path $externalEmbeddedProject "Packages"
$externalEmbeddedSettings = Join-Path $externalEmbeddedProject "ProjectSettings"
New-Item -ItemType Directory -Force -Path $externalEmbeddedPackages,$externalEmbeddedSettings,$externalEmbeddedLockRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $LabRoot "unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr") `
    -Destination $externalEmbeddedPackages -Recurse -Force
Copy-Item -LiteralPath (Join-Path $LabRoot "unity2021-dhe-demo/Packages/manifest.json") `
    -Destination (Join-Path $externalEmbeddedPackages "manifest.json") -Force
Copy-Item -LiteralPath (Join-Path $LabRoot "unity2021-dhe-demo/Packages/packages-lock.json") `
    -Destination (Join-Path $externalEmbeddedPackages "packages-lock.json") -Force
Copy-Item -LiteralPath (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset") `
    -Destination (Join-Path $externalEmbeddedSettings "HybridCLRSettings.asset") -Force

$fixtureUtf8NoBom = New-Object Text.UTF8Encoding($false)
$checkedPackageLock = Get-Content -Raw -LiteralPath (Join-Path $LabRoot "manifests/dhe-package-lock.json") | ConvertFrom-Json
$externalPackageLockPath = Join-Path $externalEmbeddedLockRoot "dhe-package-lock.json"
[IO.File]::WriteAllText($externalPackageLockPath, ($checkedPackageLock | ConvertTo-Json -Depth 8), $fixtureUtf8NoBom)
$externalEmbeddedReportRoot = Join-Path $externalEmbeddedRoot "valid-source-preflight"
& (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File `
    (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1") `
    -LabRoot $LabRoot -ProjectPath $externalEmbeddedProject -OutputRoot $externalEmbeddedReportRoot `
    -PackageLockPath $externalPackageLockPath -RequireEmbeddedPackage -RequireDheEqualsHotUpdate -ForceOutput | Out-Null
$externalEmbeddedReportPath = Join-Path $externalEmbeddedReportRoot "source-preflight-report.json"
$externalEmbeddedReport = if (Test-Path -LiteralPath $externalEmbeddedReportPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $externalEmbeddedReportPath | ConvertFrom-Json
} else { $null }
$externalEmbeddedPackageLockValidated = $LASTEXITCODE -eq 0 -and
    $null -ne $externalEmbeddedReport -and [bool]$externalEmbeddedReport.passed
Require $externalEmbeddedPackageLockValidated `
    "A project-root-relative package lock outside the consumer project did not pass source preflight."

# An embedded package lock must identify the same integrated package commit as
# the runtime lock. A matching tree hash alone is insufficient because a
# different commit can produce the same copied tree after local edits.
$staleIntegratedPackageLock = $checkedPackageLock | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$staleIntegratedPackageLock.integratedCommit = $staleIntegratedPackageLock.baseCommit
$staleIntegratedPackageLockPath = Join-Path $externalEmbeddedLockRoot "stale-integrated-commit-package-lock.json"
[IO.File]::WriteAllText($staleIntegratedPackageLockPath, ($staleIntegratedPackageLock | ConvertTo-Json -Depth 8), $fixtureUtf8NoBom)
$staleIntegratedPackageLockExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1"),
    "-LabRoot", $LabRoot,
    "-ProjectPath", $externalEmbeddedProject,
    "-OutputRoot", (Join-Path $externalEmbeddedRoot "stale-integrated-commit-source-preflight"),
    "-PackageLockPath", $staleIntegratedPackageLockPath,
    "-RequireEmbeddedPackage",
    "-RequireDheEqualsHotUpdate",
    "-ForceOutput"
)
$staleIntegratedPackageLockReportPath = Join-Path $externalEmbeddedRoot "stale-integrated-commit-source-preflight/source-preflight-report.json"
$staleIntegratedPackageLockReport = if (Test-Path -LiteralPath $staleIntegratedPackageLockReportPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $staleIntegratedPackageLockReportPath | ConvertFrom-Json
} else { $null }
$staleIntegratedPackageLockRejected = $staleIntegratedPackageLockExit -ne 0 -and
    $null -ne $staleIntegratedPackageLockReport -and
    @($staleIntegratedPackageLockReport.errors | Where-Object { $_ -like "*package-integrated-commit*" -or $_ -like "*integratedCommit*" }).Count -gt 0
Require $staleIntegratedPackageLockRejected `
    "Source preflight accepted a package lock with an integrated commit different from the runtime lock."

# A valid JSON document with missing lock fields must fail through the normal
# report contract instead of terminating on StrictMode property access.
$malformedPackageLock = $checkedPackageLock | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$malformedPackageLock.PSObject.Properties.Remove("integratedCommit")
$malformedPackageLockPath = Join-Path $externalEmbeddedLockRoot "malformed-package-lock.json"
[IO.File]::WriteAllText($malformedPackageLockPath, ($malformedPackageLock | ConvertTo-Json -Depth 8), $fixtureUtf8NoBom)
$malformedPackageLockOutputRoot = Join-Path $externalEmbeddedRoot "malformed-package-lock-source-preflight"
$malformedPackageLockExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1"),
    "-LabRoot", $LabRoot,
    "-ProjectPath", $externalEmbeddedProject,
    "-OutputRoot", $malformedPackageLockOutputRoot,
    "-PackageLockPath", $malformedPackageLockPath,
    "-RequireEmbeddedPackage",
    "-RequireDheEqualsHotUpdate",
    "-ForceOutput"
)
$malformedPackageLockReportPath = Join-Path $malformedPackageLockOutputRoot "source-preflight-report.json"
$malformedPackageLockReport = if (Test-Path -LiteralPath $malformedPackageLockReportPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $malformedPackageLockReportPath | ConvertFrom-Json
} else { $null }
$malformedPackageLockReported = $malformedPackageLockExit -ne 0 -and
    $null -ne $malformedPackageLockReport -and
    [bool]$malformedPackageLockReport.passed -eq $false -and
    @($malformedPackageLockReport.errors | Where-Object { $_ -like "*package-lock:schema*" -or $_ -like "*invalid schema*" }).Count -gt 0
Require $malformedPackageLockReported `
    "Malformed package lock did not produce a machine-readable source preflight report."

$legacyPackageLock = $checkedPackageLock | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$legacyPackageLock.packagePath = "unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr"
$legacyPackageLockPath = Join-Path $externalEmbeddedLockRoot "legacy-lab-relative-package-lock.json"
[IO.File]::WriteAllText($legacyPackageLockPath, ($legacyPackageLock | ConvertTo-Json -Depth 8), $fixtureUtf8NoBom)
$legacyPackageLockExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1"),
    "-LabRoot", $LabRoot,
    "-ProjectPath", $externalEmbeddedProject,
    "-OutputRoot", (Join-Path $externalEmbeddedRoot "legacy-source-preflight"),
    "-PackageLockPath", $legacyPackageLockPath,
    "-RequireEmbeddedPackage",
    "-RequireDheEqualsHotUpdate",
    "-ForceOutput"
)
$legacyLabRelativePackageLockRejected = $legacyPackageLockExit -ne 0
Require $legacyLabRelativePackageLockRejected `
    "Source preflight accepted a legacy LabRoot-relative embedded package lock."

$absolutePackageLock = $checkedPackageLock | ConvertTo-Json -Depth 8 | ConvertFrom-Json
$absolutePackageLock.packagePath = Join-Path $externalEmbeddedProject "Packages/com.code-philosophy.hybridclr"
$absolutePackageLockPath = Join-Path $externalEmbeddedLockRoot "absolute-package-lock.json"
[IO.File]::WriteAllText($absolutePackageLockPath, ($absolutePackageLock | ConvertTo-Json -Depth 8), $fixtureUtf8NoBom)
$absolutePackageLockExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-source-preflight.ps1"),
    "-LabRoot", $LabRoot,
    "-ProjectPath", $externalEmbeddedProject,
    "-OutputRoot", (Join-Path $externalEmbeddedRoot "absolute-source-preflight"),
    "-PackageLockPath", $absolutePackageLockPath,
    "-RequireEmbeddedPackage",
    "-RequireDheEqualsHotUpdate",
    "-ForceOutput"
)
$absolutePackageLockRejected = $absolutePackageLockExit -ne 0
Require $absolutePackageLockRejected `
    "Source preflight accepted an absolute embedded package lock path."
# Invalid inputs are probes, not evidence documents. Keep their versioned
# failure reports but do not leave invalid recognized JSON in aggregate scans.
Remove-Item -LiteralPath $legacyPackageLockPath,$absolutePackageLockPath,$staleIntegratedPackageLockPath,$malformedPackageLockPath -Force

$workflowLockTested = -not [bool]$WorkflowLockAlreadyHeld
$workflowLockRejected = $null
$lockRetryExit = $null
if ($workflowLockTested) {
    $workflowLock = $null
    $workflowLockRejected = $false
    try {
        $workflowLock = Enter-DheWorkflowLock -LabRoot $LabRoot
        $lockProbeExit = Invoke-ExpectedFailure @(
            (Join-Path $fixtures "dhe-workflow-lock-probe.ps1"),
            "-LabRoot", $LabRoot,
            "-TimeoutSeconds", "0")
        $workflowLockRejected = $lockProbeExit -ne 0
    } finally {
        Exit-DheWorkflowLock -Lock $workflowLock
    }
    Require $workflowLockRejected "Concurrent DHE workflow lock probe was not rejected."
    $lockRetryExit = Invoke-ExpectedFailure @(
        (Join-Path $fixtures "dhe-workflow-lock-probe.ps1"),
        "-LabRoot", $LabRoot,
        "-TimeoutSeconds", "0")
    Require ($lockRetryExit -eq 0) "DHE workflow lock was not reusable after release."
}
$global:LASTEXITCODE = 0

$prePrepareGateTested = -not [bool]$WorkflowLockAlreadyHeld
$invalidRuntimeRejectedBeforeAdapter = $null
$adapterTargetParameterValidated = $null
$explicitProjectVcsValidated = $null
$malformedWorkflowPackageLockReported = $null
if ($prePrepareGateTested) {
    $prepareFailureRoot = Join-Path $OutputRoot "adapter-prepare-failure"
    $prepareFailureExit = Invoke-ExpectedFailure @(
        (Join-Path $LabRoot "scripts/run-dhe-project-workflow.ps1"),
        "-AdapterScript", (Join-Path $fixtures "dhe-project-adapter-failure-fixture.ps1"),
        "-ProjectPath", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-SettingsFile", (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset"),
        "-RuntimeSource", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-OutputRoot", $prepareFailureRoot,
        "-Target", "FixtureTarget",
        "-Mode", "Exploratory",
        "-ForceOutput")
    $prepareFailureReportPath = Join-Path $prepareFailureRoot "project-workflow-failure.json"
    $prepareFailureReport = if (Test-Path -LiteralPath $prepareFailureReportPath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $prepareFailureReportPath | ConvertFrom-Json
    } else { $null }
    $invalidRuntimeRejectedBeforeAdapter = $prepareFailureExit -ne 0 -and $null -ne $prepareFailureReport -and
        [string]$prepareFailureReport.error -like "*DheRuntime.cpp was not found*" -and
        -not (Test-Path -LiteralPath (Join-Path $prepareFailureRoot "workflow-failure.json") -PathType Leaf)
    Require $invalidRuntimeRejectedBeforeAdapter "Project workflow invoked its adapter before rejecting an invalid runtime."

    $directAdapterRoot = Join-Path $OutputRoot "adapter-target-parameter"
    $directAdapterExit = Invoke-ExpectedFailure @(
        (Join-Path $fixtures "dhe-project-adapter-failure-fixture.ps1"),
        "-Action", "Prepare",
        "-ProjectPath", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-SettingsFile", (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset"),
        "-RuntimeSource", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-OutputRoot", $directAdapterRoot,
        "-Target", "FixtureTarget",
        "-ToolchainContractVersion", "1",
        "-Mode", "Exploratory")
    $directAdapterFailurePath = Join-Path $directAdapterRoot "workflow-failure.json"
    $directAdapterFailure = if (Test-Path -LiteralPath $directAdapterFailurePath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $directAdapterFailurePath | ConvertFrom-Json
    } else { $null }
    $adapterTargetParameterValidated = $directAdapterExit -ne 0 -and $null -ne $directAdapterFailure -and
        [string]$directAdapterFailure.error -eq "fixture-prepare-root-cause:FixtureTarget"
    Require $adapterTargetParameterValidated "Adapter contract did not preserve its opaque target parameter."

    # An explicit VCS selection must activate the project identity check even
    # when the caller does not separately provide -GitRoot. Otherwise
    # -ProjectVcs Svn would be silently ignored in exploratory preflight.
    $explicitVcsRoot = Join-Path $OutputRoot "explicit-project-vcs"
    $explicitVcsExit = Invoke-ExpectedFailure @(
        (Join-Path $LabRoot "scripts/run-dhe-project-workflow.ps1"),
        "-AdapterScript", (Join-Path $fixtures "dhe-project-adapter-failure-fixture.ps1"),
        "-ProjectPath", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-SettingsFile", (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset"),
        "-RuntimeSource", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-OutputRoot", $explicitVcsRoot,
        "-ProjectVcs", "Svn",
        "-Mode", "Exploratory",
        "-ForceOutput"
    )
    $explicitVcsFailurePath = Join-Path $explicitVcsRoot "project-workflow-failure.json"
    $explicitVcsFailure = if (Test-Path -LiteralPath $explicitVcsFailurePath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $explicitVcsFailurePath | ConvertFrom-Json
    } else { $null }
    $explicitProjectVcsValidated = $explicitVcsExit -ne 0 -and $null -ne $explicitVcsFailure -and
        [string]$explicitVcsFailure.error -like "*not a working copy*"
    Require $explicitProjectVcsValidated "Explicit ProjectVcs selection did not activate project VCS verification."

    # Package-lock parsing belongs to the guarded workflow transaction. A
    # malformed lock must leave a normal project-workflow-failure.json before
    # any adapter code is invoked.
    $malformedWorkflowLockRoot = Join-Path $OutputRoot "malformed-workflow-package-lock"
    $malformedWorkflowLockPath = Join-Path $externalEmbeddedLockRoot "malformed-workflow-package-lock.json"
    $workflowMalformedLock = $checkedPackageLock | ConvertTo-Json -Depth 8 | ConvertFrom-Json
    $workflowMalformedLock.PSObject.Properties.Remove("packagePath")
    [IO.File]::WriteAllText($malformedWorkflowLockPath, ($workflowMalformedLock | ConvertTo-Json -Depth 8), $fixtureUtf8NoBom)
    $malformedWorkflowLockExit = Invoke-ExpectedFailure @(
        (Join-Path $LabRoot "scripts/run-dhe-project-workflow.ps1"),
        "-AdapterScript", (Join-Path $fixtures "dhe-project-adapter-failure-fixture.ps1"),
        "-ProjectPath", $externalEmbeddedProject,
        "-SettingsFile", (Join-Path $externalEmbeddedSettings "HybridCLRSettings.asset"),
        "-RuntimeSource", (Join-Path $LabRoot "unity2021-dhe-demo"),
        "-OutputRoot", $malformedWorkflowLockRoot,
        "-PackageLockPath", $malformedWorkflowLockPath,
        "-Mode", "Exploratory",
        "-ForceOutput")
    $malformedWorkflowFailurePath = Join-Path $malformedWorkflowLockRoot "project-workflow-failure.json"
    $malformedWorkflowFailure = if (Test-Path -LiteralPath $malformedWorkflowFailurePath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $malformedWorkflowFailurePath | ConvertFrom-Json
    } else { $null }
    $malformedWorkflowPackageLockReported = $malformedWorkflowLockExit -ne 0 -and
        $null -ne $malformedWorkflowFailure -and
        [string]$malformedWorkflowFailure.format -eq "hybridclr.dhe-project-workflow-failure.json" -and
        [string]$malformedWorkflowFailure.error -like "*packagePath*"
    Require $malformedWorkflowPackageLockReported `
        "Malformed workflow package lock did not produce a machine-readable failure report."
}
$global:LASTEXITCODE = 0

# Patch checks must use the requested root even when that root is nested in an
# unrelated Git repository. Forward and reverse checks are mutually exclusive
# for both a clean and an already-applied tree.
$patchIsolationRoot = Join-Path $OutputRoot "git-apply-root-isolation"
$patchAncestorRoot = Join-Path $patchIsolationRoot "ancestor-repository"
$patchAppliedRoot = Join-Path $patchAncestorRoot "nested/applied"
$patchCleanRoot = Join-Path $patchAncestorRoot "nested/clean"
New-Item -ItemType Directory -Force -Path $patchAppliedRoot,$patchCleanRoot | Out-Null
& git -C $patchAncestorRoot init -q
Require ($LASTEXITCODE -eq 0) "Unable to initialize the Git apply isolation fixture."
$utf8NoBom = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText((Join-Path $patchAppliedRoot "value.txt"), "after`n", $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $patchAppliedRoot "new.txt"), "new`n", $utf8NoBom)
[IO.File]::WriteAllText((Join-Path $patchCleanRoot "value.txt"), "before`n", $utf8NoBom)
$patchFixturePath = Join-Path $patchIsolationRoot "change.patch"
$patchFixtureText = @(
    "diff --git a/new.txt b/new.txt",
    "new file mode 100644",
    "--- /dev/null",
    "+++ b/new.txt",
    "@@ -0,0 +1 @@",
    "+new",
    "diff --git a/value.txt b/value.txt",
    "--- a/value.txt",
    "+++ b/value.txt",
    "@@ -1 +1 @@",
    "-before",
    "+after",
    ""
) -join "`n"
[IO.File]::WriteAllText($patchFixturePath, $patchFixtureText, $utf8NoBom)
$appliedForward = Invoke-DheGitApplyAtRoot -Root $patchAppliedRoot -PatchPath $patchFixturePath -Check
$appliedReverse = Invoke-DheGitApplyAtRoot -Root $patchAppliedRoot -PatchPath $patchFixturePath -Check -Reverse
$cleanForward = Invoke-DheGitApplyAtRoot -Root $patchCleanRoot -PatchPath $patchFixturePath -Check
$cleanReverse = Invoke-DheGitApplyAtRoot -Root $patchCleanRoot -PatchPath $patchFixturePath -Check -Reverse
$gitApplyRootIsolated = $appliedForward.exitCode -ne 0 -and $appliedReverse.exitCode -eq 0 -and
    $cleanForward.exitCode -eq 0 -and $cleanReverse.exitCode -ne 0
Require $gitApplyRootIsolated "Git patch checks did not remain bound to the exact nested patch root."
$global:LASTEXITCODE = 0

$preflightFailureRoot = Join-Path $OutputRoot "project-preflight-failure"
$preflightFailureInput = Join-Path $preflightFailureRoot "input"
$preflightFailureBaseline = Join-Path $preflightFailureInput "baseline"
$preflightFailureCurrent = Join-Path $preflightFailureInput "current"
$preflightFailureProject = Join-Path $LabRoot "unity2021-dhe-demo"
$preflightFailureOutput = Join-Path $preflightFailureRoot "output"
New-Item -ItemType Directory -Force -Path $preflightFailureBaseline,$preflightFailureCurrent | Out-Null
$preflightFailureExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-project-preflight.ps1"),
    "-SettingsFile", $settingsFixture,
    "-BaselineRoot", $preflightFailureBaseline,
    "-CurrentRoot", $preflightFailureCurrent,
    "-OutputRoot", $preflightFailureOutput,
    "-ProjectRoot", $preflightFailureProject,
    "-RuntimeSource", (Join-Path $preflightFailureInput "missing-runtime/libil2cpp"),
    "-DnlibPath", $demoDnlib,
    "-RequireRuntime",
    "-RequireCleanRuntimeSources",
    "-ForceOutput")
$preflightFailureReportPath = Join-Path $preflightFailureOutput "project-preflight-report.json"
$preflightSourceFailureReportPath = Join-Path $preflightFailureOutput "source-preflight/source-preflight-report.json"
$preflightFailureReport = if (Test-Path -LiteralPath $preflightFailureReportPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $preflightFailureReportPath | ConvertFrom-Json
} else { $null }
$projectPreflightFailureReported = $preflightFailureExit -ne 0 -and
    $null -ne $preflightFailureReport -and
    [string]$preflightFailureReport.format -eq "hybridclr.dhe-project-preflight-failure.json" -and
    $preflightFailureReport.passed -is [bool] -and -not [bool]$preflightFailureReport.passed -and
    (Test-Path -LiteralPath $preflightSourceFailureReportPath -PathType Leaf)
Require $projectPreflightFailureReported "Project preflight failure did not produce both top-level and source reports."
$global:LASTEXITCODE = 0

$adapterContractMismatchRoot = Join-Path $OutputRoot "adapter-contract-mismatch"
$adapterContractMismatchExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/fixtures/dhe-project-adapter-contract-mismatch-fixture.ps1"),
    "-Action", "Prepare",
    "-ProjectPath", (Join-Path $LabRoot "unity2021-dhe-demo"),
    "-SettingsFile", (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset"),
    "-RuntimeSource", (Join-Path $LabRoot "unity2021-dhe-demo"),
    "-OutputRoot", $adapterContractMismatchRoot,
    "-Target", "FixtureTarget",
    "-ToolchainContractVersion", "1",
    "-Mode", "Exploratory"
)
$invalidPreparePath = Join-Path $adapterContractMismatchRoot "adapter/prepare.json"
$invalidPrepare = if (Test-Path -LiteralPath $invalidPreparePath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $invalidPreparePath | ConvertFrom-Json
} else { $null }
$adapterContractMismatchRejected = $adapterContractMismatchExit -eq 0 -and $null -ne $invalidPrepare
if ($adapterContractMismatchRejected) {
    try {
        $null = Assert-DheAdapterPrepareReport -Report $invalidPrepare -ToolchainContractVersion 1 `
            -Target "FixtureTarget" -ProjectPath (Join-Path $LabRoot "unity2021-dhe-demo") `
            -SettingsFile (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset")
        $adapterContractMismatchRejected = $false
    } catch {
        $adapterContractMismatchRejected = $_.Exception.Message -like "*does not match toolchain contract version 1*"
    }
}
Require $adapterContractMismatchRejected "Project workflow accepted an adapter with a mismatched toolchain contract version."
if (Test-Path -LiteralPath $invalidPreparePath -PathType Leaf) { Remove-Item -LiteralPath $invalidPreparePath -Force }
$global:LASTEXITCODE = 0

$unsafeSourceOutputRejected = $false
try {
    Assert-DheSafeOutputRoot -Path (Join-Path $LabRoot "scripts")
} catch {
    $unsafeSourceOutputRejected = $true
}
Require $unsafeSourceOutputRejected "Output safety accepted a formal tracked source directory."

# Project preflight must protect the runtime input itself before honoring
# -ForceOutput. This is a destructive-path regression test, so use an isolated
# temporary tree and verify a sentinel survives the rejected invocation.
$runtimeOutputProtectionRoot = Join-Path $OutputRoot "runtime-output-protection"
$runtimeOutputBaselineRoot = Join-Path $runtimeOutputProtectionRoot "baseline"
$runtimeOutputCurrentRoot = Join-Path $runtimeOutputProtectionRoot "current"
$runtimeOutputSettingsRoot = Join-Path $runtimeOutputProtectionRoot "project/ProjectSettings"
$runtimeOutputProjectRoot = Join-Path $runtimeOutputProtectionRoot "project"
$runtimeOutputSentinel = Join-Path $runtimeOutputProtectionRoot "runtime-sentinel.txt"
New-Item -ItemType Directory -Force -Path $runtimeOutputBaselineRoot,$runtimeOutputCurrentRoot,$runtimeOutputSettingsRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $LabRoot "unity2021-dhe-demo/ProjectSettings/HybridCLRSettings.asset") `
    -Destination (Join-Path $runtimeOutputSettingsRoot "HybridCLRSettings.asset") -Force
Set-Content -LiteralPath $runtimeOutputSentinel -Value "preserve" -Encoding ASCII
$runtimeOutputProtectionExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-project-preflight.ps1"),
    "-SettingsFile", (Join-Path $runtimeOutputSettingsRoot "HybridCLRSettings.asset"),
    "-BaselineRoot", $runtimeOutputBaselineRoot,
    "-CurrentRoot", $runtimeOutputCurrentRoot,
    "-ProjectRoot", $runtimeOutputProjectRoot,
    "-RuntimeSource", $runtimeOutputProtectionRoot,
    "-OutputRoot", $runtimeOutputProtectionRoot,
    "-ForceOutput"
)
$runtimeOutputProtected = $runtimeOutputProtectionExit -ne 0 -and
    (Test-Path -LiteralPath $runtimeOutputSentinel -PathType Leaf)
Require $runtimeOutputProtected "Project preflight allowed -ForceOutput to delete RuntimeSource."

# Filesystem roots must remain roots during normalization. Trimming the root
# separator turns `C:\` into `C:` on Windows, which SVN interprets as the
# current working copy and can make an external output directory look unsafe
# or cause the wrong repository boundary to be inspected.
$filesystemRoot = [IO.Path]::GetPathRoot((Get-Location).Path)
$normalizedFilesystemRoot = Normalize-DhePath $filesystemRoot
$filesystemRootPreserved = $normalizedFilesystemRoot.Equals(
    $filesystemRoot, [StringComparison]::OrdinalIgnoreCase)
$filesystemRootRejected = $false
try {
    Assert-DheSafeOutputRoot -Path $filesystemRoot
} catch {
    $filesystemRootRejected = $_.Exception.Message -like "DHE output root may not be a filesystem root*"
}
Require $filesystemRootPreserved "Path normalization stripped the filesystem root separator."
Require $filesystemRootRejected "Output safety accepted the filesystem root."

# Portable archive/package JSON must reject POSIX machine-local paths as well
# as Windows drive/UNC paths, without mistaking URLs for filesystem paths.
Require (Test-DheMachineLocalPath "/Users/build/Unity.app/Contents/MacOS/Unity") `
    "Machine-local path detection did not reject a POSIX absolute path."
Require (Test-DheMachineLocalPath "/tmp") `
    "Machine-local path detection did not reject a one-part POSIX path."
Require (Test-DheMachineLocalPath "/") `
    "Machine-local path detection did not reject the POSIX filesystem root."
Require (Test-DheMachineLocalPath "//server/share") `
    "Machine-local path detection did not reject a POSIX double-slash network path."
Require (-not (Test-DheMachineLocalPath "https://example.invalid/runtime.json")) `
    "Machine-local path detection incorrectly rejected a URL."
Require (-not (Test-DheMachineLocalPath "a / b")) `
    "Machine-local path detection incorrectly rejected slash-separated prose."
Require (-not (Test-DheMachineLocalPath "../payload/assemblies/Example.mv.json")) `
    "Machine-local path detection incorrectly rejected a parent-relative archive reference."
Require (-not (Test-DheMachineLocalPath "./payload/assemblies/Example.mv.json")) `
    "Machine-local path detection incorrectly rejected a current-directory archive reference."
Require (Test-DheMachineLocalPath "path=/tmp/build") `
    "Machine-local path detection missed an embedded POSIX absolute path."
Require (Test-DheMachineLocalPath "workspace: C:\\build\\output") `
    "Machine-local path detection missed an embedded Windows absolute path."

$projectBoundaryProbeRoot = Join-Path $OutputRoot "project-root-boundary-probe"
$projectBoundaryProbePath = Join-Path $projectBoundaryProbeRoot "Assets/Editor/DHE/dhe-source-boundary.json"
New-Item -ItemType Directory -Force -Path (Join-Path $projectBoundaryProbeRoot "Assets"),
    (Join-Path $projectBoundaryProbeRoot "ProjectSettings"),
    (Split-Path -Parent $projectBoundaryProbePath) | Out-Null
$resolvedProjectBoundaryRoot = Resolve-DheProjectRootFromBoundary -BoundaryPath $projectBoundaryProbePath `
    -RepositoryRoot $projectBoundaryProbeRoot
Require ($resolvedProjectBoundaryRoot -eq ([IO.Path]::GetFullPath($projectBoundaryProbeRoot))) `
    "project-root-v1 boundary resolution did not identify the Unity project root."

# PowerShell unwraps a one-item native-command result to a scalar. Prove that
# the tracked-source guard still rejects a directory containing exactly one
# tracked file and does not swallow StrictMode property-access failures.
$singleTrackedRepository = Join-Path $OutputRoot "single-tracked-output-repository"
$singleTrackedDirectory = Join-Path $singleTrackedRepository "single"
$singleTrackedFile = Join-Path $singleTrackedDirectory "source.txt"
$null = New-Item -ItemType Directory -Force -Path $singleTrackedDirectory
[IO.File]::WriteAllText($singleTrackedFile, "tracked", (New-Object Text.UTF8Encoding($false)))
& git -C $singleTrackedRepository init -q
& git -C $singleTrackedRepository config user.email "dhe-fixture@example.invalid"
& git -C $singleTrackedRepository config user.name "DHE Fixture"
& git -C $singleTrackedRepository add -- single/source.txt
& git -C $singleTrackedRepository commit -q -m "test: single tracked output"
if ($LASTEXITCODE -ne 0) { throw "Unable to commit the single-file output safety fixture." }
$singleTrackedOutputRejected = $false
try {
    Assert-DheSafeOutputRoot -Path $singleTrackedDirectory
} catch {
    $singleTrackedOutputRejected = $_.Exception.Message -like "DHE output root overlaps Git-tracked source content*"
}
Require $singleTrackedOutputRejected "Output safety accepted a directory containing exactly one tracked file."
Require (Test-Path -LiteralPath $singleTrackedFile -PathType Leaf) "Output safety changed the tracked single-file fixture."

# A custom tracked-source boundary must be structurally valid; an empty JSON
# object must not accidentally turn the Git coverage check into a no-op.
$invalidBoundaryPath = Join-Path $OutputRoot "invalid-source-boundary.json"
$invalidBoundary = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-source-boundary.json"
    exactPaths = @()
    prefixes = @()
}
[IO.File]::WriteAllText($invalidBoundaryPath, ($invalidBoundary | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
$invalidBoundaryRoot = Join-Path $OutputRoot "invalid-boundary-gate"
$invalidBoundaryReportPath = Join-Path $invalidBoundaryRoot "clean-checkout-gate-report.json"
$invalidBoundaryRejected = $false
$invalidBoundaryExit = 0
$fixtureErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/run-dhe-clean-checkout-gate.ps1") `
        -LabRoot $LabRoot -ProjectPath (Join-Path $LabRoot "unity2021-dhe-demo") -OutputRoot $invalidBoundaryRoot `
        -GitRoot $LabRoot -SourceBoundaryPath $invalidBoundaryPath -RequireTrackedSources -ForceOutput 2>&1 | Out-Null
    $invalidBoundaryExit = [int]$LASTEXITCODE
} finally {
    $ErrorActionPreference = $fixtureErrorActionPreference
}
$invalidBoundaryResult = if (Test-Path -LiteralPath $invalidBoundaryReportPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $invalidBoundaryReportPath | ConvertFrom-Json
} else { $null }
$invalidBoundaryRejected = $invalidBoundaryExit -ne 0 -and $null -ne $invalidBoundaryResult -and
    @($invalidBoundaryResult.errors | Where-Object { $_ -like "*invalid schema*" }).Count -gt 0
Require $invalidBoundaryRejected "Clean-checkout accepted an invalid custom source boundary."
Remove-Item -LiteralPath $invalidBoundaryPath -Force
$global:LASTEXITCODE = 0

# A project cannot borrow the clean status of an unrelated repository. This
# must fail even when Git cleanliness itself is only observed, not required.
$foreignProjectRoot = Join-Path ([IO.Path]::GetTempPath()) ("dhe-foreign-project-" + [Guid]::NewGuid().ToString("N"))
$foreignGitGateRoot = Join-Path $OutputRoot "foreign-git-root-gate"
$foreignGitGateReportPath = Join-Path $foreignGitGateRoot "clean-checkout-gate-report.json"
$foreignGitRootRejected = $false
try {
    $foreignSettingsRoot = Join-Path $foreignProjectRoot "ProjectSettings"
    New-Item -ItemType Directory -Force -Path $foreignSettingsRoot | Out-Null
    Copy-Item -LiteralPath (Join-Path $fixtures "dhe-settings-parser.asset") `
        -Destination (Join-Path $foreignSettingsRoot "HybridCLRSettings.asset") -Force
    $fixtureErrorActionPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = "Continue"
        & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/run-dhe-clean-checkout-gate.ps1") `
            -LabRoot $LabRoot -ProjectPath $foreignProjectRoot -OutputRoot $foreignGitGateRoot `
            -GitRoot $LabRoot -ForceOutput 2>&1 | Out-Null
        $foreignGitGateExit = [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $fixtureErrorActionPreference
    }
    $foreignGitGateReport = if (Test-Path -LiteralPath $foreignGitGateReportPath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $foreignGitGateReportPath | ConvertFrom-Json
    } else { $null }
    $foreignGitRootRejected = $foreignGitGateExit -ne 0 -and $null -ne $foreignGitGateReport -and
        @($foreignGitGateReport.errors | Where-Object { $_ -like "*outside the verified Git repository*" }).Count -eq 1
} finally {
    if (Test-Path -LiteralPath $foreignProjectRoot) {
        Remove-Item -LiteralPath $foreignProjectRoot -Recurse -Force
    }
}
Require $foreignGitRootRejected "Clean-checkout accepted a project outside the verified Git repository."
$global:LASTEXITCODE = 0

$separateProjectRoot = Join-Path ([IO.Path]::GetTempPath()) ("dhe-separate-project-" + [Guid]::NewGuid().ToString("N"))
$separateIdentityGateRoot = Join-Path $OutputRoot "separate-git-identities"
$separateGitIdentitiesValidated = $false
try {
    $separateSettingsRoot = Join-Path $separateProjectRoot "ProjectSettings"
    $separateManifestRoot = Join-Path $separateProjectRoot "manifests"
    New-Item -ItemType Directory -Force -Path $separateSettingsRoot,$separateManifestRoot | Out-Null
    Copy-Item -LiteralPath $settingsFixture -Destination (Join-Path $separateSettingsRoot "HybridCLRSettings.asset") -Force
    $separateBoundaryPath = Join-Path $separateManifestRoot "dhe-source-boundary.json"
    $separateBoundary = [ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-source-boundary.json"
        pathBase = "git-root-v1"
        exactPaths = @("manifests/dhe-source-boundary.json", "ProjectSettings/HybridCLRSettings.asset")
        prefixes = @("ProjectSettings/")
        generatedPrefixes = @("artifacts/")
    }
    [IO.File]::WriteAllText($separateBoundaryPath, ($separateBoundary | ConvertTo-Json -Depth 5), (New-Object Text.UTF8Encoding($false)))
    & git -C $separateProjectRoot init -q
    & git -C $separateProjectRoot config user.name "DHE Fixture"
    & git -C $separateProjectRoot config user.email "dhe-fixture@example.invalid"
    & git -C $separateProjectRoot add -- .
    & git -C $separateProjectRoot commit -q -m "fixture"
    Require ($LASTEXITCODE -eq 0) "Unable to commit the separate project Git fixture."

    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/run-dhe-clean-checkout-gate.ps1") `
        -LabRoot $LabRoot -ProjectPath $separateProjectRoot -OutputRoot $separateIdentityGateRoot `
        -GitRoot $separateProjectRoot -SourceBoundaryPath $separateBoundaryPath `
        -ToolGitRoot $LabRoot -ToolSourceBoundaryPath (Join-Path $LabRoot "manifests/dhe-source-boundary.json") `
        -RequireGitClean -RequireTrackedSources -ForceOutput 2>&1 | Out-Null
    $separateIdentityExit = [int]$LASTEXITCODE
    $separateIdentityReportPath = Join-Path $separateIdentityGateRoot "clean-checkout-gate-report.json"
    $separateIdentityReport = if (Test-Path -LiteralPath $separateIdentityReportPath -PathType Leaf) {
        Get-Content -Raw -LiteralPath $separateIdentityReportPath | ConvertFrom-Json
    } else { $null }
    $separateGitIdentitiesValidated = $separateIdentityExit -eq 0 -and $null -ne $separateIdentityReport -and
        $separateIdentityReport.projectGit.passed -is [bool] -and [bool]$separateIdentityReport.projectGit.passed -and
        $separateIdentityReport.toolGit.passed -is [bool] -and [bool]$separateIdentityReport.toolGit.passed -and
        [string]$separateIdentityReport.projectGit.head -match '^[0-9a-f]{40,64}$' -and
        [string]$separateIdentityReport.projectGit.tree -match '^[0-9a-f]{40,64}$' -and
        [string]$separateIdentityReport.toolGit.head -match '^[0-9a-f]{40,64}$' -and
        -not ([string]$separateIdentityReport.projectGit.root).Equals(
            [string]$separateIdentityReport.toolGit.root, [StringComparison]::OrdinalIgnoreCase)
} finally {
    if (Test-Path -LiteralPath $separateProjectRoot) {
        Remove-Item -LiteralPath $separateProjectRoot -Recurse -Force
    }
}
Require $separateGitIdentitiesValidated "Clean-checkout did not preserve separate project/tool Git identities."
$global:LASTEXITCODE = 0

# Strict schema validation must reject duplicate JSON properties before
# ConvertFrom-Json can silently collapse them to the final value.
$schemaNegativeInputRoot = Join-Path $OutputRoot "schema-negative-input"
$schemaNegativeOutputRoot = Join-Path $OutputRoot "schema-negative-gate"
$schemaDuplicatePath = Join-Path $schemaNegativeInputRoot "duplicate-property.json"
New-Item -ItemType Directory -Force -Path $schemaNegativeInputRoot | Out-Null
$schemaDuplicateJson = '{"schemaVersion":1,"format":"hybridclr.dhe-source-boundary.json","format":"hybridclr.dhe-source-boundary.json","exactPaths":["README.md"],"prefixes":["scripts/"],"generatedPrefixes":["artifacts/"]}'
[IO.File]::WriteAllText($schemaDuplicatePath, $schemaDuplicateJson, (New-Object Text.UTF8Encoding($false)))
$schemaNegativeExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-schema-gate.ps1"),
    "-InputRoot", $schemaNegativeInputRoot,
    "-OutputRoot", $schemaNegativeOutputRoot,
    "-ForceOutput")
$schemaNegativeReportPath = Join-Path $schemaNegativeOutputRoot "schema-gate-report.json"
$schemaNegativeReport = if (Test-Path -LiteralPath $schemaNegativeReportPath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $schemaNegativeReportPath | ConvertFrom-Json
} else { $null }
$schemaDuplicatePropertyRejected = $schemaNegativeExit -ne 0 -and
    $null -ne $schemaNegativeReport -and
    @($schemaNegativeReport.errors | Where-Object { $_ -like "*duplicate property*" }).Count -gt 0
Require $schemaDuplicatePropertyRejected "Schema gate accepted a duplicate JSON property."
Remove-Item -LiteralPath $schemaNegativeInputRoot -Recurse -Force
$global:LASTEXITCODE = 0

# Adapter-owned DHE report formats remain fail closed until their schema root
# is supplied explicitly. Registering that root must not modify the installed
# tool or weaken handling for any other unknown DHE format.
$customSchemaRoot = Join-Path $OutputRoot "adapter-schema-root"
$customSchemaInputRoot = Join-Path $OutputRoot "adapter-schema-input"
$customSchemaRejectRoot = Join-Path $OutputRoot "adapter-schema-unregistered"
$customSchemaAcceptRoot = Join-Path $OutputRoot "adapter-schema-registered"
New-Item -ItemType Directory -Force -Path $customSchemaRoot, $customSchemaInputRoot | Out-Null
$customSchemaPath = Join-Path $customSchemaRoot "dhe-adapter-custom.schema.json"
$customDocumentPath = Join-Path $customSchemaInputRoot "adapter-custom.json"
$customSchemaJson = @'
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://example.invalid/hybridclr/dhe-adapter-custom.schema.json",
  "type": "object",
  "additionalProperties": false,
  "required": ["schemaVersion", "format", "passed"],
  "properties": {
    "schemaVersion": { "const": 1 },
    "format": { "const": "hybridclr.dhe-adapter-custom.json" },
    "passed": { "const": true }
  }
}
'@
[IO.File]::WriteAllText($customSchemaPath, $customSchemaJson, (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText($customDocumentPath,
    '{"schemaVersion":1,"format":"hybridclr.dhe-adapter-custom.json","passed":true}',
    (New-Object Text.UTF8Encoding($false)))
$customSchemaRejectedExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-schema-gate.ps1"),
    "-InputRoot", $customSchemaInputRoot,
    "-OutputRoot", $customSchemaRejectRoot,
    "-ForceOutput")
$customSchemaRejectedReport = Get-Content -Raw -LiteralPath (
    Join-Path $customSchemaRejectRoot "schema-gate-report.json") | ConvertFrom-Json
$customSchemaUnregisteredRejected = $customSchemaRejectedExit -ne 0 -and
    @($customSchemaRejectedReport.errors | Where-Object { $_ -like "*No DHE schema is registered*" }).Count -eq 1
Require $customSchemaUnregisteredRejected "Schema gate accepted an unregistered adapter DHE format."

& (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (
    Join-Path $LabRoot "scripts/run-dhe-schema-gate.ps1") `
    -InputRoot $customSchemaInputRoot -AdditionalSchemaRoot $customSchemaRoot `
    -OutputRoot $customSchemaAcceptRoot -ForceOutput | Out-Null
$customSchemaAcceptedExit = [int]$LASTEXITCODE
$customSchemaAcceptedReport = Get-Content -Raw -LiteralPath (
    Join-Path $customSchemaAcceptRoot "schema-gate-report.json") | ConvertFrom-Json
$additionalSchemaRegistered = $customSchemaAcceptedExit -eq 0 -and
    [bool]$customSchemaAcceptedReport.passed -and
    @($customSchemaAcceptedReport.schemaRoots | Where-Object {
            ([IO.Path]::GetFullPath([string]$_)).Equals(
                [IO.Path]::GetFullPath($customSchemaRoot), [StringComparison]::OrdinalIgnoreCase)
        }).Count -eq 1 -and
    @($customSchemaAcceptedReport.documents | Where-Object {
            ([IO.Path]::GetFullPath([string]$_.path)).Equals(
                [IO.Path]::GetFullPath($customDocumentPath), [StringComparison]::OrdinalIgnoreCase) -and
            [bool]$_.passed
        }).Count -eq 1
Require $additionalSchemaRegistered "Schema gate did not validate an explicitly registered adapter schema."
Remove-Item -LiteralPath $customSchemaRoot, $customSchemaInputRoot -Recurse -Force
$global:LASTEXITCODE = 0

# Gate reports are evidence, so no gate may overwrite one of its input reports
# even when the caller explicitly chooses a custom output path.
$overwriteProbePath = Join-Path $OutputRoot "overwrite-protection.json"
[IO.File]::WriteAllText($overwriteProbePath, "{`"sentinel`":true}", (New-Object Text.UTF8Encoding($false)))
$overwriteProbeHash = (Get-FileHash -LiteralPath $overwriteProbePath -Algorithm SHA256).Hash
$releaseOverwriteExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-release-gate.ps1"),
    "-ProjectPlanValidation", $overwriteProbePath,
    "-WorkflowReport", $overwriteProbePath,
    "-Target", "StandaloneWindows64",
    "-Output", $overwriteProbePath)
Require ($releaseOverwriteExit -ne 0 -and
    (Get-FileHash -LiteralPath $overwriteProbePath -Algorithm SHA256).Hash -eq $overwriteProbeHash) `
    "Release gate allowed output to overwrite an input report."
$planOverwriteExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/validate-dhe-project-plan.ps1"),
    "-Plan", $overwriteProbePath,
    "-Output", $overwriteProbePath)
Require ($planOverwriteExit -ne 0 -and
    (Get-FileHash -LiteralPath $overwriteProbePath -Algorithm SHA256).Hash -eq $overwriteProbeHash) `
    "Project plan validator allowed output to overwrite the input plan."
$archiveOverwriteRoot = Join-Path $OutputRoot "archive-overwrite-input"
$archiveOverwritePath = Join-Path $archiveOverwriteRoot "archive-manifest.json"
New-Item -ItemType Directory -Force -Path $archiveOverwriteRoot | Out-Null
$archiveOverwriteExit = Invoke-ExpectedFailure @(
    (Join-Path $LabRoot "scripts/run-dhe-archive-gate.ps1"),
    "-InputRoot", $OutputRoot,
    "-ArchiveRoot", $archiveOverwriteRoot,
    "-Output", $archiveOverwritePath)
$archiveOverwriteReport = if (Test-Path -LiteralPath $archiveOverwritePath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $archiveOverwritePath | ConvertFrom-Json
} else { $null }
Require ($archiveOverwriteExit -ne 0 -and $null -ne $archiveOverwriteReport -and
    $archiveOverwriteReport.passed -is [bool] -and -not [bool]$archiveOverwriteReport.passed -and
    @($archiveOverwriteReport.errors | Where-Object { $_ -like "*overlaps protected input*" }).Count -eq 1) `
    "Archive gate did not emit a machine-readable report for an output overlap."
$global:LASTEXITCODE = 0

$shapeInput = Join-Path $fixtures "dhe-shapes-input.cpp"
$shapeManifest = Join-Path $fixtures "dhe-shapes-manifest.json"
$shapeOutput = Join-Path $OutputRoot "shapes.guard.cpp"
$shapeSecondOutput = Join-Path $OutputRoot "shapes.guard.second.cpp"
$shapeReport = Join-Path $OutputRoot "shapes.report.json"
$shapeSecondReport = Join-Path $OutputRoot "shapes.second.report.json"
& (Join-Path $LabRoot "scripts/inject-dhe-guard.ps1") `
    -InputFile $shapeInput -OutputFile $shapeOutput -ManifestFile $shapeManifest -ReportFile $shapeReport
if ($LASTEXITCODE -ne 0) { throw "Shape guard fixture failed." }
& (Join-Path $LabRoot "scripts/inject-dhe-guard.ps1") `
    -InputFile $shapeOutput -OutputFile $shapeSecondOutput -ManifestFile $shapeManifest -ReportFile $shapeSecondReport
if ($LASTEXITCODE -ne 0) { throw "Shape guard idempotence fixture failed." }
$shapeResult = Get-Content -Raw -LiteralPath $shapeReport | ConvertFrom-Json
$shapeSecondResult = Get-Content -Raw -LiteralPath $shapeSecondReport | ConvertFrom-Json
Require ($shapeResult.requestedMethodCount -eq 4 -and $shapeResult.transformedMethodCount -eq 4) `
    "Shape fixture did not transform all four supported methods."
Require ($shapeSecondResult.requestedMethodCount -eq 4 -and $shapeSecondResult.transformedMethodCount -eq 0) `
    "Guard transformation is not idempotent."

$abiManifest = Join-Path $OutputRoot "abi.native-manifest.json"
& (Join-Path $LabRoot "scripts/resolve-dhe-native-manifest.ps1") `
    -MvJson (Join-Path $fixtures "dhe-abi-mv.json") `
    -GeneratedCppRoot $fixtures `
    -OutputManifest $abiManifest
if ($LASTEXITCODE -ne 0) { throw "ABI resolver fixture failed." }
$abiResult = Get-Content -Raw -LiteralPath $abiManifest | ConvertFrom-Json
Require ($abiResult.changedMethodCount -eq 5 -and
    $abiResult.supportedChangedMethodCount -eq 2 -and
    $abiResult.unsupportedChangedMethodCount -eq 3) `
    "ABI resolver fixture did not classify the expected 2/5 supported shapes."
$abiReasons = @($abiResult.unsupportedChangedMethods | ForEach-Object { @($_.reasons) })
Require (@($abiReasons | Where-Object { $_ -like "unsupported-native-shape:*" }).Count -eq 2 -and
    @($abiReasons | Where-Object { $_ -like "value-type-receiver*" }).Count -eq 1) `
    "ABI resolver fixture did not record explicit unsupported shape reasons."

$genericManifest = Join-Path $OutputRoot "generic.native-manifest.json"
& (Join-Path $LabRoot "scripts/resolve-dhe-native-manifest.ps1") `
    -MvJson (Join-Path $fixtures "dhe-generic-mv.json") `
    -GeneratedCppRoot $fixtures `
    -OutputManifest $genericManifest
if ($LASTEXITCODE -ne 0) { throw "Generic ABI resolver fixture failed." }
$genericResult = Get-Content -Raw -LiteralPath $genericManifest | ConvertFrom-Json
$genericConstrainedEntries = @($genericResult.methods | Where-Object { [uint32]$_.methodToken -eq 100663501 })
$genericSelectEntries = @($genericResult.methods | Where-Object { [uint32]$_.methodToken -eq 100663502 })
$genericVirtualEntries = @($genericResult.methods | Where-Object { [uint32]$_.methodToken -eq 100663503 })
Require ($genericResult.changedMethodCount -eq 3 -and
    $genericResult.supportedChangedMethodCount -eq 3 -and
    $genericResult.unsupportedChangedMethodCount -eq 0 -and
    $genericResult.nativeEntryCount -eq 10 -and
    $genericConstrainedEntries.Count -eq 3 -and
    $genericSelectEntries.Count -eq 5 -and
    $genericVirtualEntries.Count -eq 2) `
    "Generic ABI resolver did not retain all native entries for three managed tokens."
$genericOutput = Join-Path $OutputRoot "generic.guard.cpp"
$genericGuardReport = Join-Path $OutputRoot "generic.guard-report.json"
& (Join-Path $LabRoot "scripts/inject-dhe-guard.ps1") `
    -InputFile (Join-Path $fixtures "dhe-generic-input.cpp") `
    -OutputFile $genericOutput `
    -ManifestFile $genericManifest `
    -ReportFile $genericGuardReport
if ($LASTEXITCODE -ne 0) { throw "Generic ABI guard fixture failed." }
$genericGuardResult = Get-Content -Raw -LiteralPath $genericGuardReport | ConvertFrom-Json
$genericGuardText = [IO.File]::ReadAllText($genericOutput)
$genericCoverage = New-DheNativeGuardCoverage `
    -NativeManifest $genericResult `
    -ChangedMethodCount ([int]$genericResult.changedMethodCount)
Require ($genericGuardResult.requestedMethodCount -eq 10 -and
    $genericGuardResult.transformedMethodCount -eq 10 -and
    $genericGuardText.Contains("ExecuteInterpreterInvokeArgs") -and
    $genericGuardText.Contains("reinterpret_cast<void*>(il2cppRetVal)") -and
    @([regex]::Matches($genericGuardText, "HYBRIDCLR_DHE_GUARD_V4:")).Count -eq 10) `
    "Generic ABI guard fixture did not emit ten invoke-args bridges."
Require ($genericCoverage.complete -and
    $genericCoverage.guardedMethodCount -eq 3 -and
    $genericCoverage.nativeEntryCount -eq 10) `
    "Generic ABI coverage conflated managed guards with native entries."

# Unsupported entries are part of the coverage proof, not just a diagnostic
# count. Tampering one token must therefore fail the standalone validator even
# when the supported/unsupported counts still add up to the MV changed count.
$coverageMismatchManifest = Join-Path $OutputRoot "coverage-mismatch.native-manifest.json"
$coverageMismatch = $abiResult | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$coverageMismatch.unsupportedChangedMethods[0].methodToken = 999999
[IO.File]::WriteAllText(
    $coverageMismatchManifest,
    ($coverageMismatch | ConvertTo-Json -Depth 12),
    (New-Object Text.UTF8Encoding($false)))
$coverageMismatchReport = Join-Path $OutputRoot "coverage-mismatch.validation.json"
$coverageMismatchExit = 0
# This invocation is an intentional negative test. Capture the child error in
# its JSON report so a passing fixture gate does not emit a misleading red
# error record to CI stderr. Windows PowerShell 5.1 promotes redirected
# native/error stream records to terminating errors under `$ErrorActionPreference
# = Stop`, so temporarily use Continue only around this expected failure.
$fixtureErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson (Join-Path $fixtures "dhe-abi-mv.json") `
        -NativeManifest $coverageMismatchManifest `
        -Output $coverageMismatchReport 2>&1 | Out-Null
} finally {
    $ErrorActionPreference = $fixtureErrorActionPreference
}
$coverageMismatchExit = $LASTEXITCODE
$coverageMismatchResult = if (Test-Path -LiteralPath $coverageMismatchReport -PathType Leaf) {
    Get-Content -Raw -LiteralPath $coverageMismatchReport | ConvertFrom-Json
} else { $null }
$coverageMismatchRejected = $coverageMismatchExit -ne 0 -and $null -ne $coverageMismatchResult -and
    @($coverageMismatchResult.errors | Where-Object { $_ -like "*supported+unsupported methodToken set*" }).Count -gt 0
Require $coverageMismatchRejected "Artifact validator accepted a mismatched unsupported method token."
# The negative validator intentionally exits non-zero; clear the process-wide
# status before continuing with the next successful fixture invocation.
$global:LASTEXITCODE = 0

# A malformed native token must produce a normal validation report. It must
# not escape through Group-Object conversion as an unhandled PowerShell error.
$malformedTokenManifest = Join-Path $OutputRoot "malformed-token.native-manifest.json"
$malformedToken = $genericResult | ConvertTo-Json -Depth 12 | ConvertFrom-Json
$malformedToken.methods[0].methodToken = -1
[IO.File]::WriteAllText(
    $malformedTokenManifest,
    ($malformedToken | ConvertTo-Json -Depth 12),
    (New-Object Text.UTF8Encoding($false)))
$malformedTokenReport = Join-Path $OutputRoot "malformed-token.validation.json"
$fixtureErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson (Join-Path $fixtures "dhe-generic-mv.json") `
        -NativeManifest $malformedTokenManifest `
        -Output $malformedTokenReport 2>&1 | Out-Null
} finally {
    $ErrorActionPreference = $fixtureErrorActionPreference
}
$malformedTokenExit = $LASTEXITCODE
$malformedTokenResult = if (Test-Path -LiteralPath $malformedTokenReport -PathType Leaf) {
    Get-Content -Raw -LiteralPath $malformedTokenReport | ConvertFrom-Json
} else { $null }
$malformedNativeTokenRejected = $malformedTokenExit -ne 0 -and
    $null -ne $malformedTokenResult -and
    @($malformedTokenResult.errors | Where-Object { $_ -like "*invalid methodToken*" }).Count -gt 0
Require $malformedNativeTokenRejected "Artifact validator did not report a malformed native token cleanly."
Remove-Item -LiteralPath $malformedTokenManifest -Force
$global:LASTEXITCODE = 0

$valueTypeMethod = @($abiResult.methods | Where-Object { $_.declaringTypeIsValueType -and $_.hasThis })[0]
Require ($null -ne $valueTypeMethod) "ABI resolver fixture did not produce a supported value-type receiver method."
$valueTypeManifest = Join-Path $OutputRoot "value-type.native-manifest.json"
[IO.File]::WriteAllText($valueTypeManifest, ([ordered]@{
        schemaVersion = 1
        methods = @($valueTypeMethod)
    } | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
$valueTypeOutput = Join-Path $OutputRoot "value-type.guard.cpp"
& (Join-Path $LabRoot "scripts/inject-dhe-guard.ps1") `
    -InputFile (Join-Path $fixtures "dhe-abi-input.cpp") `
    -OutputFile $valueTypeOutput `
    -ManifestFile $valueTypeManifest
if ($LASTEXITCODE -ne 0) { throw "Value-type guard fixture failed." }
$valueTypeText = [IO.File]::ReadAllText($valueTypeOutput)
Require ($valueTypeText.Contains("ExecuteInterpreterValueTypeInstanceVoidNoArgs") -and
    $valueTypeText.Contains("HYBRIDCLR_DHE_GUARD_V4:ShapeValueReceiverNoArgs:100663304")) `
    "Value-type guard fixture did not select the pointer receiver helper."

$multiCppRoot = Join-Path $OutputRoot "multi-cpp"
New-Item -ItemType Directory -Force -Path $multiCppRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $fixtures "dhe-abi-input.cpp") -Destination $multiCppRoot -Force
Copy-Item -LiteralPath (Join-Path $fixtures "dhe-multi-second-input.cpp") -Destination $multiCppRoot -Force
$hashForward = Get-DheFileSetHash @(
    (Join-Path $multiCppRoot "dhe-abi-input.cpp"),
    (Join-Path $multiCppRoot "dhe-multi-second-input.cpp")
) $multiCppRoot
$hashReverse = Get-DheFileSetHash @(
    (Join-Path $multiCppRoot "dhe-multi-second-input.cpp"),
    (Join-Path $multiCppRoot "dhe-abi-input.cpp")
) $multiCppRoot
Require ($hashForward -eq $hashReverse) "Multi-file DHE hash is order-dependent."
$hashMutationRoot = Join-Path $OutputRoot "multi-cpp-mutated"
Copy-Item -LiteralPath $multiCppRoot -Destination $hashMutationRoot -Recurse -Force
[IO.File]::AppendAllText((Join-Path $hashMutationRoot "dhe-multi-second-input.cpp"), "`r`n// mutation", (New-Object Text.UTF8Encoding($false)))
$hashMutated = Get-DheFileSetHash @(
    (Join-Path $hashMutationRoot "dhe-abi-input.cpp"),
    (Join-Path $hashMutationRoot "dhe-multi-second-input.cpp")
) $hashMutationRoot
Require ($hashForward -ne $hashMutated) "Multi-file DHE hash did not change after source mutation."
$multiManifest = Join-Path $OutputRoot "multi.native-manifest.json"
& (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
    -MvJson @(
        (Join-Path $fixtures "dhe-multi-first-mv.json"),
        (Join-Path $fixtures "dhe-multi-second-mv.json")) `
    -GeneratedCppRoot $multiCppRoot `
    -ManifestFile $multiManifest
if ($LASTEXITCODE -ne 0) { throw "Multi-assembly guard fixture failed." }
$multiResult = Get-Content -Raw -LiteralPath $multiManifest | ConvertFrom-Json
$multiPatchedRoot = Join-Path $OutputRoot "generated-cpp-patched"
$multiPatchedFiles = @(Get-ChildItem -LiteralPath $multiPatchedRoot -Recurse -File -Filter *.cpp)
Require ($multiResult.changedMethodCount -eq 4 -and
    $multiResult.supportedChangedMethodCount -eq 2 -and
    $multiResult.unsupportedChangedMethodCount -eq 2 -and
    $multiResult.methods.Count -eq 2 -and
    $multiPatchedFiles.Count -eq 2) `
    "Multi-assembly guard fixture did not produce the expected aggregate manifest."
$multiPatchedText = (@($multiPatchedFiles | ForEach-Object { [IO.File]::ReadAllText($_.FullName) }) -join "`n")
Require ($multiPatchedText.Contains("HYBRIDCLR_DHE_GUARD_V4:ShapeRefOut:100663301") -and
    $multiPatchedText.Contains("HYBRIDCLR_DHE_GUARD_V4:OtherOnly:100663401")) `
    "Multi-assembly guard fixture did not transform both assembly entries."

$inPlaceRoot = Join-Path $OutputRoot "multi-cpp-in-place"
Copy-Item -LiteralPath $multiCppRoot -Destination $inPlaceRoot -Recurse -Force
$inPlaceManifest = Join-Path $OutputRoot "multi-in-place.native-manifest.json"
& (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
    -MvJson @(
        (Join-Path $fixtures "dhe-multi-first-mv.json"),
        (Join-Path $fixtures "dhe-multi-second-mv.json")) `
    -GeneratedCppRoot $inPlaceRoot `
    -ManifestFile $inPlaceManifest `
    -InPlace
if ($LASTEXITCODE -ne 0) { throw "InPlace guard fixture failed." }
$inPlaceText = (@(Get-ChildItem -LiteralPath $inPlaceRoot -File -Filter *.cpp | ForEach-Object {
        [IO.File]::ReadAllText($_.FullName)
    }) -join "`n")
Require ($inPlaceText.Contains("HYBRIDCLR_DHE_GUARD_V4:ShapeRefOut:100663301") -and
    $inPlaceText.Contains("HYBRIDCLR_DHE_GUARD_V4:OtherOnly:100663401")) `
    "InPlace guard fixture did not commit all transformed files."

# Resolve a manifest against an untouched tree, then invalidate the second
# source before the commit phase. A failure after the first staged transform
# must leave both original files byte-for-byte unchanged.
$rollbackRoot = Join-Path $OutputRoot "multi-cpp-rollback"
Copy-Item -LiteralPath $multiCppRoot -Destination $rollbackRoot -Recurse -Force
$rollbackManifest = Join-Path $OutputRoot "multi-rollback.native-manifest.json"
& (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
    -MvJson @(
        (Join-Path $fixtures "dhe-multi-first-mv.json"),
        (Join-Path $fixtures "dhe-multi-second-mv.json")) `
    -GeneratedCppRoot $rollbackRoot `
    -ManifestFile $rollbackManifest
if ($LASTEXITCODE -ne 0) { throw "Rollback fixture manifest preparation failed." }
$rollbackFirst = Join-Path $rollbackRoot "dhe-abi-input.cpp"
$rollbackSecond = Join-Path $rollbackRoot "dhe-multi-second-input.cpp"
$rollbackFirstHash = (Get-FileHash -LiteralPath $rollbackFirst -Algorithm SHA256).Hash
$rollbackSecondText = [IO.File]::ReadAllText($rollbackSecond)
$rollbackSecondText = $rollbackSecondText -replace ', const RuntimeMethod\* method', ''
[IO.File]::WriteAllText($rollbackSecond, $rollbackSecondText, (New-Object Text.UTF8Encoding($false)))
$rollbackSecondHash = (Get-FileHash -LiteralPath $rollbackSecond -Algorithm SHA256).Hash
$rollbackRejected = $false
try {
    & (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
        -MvJson @(
            (Join-Path $fixtures "dhe-multi-first-mv.json"),
            (Join-Path $fixtures "dhe-multi-second-mv.json")) `
        -GeneratedCppRoot $rollbackRoot `
        -ResolvedManifestFile $rollbackManifest `
        -InPlace
}
catch {
    $rollbackRejected = $true
}
Require $rollbackRejected "Transactional guard fixture unexpectedly accepted an invalid second source."
Require ((Get-FileHash -LiteralPath $rollbackFirst -Algorithm SHA256).Hash -eq $rollbackFirstHash) `
    "Transactional guard fixture changed the first source before a later failure."
Require ((Get-FileHash -LiteralPath $rollbackSecond -Algorithm SHA256).Hash -eq $rollbackSecondHash) `
    "Transactional guard fixture changed the failing source."
Require (-not [IO.File]::ReadAllText($rollbackFirst).Contains("HYBRIDCLR_DHE_GUARD_V4:")) `
    "Transactional guard fixture left a guard marker after rollback."

# AOT probes are optional diagnostics but they edit generated C++ after guard
# commit. A failure in a later probe must restore both the probe and the guard
# edits, otherwise a failed diagnostic build can poison the next Player build.
$probeRollbackRoot = Join-Path $OutputRoot "probe-rollback"
New-Item -ItemType Directory -Force -Path $probeRollbackRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $fixtures "dhe-multi-second-input.cpp") `
    -Destination $probeRollbackRoot -Force
$probeRollbackMv = Join-Path $fixtures "dhe-multi-second-mv.json"
$probeRollbackManifest = Join-Path $OutputRoot "probe-rollback.native-manifest.json"
& (Join-Path $LabRoot "scripts/resolve-dhe-native-manifest.ps1") `
    -MvJson $probeRollbackMv `
    -GeneratedCppRoot $probeRollbackRoot `
    -OutputManifest $probeRollbackManifest
if ($LASTEXITCODE -ne 0) { throw "Probe rollback manifest preparation failed." }
$probeRollbackSource = Join-Path $probeRollbackRoot "dhe-multi-second-input.cpp"
$probeRollbackHash = (Get-FileHash -LiteralPath $probeRollbackSource -Algorithm SHA256).Hash
$probeRollbackRejected = $false
try {
    & (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
        -MvJson $probeRollbackMv `
        -GeneratedCppRoot $probeRollbackRoot `
        -ResolvedManifestFile $probeRollbackManifest `
        -InPlace `
        -AotProbeDeclaringType "Other.Shapes" `
        -AotProbeMethodName "Only,Missing"
}
catch {
    $probeRollbackRejected = $true
}
Require $probeRollbackRejected "AOT probe transaction fixture unexpectedly accepted a missing probe method."
Require ((Get-FileHash -LiteralPath $probeRollbackSource -Algorithm SHA256).Hash -eq $probeRollbackHash) `
    "AOT probe transaction fixture changed generated C++ after rollback."
Require (-not [IO.File]::ReadAllText($probeRollbackSource).Contains("HYBRIDCLR_DHE_GUARD_V4:")) `
    "AOT probe transaction fixture left a guard marker after rollback."
Require (-not [IO.File]::ReadAllText($probeRollbackSource).Contains("HYBRIDCLR_DHE_AOT_PROBE:")) `
    "AOT probe transaction fixture left a probe marker after rollback."

$noopMv = Join-Path $OutputRoot "noop.mv.json"
$noopManifest = Join-Path $OutputRoot "noop.native-manifest.json"
$noopHash = (Get-FileHash -LiteralPath $shapeInput -Algorithm SHA256).Hash.ToLowerInvariant()
$noopDocument = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-lite.mv.json"
    assemblyName = "Noop.Assembly"
    baseline = [ordered]@{ path = $shapeInput; sha256 = $noopHash; mvid = "fixture"; assemblyRefs = @() }
    current = [ordered]@{ path = $shapeInput; sha256 = $noopHash; mvid = "fixture"; assemblyRefs = @() }
    typeChanges = @()
    compatibility = [ordered]@{ status = "compatible"; mode = "method-body-only"; reasons = @() }
    summary = [ordered]@{
        methodCount = 0
        changedMethodCount = 0
        unchangedMethodCount = 0
        typeChangeCount = 0
        compatibleMethodOnlyChange = $true
    }
    methods = @()
}
[IO.File]::WriteAllText($noopMv, ($noopDocument | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
& (Join-Path $LabRoot "scripts/apply-dhe-generated-cpp.ps1") `
    -MvJson $noopMv `
    -GeneratedCppRoot $fixtures `
    -ManifestFile $noopManifest
if ($LASTEXITCODE -ne 0) { throw "No-op guard fixture failed." }
$noopResult = Get-Content -Raw -LiteralPath $noopManifest | ConvertFrom-Json
Require ($noopResult.changedMethodCount -eq 0 -and
    $noopResult.supportedChangedMethodCount -eq 0 -and
    $noopResult.unsupportedChangedMethodCount -eq 0 -and
    @($noopResult.methods).Count -eq 0) `
    "No-op guard fixture did not produce an empty native manifest."
Require ((Get-DheFileSetHashOrEmpty @() $fixtures) -eq
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") `
    "No-op guard fixture did not produce the canonical empty source-set hash."
Require ((Get-DheFileSetHashOrEmpty $null $fixtures) -eq
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855") `
    "Null no-op guard input did not normalize to the canonical empty source-set hash."
Require (@(Get-DheNativeManifestSourcePaths ([pscustomobject]@{ methods = @() }) $fixtures).Count -eq 0) `
    "Empty native manifest did not produce an empty generated-C++ source set."

# The validator and native parser must agree on the closed MV flag set.
$unknownFlagsBinary = Join-Path $OutputRoot "Noop.Assembly.mv.bytes"
$unknownFlagsBytes = New-Object 'System.Collections.Generic.List[byte]'
[void]$unknownFlagsBytes.AddRange([Text.Encoding]::ASCII.GetBytes("DHEMVLT1"))
foreach ($value in @([uint32]1, [uint32]$noopDocument.assemblyName.Length, [uint32]0, [uint32]2)) {
    [void]$unknownFlagsBytes.AddRange([BitConverter]::GetBytes($value))
}
for ($hashCopy = 0; $hashCopy -lt 2; $hashCopy++) {
    for ($hexOffset = 0; $hexOffset -lt 64; $hexOffset += 2) {
        [void]$unknownFlagsBytes.Add([Convert]::ToByte($noopHash.Substring($hexOffset, 2), 16))
    }
}
[void]$unknownFlagsBytes.AddRange([Text.Encoding]::UTF8.GetBytes($noopDocument.assemblyName))
[IO.File]::WriteAllBytes($unknownFlagsBinary, $unknownFlagsBytes.ToArray())
$unknownFlagsBaseline = Join-Path $OutputRoot "Noop.Assembly.dll"
Copy-Item -LiteralPath $shapeInput -Destination $unknownFlagsBaseline -Force
$unknownFlagsReport = Join-Path $OutputRoot "unknown-flags.validation.json"
$unknownFlagsRejected = $false
$fixtureErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson $noopMv -MvBytes $unknownFlagsBinary -BaselineAssembly $unknownFlagsBaseline -CurrentAssembly $unknownFlagsBaseline `
        -Output $unknownFlagsReport 2>&1 | Out-Null
} finally {
    $ErrorActionPreference = $fixtureErrorActionPreference
}
$unknownFlagsResult = if (Test-Path -LiteralPath $unknownFlagsReport -PathType Leaf) {
    Get-Content -Raw -LiteralPath $unknownFlagsReport | ConvertFrom-Json
} else { $null }
$unknownFlagsRejected = $LASTEXITCODE -ne 0 -and $null -ne $unknownFlagsResult -and
    @($unknownFlagsResult.errors | Where-Object { $_ -like "*unknown flags*" }).Count -gt 0
Require $unknownFlagsRejected "Artifact validator accepted an MV binary with unknown flags."
$global:LASTEXITCODE = 0

# The artifact validator must fail closed when two inputs share an assembly
# basename; silently keeping the last path could bind an MV to the wrong DLL.
$duplicateMappingReport = Join-Path $OutputRoot "duplicate-mapping.validation.json"
$duplicateMappingRejected = $false
try {
    & (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson $noopMv `
        -BaselineAssembly @($shapeInput, $shapeInput) `
        -CurrentAssembly $shapeInput `
        -Output $duplicateMappingReport
}
catch {
    $duplicateMappingRejected = $true
}
$duplicateMappingResult = if (Test-Path -LiteralPath $duplicateMappingReport -PathType Leaf) {
    Get-Content -Raw -LiteralPath $duplicateMappingReport | ConvertFrom-Json
} else { $null }
Require ($duplicateMappingRejected -and $null -ne $duplicateMappingResult -and
    @($duplicateMappingResult.errors | Where-Object { $_ -like "*duplicate assembly basename*" }).Count -gt 0) `
    "Artifact validator accepted duplicate assembly basename inputs."

# The validator output itself is also a user-supplied path. Refuse an output
# path that aliases the MV input instead of overwriting the evidence.
$collisionOutputHash = (Get-FileHash -LiteralPath $noopMv -Algorithm SHA256).Hash
$outputCollisionRejected = $false
try {
    & (Join-Path $LabRoot "scripts/validate-dhe-artifacts.ps1") `
        -MvJson $noopMv `
        -Output $noopMv
}
catch {
    $outputCollisionRejected = $true
}
Require $outputCollisionRejected "Artifact validator accepted an output path that aliases an MV input."
Require ((Get-FileHash -LiteralPath $noopMv -Algorithm SHA256).Hash -eq $collisionOutputHash) `
    "Artifact validator changed an MV input while rejecting an output collision."

# Complete coverage does not promote an Exploratory run to Release evidence.
# Keep independent validator outputs beside the requested gate output so a
# release check cannot mutate its workflow input directory.
$releaseNegativeRoot = Join-Path $OutputRoot "release-mode-negative"
$releaseInputRoot = Join-Path $releaseNegativeRoot "input"
$releaseOutputRoot = Join-Path $releaseNegativeRoot "output"
New-Item -ItemType Directory -Force -Path $releaseInputRoot, $releaseOutputRoot | Out-Null
$releasePlanValidationPath = Join-Path $releaseInputRoot "project-plan-validation.json"
$releaseWorkflowPath = Join-Path $releaseInputRoot "workflow-report.json"
$releaseGatePath = Join-Path $releaseOutputRoot "release-gate.json"
[IO.File]::WriteAllText($releasePlanValidationPath, ([ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-project-plan-validation.json"
        passed = $true
        coverageRequired = $true
        coverageComplete = $true
        plan = "missing/project-plan.json"
        assemblies = @()
        errors = @()
        warnings = @()
    } | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
[IO.File]::WriteAllText($releaseWorkflowPath, ([ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-fixture-workflow.json"
        passed = $true
        validationPassed = $true
        target = "StandaloneWindows64"
        mode = "Exploratory"
        coverageRequired = $true
        coverageGatePassed = $true
        releaseReady = $true
        artifactValidationPassed = $true
        buildIdentityReady = $true
        identityVersion = 2
        aotSnapshotKind = "managed-assembly-plus-generated-cpp-v1"
        nativeGuardSourceSha256 = ("0" * 64)
        nativeManifestSha256 = ("1" * 64)
        pathSemantics = "workspace-absolute-v1"
        sourcePreflight = "missing/source-preflight.json"
        cleanCheckoutGate = "missing/clean-checkout.json"
        projectPlan = "missing/project-plan.json"
        projectPlanValidation = "missing/project-plan-validation.json"
        batchReport = "missing/batch-report.json"
        transaction = [ordered]@{ status = "validated"; retryValidated = $true; retryAssemblyName = "Fixture"; retryFailure = "DHE_MV_REGISTRATION_FAILED" }
        player = [ordered]@{ passed = $true; loadError = "OK"; multiAssemblyValidated = $true }
        nativeGuardCoverage = [ordered]@{
            manifestAvailable = $true
            changedMethodCount = 0
            supportedChangedMethodCount = 0
            unsupportedChangedMethodCount = 0
            nativeEntryCount = 0
            guardedMethodCount = 0
            complete = $true
        }
        assemblyScope = [ordered]@{
            strategy = "fixture"
            aotAssemblies = @()
            loadedDheAssemblies = @()
            stagedDependencies = @()
            stagedDependenciesLoadedAsDhe = $true
        }
        capability = [ordered]@{ methodCount = 0; changedMethodCount = 0; typeChangeCount = 0; compatibility = "compatible" }
        mvJson = @()
        mvBytes = @()
        nativeManifest = "missing/native-manifest.json"
        buildIdentity = "missing/build-identity.json"
        runtimePlan = "missing/runtime-plan.json"
        runtimePlanProjectPath = "missing/runtime-plan.json"
    } | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
$releaseNegativeErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = "Continue"
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $LabRoot "scripts/run-dhe-release-gate.ps1") `
        -ProjectPlanValidation $releasePlanValidationPath `
        -WorkflowReport $releaseWorkflowPath `
        -Output $releaseGatePath 2>&1 | Out-Null
    $releaseNegativeExit = [int]$LASTEXITCODE
} finally {
    $ErrorActionPreference = $releaseNegativeErrorActionPreference
}
$releaseNegativeReport = if (Test-Path -LiteralPath $releaseGatePath -PathType Leaf) {
    Get-Content -Raw -LiteralPath $releaseGatePath | ConvertFrom-Json
} else { $null }
$exploratoryReleaseRejected = $releaseNegativeExit -ne 0 -and
    $null -ne $releaseNegativeReport -and
    -not [bool]$releaseNegativeReport.passed -and
    @($releaseNegativeReport.errors | Where-Object { $_ -eq "Workflow report mode must be Release." }).Count -eq 1
$releaseDerivedOutputsIsolated = $null -ne $releaseNegativeReport -and
    [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$releaseNegativeReport.projectPlanRevalidation)).Equals(
        [IO.Path]::GetFullPath($releaseOutputRoot), [StringComparison]::OrdinalIgnoreCase) -and
    [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath([string]$releaseNegativeReport.artifactValidation)).Equals(
        [IO.Path]::GetFullPath($releaseOutputRoot), [StringComparison]::OrdinalIgnoreCase) -and
    @(Get-ChildItem -LiteralPath $releaseInputRoot -File).Count -eq 2
Require $exploratoryReleaseRejected "Release gate accepted an Exploratory workflow report."
Require $releaseDerivedOutputsIsolated "Release gate wrote derived validation evidence into its input directory."
$global:LASTEXITCODE = 0

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-script-fixture-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $true
    guardRequested = $shapeResult.requestedMethodCount
    guardTransformed = $shapeResult.transformedMethodCount
    guardSecondPassTransformed = $shapeSecondResult.transformedMethodCount
    abiChangedMethodCount = $abiResult.changedMethodCount
    abiSupportedChangedMethodCount = $abiResult.supportedChangedMethodCount
    abiUnsupportedChangedMethodCount = $abiResult.unsupportedChangedMethodCount
    abiManifest = $abiManifest
    genericChangedMethodCount = $genericResult.changedMethodCount
    genericSupportedChangedMethodCount = $genericResult.supportedChangedMethodCount
    genericGuardedMethodCount = $genericCoverage.guardedMethodCount
    genericNativeEntryCount = $genericResult.nativeEntryCount
    genericManifest = $genericManifest
    multiAssemblyChangedMethodCount = $multiResult.changedMethodCount
    multiAssemblySupportedChangedMethodCount = $multiResult.supportedChangedMethodCount
    multiAssemblyUnsupportedChangedMethodCount = $multiResult.unsupportedChangedMethodCount
    multiAssemblyManifest = $multiManifest
    multiFileHashForward = $hashForward
    multiFileHashReverse = $hashReverse
    multiFileHashMutated = $hashMutated
    inPlaceCommitPassed = $true
    transactionRollbackPassed = $rollbackRejected
    probeTransactionRollbackPassed = $probeRollbackRejected
    noOpPassed = $true
    unknownFlagsRejected = $unknownFlagsRejected
    coverageTokenSetRejected = $coverageMismatchRejected
    malformedNativeTokenRejected = $malformedNativeTokenRejected
    duplicateMappingRejected = $duplicateMappingRejected
    outputCollisionRejected = $outputCollisionRejected
    exploratoryReleaseRejected = $exploratoryReleaseRejected
    releaseDerivedOutputsIsolated = $releaseDerivedOutputsIsolated
    unsafeSourceOutputRejected = $unsafeSourceOutputRejected
    filesystemRootPreserved = $filesystemRootPreserved
    filesystemRootRejected = $filesystemRootRejected
    singleTrackedOutputRejected = $singleTrackedOutputRejected
    invalidBoundaryRejected = $invalidBoundaryRejected
    foreignGitRootRejected = $foreignGitRootRejected
    separateGitIdentitiesValidated = $separateGitIdentitiesValidated
    gitApplyRootIsolated = $gitApplyRootIsolated
    prePrepareGateTested = $prePrepareGateTested
    invalidRuntimeRejectedBeforeAdapter = $invalidRuntimeRejectedBeforeAdapter
    adapterTargetParameterValidated = $adapterTargetParameterValidated
    explicitProjectVcsValidated = $explicitProjectVcsValidated
    malformedWorkflowPackageLockReported = $malformedWorkflowPackageLockReported
    adapterContractMismatchRejected = $adapterContractMismatchRejected
    schemaDuplicatePropertyRejected = $schemaDuplicatePropertyRejected
    customSchemaUnregisteredRejected = $customSchemaUnregisteredRejected
    additionalSchemaRegistered = $additionalSchemaRegistered
    externalDnlibFallbackRejected = $externalDnlibFallbackRejected
    externalEmbeddedPackageLockValidated = $externalEmbeddedPackageLockValidated
    legacyLabRelativePackageLockRejected = $legacyLabRelativePackageLockRejected
    absolutePackageLockRejected = $absolutePackageLockRejected
    workflowLockTested = $workflowLockTested
    workflowLockRejected = $workflowLockRejected
    workflowLockReusable = if ($workflowLockTested) { $lockRetryExit -eq 0 } else { $null }
    projectPreflightFailureReported = $projectPreflightFailureReported
}
$reportPath = Join-Path $OutputRoot "script-fixture-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE script fixture gate passed: $reportPath"
exit 0
