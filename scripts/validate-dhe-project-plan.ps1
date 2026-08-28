[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Plan,
    [switch]$RequireCompleteCoverage,
    [string]$Output = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$planPath = [IO.Path]::GetFullPath($Plan)
$outputPath = if ([string]::IsNullOrWhiteSpace($Output)) { $null } else { [IO.Path]::GetFullPath($Output) }
if (-not [IO.File]::Exists($planPath)) {
    throw "DHE project plan was not found: $planPath"
}
if ($null -ne $outputPath -and $outputPath.Equals($planPath, [StringComparison]::OrdinalIgnoreCase)) {
    throw "DHE project plan validation output must not overwrite the input plan: $outputPath"
}
$planDocument = Get-Content -Raw -LiteralPath $planPath | ConvertFrom-Json
if ([int]$planDocument.schemaVersion -ne 1 -or
    [string]$planDocument.format -ne "hybridclr.dhe-project-plan.json") {
    throw "DHE project plan has an invalid schema or format: $planPath"
}

$planDirectory = [IO.Path]::GetDirectoryName($planPath)
function Get-PlanProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}
function Get-PlanBoolean($Object, [string]$Name, [string]$Description) {
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
function Resolve-PlanReference([string]$Reference, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Reference)) {
        $errors.Add("Project plan $Description reference is empty.")
        return ""
    }
    try {
        if ([IO.Path]::IsPathRooted($Reference)) {
            return [IO.Path]::GetFullPath($Reference)
        }
        return [IO.Path]::GetFullPath((Join-Path $planDirectory ($Reference.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    } catch {
        $errors.Add("Project plan $Description reference is invalid: $Reference")
        return ""
    }
}
function Normalize-PlanPath([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path)) { return "" }
    return ([IO.Path]::GetFullPath($Path)).TrimEnd('\', '/')
}
function Resolve-BatchReference([string]$Reference) {
    if ([string]::IsNullOrWhiteSpace($Reference)) { return "" }
    try {
        if ([IO.Path]::IsPathRooted($Reference)) { return [IO.Path]::GetFullPath($Reference) }
        return [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $batchReportPath) ($Reference.Replace('/', [IO.Path]::DirectorySeparatorChar))))
    } catch { return "" }
}

$settingsPath = Resolve-PlanReference ([string](Get-PlanProperty $planDocument "settingsFile")) "settingsFile"
$baselineRootPath = Resolve-PlanReference ([string](Get-PlanProperty $planDocument "baselineRoot")) "baselineRoot"
$currentRootPath = Resolve-PlanReference ([string](Get-PlanProperty $planDocument "currentRoot")) "currentRoot"
$batchReportPath = Resolve-PlanReference ([string](Get-PlanProperty $planDocument "batchReport")) "batchReport"
if ($settingsPath.Length -gt 0 -and -not [IO.File]::Exists($settingsPath)) {
    $errors.Add("Project plan settingsFile was not found: $settingsPath")
}
if ($baselineRootPath.Length -gt 0 -and -not [IO.Directory]::Exists($baselineRootPath)) {
    $errors.Add("Project plan baselineRoot was not found: $baselineRootPath")
}
if ($currentRootPath.Length -gt 0 -and -not [IO.Directory]::Exists($currentRootPath)) {
    $errors.Add("Project plan currentRoot was not found: $currentRootPath")
}
$batchDocument = $null
if ($batchReportPath.Length -gt 0) {
    if (-not [IO.File]::Exists($batchReportPath)) {
        $errors.Add("Project plan batchReport was not found: $batchReportPath")
    } else {
        try {
            $batchDocument = Get-Content -Raw -LiteralPath $batchReportPath | ConvertFrom-Json
            if ([int]$batchDocument.schemaVersion -ne 1 -or
                [string]$batchDocument.format -ne "hybridclr.dhe-lite.batch-report.json") {
                $errors.Add("Project plan batchReport has an invalid schema or format: $batchReportPath")
            }
            if ($baselineRootPath.Length -gt 0 -and
                (Normalize-PlanPath (Resolve-BatchReference ([string](Get-PlanProperty $batchDocument "baselineRoot")))) -ne (Normalize-PlanPath $baselineRootPath)) {
                $errors.Add("Project plan baselineRoot does not match batchReport.baselineRoot.")
            }
            if ($currentRootPath.Length -gt 0 -and
                (Normalize-PlanPath (Resolve-BatchReference ([string](Get-PlanProperty $batchDocument "currentRoot")))) -ne (Normalize-PlanPath $currentRootPath)) {
                $errors.Add("Project plan currentRoot does not match batchReport.currentRoot.")
            }
        } catch {
            $errors.Add("Project plan batchReport is not valid JSON: $batchReportPath ($($_.Exception.Message))")
        }
    }
}

