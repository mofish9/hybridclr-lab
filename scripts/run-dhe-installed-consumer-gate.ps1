[CmdletBinding()]
param(
    [string]$SourceRoot = "",
    [Parameter(Mandatory = $true)]
    [string]$RuntimeSource,
    [string]$OutputRoot = "",
    [string]$ConsumerRoot = "",
    [ValidatePattern("^[A-Za-z0-9._-]+$")]
    [string]$Target = "StandaloneWindows64",
    [switch]$KeepConsumer,
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

if ($PSVersionTable.PSEdition -ne "Core") {
    throw "The installed-consumer gate requires PowerShell 7 (pwsh)."
}

$SourceRoot = if ([string]::IsNullOrWhiteSpace($SourceRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($SourceRoot)
}
$RuntimeSource = [IO.Path]::GetFullPath($RuntimeSource)
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $SourceRoot "artifacts/dhe-installed-consumer-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}

Assert-DheSafeOutputRoot -Path $OutputRoot
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $SourceRoot
$null = Initialize-DheOutputRoot -Path $OutputRoot -Force:$ForceOutput

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$evidenceRecords = New-Object System.Collections.Generic.List[object]
$packageRoot = Join-Path $OutputRoot "package"
$evidenceRoot = Join-Path $OutputRoot "evidence"
$archiveEvidenceRoot = Join-Path $OutputRoot "workflow-archive"
$archiveGateEvidencePath = Join-Path $OutputRoot "workflow-archive.gate.json"
$packageGatePath = Join-Path $evidenceRoot "package-gate.json"
$installReportPath = Join-Path $evidenceRoot "install.json"
$doctorReportPath = Join-Path $evidenceRoot "doctor.json"
$autoConsumer = [string]::IsNullOrWhiteSpace($ConsumerRoot)
$temporaryConsumerBase = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) "HybridCLRDhe/installed-consumer"))
if ($autoConsumer) {
    $ConsumerRoot = Join-Path $temporaryConsumerBase ([Guid]::NewGuid().ToString("N"))
} else {
    $ConsumerRoot = [IO.Path]::GetFullPath($ConsumerRoot)
}

function Invoke-Git([string]$WorkingRoot, [string[]]$Arguments) {
    $output = @(& git -C $WorkingRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git -C '$WorkingRoot' $($Arguments -join ' ') failed: $($output -join [Environment]::NewLine)"
    }
    return @($output)
}

function Invoke-PwshFile([string]$Path, [string[]]$Arguments) {
    & (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File $Path @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "DHE child command failed with exit code ${LASTEXITCODE}: $Path"
    }
}

