[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$InputRoot,
    [Parameter(Mandatory = $true)]
    [string]$ArchiveRoot,
    [string]$LabRoot = "",
    [string]$WorkflowReport = "workflow-report.json",
    [string]$BuildIdentity = "build-identity.json",
    [string]$NativeManifest = "dhe-native-manifest.json",
    [string]$RuntimePlan = "runtime-plan/dhe-runtime-plan.json",
    [string]$ProjectPlan = "project-preflight/dhe-project-plan.json",
    [string]$ProjectPlanValidation = "project-preflight/project-plan-validation.json",
    [string]$BatchReport = "",
    [switch]$ForceOutput,
    [switch]$RequireCompleteCoverage,
    [string]$Output = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$global:LASTEXITCODE = 0
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$labPath = if ([string]::IsNullOrWhiteSpace($LabRoot)) { Split-Path -Parent $PSScriptRoot } else { [IO.Path]::GetFullPath($LabRoot) }
$inputPath = [IO.Path]::GetFullPath($InputRoot)
$archivePath = [IO.Path]::GetFullPath($ArchiveRoot)
$reportPath = if ([string]::IsNullOrWhiteSpace($Output)) {
    $archivePath + ".gate.json"
} else {
    [IO.Path]::GetFullPath($Output)
}

$archiveManifestPath = Join-Path $archivePath "archive-manifest.json"
$archiveFiles = @()
$copySucceeded = $false
$errors = New-Object System.Collections.Generic.List[string]

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Find-MachineLocalJsonPaths($Value, [string]$Location, [System.Collections.Generic.List[string]]$Findings) {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        if ([string]$Value -match '(?i)(?:^|[^A-Z0-9])(?:[A-Z]:[\\/]|\\\\[^\\/])') {
            $Findings.Add($Location)
        }
        return
    }
    if ($Value -is [ValueType]) { return }
    if ($Value -is [System.Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            Find-MachineLocalJsonPaths $Value[$key] "$Location.$key" $Findings
        }
        return
    }
    if ($Value -is [System.Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Value) {
            Find-MachineLocalJsonPaths $item "${Location}[$index]" $Findings
            $index++
        }
        return
    }
    foreach ($property in @($Value.PSObject.Properties)) {
        Find-MachineLocalJsonPaths $property.Value "$Location.$($property.Name)" $Findings
    }
}

function Resolve-ArchivePath([string]$Relative, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Relative) -or [IO.Path]::IsPathRooted($Relative) -or
        $Relative.Replace('\', '/') -match '(^|/)\.\.(/|$)') {
        throw "$Description is not a safe archive-relative path: $Relative"
    }
    $resolved = [IO.Path]::GetFullPath((Join-Path $archivePath $Relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
    $prefix = $archivePath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description escapes the archive root: $resolved"
    }
    return $resolved
}

