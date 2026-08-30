[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$AdapterScript,
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,
    [Parameter(Mandatory = $true)]
    [string]$SettingsFile,
    [Parameter(Mandatory = $true)]
    [string]$RuntimeSource,
    [Parameter(Mandatory = $true)]
    [string]$OutputRoot,
    [string]$ArchiveRoot = "",
    [string]$DnlibPath = "",
    [string]$PackageLockPath = "",
    [string]$IdentityTemplatePath = "",
    [string]$BaselineAotRoot = "",
    [string]$BaselineManifestPath = "",
    [string]$AdapterOptionsPath = "",
    [string]$PlayerSmokeRunner = "",
    [ValidateSet("Auto", "Git", "Svn")]
    [string]$ProjectVcs = "Auto",
    [string]$GitRoot = "",
    [string]$SourceBoundaryPath = "",
    [ValidatePattern("^$|^[0-9a-fA-F]{64}$")]
    [string]$ExpectedToolchainPackageId = "",
    [ValidatePattern("^[A-Za-z0-9._-]+$")]
    [string]$Target = "StandaloneWindows64",
    [ValidateSet("Release", "Exploratory")]
    [string]$Mode = "Release",
    [switch]$RequireCompleteCoverage,
    [switch]$RequireEmbeddedPackage,
    [switch]$RequireIdentityTemplate,
    [switch]$RequireGitClean,
    [switch]$RequireTrackedSources,
    [switch]$StopAfterPreflight,
    [switch]$ForceOutput,
    [ValidateRange(0, 3600)]
    [int]$WorkflowLockTimeoutSeconds = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$global:LASTEXITCODE = 0
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$labRoot = Split-Path -Parent $PSScriptRoot
$toolchainContractVersion = 1
$adapterPath = [IO.Path]::GetFullPath($AdapterScript)
$projectPath = [IO.Path]::GetFullPath($ProjectPath)
$settingsPath = [IO.Path]::GetFullPath($SettingsFile)
$runtimePath = [IO.Path]::GetFullPath($RuntimeSource)
$outputPath = [IO.Path]::GetFullPath($OutputRoot)
$baselineAotPath = if ([string]::IsNullOrWhiteSpace($BaselineAotRoot)) { "" } else { [IO.Path]::GetFullPath($BaselineAotRoot) }
$baselineManifestPath = if ([string]::IsNullOrWhiteSpace($BaselineManifestPath)) {
    if (-not [string]::IsNullOrWhiteSpace($baselineAotPath)) {
        $candidate = Join-Path $baselineAotPath "dhe-baseline-manifest.json"
        if ([IO.File]::Exists($candidate)) { [IO.Path]::GetFullPath($candidate) } else { "" }
    } else { "" }
} else { [IO.Path]::GetFullPath($BaselineManifestPath) }
$adapterOptionsPath = if ([string]::IsNullOrWhiteSpace($AdapterOptionsPath)) { "" } else { [IO.Path]::GetFullPath($AdapterOptionsPath) }
$gitRootPath = if ([string]::IsNullOrWhiteSpace($GitRoot)) {
    $projectPath
} else {
    [IO.Path]::GetFullPath($GitRoot)
}
$archivePath = if ([string]::IsNullOrWhiteSpace($ArchiveRoot)) {
    $outputPath + "-archive"
} else {
    [IO.Path]::GetFullPath($ArchiveRoot)
}
$gitVerificationRequested = $Mode -eq "Release" -or [bool]$RequireGitClean -or
    [bool]$RequireTrackedSources -or -not [string]::IsNullOrWhiteSpace($GitRoot) -or
    $ProjectVcs -ne "Auto"
$coverageRequired = $Mode -eq "Release" -or [bool]$RequireCompleteCoverage
$baselineRequired = $Mode -eq "Release"
$trackedSourcesRequired = $Mode -eq "Release" -or [bool]$RequireTrackedSources
$toolchainManifestPath = Join-Path $labRoot "dhe-toolchain-manifest.json"
$installedToolchain = Test-Path -LiteralPath $toolchainManifestPath -PathType Leaf
$packageLockPathResolved = if ([string]::IsNullOrWhiteSpace($PackageLockPath)) {
    ""
} else {
    [IO.Path]::GetFullPath($PackageLockPath)
}
$embeddedPackageRoot = Resolve-DheEmbeddedPackageRoot -ProjectRoot $projectPath -PackageLockPath $packageLockPathResolved -AllowMissing
$embeddedPackagePresent = $null -ne $embeddedPackageRoot -and (Test-Path -LiteralPath $embeddedPackageRoot -PathType Container)
$embeddedPackageRequired = [bool]$RequireEmbeddedPackage -or ($Mode -eq "Release" -and $embeddedPackagePresent)
$outputRootSafe = $false
$workflowLock = $null
$summaryPath = Join-Path $outputPath "project-workflow-report.json"
$failurePath = Join-Path $outputPath "project-workflow-failure.json"
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$stages = [ordered]@{
    toolchain = [ordered]@{ passed = $false; report = $null }
    prepare = [ordered]@{ passed = $false; report = $null }
    cleanCheckout = [ordered]@{ passed = $false; report = $null }
    sourcePreflight = [ordered]@{ passed = $false; report = $null }
    projectPreflight = [ordered]@{ passed = $false; report = $null }
    player = [ordered]@{ passed = $false; report = $null }
    archive = [ordered]@{ passed = $false; report = $null }
    release = [ordered]@{ passed = $false; report = $null }
}

function Read-JsonFile([string]$Path, [string]$Description) {
    if (-not [IO.File]::Exists($Path)) {
        throw "$Description was not found: $Path"
    }
    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    } catch {
        throw "$Description is not valid JSON: $Path ($($_.Exception.Message))"
    }
}

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Normalize-Path([string]$Path) {
    return ([IO.Path]::GetFullPath($Path)).TrimEnd('\', '/')
}

function Invoke-Adapter([string]$Action, [hashtable]$AdditionalParameters) {
    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $adapterPath,
        "-Action", $Action,
        "-ProjectPath", $projectPath,
        "-SettingsFile", $settingsPath,
        "-RuntimeSource", $runtimePath,
        "-OutputRoot", $outputPath,
        "-Target", $Target,
        "-ToolchainContractVersion", $toolchainContractVersion
    )
    foreach ($key in @($AdditionalParameters.Keys)) {
        $value = $AdditionalParameters[$key]
        if ($value -is [bool]) {
            if ([bool]$value) { $arguments += "-$key" }
        } elseif ($null -ne $value -and -not [string]::IsNullOrWhiteSpace([string]$value)) {
            $arguments += @("-$key", [string]$value)
        }
    }
    $previousToolRoot = $env:DHE_TOOL_ROOT
    $previousWorkflowRoot = $env:DHE_WORKFLOW_ROOT
    $env:DHE_TOOL_ROOT = $labRoot
    $env:DHE_WORKFLOW_ROOT = $labRoot
    try {
        & $scriptHost @arguments | Out-Null
        return [int]$LASTEXITCODE
    } finally {
        if ($null -eq $previousToolRoot) { Remove-Item Env:DHE_TOOL_ROOT -ErrorAction SilentlyContinue }
        else { $env:DHE_TOOL_ROOT = $previousToolRoot }
        if ($null -eq $previousWorkflowRoot) { Remove-Item Env:DHE_WORKFLOW_ROOT -ErrorAction SilentlyContinue }
        else { $env:DHE_WORKFLOW_ROOT = $previousWorkflowRoot }
    }
}

