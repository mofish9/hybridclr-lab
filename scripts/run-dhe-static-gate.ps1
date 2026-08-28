[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$OutputRoot = "",
    [switch]$ForceOutput
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
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-static-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}

Assert-DheSafeOutputRoot -Path $OutputRoot
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
if (Test-Path -LiteralPath $OutputRoot) {
    if (-not $ForceOutput -and @(Get-ChildItem -LiteralPath $OutputRoot -Force).Count -gt 0) {
        throw "OutputRoot is not empty: $OutputRoot. Pass -ForceOutput to replace a prior run."
    }
    if ($ForceOutput) {
        Remove-Item -LiteralPath $OutputRoot -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$errors = New-Object System.Collections.Generic.List[string]
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Name, [bool]$Passed, [string]$Detail) {
    $checks.Add([ordered]@{
        name = $Name
        passed = $Passed
        detail = $Detail
    })
    if (-not $Passed) {
        $errors.Add($Name + ": " + $Detail)
    }
}

function Get-StaticProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-StaticBoolean($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) { return $null }
    return [bool]$property.Value
}

# Parse the formal DHE scripts and fixtures before running behavioral tests. Do
# not scan ignored historical probes: their presence is intentionally
# machine-local and must not change the clean-checkout gate result.
$formalScriptNames = @(
    "dhe-workflow-common.ps1",
    "runtime-provenance.ps1",
    "resolve-repos-root.ps1",
    "assemble-runtime.ps1",
    "apply-dhe-runtime-patches.ps1",
    "generate-dhe-mv.ps1",
    "generate-dhe-batch.ps1",
    "run-dhe-project-preflight.ps1",
    "run-dhe-project-workflow.ps1",
    "adapters/dhe-demo-project-adapter.ps1",
    "validate-dhe-project-plan.ps1",
    "resolve-dhe-native-manifest.ps1",
    "inject-dhe-guard.ps1",
    "apply-dhe-generated-cpp.ps1",
    "run-dhe-deterministic-player-build.ps1",
    "run-dhe-demo-workflow.ps1",
    "validate-dhe-artifacts.ps1",
    "run-dhe-release-gate.ps1",
    "run-dhe-schema-gate.ps1",
    "run-dhe-capability-gate.ps1",
    "run-dhe-compatibility-negative-gate.ps1",
    "run-dhe-script-fixture-gate.ps1",
    "run-dhe-clean-checkout-gate.ps1",
    "run-dhe-source-boundary-gate.ps1",
    "run-dhe-source-preflight.ps1",
    "run-dhe-native-gate.ps1",
    "archive-dhe-artifacts.ps1",
    "run-dhe-archive-gate.ps1",
    # Shared helpers invoked by the demo adapter and the DHE native lane.
    "bootstrap-repos.ps1",
    "build-clean-baseline.ps1",
    "build-managed-cases.ps1",
    "generate-test-manifest.ps1",
    "generate-metadata-stress-source.ps1",
    "run-native-tests.ps1",
    "run-dhe-static-gate.ps1"
)
$formalScripts = New-Object System.Collections.Generic.List[string]
foreach ($scriptName in $formalScriptNames) {
    $scriptPath = Join-Path $LabRoot ("scripts/" + $scriptName)
    $formalScripts.Add($scriptPath)
}
foreach ($fixtureScript in @(Get-ChildItem -LiteralPath (Join-Path $LabRoot "scripts/fixtures") -Recurse -File -Filter "*.ps1" -ErrorAction SilentlyContinue)) {
    $formalScripts.Add([string]$fixtureScript.FullName)
}
$scriptParseFailures = New-Object System.Collections.Generic.List[string]
foreach ($scriptPath in $formalScripts) {
    if (-not (Test-Path -LiteralPath $scriptPath -PathType Leaf)) {
        $scriptParseFailures.Add($scriptPath)
        continue
    }
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile(
        $scriptPath, [ref]$tokens, [ref]$parseErrors) | Out-Null
    if ($parseErrors.Count -gt 0) {
        $scriptParseFailures.Add($scriptPath)
    }
}
$scriptParseDetail = if ($scriptParseFailures.Count -eq 0) {
    "all formal DHE scripts and fixtures parsed"
} else {
    "parse failures: $($scriptParseFailures -join ', ')"
}
Add-Check "powershell-parse" ($scriptParseFailures.Count -eq 0) $scriptParseDetail