foreach ($protectedPath in @(
        $inputPath,
        $archivePath,
        $WorkflowReport,
        $BuildIdentity,
        $NativeManifest,
        $RuntimePlan,
        $ProjectPlan,
        $ProjectPlanValidation,
        $BatchReport)) {
    if ([string]::IsNullOrWhiteSpace([string]$protectedPath)) { continue }
    $candidatePath = if ([IO.Path]::IsPathRooted([string]$protectedPath)) {
        [IO.Path]::GetFullPath([string]$protectedPath)
    } else {
        [IO.Path]::GetFullPath((Join-Path $inputPath ([string]$protectedPath)))
    }
    if ((Test-DhePathRelation $reportPath $candidatePath) -and
        -not $reportPath.Equals($archivePath + ".gate.json", [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE archive gate output overlaps protected input or archive content: $reportPath"
    }
}

try {
    if (-not [IO.Directory]::Exists($inputPath)) { throw "DHE workflow input root was not found: $inputPath" }
    # The archive creator may recursively replace ArchiveRoot under
    # -ForceOutput. Reject LabRoot itself and its ancestors even when the
    # supplied input root is outside the workspace.
    Assert-DheOutputNotAncestor -Path $archivePath -Root $labPath
    Assert-DheSafeOutputRoot -Path $archivePath -ProtectedPaths @($inputPath)
    & (Join-Path $labPath "scripts/archive-dhe-artifacts.ps1") `
        -InputRoot $inputPath `
        -ArchiveRoot $archivePath `
        -LabRoot $labPath `
        -WorkflowReport $WorkflowReport `
        -BuildIdentity $BuildIdentity `
        -NativeManifest $NativeManifest `
        -RuntimePlan $RuntimePlan `
        -ProjectPlan $ProjectPlan `
        -ProjectPlanValidation $ProjectPlanValidation `
        -BatchReport $BatchReport `
        -ForceOutput:$ForceOutput
    if ($LASTEXITCODE -ne 0) { throw "DHE archive creation failed." }

    if (-not [IO.File]::Exists($archiveManifestPath)) { throw "Archive manifest was not produced: $archiveManifestPath" }
    $archiveManifest = Get-Content -Raw -LiteralPath $archiveManifestPath | ConvertFrom-Json
    if ([int](Get-PropertyValue $archiveManifest "schemaVersion") -ne 1 -or
        [string](Get-PropertyValue $archiveManifest "format") -ne "hybridclr.dhe-archive-manifest.json") {
        throw "Archive manifest schema or format is invalid."
    }

    $archiveFiles = @($archiveManifest.files)
    $filePaths = New-Object System.Collections.Generic.List[string]
    $filePathSet = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
    foreach ($fileRecord in $archiveFiles) {
        $relative = [string](Get-PropertyValue $fileRecord "path")
        if (-not $filePathSet.Add($relative.Replace('\', '/'))) {
            throw "Archive manifest contains a duplicate file path: $relative"
        }
        $path = Resolve-ArchivePath $relative "Archive file"
        if (-not [IO.File]::Exists($path)) { throw "Archive file is missing: $path" }
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualHash, [string](Get-PropertyValue $fileRecord "sha256"))) {
            throw "Archive file hash mismatch: $relative"
        }
        if ([int64](Get-PropertyValue $fileRecord "size") -ne (Get-Item -LiteralPath $path).Length) {
            throw "Archive file size mismatch: $relative"
        }
        $filePaths.Add($path)
    }
    $actualArchiveRelativePaths = @(
        Get-ChildItem -LiteralPath $archivePath -Recurse -File -Force |
            Where-Object { $_.Name -ne "archive-manifest.json" } |
            ForEach-Object {
                $_.FullName.Substring($archivePath.Length).TrimStart('\', '/').Replace('\', '/')
            }
    )
    $missingArchiveFiles = @($filePathSet | Where-Object { $_ -notin $actualArchiveRelativePaths })
    $extraArchiveFiles = @($actualArchiveRelativePaths | Where-Object { -not $filePathSet.Contains($_) })
    if ($missingArchiveFiles.Count -gt 0 -or $extraArchiveFiles.Count -gt 0) {
        throw "Archive files[] does not exactly match the archive directory. Missing: $($missingArchiveFiles -join ', '); extra: $($extraArchiveFiles -join ', ')"
    }
    if ([int](Get-PropertyValue $archiveManifest "fileCount") -ne $archiveFiles.Count) {
        throw "Archive manifest fileCount does not match files[]."
    }
    $actualFileSetHash = Get-DheFileSetHash $filePaths.ToArray() $archivePath
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualFileSetHash, [string](Get-PropertyValue $archiveManifest "fileSetSha256"))) {
        throw "Archive file-set hash does not match archive-manifest.json."
    }
    foreach ($generatedPath in @($archiveManifest.generatedCppPaths)) {
        $generatedRelative = [string]$generatedPath
        if (-not $filePathSet.Contains($generatedRelative.Replace('\', '/'))) {
            throw "Archive generated C++ path is missing from files[]: $generatedRelative"
        }
    }

    # JSON reports are part of the portable evidence contract. PowerShell can
    # silently reformat an ISO timestamp when a value is explicitly cast to
    # [string], so reject locale-formatted timestamps before the archive is
    # accepted. This catches archive rewrites that would otherwise pass
    # field-only checks while violating the date-time schemas.
    # generatedAtUtc is a UTC contract, not merely an arbitrary ISO-8601
    # offset. Archive rewrites normalize it to Z so copied evidence is
    # comparable without applying a local timezone conversion.
    $isoDateTimePattern = '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]00:00)$'
    foreach ($jsonFile in @($filePaths | Where-Object { $_.EndsWith('.json', [StringComparison]::OrdinalIgnoreCase) })) {
        $rawJson = Get-Content -Raw -LiteralPath $jsonFile
        try {
            $jsonDocument = $rawJson | ConvertFrom-Json
        } catch {
            throw "Archive JSON cannot be parsed: $jsonFile ($($_.Exception.Message))"
        }
        # ConvertFrom-Json in PowerShell 7 materializes ISO strings as DateTime,
        # which would reintroduce the locale-formatting bug while validating. Read
        # the serialized value directly instead.
        $generatedAtMatches = [regex]::Matches($rawJson, '"generatedAtUtc"\s*:\s*"([^"]*)"')
        foreach ($generatedAtMatch in $generatedAtMatches) {
            if ($generatedAtMatch.Groups[1].Value -notmatch $isoDateTimePattern) {
                throw "Archive JSON has a non-ISO generatedAtUtc: $($jsonFile.Substring($archivePath.Length).TrimStart([char]92, [char]47))"
            }
        }
        $machineLocalPaths = New-Object System.Collections.Generic.List[string]
        Find-MachineLocalJsonPaths $jsonDocument '$' $machineLocalPaths
        if ($machineLocalPaths.Count -gt 0) {
            $relativeJsonPath = $jsonFile.Substring($archivePath.Length).TrimStart([char]92, [char]47)
            throw "Archive JSON retains machine-local paths: $relativeJsonPath ($($machineLocalPaths -join ', '))"
        }
    }

    # An archive is a portable evidence handoff, not a second workspace. The
    # runtime manifest and identity therefore carry explicit archive path
    # semantics and must not retain machine-local paths that cannot be used
    # after copying the archive.
    $runtimeManifestReference = [string](Get-PropertyValue $archiveManifest "runtimeManifest")
    $runtimeManifestArchivePath = Resolve-ArchivePath $runtimeManifestReference "Runtime manifest"
    $runtimeManifestArchive = Get-Content -Raw -LiteralPath $runtimeManifestArchivePath | ConvertFrom-Json
    if ([string](Get-PropertyValue $runtimeManifestArchive "pathSemantics") -ne "archive-relative-v1") {
        throw "Archived runtime manifest does not declare archive-relative-v1 path semantics."
    }
    if ($null -ne $runtimeManifestArchive.PSObject.Properties["stagedLibil2cpp"] -and
        $null -ne $runtimeManifestArchive.stagedLibil2cpp) {
        throw "Archived runtime manifest retains a workspace stagedLibil2cpp path."
    }
    if ([string](Get-PropertyValue $runtimeManifestArchive "dheRuntimeLock") -ne "provenance/dhe-runtime-lock.json") {
        throw "Archived runtime manifest does not bind dheRuntimeLock to the archived provenance file."
    }
    if ($null -ne $runtimeManifestArchive.engine -and
        $null -ne $runtimeManifestArchive.engine.PSObject.Properties["executablePath"] -and
        $null -ne $runtimeManifestArchive.engine.executablePath) {
        throw "Archived runtime manifest retains a machine-local engine executable path."
    }
    if ($null -ne $runtimeManifestArchive.externalHeaders -and
        $null -ne $runtimeManifestArchive.externalHeaders.PSObject.Properties["sourcePath"] -and
        $null -ne $runtimeManifestArchive.externalHeaders.sourcePath) {
        throw "Archived runtime manifest retains a machine-local external-header path."
    }
    foreach ($sourceName in @("hybridclr", "il2cpp_plus", "hybridclr_unity")) {
        $sourceRoot = Get-PropertyValue $runtimeManifestArchive "source"
        $sourceEntry = Get-PropertyValue $sourceRoot $sourceName
        if ($null -ne $sourceEntry -and
            $null -ne $sourceEntry.PSObject.Properties["path"] -and
            $null -ne $sourceEntry.path) {
            throw "Archived runtime manifest retains a workspace source path for '$sourceName'."
        }
    }
    foreach ($patch in @($runtimeManifestArchive.dhePatches)) {
        if ($null -ne $patch -and $null -ne $patch.PSObject.Properties["path"] -and
            $null -ne $patch.path) {
            throw "Archived runtime manifest retains a source patch path."
        }
    }

    $identityArchivePath = Resolve-ArchivePath ([string](Get-PropertyValue $archiveManifest "buildIdentity")) "Build identity"
    $identityArchive = Get-Content -Raw -LiteralPath $identityArchivePath | ConvertFrom-Json
    if ([string](Get-PropertyValue $identityArchive "pathSemantics") -ne "archive-relative-v1" -or
        ($null -ne $identityArchive.PSObject.Properties["runtimeSource"] -and $null -ne $identityArchive.runtimeSource)) {
        throw "Archived build identity retains workspace path semantics."
    }
    $workflowArchivePath = Resolve-ArchivePath ([string](Get-PropertyValue $archiveManifest "workflowReport")) "Workflow report"
    $workflowArchive = Get-Content -Raw -LiteralPath $workflowArchivePath | ConvertFrom-Json
    if ([string](Get-PropertyValue $workflowArchive "pathSemantics") -ne "archive-relative-v1" -or
        ($null -ne $workflowArchive.PSObject.Properties["runtimeSource"] -and $null -ne $workflowArchive.runtimeSource)) {
        throw "Archived workflow report retains workspace path semantics."
    }
    $sourcePreflightArchivePath = Resolve-ArchivePath "source-preflight-report.json" "Source preflight"
    $sourcePreflightArchive = Get-Content -Raw -LiteralPath $sourcePreflightArchivePath | ConvertFrom-Json
    if ([string](Get-PropertyValue $sourcePreflightArchive "pathSemantics") -ne "archive-relative-v1") {
        throw "Archived source preflight does not declare archive-relative-v1 path semantics."
    }
    $cleanCheckoutArchivePath = Resolve-ArchivePath "clean-checkout-gate-report.json" "Clean checkout"
    $cleanCheckoutArchive = Get-Content -Raw -LiteralPath $cleanCheckoutArchivePath | ConvertFrom-Json
    if ([string](Get-PropertyValue $cleanCheckoutArchive "pathSemantics") -ne "archive-relative-v1") {
        throw "Archived clean checkout does not declare archive-relative-v1 path semantics."
    }
    $archiveSourceIdentities = Get-PropertyValue $archiveManifest "sourceIdentities"
    foreach ($gitIdentityName in @("projectGit", "toolGit")) {
        $cleanGitIdentity = Get-PropertyValue $cleanCheckoutArchive $gitIdentityName
        $manifestGitIdentity = Get-PropertyValue $archiveSourceIdentities $gitIdentityName
        if ([string](Get-PropertyValue $workflowArchive "mode") -eq "Release" -and
            ($null -eq $cleanGitIdentity -or $null -eq $manifestGitIdentity)) {
            throw "Release archive is missing $gitIdentityName source identity."
        }
        if (($null -eq $cleanGitIdentity) -ne ($null -eq $manifestGitIdentity)) {
            throw "Archive manifest $gitIdentityName identity does not match clean checkout."
        }
        if ($null -ne $cleanGitIdentity -and (
                [string](Get-PropertyValue $cleanGitIdentity "head") -ne [string](Get-PropertyValue $manifestGitIdentity "head") -or
                [string](Get-PropertyValue $cleanGitIdentity "tree") -ne [string](Get-PropertyValue $manifestGitIdentity "tree") -or
                [string](Get-PropertyValue $cleanGitIdentity "sourceBoundarySha256") -ne [string](Get-PropertyValue $manifestGitIdentity "sourceBoundarySha256"))) {
            throw "Archive manifest $gitIdentityName identity does not match clean checkout."
        }
    }

    $archiveAssemblies = @($archiveManifest.assemblies)
    $archiveMvJson = @($archiveAssemblies | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.mvJson) } | ForEach-Object { Resolve-ArchivePath ([string]$_.mvJson) "MV JSON" })
    $archiveMvBytes = @($archiveAssemblies | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.mvBytes) } | ForEach-Object { Resolve-ArchivePath ([string]$_.mvBytes) "MV binary" })
    $archiveBaseline = @($archiveAssemblies | ForEach-Object { Resolve-ArchivePath ([string]$_.baseline) "Baseline assembly" })
    $archiveCurrent = @($archiveAssemblies | ForEach-Object { Resolve-ArchivePath ([string]$_.current) "Current assembly" })

    $copyRoot = Join-Path ([IO.Path]::GetTempPath()) ("dhe-archive-copy-" + [Guid]::NewGuid().ToString("N"))
    $copyOutput = Join-Path $copyRoot "copied-artifact-validation.json"
    try {
        New-Item -ItemType Directory -Force -Path $copyRoot | Out-Null
        Copy-Item -LiteralPath $archivePath -Destination $copyRoot -Recurse -Force
        $copiedArchivePath = Join-Path $copyRoot ([IO.Path]::GetFileName($archivePath))
        $copiedArchiveManifest = Join-Path $copiedArchivePath "archive-manifest.json"
        $copiedManifest = Get-Content -Raw -LiteralPath $copiedArchiveManifest | ConvertFrom-Json
        $copiedAssemblies = @($copiedManifest.assemblies)
        $copiedMvJson = @($copiedAssemblies | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.mvJson) } | ForEach-Object { Join-Path $copiedArchivePath ([string]$_.mvJson).Replace('/', [IO.Path]::DirectorySeparatorChar) })
        $copiedMvBytes = @($copiedAssemblies | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_.mvBytes) } | ForEach-Object { Join-Path $copiedArchivePath ([string]$_.mvBytes).Replace('/', [IO.Path]::DirectorySeparatorChar) })
        $copiedBaseline = @($copiedAssemblies | ForEach-Object { Join-Path $copiedArchivePath ([string]$_.baseline).Replace('/', [IO.Path]::DirectorySeparatorChar) })
        $copiedCurrent = @($copiedAssemblies | ForEach-Object { Join-Path $copiedArchivePath ([string]$_.current).Replace('/', [IO.Path]::DirectorySeparatorChar) })
        $validator = Join-Path $labPath "scripts/validate-dhe-artifacts.ps1"
        Push-Location ([IO.Path]::GetTempPath())
        try {
            & $validator `
                -MvJsonList (ConvertTo-DheStringListArgument $copiedMvJson) `
                -MvBytesList (ConvertTo-DheStringListArgument $copiedMvBytes) `
                -BaselineAssemblyList (ConvertTo-DheStringListArgument $copiedBaseline) `
                -CurrentAssemblyList (ConvertTo-DheStringListArgument $copiedCurrent) `
                -NativeManifest (Join-Path $copiedArchivePath "dhe-native-manifest.json") `
                -BuildIdentity (Join-Path $copiedArchivePath "build-identity.json") `
                -WorkflowReport (Join-Path $copiedArchivePath "workflow-report.json") `
                -RuntimePlan (Join-Path $copiedArchivePath "runtime-plan/dhe-runtime-plan.json") `
                -BatchReport (Join-Path $copiedArchivePath "batch/dhe-batch-summary.json") `
                -RequireCompleteCoverage:$RequireCompleteCoverage `
                -Output $copyOutput
        } finally {
            Pop-Location
        }
        $copyValidation = Get-Content -Raw -LiteralPath $copyOutput | ConvertFrom-Json
        if (-not (Get-DheStrictBooleanProperty $copyValidation "passed" "Copied archive artifact validation passed")) {
            throw "Copied archive artifact validation did not pass."
        }
        $copySucceeded = $true
    } finally {
        if (Test-Path -LiteralPath $copyRoot) {
            Remove-Item -LiteralPath $copyRoot -Recurse -Force
        }
    }
} catch {
    $errors.Add($_.Exception.Message)
}

$result = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-archive-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $copySucceeded -and $errors.Count -eq 0
    inputRoot = $inputPath
    archiveRoot = $archivePath
    archiveManifest = $archiveManifestPath
    archiveFileCount = [int]$archiveFiles.Count
    copiedValidationPassed = $copySucceeded
    requireCompleteCoverage = [bool]$RequireCompleteCoverage
    errors = $errors.ToArray()
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath)) | Out-Null
[IO.File]::WriteAllText($reportPath, ($result | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
if (-not $result.passed) {
    Write-Error ("DHE archive gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host "DHE archive gate passed: $reportPath"
exit 0