function Require-Directory([string]$Path, [string]$Description) {
    if (-not [IO.Directory]::Exists($Path)) {
        throw "$Description was not found: $Path"
    }
}

function Require-File([string]$Path, [string]$Description) {
    if (-not [IO.File]::Exists($Path)) {
        throw "$Description was not found: $Path"
    }
}

function Require-Reference([string]$Value, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Value)) {
        throw "$Description is empty."
    }
    return $Value
}

function Require-AbsoluteReference([string]$Value, [string]$Description) {
    $reference = Require-Reference $Value $Description
    if (-not [IO.Path]::IsPathRooted($reference)) {
        throw "$Description must be an absolute path under workspace-absolute-v1: $reference"
    }
    return $reference
}

function Require-BooleanProperty($Object, [string]$Name, [string]$Description) {
    if ($null -eq $Object) {
        throw "$Description is missing because its report is null."
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) {
        throw "$Description must be a JSON boolean."
    }
    return [bool]$property.Value
}

function Get-AdapterFailureDetail {
    $adapterFailurePath = Join-Path $outputPath "workflow-failure.json"
    if (-not (Test-Path -LiteralPath $adapterFailurePath -PathType Leaf)) {
        return "no adapter failure report was produced"
    }
    try {
        $adapterFailure = Read-JsonFile $adapterFailurePath "DHE adapter failure report"
        $detail = [string](Get-PropertyValue $adapterFailure "error")
        if ([string]::IsNullOrWhiteSpace($detail)) { return "adapter failure report has no error detail" }
        return $detail
    } catch {
        return $_.Exception.Message
    }
}

