[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPlanValidation,
    [Parameter(Mandatory = $true)]
    [string]$WorkflowReport,
    [ValidatePattern("^[A-Za-z0-9._-]+$")]
    [string]$Target = "StandaloneWindows64",
    [string]$Output = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]

function Read-Json([string]$Path, [string]$Description) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($resolved)) {
        throw "$Description was not found: $resolved"
    }
    try { return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json }
    catch { throw "$Description is not valid JSON: $resolved ($($_.Exception.Message))" }
}

function Get-Property($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Test-StrictBoolean($Object, [string]$Name, [string]$Description) {
    if ($null -eq $Object) {
        $errors.Add("$Description is missing because its report is null.")
        return $false
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) {
        $errors.Add("$Description must be a JSON boolean.")
        return $false
    }
    return [bool]$property.Value
}

function Test-FormalGitIdentity($Identity, [string]$ExpectedName) {
    if ($null -eq $Identity) {
        $errors.Add("Release clean-checkout is missing $ExpectedName Git identity.")
        return $false
    }
    $vcs = [string](Get-Property $Identity "vcs")
    if ([string]::IsNullOrWhiteSpace($vcs)) { $vcs = "git" }
    $commonValid = [string](Get-Property $Identity "name") -eq $ExpectedName -and
        (Test-StrictBoolean $Identity "tested" "$ExpectedName Git tested") -and
        (Test-StrictBoolean $Identity "passed" "$ExpectedName Git passed") -and
        (Test-StrictBoolean $Identity "cleanRequired" "$ExpectedName Git cleanRequired") -and
        (Test-StrictBoolean $Identity "clean" "$ExpectedName Git clean") -and
        (Test-StrictBoolean $Identity "trackedSourcesTested" "$ExpectedName Git trackedSourcesTested") -and
        (Test-StrictBoolean $Identity "trackedSourcesRequired" "$ExpectedName Git trackedSourcesRequired") -and
        (Test-StrictBoolean $Identity "trackedSourcesComplete" "$ExpectedName Git trackedSourcesComplete") -and
        [string](Get-Property $Identity "sourceBoundarySha256") -match '^[0-9a-fA-F]{64}$' -and
        -not [string]::IsNullOrWhiteSpace([string](Get-Property $Identity "root"))
    $vcsValid = if ($vcs -eq "svn") {
        [string](Get-Property $Identity "revision") -match '^[0-9]+$' -and
            -not [string]::IsNullOrWhiteSpace([string](Get-Property $Identity "repository"))
    } elseif ($vcs -eq "git") {
        [string](Get-Property $Identity "head") -match '^[0-9a-fA-F]{40,64}$' -and
            [string](Get-Property $Identity "tree") -match '^[0-9a-fA-F]{40,64}$'
    } else { $false }
    $valid = $commonValid -and $vcsValid
    if (-not $valid) {
        $errors.Add("Release $ExpectedName Git identity is incomplete or not clean/tracked.")
    }
    return $valid
}

function Resolve-ReportReference([string]$Value, [string]$BaseDirectory) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    if ([IO.Path]::IsPathRooted($Value)) { return [IO.Path]::GetFullPath($Value) }
    return [IO.Path]::GetFullPath((Join-Path $BaseDirectory ($Value.Replace('/', [IO.Path]::DirectorySeparatorChar))))
}