function Copy-Evidence([string]$Source, [string]$Name) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) {
        throw "Installed-consumer evidence was not found: $Source"
    }
    $destination = Join-Path $evidenceRoot $Name
    Copy-Item -LiteralPath $Source -Destination $destination -Force
    $item = Get-Item -LiteralPath $destination
    $evidenceRecords.Add([ordered]@{
        path = ("evidence/" + $Name).Replace('\', '/')
        size = [int64]$item.Length
        sha256 = (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}

$sourceHead = ""
$sourceTree = ""
$consumerHead = ""
$consumerTree = ""
$packageId = ""
$runtimeTreeSha256 = ""
$consumerWorkflowRoot = ""
$consumerArchiveRoot = ""
$consumerArchiveGate = ""
$consumerSchemaRoot = ""

try {
    if (-not (Test-Path -LiteralPath $RuntimeSource -PathType Container)) {
        throw "DHE runtime source was not found: $RuntimeSource"
    }
    if (Test-Path -LiteralPath $ConsumerRoot) {
        throw "Installed-consumer root must not already exist: $ConsumerRoot"
    }
    if ($autoConsumer) {
        $resolvedBase = $temporaryConsumerBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        if (-not $ConsumerRoot.StartsWith($resolvedBase, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Automatic consumer root escaped its temporary parent: $ConsumerRoot"
        }
    }

    $sourceStatus = @(Invoke-Git $SourceRoot @("status", "--porcelain=v1", "--untracked-files=all"))
    if ($sourceStatus.Count -ne 0) {
        throw "Installed-consumer Release gate requires a clean source repository."
    }
    $sourceHead = [string](Invoke-Git $SourceRoot @("rev-parse", "HEAD") | Select-Object -First 1)
    $sourceTree = [string](Invoke-Git $SourceRoot @("rev-parse", "HEAD^{tree}") | Select-Object -First 1)

    New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null
    Invoke-PwshFile (Join-Path $SourceRoot "scripts/publish-dhe-toolchain.ps1") @(
        "-LabRoot", $SourceRoot,
        "-OutputRoot", $packageRoot,
        "-Mode", "Release",
        "-ForceOutput")
    $packageManifestPath = Join-Path $packageRoot "dhe-toolchain-manifest.json"
    $packageManifest = Get-Content -Raw -LiteralPath $packageManifestPath | ConvertFrom-Json
    $packageId = [string]$packageManifest.packageId
    if ($packageId -notmatch '^[0-9a-fA-F]{64}$') {
        throw "Published installed-consumer package ID is invalid."
    }
    Invoke-PwshFile (Join-Path $SourceRoot "scripts/test-dhe-toolchain-package.ps1") @(
        "-PackageRoot", $packageRoot,
        "-Output", $packageGatePath,
        "-RequireRelease",
        "-ExpectedPackageId", $packageId)

    $consumerParent = [IO.Path]::GetDirectoryName($ConsumerRoot)
    $null = New-Item -ItemType Directory -Force -Path $consumerParent
    $cloneOutput = @(& git clone --no-local --no-checkout $SourceRoot $ConsumerRoot 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to clone the installed-consumer repository: $($cloneOutput -join [Environment]::NewLine)"
    }
    $null = Invoke-Git $ConsumerRoot @("checkout", "-b", "dhe-installed-consumer", $sourceHead)
    $null = Invoke-Git $ConsumerRoot @("config", "user.name", "DHE Installed Consumer Gate")
    $null = Invoke-Git $ConsumerRoot @("config", "user.email", "dhe-installed-consumer@local.invalid")

    $installedRoot = Join-Path $ConsumerRoot "Tools/HybridCLRDhe"
    Invoke-PwshFile (Join-Path $SourceRoot "scripts/install-dhe-toolchain.ps1") @(
        "-PackageRoot", $packageRoot,
        "-Destination", $installedRoot,
        "-Output", $installReportPath,
        "-ExpectedPackageId", $packageId)
    $null = Invoke-Git $ConsumerRoot @("add", "--", "Tools/HybridCLRDhe")
    $null = Invoke-Git $ConsumerRoot @("commit", "-m", "test: install DHE toolchain $($packageManifest.toolchainVersion)")
    $consumerHead = [string](Invoke-Git $ConsumerRoot @("rev-parse", "HEAD") | Select-Object -First 1)
    $consumerTree = [string](Invoke-Git $ConsumerRoot @("rev-parse", "HEAD^{tree}") | Select-Object -First 1)
    if (@(Invoke-Git $ConsumerRoot @("status", "--porcelain=v1", "--untracked-files=all")).Count -ne 0) {
        throw "Installed-consumer repository is not clean after committing the tool package."
    }

    $projectRoot = Join-Path $ConsumerRoot "unity2021-dhe-demo"
    $dnlibPath = Join-Path $projectRoot "Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll"
    Invoke-PwshFile (Join-Path $installedRoot "dhe.ps1") @(
        "doctor",
        "-ProjectPath", $projectRoot,
        "-DnlibPath", $dnlibPath,
        "-Output", $doctorReportPath,
        "-RequireRelease",
        "-ExpectedPackageId", $packageId)

    $runtimeManifestPath = Join-Path ([IO.Path]::GetDirectoryName($RuntimeSource)) "runtime-manifest.json"
    $runtimeManifest = Get-Content -Raw -LiteralPath $runtimeManifestPath | ConvertFrom-Json
    $runtimeTreeSha256 = [string]$runtimeManifest.stagedRuntimeSha256
    $consumerWorkflowRoot = Join-Path $ConsumerRoot "artifacts/dhe-installed-consumer-workflow"
    $consumerArchiveRoot = Join-Path $ConsumerRoot "artifacts/dhe-installed-consumer-workflow-archive"
    $consumerArchiveGate = $consumerArchiveRoot + ".gate.json"
    Invoke-PwshFile (Join-Path $installedRoot "dhe.ps1") @(
        "workflow",
        "-AdapterScript", (Join-Path $ConsumerRoot "scripts/adapters/dhe-demo-project-adapter.ps1"),
        "-ProjectPath", $projectRoot,
        "-SettingsFile", (Join-Path $projectRoot "ProjectSettings/HybridCLRSettings.asset"),
        "-RuntimeSource", $RuntimeSource,
        "-OutputRoot", $consumerWorkflowRoot,
        "-ArchiveRoot", $consumerArchiveRoot,
        "-DnlibPath", $dnlibPath,
        "-PackageLockPath", (Join-Path $ConsumerRoot "manifests/dhe-package-lock.json"),
        "-IdentityTemplatePath", (Join-Path $projectRoot "Assets/Runtime/HybridCLRDheBuildIdentity.cs"),
        "-GitRoot", $ConsumerRoot,
        "-SourceBoundaryPath", (Join-Path $ConsumerRoot "manifests/dhe-source-boundary.json"),
        "-ExpectedToolchainPackageId", $packageId,
        "-Target", $Target,
        "-Mode", "Release",
        "-RequireEmbeddedPackage",
        "-RequireIdentityTemplate",
        "-ForceOutput")

    $consumerSchemaRoot = Join-Path $ConsumerRoot "artifacts/dhe-installed-consumer-schema"
    Invoke-PwshFile (Join-Path $installedRoot "dhe.ps1") @(
        "schema",
        "-InputRoot", $consumerWorkflowRoot,
        "-AdditionalSchemaRoot", (Join-Path $ConsumerRoot "schemas"),
        "-OutputRoot", $consumerSchemaRoot,
        "-ForceOutput")

    Copy-Evidence (Join-Path $consumerWorkflowRoot "project-workflow-report.json") "project-workflow-report.json"
    Copy-Evidence (Join-Path $consumerWorkflowRoot "workflow-report.json") "workflow-report.json"
    Copy-Evidence (Join-Path $consumerWorkflowRoot "dhe-player-result.json") "dhe-player-result.json"
    Copy-Evidence (Join-Path $consumerWorkflowRoot "release-gate.json") "release-gate.json"
    Copy-Evidence (Join-Path $consumerSchemaRoot "schema-gate-report.json") "schema-gate-report.json"
    Copy-Item -LiteralPath $consumerArchiveRoot -Destination $archiveEvidenceRoot -Recurse -Force
    Copy-Item -LiteralPath $consumerArchiveGate -Destination $archiveGateEvidencePath -Force

    $projectWorkflow = Get-Content -Raw -LiteralPath (Join-Path $consumerWorkflowRoot "project-workflow-report.json") | ConvertFrom-Json
    $workflow = Get-Content -Raw -LiteralPath (Join-Path $consumerWorkflowRoot "workflow-report.json") | ConvertFrom-Json
    $releaseGate = Get-Content -Raw -LiteralPath (Join-Path $consumerWorkflowRoot "release-gate.json") | ConvertFrom-Json
    $archiveGate = Get-Content -Raw -LiteralPath $consumerArchiveGate | ConvertFrom-Json
    $schemaGate = Get-Content -Raw -LiteralPath (Join-Path $consumerSchemaRoot "schema-gate-report.json") | ConvertFrom-Json
    if (-not [bool]$projectWorkflow.passed -or -not [bool]$workflow.releaseReady -or
        -not [bool]$releaseGate.passed -or -not [bool]$archiveGate.passed -or
        -not [bool]$schemaGate.passed) {
        throw "Installed-consumer evidence did not pass every Release stage."
    }
    if (@(Invoke-Git $ConsumerRoot @("status", "--porcelain=v1", "--untracked-files=all")).Count -ne 0) {
        throw "Installed-consumer workflow changed its committed source repository."
    }
} catch {
    $errors.Add($_.Exception.Message)
} finally {
    if ($autoConsumer -and -not $KeepConsumer -and (Test-Path -LiteralPath $ConsumerRoot)) {
        $resolvedBase = $temporaryConsumerBase.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        $resolvedConsumer = [IO.Path]::GetFullPath($ConsumerRoot)
        if ($resolvedConsumer.StartsWith($resolvedBase, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $resolvedConsumer -Recurse -Force
        } else {
            $warnings.Add("Automatic consumer cleanup was skipped because its path escaped the temporary parent.")
        }
    }
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-installed-consumer-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    target = $Target
    sourceRoot = $SourceRoot
    sourceHead = $sourceHead
    sourceTree = $sourceTree
    consumerHead = $consumerHead
    consumerTree = $consumerTree
    packageRoot = $packageRoot
    packageId = $packageId
    runtimeSource = $RuntimeSource
    runtimeTreeSha256 = $runtimeTreeSha256
    consumerRetained = [bool]($KeepConsumer -or -not $autoConsumer)
    consumerRoot = if ($KeepConsumer -or -not $autoConsumer) { $ConsumerRoot } else { $null }
    archiveRoot = if (Test-Path -LiteralPath $archiveEvidenceRoot) { $archiveEvidenceRoot } else { $null }
    archiveGate = if (Test-Path -LiteralPath $archiveGateEvidencePath) { $archiveGateEvidencePath } else { $null }
    evidence = $evidenceRecords.ToArray()
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
}
$reportPath = Join-Path $OutputRoot "installed-consumer-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))

$schemaPath = Join-Path $SourceRoot "schemas/dhe-installed-consumer-gate.schema.json"
$schemaErrors = @()
$schemaValid = Test-Json -LiteralPath $reportPath -SchemaFile $schemaPath -ErrorVariable schemaErrors 2>$null
if (-not $schemaValid) {
    Write-Error ("Installed-consumer gate report failed schema validation: " +
        (@($schemaErrors | ForEach-Object { $_.Exception.Message }) -join " | "))
    exit 1
}
Write-Host "DHE installed-consumer gate: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE installed-consumer gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host "DHE installed-consumer gate passed: packageId=$packageId; source=$sourceHead; consumer=$consumerHead"
exit 0