function Invoke-SourcePreflightStage {
    $sourceRoot = Join-Path $outputPath "source-preflight"
    $sourceArgs = @{
        LabRoot = $labRoot
        ProjectPath = $projectPath
        RuntimeSource = $runtimePath
        OutputRoot = $sourceRoot
        RequireRuntime = $true
        RequireDheEqualsHotUpdate = $true
        ForceOutput = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        $sourceArgs.PackageLockPath = [IO.Path]::GetFullPath($PackageLockPath)
    }
    if (-not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
        $sourceArgs.IdentityTemplatePath = [IO.Path]::GetFullPath($IdentityTemplatePath)
    }
    if ($embeddedPackageRequired) { $sourceArgs.RequireEmbeddedPackage = $true }
    if ($RequireIdentityTemplate) { $sourceArgs.RequireIdentityTemplate = $true }
    if ($Mode -eq "Release") {
        $sourceArgs.RequireNonSurrogateExternalHeaders = $true
        $sourceArgs.RequireCleanRuntimeSources = $true
    }
    & (Join-Path $labRoot "scripts/run-dhe-source-preflight.ps1") @sourceArgs | Out-Null
    $sourceExitCode = [int]$LASTEXITCODE
    $sourceReportPath = Join-Path $sourceRoot "source-preflight-report.json"
    Require-File $sourceReportPath "DHE source preflight report"
    $sourceReport = Read-JsonFile $sourceReportPath "DHE source preflight report"
    $stages.sourcePreflight.report = $sourceReportPath
    $stages.sourcePreflight.passed = $sourceExitCode -eq 0 -and
        (Require-BooleanProperty $sourceReport "passed" "DHE source preflight passed")
    if (-not $stages.sourcePreflight.passed) {
        throw "DHE source preflight failed. See $sourceReportPath"
    }
}

function Invoke-CleanCheckoutStage {
    $resolvedProjectBoundaryPath = if ([string]::IsNullOrWhiteSpace($SourceBoundaryPath)) { "" } else {
        [IO.Path]::GetFullPath($SourceBoundaryPath)
    }
    if ($trackedSourcesRequired -and [string]::IsNullOrWhiteSpace($resolvedProjectBoundaryPath)) {
        $defaultProjectBoundary = Join-Path $projectPath "manifests/dhe-source-boundary.json"
        if (Test-Path -LiteralPath $defaultProjectBoundary -PathType Leaf) {
            $resolvedProjectBoundaryPath = $defaultProjectBoundary
        } else {
            throw "Tracked-source verification is required for Release, but no SourceBoundaryPath was supplied and no project boundary manifest exists: $defaultProjectBoundary"
        }
    }
    $cleanCheckoutRoot = Join-Path $outputPath "clean-checkout"
    $toolBoundaryCandidates = @(
        (Join-Path $labRoot "dhe-source-boundary.json"),
        (Join-Path $labRoot "manifests/dhe-source-boundary.json")
    )
    $toolBoundaryPath = @($toolBoundaryCandidates | Where-Object {
        Test-Path -LiteralPath $_ -PathType Leaf
    } | Select-Object -First 1)
    if ($toolBoundaryPath.Count -ne 1) {
        throw "DHE tool source boundary manifest was not found under the tool root: $labRoot"
    }
    $cleanCheckoutArgs = @{
        LabRoot = $labRoot
        ProjectPath = $projectPath
        RuntimeSource = $runtimePath
        OutputRoot = $cleanCheckoutRoot
        ToolSourceBoundaryPath = [IO.Path]::GetFullPath([string]$toolBoundaryPath[0])
        ProjectVcs = $ProjectVcs
        ForceOutput = $true
    }
    if ($installedToolchain) {
        $cleanCheckoutArgs.ToolPackageManifestPath = $toolchainManifestPath
    } else {
        $cleanCheckoutArgs.ToolGitRoot = $labRoot
    }
    if ($gitVerificationRequested) { $cleanCheckoutArgs.GitRoot = $gitRootPath }
    if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        $cleanCheckoutArgs.PackageLockPath = [IO.Path]::GetFullPath($PackageLockPath)
    }
    if ($RequireGitClean -or $Mode -eq "Release") { $cleanCheckoutArgs.RequireGitClean = $true }
    if ($trackedSourcesRequired) { $cleanCheckoutArgs.RequireTrackedSources = $true }
    if ($Mode -eq "Release") {
        if (-not $installedToolchain) {
            $cleanCheckoutArgs.RequireToolGitClean = $true
            $cleanCheckoutArgs.RequireToolTrackedSources = $true
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($resolvedProjectBoundaryPath)) {
        $cleanCheckoutArgs.SourceBoundaryPath = $resolvedProjectBoundaryPath
    }
    if (-not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
        $cleanCheckoutArgs.IdentityTemplatePath = [IO.Path]::GetFullPath($IdentityTemplatePath)
    }
    if ($RequireIdentityTemplate) { $cleanCheckoutArgs.RequireIdentityTemplate = $true }
    if ($embeddedPackageRequired) { $cleanCheckoutArgs.RequireEmbeddedPackage = $true }
    & (Join-Path $labRoot "scripts/run-dhe-clean-checkout-gate.ps1") @cleanCheckoutArgs | Out-Null
    $cleanCheckoutExitCode = [int]$LASTEXITCODE
    $cleanCheckoutReportPath = Join-Path $cleanCheckoutRoot "clean-checkout-gate-report.json"
    Require-File $cleanCheckoutReportPath "DHE clean-checkout gate report"
    $cleanCheckoutReport = Read-JsonFile $cleanCheckoutReportPath "DHE clean-checkout gate report"
    $stages.cleanCheckout.report = $cleanCheckoutReportPath
    $stages.cleanCheckout.passed = $cleanCheckoutExitCode -eq 0 -and
        (Require-BooleanProperty $cleanCheckoutReport "passed" "DHE clean-checkout gate passed")
    if (-not $stages.cleanCheckout.passed) {
        throw "DHE clean-checkout gate failed. See $cleanCheckoutReportPath"
    }
}