$validationRoot = Join-Path ([IO.Path]::GetDirectoryName($planPath)) "plan-validation"
New-Item -ItemType Directory -Force -Path $validationRoot | Out-Null
$scriptHost = Resolve-DhePowerShellHost
$records = New-Object System.Collections.Generic.List[object]
$seenAssemblyNames = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::OrdinalIgnoreCase)
$planAssemblies = @($planDocument.assemblies)
$batchByAssembly = @{}
if ($null -ne $batchDocument) {
    foreach ($batchAssembly in @((Get-PlanProperty $batchDocument "assemblies"))) {
        $batchName = [string](Get-PlanProperty $batchAssembly "assemblyName")
        if ([string]::IsNullOrWhiteSpace($batchName)) {
            $errors.Add("Project plan batchReport contains an assembly without a name.")
        } elseif ($batchByAssembly.ContainsKey($batchName)) {
            $errors.Add("Project plan batchReport contains duplicate assembly '$batchName'.")
        } else {
            $batchByAssembly[$batchName] = $batchAssembly
        }
    }
    if ($batchByAssembly.Count -ne $planAssemblies.Count) {
        $errors.Add("Project plan assembly count does not match batchReport assembly count.")
    }
}
$planRequiresDheCoverage = $false
$planHotUpdateAssemblies = @()
$planDheAssemblies = @()
if ($null -eq $planDocument.PSObject.Properties["requireDheEqualsHotUpdate"] -or
    $null -eq $planDocument.PSObject.Properties["hotUpdateAssemblies"] -or
    $null -eq $planDocument.PSObject.Properties["dheAotAssemblies"] -or
    $null -eq $planDocument.PSObject.Properties["dheEqualsHotUpdate"]) {
    $errors.Add("Project plan is missing DHE/hot-update scope metadata.")
} else {
    $planRequiresDheCoverage = Get-PlanBoolean $planDocument "requireDheEqualsHotUpdate" "Project plan requireDheEqualsHotUpdate"
    $planHotUpdateAssemblies = @($planDocument.hotUpdateAssemblies | ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
    $planDheAssemblies = @($planDocument.dheAotAssemblies | ForEach-Object { ([string]$_).Trim() } |
        Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
    if ($planRequiresDheCoverage) {
        if ($planHotUpdateAssemblies.Count -eq 0) {
            $errors.Add("Project plan requires complete DHE coverage but hotUpdateAssemblies is empty.")
        }
        if ($planDheAssemblies.Count -eq 0) {
            $errors.Add("Project plan requires complete DHE coverage but dheAotAssemblies is empty.")
        }
        if (($planHotUpdateAssemblies -join ",") -ne ($planDheAssemblies -join ",") -or
            -not (Get-PlanBoolean $planDocument "dheEqualsHotUpdate" "Project plan dheEqualsHotUpdate")) {
            $errors.Add("Project plan DHE AOT assembly set does not exactly match hot-update assembly set.")
        }
    }
}
if ($planAssemblies.Count -eq 0) {
    $errors.Add("Project plan must contain at least one assembly.")
}
foreach ($assembly in $planAssemblies) {
    $name = [string](Get-PlanProperty $assembly "assemblyName")
    $status = [string](Get-PlanProperty $assembly "status")
    if ([string]::IsNullOrWhiteSpace($name)) {
        $errors.Add("Project plan contains an assembly without a name.")
        continue
    }
    if (-not $seenAssemblyNames.Add($name)) {
        $errors.Add("Project plan contains duplicate assembly '$name'.")
        continue
    }
    if ([IO.Path]::IsPathRooted($name) -or $name.Contains('/') -or $name.Contains('\\') -or
        $name.Contains('..') -or [IO.Path]::GetFileName($name) -ne $name) {
        $errors.Add("Project plan contains an unsafe assembly name '$name'.")
        continue
    }
    if ($status -notin @("compatible", "incompatible", "missing", "error")) {
        $errors.Add("Project plan contains invalid status '$status' for '$name'.")
        continue
    }

    if ($null -ne $batchDocument) {
        if (-not $batchByAssembly.ContainsKey($name)) {
            $errors.Add("Project plan batchReport is missing assembly '$name'.")
        } else {
            $batchAssembly = $batchByAssembly[$name]
            if ([string](Get-PlanProperty $batchAssembly "status") -ne $status) {
                $errors.Add("Project plan status for '$name' does not match batchReport.")
            }
            foreach ($field in @("baseline", "current", "mvJson", "mvBytes")) {
                $planReference = [string](Get-PlanProperty $assembly $field)
                $batchField = if ($field -eq "mvJson") { "report" } elseif ($field -eq "mvBytes") { "binary" } else { $field }
                $batchReference = [string](Get-PlanProperty $batchAssembly $batchField)
                $planResolved = Resolve-PlanReference $planReference "$name.$field"
                $batchResolved = if ([string]::IsNullOrWhiteSpace($batchReference)) { "" } else {
                    try {
                        if ([IO.Path]::IsPathRooted($batchReference)) { [IO.Path]::GetFullPath($batchReference) }
                        else { [IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $batchReportPath) ($batchReference.Replace('/', [IO.Path]::DirectorySeparatorChar)))) }
                    } catch { "" }
                }
                if ((Normalize-PlanPath $planResolved) -ne (Normalize-PlanPath $batchResolved)) {
                    $errors.Add("Project plan $field path for '$name' does not match batchReport.")
                }
            }
        }
    }

    $validationPath = Join-Path $validationRoot "$name.json"
    $validationPassed = $false
    $validationError = $null
    if ($status -eq "compatible") {
        $assemblyMvJsonPath = Resolve-PlanReference ([string](Get-PlanProperty $assembly "mvJson")) "$name.mvJson"
        $assemblyMvBytesPath = Resolve-PlanReference ([string](Get-PlanProperty $assembly "mvBytes")) "$name.mvBytes"
        $assemblyBaselinePath = Resolve-PlanReference ([string](Get-PlanProperty $assembly "baseline")) "$name.baseline"
        $assemblyCurrentPath = Resolve-PlanReference ([string](Get-PlanProperty $assembly "current")) "$name.current"
        if ([string]::IsNullOrWhiteSpace($assemblyMvJsonPath) -or
            [string]::IsNullOrWhiteSpace($assemblyMvBytesPath)) {
            $validationError = "Compatible assembly has no MV JSON/binary paths."
        } else {
            & $scriptHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "validate-dhe-artifacts.ps1") `
                -MvJson $assemblyMvJsonPath `
                -MvBytes $assemblyMvBytesPath `
                -BaselineAssembly $assemblyBaselinePath `
                -CurrentAssembly $assemblyCurrentPath `
                -Output $validationPath | Out-Null
            $validatorExitCode = $LASTEXITCODE
            if (Test-Path -LiteralPath $validationPath -PathType Leaf) {
                $validation = Get-Content -Raw -LiteralPath $validationPath | ConvertFrom-Json
            $validationPassed = (Get-PlanBoolean $validation "passed" "Artifact validation passed for '$name'") -and $validatorExitCode -eq 0
                if (-not $validationPassed) {
                    $validationError = "Artifact validator rejected '$name'."
                }
            } else {
                $validationError = "Artifact validator did not produce a report for '$name'."
            }
        }
        if ($validationPassed) {
            $mv = Get-Content -Raw -LiteralPath $assemblyMvJsonPath | ConvertFrom-Json
            if ([string]$mv.assemblyName -ne $name) {
                $validationPassed = $false
                $validationError = "Project plan assembly name '$name' does not match MV assemblyName '$([string]$mv.assemblyName)'."
            }
            if ([int]$assembly.changedMethodCount -ne [int]$mv.summary.changedMethodCount) {
                $validationPassed = $false
                $validationError = "Project plan changedMethodCount does not match MV for '$name'."
            }
            if (-not [StringComparer]::OrdinalIgnoreCase.Equals(
                    [string]$assembly.baselineSha256, [string]$mv.baseline.sha256) -or
                -not [StringComparer]::OrdinalIgnoreCase.Equals(
                    [string]$assembly.currentSha256, [string]$mv.current.sha256)) {
                $validationPassed = $false
                $validationError = "Project plan assembly hash does not match MV for '$name'."
            }
        }
    } elseif ($status -in @("missing", "incompatible")) {
        $validationError = "Assembly is $status."
        if ($RequireCompleteCoverage) {
            $errors.Add("Complete project plan contains $status assembly '$name'.")
        } else {
            $warnings.Add("Project plan contains $status assembly '$name'.")
        }
    } else {
        $validationError = "Assembly status is error."
        $errors.Add("Project plan contains errored assembly '$name'.")
    }
    if (-not $validationPassed -and $status -eq "compatible") {
        $errors.Add($validationError)
    }
    $records.Add([ordered]@{
        assemblyName = $name
        status = $status
        validationPassed = $validationPassed
        validationReport = if (Test-Path -LiteralPath $validationPath -PathType Leaf) { $validationPath } else { $null }
        error = $validationError
    })
}

$coverageComplete = @($records | Where-Object { $_.status -ne "compatible" -or -not $_.validationPassed }).Count -eq 0
$recordAssemblyNames = @($records | ForEach-Object { [string]$_.assemblyName } | Sort-Object -Unique)
if ($planDheAssemblies.Count -gt 0 -and ($recordAssemblyNames -join ",") -ne ($planDheAssemblies -join ",")) {
    $errors.Add("Project plan assembly records do not exactly match dheAotAssemblies metadata.")
}
if ((Get-PlanBoolean $planDocument "complete" "Project plan complete") -ne $coverageComplete) {
    $errors.Add("Project plan complete flag does not match its assembly records.")
}
$passed = $errors.Count -eq 0 -and (-not $RequireCompleteCoverage -or $coverageComplete)
$result = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-project-plan-validation.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $passed
    coverageRequired = [bool]$RequireCompleteCoverage
    coverageComplete = $coverageComplete
    plan = $planPath
    assemblies = $records.ToArray()
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
}
if ($null -ne $outputPath) {
    $protectedInputPaths = @(
        $planPath,
        $settingsPath,
        $baselineRootPath,
        $currentRootPath,
        $batchReportPath,
        @($planAssemblies | ForEach-Object {
            foreach ($field in @("baseline", "current", "mvJson", "mvBytes")) {
                $reference = [string](Get-PlanProperty $_ $field)
                if (-not [string]::IsNullOrWhiteSpace($reference)) {
                    Resolve-PlanReference $reference "output-protection.$field"
                }
            }
        }),
        @($records | ForEach-Object { [string]$_.validationReport })
    ) | ForEach-Object {
        if (-not [string]::IsNullOrWhiteSpace([string]$_)) {
            try { [IO.Path]::GetFullPath([string]$_) } catch { }
        }
    }
    foreach ($protectedInputPath in @($protectedInputPaths)) {
        if ([IO.Path]::GetFullPath([string]$protectedInputPath).Equals($outputPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "DHE project plan validation output must not overwrite an input or child validation report: $outputPath"
        }
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    [IO.File]::WriteAllText($outputPath, ($result | ConvertTo-Json -Depth 14), (New-Object Text.UTF8Encoding($false)))
}
if (-not $passed) {
    Write-Error ("DHE project plan validation failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host "DHE project plan validation passed: $planPath"
exit 0