function Normalize-ReleasePath([string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return "" }
    return ([IO.Path]::GetFullPath($Value)).TrimEnd('\', '/')
}

function Read-MvBinaryAssemblyName([string]$Path) {
    $bytes = [IO.File]::ReadAllBytes($Path)
    if ($bytes.Length -lt 88) { return "" }
    $magic = [Text.Encoding]::ASCII.GetString($bytes, 0, 8)
    if ($magic -ne "DHEMVLT1") { return "" }
    $nameSize = [BitConverter]::ToUInt32($bytes, 12)
    if ($nameSize -eq 0 -or 88 + [int64]$nameSize -gt $bytes.Length) { return "" }
    return [Text.Encoding]::UTF8.GetString($bytes, 88, [int]$nameSize)
}

$planValidationPath = [IO.Path]::GetFullPath($ProjectPlanValidation)
$workflowPath = [IO.Path]::GetFullPath($WorkflowReport)
$workflowDirectory = Split-Path -Parent $workflowPath
$outputPath = if ([string]::IsNullOrWhiteSpace($Output)) {
    Join-Path $workflowDirectory "release-gate.json"
} else {
    [IO.Path]::GetFullPath($Output)
}
$outputDirectory = [IO.Path]::GetDirectoryName($outputPath)
$outputBaseName = [IO.Path]::GetFileNameWithoutExtension($outputPath)
$revalidatedPlanPath = Join-Path $outputDirectory ($outputBaseName + ".project-plan-validation.json")
$artifactValidationPath = Join-Path $outputDirectory ($outputBaseName + ".artifact-validation.json")
foreach ($inputReportPath in @($planValidationPath, $workflowPath)) {
    if ([IO.Path]::GetFullPath($inputReportPath).Equals($outputPath, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE release gate output must not overwrite an input report: $outputPath"
    }
}
foreach ($derivedOutputPath in @($revalidatedPlanPath, $artifactValidationPath)) {
    foreach ($inputReportPath in @($planValidationPath, $workflowPath)) {
        if ([IO.Path]::GetFullPath($inputReportPath).Equals($derivedOutputPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "DHE release gate derived output must not overwrite an input report: $derivedOutputPath"
        }
    }
}
[IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$planValidation = $null
$workflow = $null
try { $planValidation = Read-Json $planValidationPath "Project plan validation report" }
catch { $errors.Add($_.Exception.Message) }
try { $workflow = Read-Json $workflowPath "DHE workflow report" }
catch { $errors.Add($_.Exception.Message) }
$labRoot = Split-Path -Parent $PSScriptRoot

if ([int](Get-Property $planValidation "schemaVersion") -ne 1 -or
    [string](Get-Property $planValidation "format") -ne "hybridclr.dhe-project-plan-validation.json") {
    $errors.Add("Project plan validation report has an invalid schema or format.")
}
if ([int](Get-Property $workflow "schemaVersion") -ne 1 -or
    [string](Get-Property $workflow "format") -notmatch '^hybridclr\.dhe-[A-Za-z0-9._-]+-workflow\.json$') {
    $errors.Add("Workflow report has an invalid schema or format.")
}
$workflowMode = [string](Get-Property $workflow "mode")
if ($workflowMode -ne "Release") {
    $errors.Add("Workflow report mode must be Release.")
}
if (-not (Test-StrictBoolean $workflow "coverageRequired" "DHE workflow coverageRequired")) {
    $errors.Add("DHE release workflow must require complete coverage.")
}
$planValidationPassed = Test-StrictBoolean $planValidation "passed" "Project plan validation passed"
$planValidationCoverageComplete = Test-StrictBoolean $planValidation "coverageComplete" "Project plan validation coverageComplete"
if (-not $planValidationPassed -or -not $planValidationCoverageComplete) {
    $errors.Add("Project plan validation is not a complete pass.")
}
$workflowPassed = Test-StrictBoolean $workflow "passed" "DHE workflow passed"
$workflowValidationPassed = Test-StrictBoolean $workflow "validationPassed" "DHE workflow validationPassed"
if (-not $workflowPassed -or -not $workflowValidationPassed) {
    $errors.Add("DHE Player workflow validation did not pass.")
}
if (-not (Test-StrictBoolean $workflow "releaseReady" "DHE workflow releaseReady")) {
    $errors.Add("DHE workflow is not releaseReady.")
}
if (-not (Test-StrictBoolean $workflow "artifactValidationPassed" "DHE workflow artifactValidationPassed")) {
    $errors.Add("DHE workflow artifact validation did not pass.")
}
$player = Get-Property $workflow "player"
if ($null -eq $player -or -not (Test-StrictBoolean $player "passed" "DHE Player passed") -or
    [string](Get-Property $player "loadError") -ne "OK") {
    $errors.Add("DHE Player report is not a successful load/dispatch result.")
}
$transaction = Get-Property $workflow "transaction"
if ($null -eq $transaction -or -not (Test-StrictBoolean $transaction "retryValidated" "DHE transaction retryValidated") -or
    [string](Get-Property $transaction "retryFailure") -ne "DHE_MV_REGISTRATION_FAILED" -or
    [string]::IsNullOrWhiteSpace([string](Get-Property $transaction "retryAssemblyName"))) {
    $errors.Add("DHE workflow is missing a successful same-process transaction retry probe.")
}
if ($null -ne $player -and $player.PSObject.Properties["multiAssemblyValidated"] -ne $null -and
    -not (Test-StrictBoolean $player "multiAssemblyValidated" "DHE Player multiAssemblyValidated")) {
    $errors.Add("DHE Player did not validate the declared multi-assembly probes.")
}
if ($null -ne $player -and $player.PSObject.Properties["secondaryAssemblyChangedValidated"] -ne $null -and
    -not (Test-StrictBoolean $player "secondaryAssemblyChangedValidated" "DHE Player secondaryAssemblyChangedValidated")) {
    $errors.Add("DHE Player reported a failed changed-method probe for a secondary assembly.")
}
if ($null -ne $player -and $player.PSObject.Properties["secondaryAssemblyDirectValidated"] -ne $null -and
    -not (Test-StrictBoolean $player "secondaryAssemblyDirectValidated" "DHE Player secondaryAssemblyDirectValidated")) {
    $errors.Add("DHE Player reported a failed direct AOT-entry probe for a secondary assembly.")
}
if ([string](Get-Property $workflow "aotSnapshotKind") -ne "managed-assembly-plus-generated-cpp-v1") {
    $errors.Add("Workflow does not carry the required AOT snapshot identity.")
}
$nativeCoverage = Get-Property $workflow "nativeGuardCoverage"
if ($null -eq $nativeCoverage -or -not (Test-StrictBoolean $nativeCoverage "complete" "Native guard coverage complete")) {
    $errors.Add("Native guard coverage is incomplete.")
}
$assemblyScope = Get-Property $workflow "assemblyScope"
$planAssemblies = if ($null -eq $planValidation) {
    @()
} else {
    @((Get-Property $planValidation "assemblies") | Where-Object { $null -ne $_ -and $_.status -eq "compatible" })
}
$aotAssemblies = @((Get-Property $assemblyScope "aotAssemblies"))
$loadedAssemblies = @((Get-Property $assemblyScope "loadedDheAssemblies"))
if ($null -eq $assemblyScope -or [string]::IsNullOrWhiteSpace([string](Get-Property $assemblyScope "strategy"))) {
    $errors.Add("Workflow assembly scope is missing a strategy.")
}
$planNames = @($planAssemblies | ForEach-Object { [string]$_.assemblyName } | Sort-Object -Unique)
$aotNames = @($aotAssemblies | ForEach-Object { [string]$_ } | Sort-Object -Unique)
$loadedNames = @($loadedAssemblies | ForEach-Object { [string]$_ } | Sort-Object -Unique)
foreach ($planName in $planNames) {
    if ($aotNames -notcontains $planName) {
        $errors.Add("Validated project assembly '$planName' is missing from the workflow AOT assembly set.")
    }
}
if (($planNames -join ",") -ne ($aotNames -join ",")) {
    $errors.Add("Workflow AOT assembly set does not exactly match the validated project plan. Expected [$($planNames -join ', ')], got [$($aotNames -join ', ')].")
}
if (($planNames -join ",") -ne ($loadedNames -join ",")) {
    $errors.Add("Loaded DHE assembly set does not match the validated project plan. Expected [$($planNames -join ', ')], got [$($loadedNames -join ', ')].")
}
if ([string](Get-Property $workflow "target") -ne $Target) {
    $errors.Add("Workflow target is '$([string](Get-Property $workflow 'target'))', but the gate requires '$Target'.")
}

# A release decision must independently verify the source/runtime and Git
# provenance gates. Exploratory reports intentionally omit these requirements
# and cannot be promoted merely by carrying releaseReady=true.
$sourcePreflightPath = Resolve-ReportReference ([string](Get-Property $workflow "sourcePreflight")) $workflowDirectory
$sourcePreflight = $null
if ([string]::IsNullOrWhiteSpace($sourcePreflightPath) -or
    -not (Test-Path -LiteralPath $sourcePreflightPath -PathType Leaf)) {
    $errors.Add("Workflow source preflight report was not found: $sourcePreflightPath")
} else {
    try { $sourcePreflight = Read-Json $sourcePreflightPath "Workflow source preflight report" }
    catch { $errors.Add($_.Exception.Message) }
}
$sourcePreflightValidated = $false
if ($null -ne $sourcePreflight) {
    $sourcePreflightValidated =
        [int](Get-Property $sourcePreflight "schemaVersion") -eq 1 -and
        [string](Get-Property $sourcePreflight "format") -eq "hybridclr.dhe-source-preflight.json" -and
        (Test-StrictBoolean $sourcePreflight "passed" "Source preflight passed") -and
        (Test-StrictBoolean $sourcePreflight "runtimeRequired" "Source preflight runtimeRequired") -and
        (Test-StrictBoolean $sourcePreflight "runtimeReady" "Source preflight runtimeReady") -and
        (Test-StrictBoolean $sourcePreflight "cleanRuntimeSourcesRequired" "Source preflight cleanRuntimeSourcesRequired") -and
        (Test-StrictBoolean $sourcePreflight "externalHeadersRequired" "Source preflight externalHeadersRequired") -and
        ((Get-Property $sourcePreflight "externalHeadersSurrogate") -is [bool]) -and
        -not [bool](Get-Property $sourcePreflight "externalHeadersSurrogate")
    if (-not $sourcePreflightValidated) {
        $errors.Add("Workflow source preflight is not a formal Release result.")
    }
}

$cleanCheckoutPath = Resolve-ReportReference ([string](Get-Property $workflow "cleanCheckoutGate")) $workflowDirectory
$cleanCheckout = $null
if ([string]::IsNullOrWhiteSpace($cleanCheckoutPath) -or
    -not (Test-Path -LiteralPath $cleanCheckoutPath -PathType Leaf)) {
    $errors.Add("Workflow clean-checkout report was not found: $cleanCheckoutPath")
} else {
    try { $cleanCheckout = Read-Json $cleanCheckoutPath "Workflow clean-checkout report" }
    catch { $errors.Add($_.Exception.Message) }
}
$cleanCheckoutValidated = $false
$projectGitIdentity = $null
$toolGitIdentity = $null
if ($null -ne $cleanCheckout) {
    $projectGitIdentity = Get-Property $cleanCheckout "projectGit"
    $toolGitIdentity = Get-Property $cleanCheckout "toolGit"
    $projectGitValidated = Test-FormalGitIdentity $projectGitIdentity "project"
    $toolGitValidated = Test-FormalGitIdentity $toolGitIdentity "tool"
    $projectVcsIdentity = [string](Get-Property $projectGitIdentity "vcs")
    if ([string]::IsNullOrWhiteSpace($projectVcsIdentity)) { $projectVcsIdentity = "git" }
    $flatProjectIdentityValid = if ($projectVcsIdentity -eq "svn") {
        [string](Get-Property $cleanCheckout "vcs") -eq "svn" -and
            [string](Get-Property $cleanCheckout "vcsRevision") -eq [string](Get-Property $projectGitIdentity "revision") -and
            [string](Get-Property $cleanCheckout "vcsRepository") -eq [string](Get-Property $projectGitIdentity "repository") -and
            [string](Get-Property $cleanCheckout "sourceBoundarySha256") -eq [string](Get-Property $projectGitIdentity "sourceBoundarySha256")
    } else {
        [string](Get-Property $cleanCheckout "gitHead") -eq [string](Get-Property $projectGitIdentity "head") -and
            [string](Get-Property $cleanCheckout "gitTree") -eq [string](Get-Property $projectGitIdentity "tree") -and
            [string](Get-Property $cleanCheckout "sourceBoundarySha256") -eq [string](Get-Property $projectGitIdentity "sourceBoundarySha256")
    }
    $cleanCheckoutValidated =
        [int](Get-Property $cleanCheckout "schemaVersion") -eq 1 -and
        [string](Get-Property $cleanCheckout "format") -eq "hybridclr.dhe-clean-checkout-gate.json" -and
        (Test-StrictBoolean $cleanCheckout "passed" "Clean checkout passed") -and
        (Test-StrictBoolean $cleanCheckout "gitTested" "Clean checkout gitTested") -and
        (Test-StrictBoolean $cleanCheckout "gitCleanRequired" "Clean checkout gitCleanRequired") -and
        (Test-StrictBoolean $cleanCheckout "gitClean" "Clean checkout gitClean") -and
        (Test-StrictBoolean $cleanCheckout "trackedSourcesTested" "Clean checkout trackedSourcesTested") -and
        (Test-StrictBoolean $cleanCheckout "trackedSourcesRequired" "Clean checkout trackedSourcesRequired") -and
        (Test-StrictBoolean $cleanCheckout "trackedSourcesComplete" "Clean checkout trackedSourcesComplete") -and
        $projectGitValidated -and $toolGitValidated -and
        $flatProjectIdentityValid
    if (-not $cleanCheckoutValidated) {
        $errors.Add("Workflow clean-checkout evidence is not a formal Release result.")
    }
}

# Re-run artifact validation from the files referenced by the reports. The
# workflow booleans are evidence produced by an earlier stage, not trust
# anchors for a release decision.
$planPath = Resolve-ReportReference ([string](Get-Property $planValidation "plan")) (Split-Path -Parent $planValidationPath)
$planDocument = $null
if ([string]::IsNullOrWhiteSpace($planPath)) {
    $errors.Add("Project plan validation report has no plan path.")
} elseif (Test-Path -LiteralPath ([IO.Path]::GetFullPath($planPath)) -PathType Leaf) {
    try { $planDocument = Read-Json $planPath "Project plan" } catch { $errors.Add($_.Exception.Message) }
} else {
    $errors.Add("Project plan was not found: $planPath")
}
$planAssemblyRecords = if ($null -eq $planDocument) { @() } else { @($planDocument.assemblies | Where-Object { $_.status -eq "compatible" }) }
$planDocumentAssemblies = if ($null -eq $planDocument) { @() } else { @($planDocument.assemblies) }
$planDocumentNames = @($planDocumentAssemblies | ForEach-Object { [string](Get-Property $_ "assemblyName") } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Sort-Object -Unique)
$validationNames = @($planAssemblies | ForEach-Object { [string]$_.assemblyName } | Sort-Object -Unique)
if (($validationNames -join ",") -ne ($planDocumentNames -join ",")) {
    $errors.Add("Project plan validation assembly set does not match the referenced project plan.")
}

# Re-run the project-plan validator against the plan referenced by the report.
# A previously generated validation JSON is evidence, not an authority: the
# release decision must be tied to the exact plan and MV files consumed here.
$revalidatedPlanExitCode = 1
if ($null -ne $planDocument) {
    $planValidatorArgs = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $labRoot "scripts/validate-dhe-project-plan.ps1"),
        "-Plan", $planPath,
        "-RequireCompleteCoverage",
        "-Output", $revalidatedPlanPath
    )
    try {
        & (Resolve-DhePowerShellHost) @planValidatorArgs | Out-Null
        $revalidatedPlanExitCode = $LASTEXITCODE
    } catch {
        $errors.Add("Independent project plan validator failed to start: $($_.Exception.Message)")
    }
    if (-not (Test-Path -LiteralPath $revalidatedPlanPath -PathType Leaf)) {
        $errors.Add("Independent project plan validator did not produce a report: $revalidatedPlanPath")
    } else {
        try {
            $revalidatedPlan = Read-Json $revalidatedPlanPath "Independent project plan validation report"
            $revalidatedPlanPassed = Test-StrictBoolean $revalidatedPlan "passed" "Independent project plan validation passed"
            $revalidatedPlanCoverageComplete = Test-StrictBoolean $revalidatedPlan "coverageComplete" "Independent project plan validation coverageComplete"
            if ($revalidatedPlanExitCode -ne 0 -or -not $revalidatedPlanPassed -or
                -not $revalidatedPlanCoverageComplete) {
                $errors.Add("Independent project plan validation did not pass.")
                foreach ($planError in @((Get-Property $revalidatedPlan "errors"))) {
                    if (-not [string]::IsNullOrWhiteSpace([string]$planError)) {
                        $errors.Add("Independent project plan validation: $planError")
                    }
                }
            }
        } catch { $errors.Add($_.Exception.Message) }
    }
}

$planRequiresDheCoverage = $false
$planHotUpdateNames = @()
$planDheNames = @()
if ($null -ne $planDocument) {
    if ($null -eq $planDocument.PSObject.Properties["requireDheEqualsHotUpdate"] -or
        $null -eq $planDocument.PSObject.Properties["hotUpdateAssemblies"] -or
        $null -eq $planDocument.PSObject.Properties["dheAotAssemblies"] -or
        $null -eq $planDocument.PSObject.Properties["dheEqualsHotUpdate"]) {
        $errors.Add("Project plan is missing DHE/hot-update scope metadata.")
    } else {
        $planRequiresDheCoverage = Test-StrictBoolean $planDocument "requireDheEqualsHotUpdate" "Project plan requireDheEqualsHotUpdate"
        $planHotUpdateNames = @($planDocument.hotUpdateAssemblies | ForEach-Object { ([string]$_).Trim() } |
            Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
        $planDheNames = @($planDocument.dheAotAssemblies | ForEach-Object { ([string]$_).Trim() } |
            Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
        $planDheEqualsHotUpdate = Test-StrictBoolean $planDocument "dheEqualsHotUpdate" "Project plan dheEqualsHotUpdate"
        if (-not $planRequiresDheCoverage -or -not $planDheEqualsHotUpdate -or
            ($planHotUpdateNames -join ",") -ne ($planDheNames -join ",")) {
            $errors.Add("Release gate requires a project plan with exact hot-update/DHE assembly coverage.")
        }
    }
}
$workflowMvJson = @((Get-Property $workflow "mvJson") | ForEach-Object {
        Resolve-ReportReference ([string]$_) $workflowDirectory
    } | Where-Object { $_.Length -gt 0 })
$workflowMvBytes = @((Get-Property $workflow "mvBytes") | ForEach-Object {
        Resolve-ReportReference ([string]$_) $workflowDirectory
    } | Where-Object { $_.Length -gt 0 })
$nativeManifestPath = Resolve-ReportReference ([string](Get-Property $workflow "nativeManifest")) $workflowDirectory
$buildIdentityPath = Resolve-ReportReference ([string](Get-Property $workflow "buildIdentity")) $workflowDirectory
$runtimePlanPath = Resolve-ReportReference ([string](Get-Property $workflow "runtimePlan")) $workflowDirectory
$planDirectory = if ([string]::IsNullOrWhiteSpace($planPath)) { $workflowDirectory } else { Split-Path -Parent $planPath }
$planBaselines = @($planAssemblyRecords | ForEach-Object {
    Resolve-ReportReference ([string](Get-Property $_ "baseline")) $planDirectory
})
$planCurrents = @($planAssemblyRecords | ForEach-Object {
    Resolve-ReportReference ([string](Get-Property $_ "current")) $planDirectory
})
$batchReportReference = if ($null -eq $planDocument) { "" } else { [string](Get-Property $planDocument "batchReport") }
$batchReportPath = if ([string]::IsNullOrWhiteSpace($batchReportReference)) {
    ""
} else {
    Resolve-ReportReference $batchReportReference $planDirectory
}

# Build assembly-keyed maps for every material set. Array ordering is not a
# contract, so a reordered workflow report must still bind the correct MV,
# DLL, batch record, and runtime-plan entry to the same assembly name.
$workflowMvJsonByAssembly = @{}
foreach ($mvPath in $workflowMvJson) {
    if (-not (Test-Path -LiteralPath $mvPath -PathType Leaf)) {
        $errors.Add("Workflow MV JSON was not found: $mvPath")
        continue
    }
    try {
        $mvDocument = Read-Json $mvPath "Workflow MV JSON"
        $mvName = [string](Get-Property $mvDocument "assemblyName")
        if ([string]::IsNullOrWhiteSpace($mvName)) {
            $errors.Add("Workflow MV JSON has no assemblyName: $mvPath")
        } elseif ($workflowMvJsonByAssembly.ContainsKey($mvName)) {
            $errors.Add("Workflow MV JSON contains duplicate assembly '$mvName'.")
        } else {
            $workflowMvJsonByAssembly[$mvName] = $mvPath
        }
    } catch { $errors.Add($_.Exception.Message) }
}
$workflowMvBytesByAssembly = @{}
foreach ($mvPath in $workflowMvBytes) {
    if (-not (Test-Path -LiteralPath $mvPath -PathType Leaf)) {
        $errors.Add("Workflow MV binary was not found: $mvPath")
        continue
    }
    try { $mvName = Read-MvBinaryAssemblyName $mvPath } catch { $mvName = "" }
    if ([string]::IsNullOrWhiteSpace($mvName)) {
        $errors.Add("Workflow MV binary has an invalid header or assembly name: $mvPath")
        continue
    }
    if ($workflowMvBytesByAssembly.ContainsKey($mvName)) {
        $errors.Add("Workflow MV binary contains duplicate assembly '$mvName'.")
    } else {
        $workflowMvBytesByAssembly[$mvName] = $mvPath
    }
}
if ($workflowMvJsonByAssembly.Count -ne $planDocumentNames.Count -or
    $workflowMvBytesByAssembly.Count -ne $planDocumentNames.Count) {
    $errors.Add("Workflow MV material count does not match the project plan assembly count.")
}

$batchDocument = $null
$batchByAssembly = @{}
if ([string]::IsNullOrWhiteSpace($batchReportPath)) {
    $errors.Add("Project plan does not declare a batchReport path.")
} elseif (-not (Test-Path -LiteralPath $batchReportPath -PathType Leaf)) {
    $errors.Add("Batch report was not found beside the project plan: $batchReportPath")
} else {
    try {
        $batchDocument = Read-Json $batchReportPath "Batch report"
        foreach ($batchAssembly in @((Get-Property $batchDocument "assemblies"))) {
            $batchName = [string](Get-Property $batchAssembly "assemblyName")
            if ([string]::IsNullOrWhiteSpace($batchName)) {
                $errors.Add("Batch report contains an assembly without a name.")
            } elseif ($batchByAssembly.ContainsKey($batchName)) {
                $errors.Add("Batch report contains duplicate assembly '$batchName'.")
            } else {
                $batchByAssembly[$batchName] = $batchAssembly
            }
        }
        if ([string](Get-Property $batchDocument "format") -ne "hybridclr.dhe-lite.batch-report.json") {
            $errors.Add("Batch report schema or format is invalid: $batchReportPath")
        }
    } catch { $errors.Add($_.Exception.Message) }
}

foreach ($planAssembly in $planDocumentAssemblies) {
    $assemblyName = [string](Get-Property $planAssembly "assemblyName")
    if ([string]::IsNullOrWhiteSpace($assemblyName)) { continue }
    $planMvJsonPath = Resolve-ReportReference ([string](Get-Property $planAssembly "mvJson")) $planDirectory
    $planMvBytesPath = Resolve-ReportReference ([string](Get-Property $planAssembly "mvBytes")) $planDirectory
    $workflowJsonPath = if ($workflowMvJsonByAssembly.ContainsKey($assemblyName)) { $workflowMvJsonByAssembly[$assemblyName] } else { "" }
    $workflowBytesPath = if ($workflowMvBytesByAssembly.ContainsKey($assemblyName)) { $workflowMvBytesByAssembly[$assemblyName] } else { "" }
    if ((Normalize-ReleasePath $planMvJsonPath) -ne (Normalize-ReleasePath $workflowJsonPath)) {
        $errors.Add("MV JSON path for '$assemblyName' does not match between project plan and workflow report.")
    }
    if ((Normalize-ReleasePath $planMvBytesPath) -ne (Normalize-ReleasePath $workflowBytesPath)) {
        $errors.Add("MV binary path for '$assemblyName' does not match between project plan and workflow report.")
    }
    if ($batchByAssembly.ContainsKey($assemblyName)) {
        $batchAssembly = $batchByAssembly[$assemblyName]
        foreach ($field in @(@("baseline", "baseline"), @("current", "current"), @("report", "mvJson"), @("binary", "mvBytes"))) {
            $batchPathValue = Resolve-ReportReference ([string](Get-Property $batchAssembly $field[0])) (Split-Path -Parent $batchReportPath)
            $planPathValue = Resolve-ReportReference ([string](Get-Property $planAssembly $field[1])) $planDirectory
            if ((Normalize-ReleasePath $batchPathValue) -ne (Normalize-ReleasePath $planPathValue)) {
                $errors.Add("Batch $($field[0]) path for '$assemblyName' does not match the project plan.")
            }
        }
    } else {
        $errors.Add("Batch report is missing project-plan assembly '$assemblyName'.")
    }
}

$runtimePlanDocument = $null
if (-not (Test-Path -LiteralPath $runtimePlanPath -PathType Leaf)) {
    $errors.Add("Runtime plan was not found: $runtimePlanPath")
} else {
    try { $runtimePlanDocument = Read-Json $runtimePlanPath "Runtime plan" } catch { $errors.Add($_.Exception.Message) }
}
if ($null -ne $runtimePlanDocument) {
    $runtimePlanByAssembly = @{}
    foreach ($runtimeAssembly in @((Get-Property $runtimePlanDocument "assemblies"))) {
        $runtimeName = [string](Get-Property $runtimeAssembly "assemblyName")
        if ([string]::IsNullOrWhiteSpace($runtimeName) -or $runtimePlanByAssembly.ContainsKey($runtimeName)) {
            $errors.Add("Runtime plan contains a missing or duplicate assembly name.")
        } else { $runtimePlanByAssembly[$runtimeName] = $runtimeAssembly }
    }
    if (($runtimePlanByAssembly.Keys | Sort-Object) -join "," -ne ($planDocumentNames -join ",")) {
        $errors.Add("Runtime plan assembly set does not match the project plan.")
    }
    foreach ($planAssembly in $planDocumentAssemblies) {
        $assemblyName = [string](Get-Property $planAssembly "assemblyName")
        if (-not $runtimePlanByAssembly.ContainsKey($assemblyName)) { continue }
        $runtimeAssembly = $runtimePlanByAssembly[$assemblyName]
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals([string](Get-Property $planAssembly "baselineSha256"), [string](Get-Property $runtimeAssembly "baselineSha256")) -or
            -not [StringComparer]::OrdinalIgnoreCase.Equals([string](Get-Property $planAssembly "currentSha256"), [string](Get-Property $runtimeAssembly "currentSha256"))) {
            $errors.Add("Runtime plan hashes do not match project plan assembly '$assemblyName'.")
        }
    }
}
$validatorArgs = @(
    "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", (Join-Path $labRoot "scripts/validate-dhe-artifacts.ps1"),
    "-MvJsonList", (ConvertTo-DheStringListArgument $workflowMvJson),
    "-MvBytesList", (ConvertTo-DheStringListArgument $workflowMvBytes),
    "-BaselineAssemblyList", (ConvertTo-DheStringListArgument $planBaselines),
    "-CurrentAssemblyList", (ConvertTo-DheStringListArgument $planCurrents),
    "-NativeManifest", $nativeManifestPath,
    "-BuildIdentity", $buildIdentityPath,
    "-WorkflowReport", $workflowPath,
    "-RuntimePlan", $runtimePlanPath,
    "-RequireCompleteCoverage",
    "-Output", $artifactValidationPath
)
$validatorExitCode = 1
try {
    & (Resolve-DhePowerShellHost) @validatorArgs | Out-Null
    $validatorExitCode = $LASTEXITCODE
} catch {
    $errors.Add("Independent artifact validator failed to start: $($_.Exception.Message)")
}
if (-not (Test-Path -LiteralPath $artifactValidationPath -PathType Leaf)) {
    $errors.Add("Independent artifact validator did not produce a report: $artifactValidationPath")
} else {
    try {
        $artifactValidation = Read-Json $artifactValidationPath "Independent artifact validation report"
if ($validatorExitCode -ne 0 -or -not (Test-StrictBoolean $artifactValidation "passed" "Artifact validation passed")) {
            $errors.Add("Independent artifact validation did not pass.")
            foreach ($artifactError in @((Get-Property $artifactValidation "errors"))) {
                if (-not [string]::IsNullOrWhiteSpace([string]$artifactError)) {
                    $errors.Add("Independent artifact validation: $artifactError")
                }
            }
        }
    } catch { $errors.Add($_.Exception.Message) }
}

$result = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-release-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    target = $Target
    workflowMode = $workflowMode
    sourcePreflight = $sourcePreflightPath
    sourcePreflightValidated = $sourcePreflightValidated
    cleanCheckout = $cleanCheckoutPath
    cleanCheckoutValidated = $cleanCheckoutValidated
    projectGitHead = [string](Get-Property $projectGitIdentity "head")
    projectGitTree = [string](Get-Property $projectGitIdentity "tree")
    projectSourceBoundarySha256 = [string](Get-Property $projectGitIdentity "sourceBoundarySha256")
    toolGitHead = [string](Get-Property $toolGitIdentity "head")
    toolGitTree = [string](Get-Property $toolGitIdentity "tree")
    toolSourceBoundarySha256 = [string](Get-Property $toolGitIdentity "sourceBoundarySha256")
    projectVcs = [string](Get-Property $projectGitIdentity "vcs")
    projectRevision = [string](Get-Property $projectGitIdentity "revision")
    projectRevisionSpec = [string](Get-Property $projectGitIdentity "revisionSpec")
    projectRepository = [string](Get-Property $projectGitIdentity "repository")
    toolVcs = [string](Get-Property $toolGitIdentity "vcs")
    toolRevision = [string](Get-Property $toolGitIdentity "revision")
    toolRevisionSpec = [string](Get-Property $toolGitIdentity "revisionSpec")
    toolRepository = [string](Get-Property $toolGitIdentity "repository")
    projectPlanValidation = $planValidationPath
    workflowReport = $workflowPath
    artifactValidation = $artifactValidationPath
    projectPlanRevalidation = $revalidatedPlanPath
    batchReport = $batchReportPath
    runtimePlan = $runtimePlanPath
    artifactValidatorExitCode = $validatorExitCode
    projectPlanValidatorExitCode = $revalidatedPlanExitCode
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
    validatedAssemblyCount = $planNames.Count
    loadedDheAssemblyCount = $loadedNames.Count
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
[IO.File]::WriteAllText($outputPath, ($result | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
if (-not $result.passed) {
    Write-Error ("DHE release gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host "DHE release gate passed: $outputPath"
exit 0