try {
    $workflowLock = Enter-DheWorkflowLock -LabRoot $labRoot -TimeoutSeconds $WorkflowLockTimeoutSeconds
    if ($StopAfterPreflight -and $Mode -eq "Release") {
        throw "-StopAfterPreflight is a contract-test mode and cannot be combined with -Mode Release. Use -Mode Exploratory when intentionally skipping Player, archive, and release stages."
    }
    # Establish the report directory before trust or input checks so every
    # pre-adapter rejection, including a missing Release package pin, leaves a
    # machine-readable failure report.
    $protectedInputs = @($projectPath, $runtimePath)
    if (-not [string]::IsNullOrWhiteSpace($baselineAotPath)) { $protectedInputs += $baselineAotPath }
    $null = Assert-DheSafeOutputRoot -Path $outputPath -ProtectedPaths $protectedInputs
    $null = Assert-DheOutputNotAncestor -Path $outputPath -Root $labRoot
    $null = Assert-DheSafeOutputRoot -Path $archivePath -ProtectedPaths @($projectPath, $runtimePath, $outputPath)
    $null = Assert-DheOutputNotAncestor -Path $archivePath -Root $labRoot
    if (Test-Path -LiteralPath $outputPath) {
        if (-not $ForceOutput -and @(Get-ChildItem -LiteralPath $outputPath -Force).Count -gt 0) {
            throw "OutputRoot is not empty: $outputPath. Pass -ForceOutput to replace a prior run."
        }
        if ($ForceOutput) { Remove-Item -LiteralPath $outputPath -Recurse -Force }
    }
    New-Item -ItemType Directory -Force -Path $outputPath | Out-Null
    $outputRootSafe = $true

    # Establish trust in an installed Release toolchain before validating any
    # project inputs or invoking project-owned code. This keeps a missing
    # external package pin fail-closed even when other inputs are incomplete.
    if ($installedToolchain -and $Mode -eq "Release" -and
        [string]::IsNullOrWhiteSpace($ExpectedToolchainPackageId)) {
        throw "An installed DHE Release workflow requires -ExpectedToolchainPackageId from a trusted external release record."
    }
    if ($baselineRequired -and [string]::IsNullOrWhiteSpace($baselineAotPath)) {
        throw "DHE Release requires -BaselineAotRoot from a previous stripped-AOT release."
    }
    if (-not [string]::IsNullOrWhiteSpace($baselineAotPath) -and -not [IO.Directory]::Exists($baselineAotPath)) {
        throw "DHE baseline AOT root was not found: $baselineAotPath"
    }
    if (-not [string]::IsNullOrWhiteSpace($baselineManifestPath) -and
        -not [IO.File]::Exists($baselineManifestPath)) {
        throw "DHE baseline manifest was not found: $baselineManifestPath"
    }
    if ($baselineRequired -and [string]::IsNullOrWhiteSpace($baselineManifestPath)) {
        throw "DHE Release requires dhe-baseline-manifest.json beside the previous stripped-AOT baseline or an explicit -BaselineManifestPath."
    }
    if (-not [string]::IsNullOrWhiteSpace($baselineManifestPath) -and
        [string]::IsNullOrWhiteSpace($baselineAotPath)) {
        throw "DHE baseline manifest requires -BaselineAotRoot."
    }
    if (-not [string]::IsNullOrWhiteSpace($adapterOptionsPath) -and
        -not [IO.File]::Exists($adapterOptionsPath)) {
        throw "DHE adapter options were not found: $adapterOptionsPath"
    }
    $scriptHost = Resolve-DhePowerShellHost
    Require-File $adapterPath "DHE project adapter"
    Require-Directory $projectPath "DHE project"
    Require-File $settingsPath "HybridCLR settings"
    Require-Directory $runtimePath "DHE runtime source"
    if (-not [string]::IsNullOrWhiteSpace($baselineManifestPath)) {
        $runtimeManifestForBaseline = Join-Path ([IO.Path]::GetDirectoryName($runtimePath)) "runtime-manifest.json"
        $settingsForBaseline = Resolve-DheSettingsAssemblySets -SettingsFile $settingsPath -ProjectRoot $projectPath
        Assert-DheBaselineManifestBinding -Manifest (Read-DheBaselineManifest $baselineManifestPath) `
            -ManifestPath $baselineManifestPath -BaselineRoot $baselineAotPath -Target $Target `
            -RuntimeManifestPath $runtimeManifestForBaseline -PackageLockPath $PackageLockPath `
            -AssemblyNames @($settingsForBaseline.dheAotAssemblies)
    }
    if ($embeddedPackageRequired -and [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        throw "Embedded package provenance requires an explicit -PackageLockPath. The core workflow does not use a Demo package lock fallback."
    }
    if ($RequireIdentityTemplate -and [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
        throw "Identity template verification requires an explicit -IdentityTemplatePath."
    }

    $toolchainGatePath = $null
    if (Test-Path -LiteralPath $toolchainManifestPath -PathType Leaf) {
        $toolchainGatePath = Join-Path $outputPath "toolchain/toolchain-gate.json"
        $toolchainGateArgs = @{
            PackageRoot = $labRoot
            Output = $toolchainGatePath
        }
        if ($Mode -eq "Release") { $toolchainGateArgs.RequireRelease = $true }
        if (-not [string]::IsNullOrWhiteSpace($ExpectedToolchainPackageId)) {
            $toolchainGateArgs.ExpectedPackageId = $ExpectedToolchainPackageId
        }
        & (Join-Path $labRoot "scripts/test-dhe-toolchain-package.ps1") @toolchainGateArgs | Out-Null
        $toolchainGateExitCode = [int]$LASTEXITCODE
        $toolchainGate = Read-JsonFile $toolchainGatePath "DHE toolchain package gate"
        $stages.toolchain.report = $toolchainGatePath
        $stages.toolchain.passed = $toolchainGateExitCode -eq 0 -and
            (Require-BooleanProperty $toolchainGate "passed" "DHE toolchain package gate passed")
        if (-not $stages.toolchain.passed) {
            throw "DHE toolchain package gate failed. See $toolchainGatePath"
        }
    } else {
        if (-not [string]::IsNullOrWhiteSpace($ExpectedToolchainPackageId)) {
            throw "-ExpectedToolchainPackageId requires an installed toolchain package manifest."
        }
        $stages.toolchain.passed = $true
        $warnings.Add("Toolchain is running from a source checkout; package-manifest verification is not applicable.")
    }

    # No adapter or Unity generation may run until formal project/tool Git and
    # runtime/package provenance have passed. Repeat both gates after Prepare
    # to detect adapter-side mutation of those inputs.
    Invoke-CleanCheckoutStage
    Invoke-SourcePreflightStage

    $prepareExitCode = Invoke-Adapter "Prepare" @{
        Mode = $Mode
        BaselineAotRoot = $BaselineAotRoot
        BaselineManifestPath = $baselineManifestPath
        AdapterOptionsPath = $adapterOptionsPath
        PlayerSmokeRunner = $PlayerSmokeRunner
    }
    $preparePath = Join-Path $outputPath "adapter/prepare.json"
    if ($prepareExitCode -ne 0 -and -not (Test-Path -LiteralPath $preparePath -PathType Leaf)) {
        throw "DHE adapter Prepare action exited with code ${prepareExitCode}: $(Get-AdapterFailureDetail)"
    }
    Require-File $preparePath "DHE adapter prepare report"
    $prepare = Read-JsonFile $preparePath "DHE adapter prepare report"
    $stages.prepare.report = $preparePath
    $prepareBinding = Assert-DheAdapterPrepareReport -Report $prepare `
        -ToolchainContractVersion $toolchainContractVersion -Target $Target `
        -ProjectPath $projectPath -SettingsFile $settingsPath -Mode $Mode `
        -BaselineAotRoot $baselineAotPath -BaselineManifestPath $baselineManifestPath
    if ($prepareExitCode -ne 0) {
        throw "DHE adapter Prepare action failed. See $preparePath"
    }
    $baselinePath = [string]$prepareBinding.baselineRoot
    $currentPath = [string]$prepareBinding.currentRoot
    Require-Directory $baselinePath "DHE adapter baseline root"
    Require-Directory $currentPath "DHE adapter current root"
    $stages.prepare.passed = $true

    Invoke-CleanCheckoutStage
    Invoke-SourcePreflightStage

    $preflightRoot = Join-Path $outputPath "project-preflight"
    $preflightArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
        (Join-Path $labRoot "scripts/run-dhe-project-preflight.ps1"),
        "-SettingsFile", $settingsPath,
        "-BaselineRoot", $baselinePath,
        "-CurrentRoot", $currentPath,
        "-OutputRoot", $preflightRoot,
        "-ProjectRoot", $projectPath,
        "-RuntimeSource", $runtimePath,
        "-RequireRuntime",
        "-RequireDheEqualsHotUpdate",
        "-ForceOutput"
    )
    if (-not [string]::IsNullOrWhiteSpace($DnlibPath)) {
        $preflightArgs += @("-DnlibPath", [IO.Path]::GetFullPath($DnlibPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        $preflightArgs += @("-PackageLockPath", [IO.Path]::GetFullPath($PackageLockPath))
    }
    if (-not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
        $preflightArgs += @("-IdentityTemplatePath", [IO.Path]::GetFullPath($IdentityTemplatePath))
    }
    if ($embeddedPackageRequired) { $preflightArgs += "-RequireEmbeddedPackage" }
    if ($RequireIdentityTemplate) { $preflightArgs += "-RequireIdentityTemplate" }
    if ($Mode -eq "Release") {
        $preflightArgs += "-RequireNonSurrogateExternalHeaders"
        $preflightArgs += "-RequireCleanRuntimeSources"
    }
    if ($coverageRequired) { $preflightArgs += "-RequireCompleteCoverage" }
    & $scriptHost @preflightArgs | Out-Null
    $preflightExitCode = [int]$LASTEXITCODE
    $preflightReportPath = Join-Path $preflightRoot "project-preflight-report.json"
    Require-File $preflightReportPath "DHE project preflight report"
    $preflightReport = Read-JsonFile $preflightReportPath "DHE project preflight report"
    $projectPreflightSourcePassed = Require-BooleanProperty $preflightReport "sourcePreflightPassed" "DHE project preflight sourcePreflightPassed"
    if (-not $projectPreflightSourcePassed) {
        throw "DHE project preflight repeated source provenance validation and detected drift. See $preflightReportPath"
    }
    $stages.projectPreflight.report = $preflightReportPath
    $stages.projectPreflight.passed = $preflightExitCode -eq 0 -and
        (Require-BooleanProperty $preflightReport "passed" "DHE project preflight passed") -and
        (Require-BooleanProperty $preflightReport "generationPassed" "DHE project preflight generationPassed") -and
        (Require-BooleanProperty $preflightReport "validationPassed" "DHE project preflight validationPassed")
    if (-not $stages.projectPreflight.passed) {
        throw "DHE project preflight failed. See $preflightReportPath"
    }

    $projectPlanPath = [IO.Path]::GetFullPath((Require-Reference ([string](Get-PropertyValue $preflightReport "projectPlan")) "DHE project preflight projectPlan"))
    $projectPlanValidationPath = [IO.Path]::GetFullPath((Require-Reference ([string](Get-PropertyValue $preflightReport "projectPlanValidation")) "DHE project preflight projectPlanValidation"))
    $batchReportPath = [IO.Path]::GetFullPath((Require-Reference ([string](Get-PropertyValue $preflightReport "batchReport")) "DHE project preflight batchReport"))
    Require-File $projectPlanPath "DHE project plan"
    Require-File $projectPlanValidationPath "DHE project plan validation"
    Require-File $batchReportPath "DHE batch report"

    if ($StopAfterPreflight) {
        $summary = [ordered]@{
            schemaVersion = 1
            format = "hybridclr.dhe-project-workflow.json"
            generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
            passed = $true
            mode = "Preflight"
            toolchainContractVersion = $toolchainContractVersion
            target = $Target
            adapterScript = $adapterPath
            projectPath = $projectPath
            baselineManifestPath = if ([string]::IsNullOrWhiteSpace($baselineManifestPath)) { $null } else { $baselineManifestPath }
            adapterOptionsPath = if ([string]::IsNullOrWhiteSpace($adapterOptionsPath)) { $null } else { $adapterOptionsPath }
            toolchainGate = $toolchainGatePath
            expectedToolchainPackageId = if ([string]::IsNullOrWhiteSpace($ExpectedToolchainPackageId)) { $null } else { $ExpectedToolchainPackageId.ToLowerInvariant() }
            sourcePreflight = $stages.sourcePreflight.report
            cleanCheckout = $stages.cleanCheckout.report
            projectPreflight = $preflightReportPath
            workflowReport = $null
            yooAssetBuild = $null
            archiveGate = $null
            releaseGate = $null
            stages = $stages
            errors = @()
            warnings = @($warnings.ToArray()) + @("Player, archive, and release stages were skipped by -StopAfterPreflight.")
        }
        [IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 16), (New-Object Text.UTF8Encoding($false)))
        Write-Host "DHE project preflight workflow passed: $summaryPath"
        exit 0
    }

    $playerParameters = @{
        Mode = $Mode
        ProjectPlan = $projectPlanPath
        ProjectPlanValidation = $projectPlanValidationPath
        BatchReport = $batchReportPath
        SourcePreflight = [string]$stages.sourcePreflight.report
        BaselineManifestPath = $baselineManifestPath
        AdapterOptionsPath = $adapterOptionsPath
        PlayerSmokeRunner = $PlayerSmokeRunner
    }
    if ($coverageRequired) { $playerParameters.RequireCompleteCoverage = $true }
    if (-not [string]::IsNullOrWhiteSpace([string]$stages.cleanCheckout.report)) {
        $playerParameters.CleanCheckoutGate = [string]$stages.cleanCheckout.report
    }
    $playerExitCode = Invoke-Adapter "Player" $playerParameters
    $workflowPath = Join-Path $outputPath "workflow-report.json"
    if ($playerExitCode -ne 0 -and -not (Test-Path -LiteralPath $workflowPath -PathType Leaf)) {
        throw "DHE adapter Player action exited with code ${playerExitCode}: $(Get-AdapterFailureDetail)"
    }
    Require-File $workflowPath "DHE adapter workflow report"
    $workflow = Read-JsonFile $workflowPath "DHE adapter workflow report"
    $stages.player.report = $workflowPath
    $workflowPassed = Require-BooleanProperty $workflow "passed" "DHE adapter workflow passed"
    $workflowValidationPassed = Require-BooleanProperty $workflow "validationPassed" "DHE adapter workflow validationPassed"
    $stages.player.passed = $playerExitCode -eq 0 -and $workflowPassed -and $workflowValidationPassed
    if (-not $stages.player.passed) {
        throw "DHE adapter Player action did not produce a passing workflow report. See $workflowPath"
    }
    $yooAssetBuildReference = Get-PropertyValue $workflow "yooAssetBuild"
    $yooAssetBuildPath = if ($null -eq $yooAssetBuildReference -or
        [string]::IsNullOrWhiteSpace([string]$yooAssetBuildReference)) {
        $null
    } elseif ([IO.Path]::IsPathRooted([string]$yooAssetBuildReference)) {
        [IO.Path]::GetFullPath([string]$yooAssetBuildReference)
    } else {
        [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($workflowPath)) [string]$yooAssetBuildReference))
    }
    if ($null -ne $yooAssetBuildPath) {
        Require-File $yooAssetBuildPath "DHE adapter YooAsset evidence"
        $yooAssetBuildDocument = Read-JsonFile $yooAssetBuildPath "DHE adapter YooAsset evidence"
        if (-not (Require-BooleanProperty $yooAssetBuildDocument "passed" "DHE YooAsset evidence passed")) {
            throw "DHE adapter YooAsset evidence did not pass: $yooAssetBuildPath"
        }
    }

    # Player generation can touch tracked Unity/HybridCLR outputs (and an
    # adapter may install or rewrite project-owned sources). Release evidence
    # must describe the checkout after that work, not only the pre-build state.
    if ($Mode -eq "Release") {
        Invoke-CleanCheckoutStage
        Invoke-SourcePreflightStage
    }
    if ([string](Get-PropertyValue $workflow "target") -ne $Target -or
        [string](Get-PropertyValue $workflow "pathSemantics") -ne "workspace-absolute-v1") {
        throw "DHE adapter workflow report has an unsupported target or path semantics."
    }
    foreach ($binding in @(
            @("projectPlan", $projectPlanPath),
            @("projectPlanValidation", $projectPlanValidationPath),
            @("batchReport", $batchReportPath),
            @("sourcePreflight", [string]$stages.sourcePreflight.report),
            @("cleanCheckoutGate", [string]$stages.cleanCheckout.report))) {
        $workflowReference = [string](Get-PropertyValue $workflow $binding[0])
        if ([string]::IsNullOrWhiteSpace($workflowReference) -or
            (Normalize-Path $workflowReference) -ne (Normalize-Path ([string]$binding[1]))) {
            throw "DHE adapter workflow $($binding[0]) does not match the orchestrator-owned evidence path."
        }
    }
    $workflowPackageLockReference = [string](Get-PropertyValue $workflow "packageLock")
    if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        if ([string]::IsNullOrWhiteSpace($workflowPackageLockReference)) {
            throw "DHE adapter workflow report must include packageLock when -PackageLockPath is supplied."
        }
        $workflowPackageLockPath = if ([IO.Path]::IsPathRooted($workflowPackageLockReference)) {
            [IO.Path]::GetFullPath($workflowPackageLockReference)
        } else {
            [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($workflowPath)) $workflowPackageLockReference))
        }
        if ((Normalize-Path $workflowPackageLockPath) -ne (Normalize-Path ([IO.Path]::GetFullPath($PackageLockPath)))) {
            throw "DHE adapter workflow packageLock does not match -PackageLockPath."
        }
    } elseif ($RequireEmbeddedPackage -and [string]::IsNullOrWhiteSpace($workflowPackageLockReference)) {
        throw "DHE adapter workflow report must include packageLock when embedded package provenance is required."
    }

    $archiveGatePath = $archivePath + ".gate.json"
    $archiveParameters = @{
        InputRoot = $outputPath
        ArchiveRoot = $archivePath
        LabRoot = $labRoot
        WorkflowReport = $workflowPath
        BuildIdentity = [IO.Path]::GetFullPath((Require-Reference ([string](Get-PropertyValue $workflow "buildIdentity")) "DHE adapter workflow buildIdentity"))
        NativeManifest = [IO.Path]::GetFullPath((Require-Reference ([string](Get-PropertyValue $workflow "nativeManifest")) "DHE adapter workflow nativeManifest"))
        RuntimePlan = [IO.Path]::GetFullPath((Require-Reference ([string](Get-PropertyValue $workflow "runtimePlan")) "DHE adapter workflow runtimePlan"))
        ProjectPlan = $projectPlanPath
        ProjectPlanValidation = $projectPlanValidationPath
        BatchReport = $batchReportPath
        Output = $archiveGatePath
    }
    # Replacing an existing sibling archive is an explicit caller decision.
    # The workflow's -ForceOutput applies to both roots; without it the
    # archive gate must reject stale evidence instead of silently overwriting
    # a handoff another process may still be using.
    if ($ForceOutput) { $archiveParameters.ForceOutput = $true }
    if ($coverageRequired) { $archiveParameters.RequireCompleteCoverage = $true }
    & (Join-Path $labRoot "scripts/run-dhe-archive-gate.ps1") @archiveParameters | Out-Null
    $archiveExitCode = [int]$LASTEXITCODE
    Require-File $archiveGatePath "DHE archive gate report"
    $archiveReport = Read-JsonFile $archiveGatePath "DHE archive gate report"
    $stages.archive.report = $archiveGatePath
    $stages.archive.passed = $archiveExitCode -eq 0 -and
        (Require-BooleanProperty $archiveReport "passed" "DHE archive gate passed")
    if (-not $stages.archive.passed) {
        throw "DHE archive gate failed. See $archiveGatePath"
    }

    $releaseGatePath = $null
    if ($Mode -eq "Release" -or $RequireCompleteCoverage) {
        $releaseGatePath = Join-Path $outputPath "release-gate.json"
        & (Join-Path $labRoot "scripts/run-dhe-release-gate.ps1") `
            -ProjectPlanValidation $projectPlanValidationPath `
            -WorkflowReport $workflowPath `
            -Target $Target `
            -Output $releaseGatePath | Out-Null
        $releaseExitCode = [int]$LASTEXITCODE
        Require-File $releaseGatePath "DHE release gate report"
        $releaseReport = Read-JsonFile $releaseGatePath "DHE release gate report"
        $stages.release.report = $releaseGatePath
        $stages.release.passed = $releaseExitCode -eq 0 -and
            (Require-BooleanProperty $releaseReport "passed" "DHE release gate passed")
        if (-not $stages.release.passed) {
            throw "DHE release gate failed. See $releaseGatePath"
        }
    }

    $summary = [ordered]@{
        schemaVersion = 1
        format = "hybridclr.dhe-project-workflow.json"
        generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
        passed = [bool]$stages.toolchain.passed -and [bool]$stages.player.passed -and [bool]$stages.archive.passed -and
            ($Mode -ne "Release" -and -not $RequireCompleteCoverage -or [bool]$stages.release.passed)
        mode = $Mode
        toolchainContractVersion = $toolchainContractVersion
        target = $Target
        adapterScript = $adapterPath
        projectPath = $projectPath
        baselineManifestPath = if ([string]::IsNullOrWhiteSpace($baselineManifestPath)) { $null } else { $baselineManifestPath }
        adapterOptionsPath = if ([string]::IsNullOrWhiteSpace($adapterOptionsPath)) { $null } else { $adapterOptionsPath }
        toolchainGate = $toolchainGatePath
        expectedToolchainPackageId = if ([string]::IsNullOrWhiteSpace($ExpectedToolchainPackageId)) { $null } else { $ExpectedToolchainPackageId.ToLowerInvariant() }
        sourcePreflight = $stages.sourcePreflight.report
        cleanCheckout = $stages.cleanCheckout.report
        projectPreflight = $preflightReportPath
        workflowReport = $workflowPath
        yooAssetBuild = $yooAssetBuildPath
        archiveGate = $archiveGatePath
        releaseGate = $releaseGatePath
        stages = $stages
        errors = @()
        warnings = $warnings.ToArray()
    }
    [IO.File]::WriteAllText($summaryPath, ($summary | ConvertTo-Json -Depth 16), (New-Object Text.UTF8Encoding($false)))
    if (-not [bool]$summary.passed) {
        throw "DHE project workflow did not pass. See $summaryPath"
    }
    Write-Host "DHE project workflow passed: $summaryPath"
    exit 0
}
catch {
    $message = $_.Exception.Message
    $errors.Add($message)
    try {
        if ($outputRootSafe -and [IO.Directory]::Exists($outputPath)) {
            $failure = [ordered]@{
                schemaVersion = 1
                format = "hybridclr.dhe-project-workflow-failure.json"
                generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
                passed = $false
                toolchainContractVersion = $toolchainContractVersion
                adapterScript = $adapterPath
                projectPath = $projectPath
                baselineManifestPath = if ([string]::IsNullOrWhiteSpace($baselineManifestPath)) { $null } else { $baselineManifestPath }
                adapterOptionsPath = if ([string]::IsNullOrWhiteSpace($adapterOptionsPath)) { $null } else { $adapterOptionsPath }
                expectedToolchainPackageId = if ([string]::IsNullOrWhiteSpace($ExpectedToolchainPackageId)) { $null } else { $ExpectedToolchainPackageId.ToLowerInvariant() }
                error = $message
                errors = $errors.ToArray()
                stages = $stages
            }
            [IO.File]::WriteAllText($failurePath, ($failure | ConvertTo-Json -Depth 16), (New-Object Text.UTF8Encoding($false)))
        }
    } catch {
        Write-Warning "Unable to write DHE project workflow failure report: $($_.Exception.Message)"
    }
    Write-Error "DHE project workflow failed: $message"
    exit 1
}
finally {
    Exit-DheWorkflowLock -Lock $workflowLock
}