$schemaParseFailures = New-Object System.Collections.Generic.List[string]
foreach ($schema in @(Get-ChildItem -LiteralPath (Join-Path $LabRoot "schemas") -File -Filter "*.json")) {
    try {
        Get-Content -Raw -LiteralPath $schema.FullName | ConvertFrom-Json | Out-Null
    } catch {
        $schemaParseFailures.Add($schema.FullName)
    }
}
$schemaParseDetail = if ($schemaParseFailures.Count -eq 0) {
    "all schema documents parsed"
} else {
    "parse failures: $($schemaParseFailures -join ', ')"
}
Add-Check "schema-parse" ($schemaParseFailures.Count -eq 0) $schemaParseDetail

$schemaGateRoot = Join-Path $OutputRoot "schema-contract"
$schemaGateReportPath = Join-Path $schemaGateRoot "schema-gate-report.json"
$schemaGateExitCode = 1
try {
    & (Join-Path $LabRoot "scripts/run-dhe-schema-gate.ps1") `
        -LabRoot $LabRoot -OutputRoot $schemaGateRoot -ForceOutput | Out-Null
    $schemaGateExitCode = [int]$LASTEXITCODE
} catch {
    $errors.Add("schema-contract: $($_.Exception.Message)")
}
$schemaGateReport = $null
if (Test-Path -LiteralPath $schemaGateReportPath -PathType Leaf) {
    try { $schemaGateReport = Get-Content -Raw -LiteralPath $schemaGateReportPath | ConvertFrom-Json } catch { }
}
$schemaGatePassedValue = Get-StaticBoolean $schemaGateReport "passed"
$schemaGatePassed = $schemaGateExitCode -eq 0 -and $null -ne $schemaGateReport -and
    $null -ne $schemaGatePassedValue -and $schemaGatePassedValue
$schemaGateDetail = if ($schemaGatePassed) {
    "strict JSON schemas and checked-in DHE manifests validated"
} elseif ($null -eq $schemaGateReport) {
    "schema gate did not produce a report"
} else {
    "schema gate exit=$schemaGateExitCode"
}
Add-Check "schema-contract" $schemaGatePassed $schemaGateDetail

# Enforce the source-control boundary before running behavioral fixtures. This
# catches a newly created log/cache file that would otherwise be easy to miss
# when assembling a release commit by hand.
$boundaryRoot = Join-Path $OutputRoot "source-boundary"
$boundaryScript = Join-Path $LabRoot "scripts/run-dhe-source-boundary-gate.ps1"
$boundaryExitCode = 1
try {
    & $boundaryScript -LabRoot $LabRoot -OutputRoot $boundaryRoot -ForceOutput | Out-Null
    $boundaryExitCode = [int]$LASTEXITCODE
} catch {
    $errors.Add("source-boundary: $($_.Exception.Message)")
}
$boundaryReportPath = Join-Path $boundaryRoot "source-boundary-gate-report.json"
$boundaryReport = $null
if (Test-Path -LiteralPath $boundaryReportPath -PathType Leaf) {
    try { $boundaryReport = Get-Content -Raw -LiteralPath $boundaryReportPath | ConvertFrom-Json } catch { }
}
$boundaryPassedValue = Get-StaticBoolean $boundaryReport "passed"
$boundaryPassed = $boundaryExitCode -eq 0 -and $null -ne $boundaryReport -and
    $null -ne $boundaryPassedValue -and $boundaryPassedValue
$boundaryDetail = if ($boundaryPassed) {
    "all non-ignored changes are within the declared DHE source boundary"
} elseif ($null -eq $boundaryReport) {
    "source boundary gate did not produce a report"
} else {
    "source boundary gate exit=$boundaryExitCode"
}
Add-Check "source-boundary" $boundaryPassed $boundaryDetail

# Keep the CI managed build aligned with the assemblies used by the DHE
# adapter. A project omitted from the solution can still compile locally via
# a helper script, but would silently disappear from a clean CI build.
$solutionPath = Join-Path $LabRoot "HybridCLR.Lab.sln"
$requiredSolutionProjects = @(
    "HybridCLR.ManagedCases",
    "HybridCLR.ReferenceRunner",
    "HybridCLR.BoundaryContracts",
    "HybridCLR.ManagedCasesAot",
    "HybridCLR.MetadataStress",
    "HybridCLR.CrossAssemblyDerived"
)
$solutionProjectFailures = New-Object System.Collections.Generic.List[string]
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    $solutionProjectFailures.Add("missing solution: $solutionPath")
} else {
    $solutionText = Get-Content -Raw -LiteralPath $solutionPath
    foreach ($projectName in $requiredSolutionProjects) {
        if ($solutionText -notmatch ('(?m)^Project\("[^\"]+"\) = "' + [regex]::Escape($projectName) + '",')) {
            $solutionProjectFailures.Add($projectName)
        }
    }
}
$solutionProjectDetail = if ($solutionProjectFailures.Count -eq 0) {
    "all required managed projects are present"
} else {
    "missing solution projects: $($solutionProjectFailures -join ', ')"
}
Add-Check "solution-projects" ($solutionProjectFailures.Count -eq 0) $solutionProjectDetail

$runtimeLockPath = Join-Path $LabRoot "manifests/dhe-runtime-lock.json"
$packageLockPath = Join-Path $LabRoot "manifests/dhe-package-lock.json"
$runtimeLock = $null
$packageLock = $null
try {
    $runtimeLock = Get-Content -Raw -LiteralPath $runtimeLockPath | ConvertFrom-Json
    Add-Check "runtime-lock-parse" $true "runtime lock parsed"
} catch {
    Add-Check "runtime-lock-parse" $false $_.Exception.Message
}
try {
    $packageLock = Get-Content -Raw -LiteralPath $packageLockPath | ConvertFrom-Json
    Add-Check "package-lock-parse" $true "package lock parsed"
} catch {
    Add-Check "package-lock-parse" $false $_.Exception.Message
}

if ($null -ne $runtimeLock) {
    $runtimeLockErrors = New-Object System.Collections.Generic.List[string]
    if ([int](Get-StaticProperty $runtimeLock "schemaVersion") -ne 1 -or
        [string](Get-StaticProperty $runtimeLock "format") -ne "hybridclr.dhe-runtime-lock.json") {
        $runtimeLockErrors.Add("invalid schemaVersion or format")
    }
    $runtimePatchProperty = $runtimeLock.PSObject.Properties["patches"]
    $runtimePatches = @((Get-StaticProperty $runtimeLock "patches"))
    if ($null -eq $runtimePatchProperty -or $runtimePatches.Count -eq 0) {
        $runtimeLockErrors.Add("patches must be a non-empty array")
    } else {
        foreach ($entry in $runtimePatches) {
            if ($null -eq $entry) {
                $runtimeLockErrors.Add("patches contains a null entry")
                continue
            }
            $entryId = [string](Get-StaticProperty $entry "id")
            $entryRepository = [string](Get-StaticProperty $entry "repository")
            $entryPath = [string](Get-StaticProperty $entry "path")
            $entryApplyRoot = [string](Get-StaticProperty $entry "applyRoot")
            $entryBaseCommit = [string](Get-StaticProperty $entry "baseCommit")
            $entryHash = [string](Get-StaticProperty $entry "sha256")
            $entryStrip = $null
            if ($null -ne $entry.PSObject.Properties["stripComponents"]) {
                try { $entryStrip = [int]$entry.stripComponents } catch { }
            }
            if ([string]::IsNullOrWhiteSpace($entryId) -or
                [string]::IsNullOrWhiteSpace($entryRepository) -or
                [string]::IsNullOrWhiteSpace($entryPath) -or
                $entryBaseCommit -notmatch '^[0-9a-fA-F]{40}$' -or
                $entryHash -notmatch '^[0-9a-fA-F]{64}$' -or
                $entryApplyRoot -notin @("libil2cpp", "package") -or
                $null -eq $entryStrip -or $entryStrip -lt 0 -or $entryStrip -gt 8 -or
                $entryPath.Replace('\', '/') -match '(^|/)\.\.(/|$)' -or
                [IO.Path]::IsPathRooted($entryPath)) {
                $runtimeLockErrors.Add("invalid patch entry '$entryId'")
            }
            $expectedRepository = if ($entryApplyRoot -eq "package") { "hybridclr_unity" } else { "" }
            if ($entryApplyRoot -eq "libil2cpp" -and $entryRepository -notin @("hybridclr", "il2cpp_plus")) {
                $runtimeLockErrors.Add("invalid native repository '$entryRepository' for '$entryId'")
            } elseif ($entryApplyRoot -eq "package" -and $entryRepository -ne $expectedRepository) {
                $runtimeLockErrors.Add("package patch '$entryId' must use hybridclr_unity")
            }
        }
    }
    $runtimeLockShapeDetail = if ($runtimeLockErrors.Count -eq 0) {
        "schemaVersion=1; patch entries are structurally valid"
    } else {
        $runtimeLockErrors -join ", "
    }
    Add-Check "runtime-lock-schema" ($runtimeLockErrors.Count -eq 0) $runtimeLockShapeDetail

    $patchFailures = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $runtimePatches) {
        $patchPath = Join-Path $LabRoot ([string](Get-StaticProperty $entry "path"))
        if (-not (Test-Path -LiteralPath $patchPath -PathType Leaf)) {
            $patchFailures.Add("$([string](Get-StaticProperty $entry 'id')): missing patch")
            continue
        }
        $actualHash = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualHash, [string](Get-StaticProperty $entry "sha256"))) {
            $patchFailures.Add("$([string](Get-StaticProperty $entry 'id')): hash mismatch")
        }
    }
    $patchDetail = if ($patchFailures.Count -eq 0) {
        "all locked patch hashes match"
    } else {
        $patchFailures -join ", "
    }
    Add-Check "runtime-lock-hashes" ($patchFailures.Count -eq 0) $patchDetail
}

if ($null -ne $packageLock) {
    $packageLockErrors = New-Object System.Collections.Generic.List[string]
    $packagePathValue = [string](Get-StaticProperty $packageLock "packagePath")
    if ([int](Get-StaticProperty $packageLock "schemaVersion") -ne 1 -or
        [string](Get-StaticProperty $packageLock "format") -ne "hybridclr.dhe-package-lock.json" -or
        [string](Get-StaticProperty $packageLock "repository") -ne "hybridclr_unity" -or
        [string](Get-StaticProperty $packageLock "baseCommit") -notmatch '^[0-9a-fA-F]{40}$' -or
        [string](Get-StaticProperty $packageLock "treeSha256") -notmatch '^[0-9a-fA-F]{64}$' -or
        [string]::IsNullOrWhiteSpace($packagePathValue) -or
        [IO.Path]::IsPathRooted($packagePathValue) -or
        $packagePathValue.Replace('\', '/') -match '(^|/)\.\.(/|$)') {
        $packageLockErrors.Add("invalid schema, repository, path, commit, or tree hash")
    }
    $packagePatchProperty = $packageLock.PSObject.Properties["patches"]
    $packagePatches = @((Get-StaticProperty $packageLock "patches"))
    if ($null -eq $packagePatchProperty -or $packagePatches.Count -eq 0) {
        $packageLockErrors.Add("patches must be a non-empty array")
    }
    if ($null -ne $runtimeLock -and $null -ne $runtimeLock.PSObject.Properties["patches"] -and
        $null -ne $packagePatchProperty) {
        $runtimePackageIds = @($runtimePatches | Where-Object { [string](Get-StaticProperty $_ "applyRoot") -eq "package" } |
            ForEach-Object { [string](Get-StaticProperty $_ "id") } | Sort-Object)
        $lockedPackageIds = @($packagePatches | ForEach-Object { [string]$_ } | Sort-Object)
        if (($runtimePackageIds -join ",") -ne ($lockedPackageIds -join ",")) {
            $packageLockErrors.Add("package patch IDs do not match runtime lock")
        }
    }
    $packageLockShapeDetail = if ($packageLockErrors.Count -eq 0) {
        "schemaVersion=1; package path and patch IDs are structurally valid"
    } else {
        $packageLockErrors -join ", "
    }
    Add-Check "package-lock-schema" ($packageLockErrors.Count -eq 0) $packageLockShapeDetail

    $packagePath = $null
    if (-not [string]::IsNullOrWhiteSpace($packagePathValue) -and
        -not [IO.Path]::IsPathRooted($packagePathValue) -and
        $packagePathValue.Replace('\', '/') -notmatch '(^|/)\.\.(/|$)') {
        $packagePath = [IO.Path]::GetFullPath((Join-Path $LabRoot $packagePathValue))
    }
    $packageHashMatches = $false
    $packageDetail = ""
    if ($null -eq $packagePath) {
        $packageDetail = "embedded package path is invalid"
    } elseif (-not (Test-Path -LiteralPath $packagePath -PathType Container)) {
        $packageDetail = "embedded package was not found: $packagePath"
    } else {
        $actualPackageHash = Get-TreeHash $packagePath
        $packageHashMatches = [StringComparer]::OrdinalIgnoreCase.Equals(
            $actualPackageHash, [string](Get-StaticProperty $packageLock "treeSha256"))
        $packageDetail = "expected=$([string](Get-StaticProperty $packageLock 'treeSha256')), actual=$actualPackageHash"
    }
    Add-Check "package-tree-hash" $packageHashMatches $packageDetail
}

$fixtureRoot = Join-Path $OutputRoot "fixture-gate"
$fixtureScript = Join-Path $LabRoot "scripts/run-dhe-script-fixture-gate.ps1"
$fixtureExitCode = 1
try {
    & $fixtureScript -LabRoot $LabRoot -OutputRoot $fixtureRoot -ForceOutput | Out-Null
    $fixtureExitCode = [int]$LASTEXITCODE
} catch {
    $errors.Add("fixture-gate: $($_.Exception.Message)")
}
$fixtureReportPath = Join-Path $fixtureRoot "script-fixture-gate-report.json"
$fixtureReport = $null
if (Test-Path -LiteralPath $fixtureReportPath -PathType Leaf) {
    try { $fixtureReport = Get-Content -Raw -LiteralPath $fixtureReportPath | ConvertFrom-Json } catch { }
}
$fixturePassedValue = Get-StaticBoolean $fixtureReport "passed"
$fixturePassed = $fixtureExitCode -eq 0 -and $null -ne $fixtureReport -and
    $null -ne $fixturePassedValue -and $fixturePassedValue
$fixtureDetail = if ($fixturePassed) {
    "guard, resolver, coverage and no-op fixtures passed"
} elseif ($null -eq $fixtureReport) {
    "fixture gate did not produce a report"
} else {
    "fixture gate exit=$fixtureExitCode"
}
Add-Check "fixture-gate" $fixturePassed $fixtureDetail

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-static-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    checks = $checks.ToArray()
    sourceBoundaryReport = if ($null -eq $boundaryReport) { $null } else { $boundaryReportPath }
    fixtureReport = if ($null -eq $fixtureReport) { $null } else { $fixtureReportPath }
    schemaGateReport = if ($null -eq $schemaGateReport) { $null } else { $schemaGateReportPath }
    errors = $errors.ToArray()
}
$reportPath = Join-Path $OutputRoot "static-gate-report.json"
[IO.File]::WriteAllText(
    $reportPath,
    ($report | ConvertTo-Json -Depth 12),
    (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE static gate: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE static gate failed" + [Environment]::NewLine + " - " + ($errors -join ([Environment]::NewLine + " - ")))
    exit 1
}
exit 0
