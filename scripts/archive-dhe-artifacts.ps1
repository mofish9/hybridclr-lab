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
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$global:LASTEXITCODE = 0
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$labPath = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$inputPath = [IO.Path]::GetFullPath($InputRoot)
$archivePath = [IO.Path]::GetFullPath($ArchiveRoot)
if (-not [IO.Directory]::Exists($inputPath)) {
    throw "DHE workflow input root was not found: $inputPath"
}
# `-ForceOutput` recursively replaces the archive directory. Keep the
# workspace itself out of that deletion boundary even when the input artifacts
# were produced outside LabRoot and therefore cannot protect it indirectly.
Assert-DheOutputNotAncestor -Path $archivePath -Root $labPath
Assert-DheSafeOutputRoot -Path $archivePath -ProtectedPaths @($inputPath)
$null = Initialize-DheOutputRoot -Path $archivePath -Force:$ForceOutput -ProtectedPaths @($inputPath)

function Get-PropertyValue($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-NullableStringProperty($Object, [string]$Name) {
    $property = if ($null -eq $Object) { $null } else { $Object.PSObject.Properties[$Name] }
    if ($null -eq $property -or $null -eq $property.Value) { return $null }
    return [string]$property.Value
}

function Set-PropertyValue($Object, [string]$Name, $Value) {
    $Object | Add-Member -NotePropertyName $Name -NotePropertyValue $Value -Force
}

function Set-ArchiveValidationReferences {
    param(
        [Parameter(Mandatory = $true)]
        $Document,
        [Parameter(Mandatory = $true)]
        [object[]]$AssemblyRecords,
        [string]$Prefix = "",
        [switch]$IncludeCoreReports
    )

    $mvJsons = @($AssemblyRecords | ForEach-Object { $Prefix + [string]$_.mvJson })
    $mvBytes = @($AssemblyRecords | ForEach-Object { $Prefix + [string]$_.mvBytes })
    $baselines = @($AssemblyRecords | ForEach-Object { $Prefix + [string]$_.baseline })
    $currents = @($AssemblyRecords | ForEach-Object { $Prefix + [string]$_.current })
    Set-PropertyValue $Document "mvJson" $(if ($mvJsons.Count -eq 0) { $null } else { $mvJsons[0] })
    Set-PropertyValue $Document "mvBytes" $(if ($mvBytes.Count -eq 0) { $null } else { $mvBytes[0] })
    Set-PropertyValue $Document "baselineAssembly" $(if ($baselines.Count -eq 0) { $null } else { $baselines[0] })
    Set-PropertyValue $Document "currentAssembly" $(if ($currents.Count -eq 0) { $null } else { $currents[0] })
    Set-PropertyValue $Document "mvJsons" $mvJsons
    Set-PropertyValue $Document "mvBytesList" $mvBytes
    Set-PropertyValue $Document "baselineAssemblies" $baselines
    Set-PropertyValue $Document "currentAssemblies" $currents
    Set-PropertyValue $Document "pathSemantics" "archive-relative-v1"
    if ($IncludeCoreReports) {
        Set-PropertyValue $Document "nativeManifest" "dhe-native-manifest.json"
        Set-PropertyValue $Document "buildIdentity" "build-identity.json"
        Set-PropertyValue $Document "workflowReport" "workflow-report.json"
        Set-PropertyValue $Document "runtimePlan" "runtime-plan/dhe-runtime-plan.json"
        Set-PropertyValue $Document "batchReport" "batch/dhe-batch-summary.json"
    }
}

function ConvertTo-DheUtcTimestamp($Value) {
    if ($null -eq $Value) {
        return $null
    }
    try {
        $timestamp = if ($Value -is [DateTimeOffset]) {
            [DateTimeOffset]$Value
        } elseif ($Value -is [DateTime]) {
            [DateTimeOffset]$Value
        } else {
            [DateTimeOffset]::Parse(
                [string]$Value,
                [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind)
        }
        return $timestamp.ToUniversalTime().ToString("O")
    } catch {
        throw "generatedAtUtc is not a valid ISO timestamp: $Value"
    }
}

function Normalize-DheGeneratedAtUtc($Document) {
    if ($null -eq $Document) {
        return
    }
    $property = $Document.PSObject.Properties["generatedAtUtc"]
    if ($null -ne $property) {
        Set-PropertyValue $Document "generatedAtUtc" (ConvertTo-DheUtcTimestamp $property.Value)
    }
}

function Normalize-ArchiveRelative([string]$Path) {
    if ([string]::IsNullOrWhiteSpace($Path) -or [IO.Path]::IsPathRooted($Path)) {
        throw "Archive destination must be a non-empty relative path: '$Path'"
    }
    $normalized = $Path.Replace('\', '/')
    if ($normalized -match '(^|/)\.\.(/|$)' -or $normalized.StartsWith('/')) {
        throw "Archive destination contains path traversal: '$Path'"
    }
    return $normalized.TrimStart('./')
}

function Resolve-InputFile([string]$Reference, [string]$Description, [string]$BaseDirectory = $inputPath) {
    if ([string]::IsNullOrWhiteSpace($Reference)) {
        throw "$Description is empty."
    }
    $candidate = if ([IO.Path]::IsPathRooted($Reference)) {
        [IO.Path]::GetFullPath($Reference)
    } else {
        [IO.Path]::GetFullPath((Join-Path $BaseDirectory $Reference))
    }
    if (-not [IO.File]::Exists($candidate)) {
        throw "$Description was not found: $candidate"
    }
    return $candidate
}

function Read-JsonFile([string]$Path, [string]$Description) {
    $resolved = Resolve-InputFile $Path $Description
    try {
        return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
    } catch {
        throw "$Description is not valid JSON: $resolved ($($_.Exception.Message))"
    }
}

function Copy-ArchiveFile([string]$Source, [string]$DestinationRelative) {
    $relative = Normalize-ArchiveRelative $DestinationRelative
    $destination = Join-Path $archivePath ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
    $directory = [IO.Path]::GetDirectoryName($destination)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    [IO.File]::Copy($Source, $destination, $true)
    return $relative
}

function Write-ArchiveJson([string]$RelativePath, $Document) {
    $relative = Normalize-ArchiveRelative $RelativePath
    $destination = Join-Path $archivePath ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))
    $directory = [IO.Path]::GetDirectoryName($destination)
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        [IO.Directory]::CreateDirectory($directory) | Out-Null
    }
    Normalize-DheGeneratedAtUtc $Document
    [IO.File]::WriteAllText($destination, ($Document | ConvertTo-Json -Depth 24), (New-Object Text.UTF8Encoding($false)))
    return $relative
}

function Assert-SafeAssemblyName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name) -or
        $Name.Contains('/') -or $Name.Contains('\') -or
        $Name.Contains('..') -or [IO.Path]::GetFileName($Name) -ne $Name) {
        throw "Assembly name is not safe for archive material: '$Name'"
    }
}

function Resolve-GeneratedSource([string]$Reference, [string]$NativeManifestDirectory) {
    if ([IO.Path]::IsPathRooted($Reference)) {
        return [IO.Path]::GetFullPath($Reference)
    }
    return [IO.Path]::GetFullPath((Join-Path $NativeManifestDirectory $Reference))
}

$workflowPath = Resolve-InputFile $WorkflowReport "workflow report"
$identityPath = Resolve-InputFile $BuildIdentity "build identity"
$nativeManifestPath = Resolve-InputFile $NativeManifest "native manifest"
$runtimePlanPath = Resolve-InputFile $RuntimePlan "runtime plan"
$projectPlanPath = Resolve-InputFile $ProjectPlan "project plan"
$projectPlanValidationPath = Resolve-InputFile $ProjectPlanValidation "project plan validation"

$workflow = Read-JsonFile $workflowPath "workflow report"
$identity = Read-JsonFile $identityPath "build identity"
$nativeManifestDocument = Read-JsonFile $nativeManifestPath "native manifest"
$runtimePlanDocument = Read-JsonFile $runtimePlanPath "runtime plan"
$projectPlanDocument = Read-JsonFile $projectPlanPath "project plan"
$projectPlanValidationDocument = Read-JsonFile $projectPlanValidationPath "project plan validation"
$workflowDirectory = [IO.Path]::GetDirectoryName($workflowPath)
$packageLockReference = [string](Get-PropertyValue $workflow "packageLock")
$packageLockPath = if ([string]::IsNullOrWhiteSpace($packageLockReference)) {
    $null
} else {
    Resolve-InputFile $packageLockReference "package lock" $workflowDirectory
}
$planBatchReference = [string](Get-PropertyValue $projectPlanDocument "batchReport")
if ([string]::IsNullOrWhiteSpace($planBatchReference)) {
    throw "Project plan has no batchReport reference."
}
$planDirectory = [IO.Path]::GetDirectoryName($projectPlanPath)
$planBatchPath = if ([IO.Path]::IsPathRooted($planBatchReference)) {
    [IO.Path]::GetFullPath($planBatchReference)
} else {
    [IO.Path]::GetFullPath((Join-Path $planDirectory $planBatchReference.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}
if (-not [string]::IsNullOrWhiteSpace($BatchReport)) {
    $requestedBatchReportPath = Resolve-InputFile $BatchReport "batch report argument"
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals([IO.Path]::GetFullPath($planBatchPath), [IO.Path]::GetFullPath($requestedBatchReportPath))) {
        throw "BatchReport argument does not match project plan batchReport: plan='$planBatchPath', argument='$requestedBatchReportPath'"
    }
}
$batchReportPath = $planBatchPath
$batchReportDocument = Read-JsonFile $batchReportPath "batch report"

$planScopeFields = @("requireDheEqualsHotUpdate", "hotUpdateAssemblies", "dheAotAssemblies", "dheEqualsHotUpdate")
foreach ($field in $planScopeFields) {
    if ($null -eq $projectPlanDocument.PSObject.Properties[$field]) {
        throw "Project plan is missing DHE/hot-update scope metadata: $field"
    }
}
$planRequiresDheCoverage = Get-DheStrictBooleanProperty $projectPlanDocument "requireDheEqualsHotUpdate" "Project plan requireDheEqualsHotUpdate"
$planHotUpdateNames = @((Get-PropertyValue $projectPlanDocument "hotUpdateAssemblies") |
    ForEach-Object { ([string]$_).Trim() } | Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
$planDheNames = @((Get-PropertyValue $projectPlanDocument "dheAotAssemblies") |
    ForEach-Object { ([string]$_).Trim() } | Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
if ($planRequiresDheCoverage -and (
        (Get-PropertyValue $projectPlanDocument "dheEqualsHotUpdate") -ne $true -or
        ($planHotUpdateNames -join ",") -ne ($planDheNames -join ","))) {
    throw "Project plan does not prove exact hot-update/DHE assembly coverage."
}
$planRecordNames = @((Get-PropertyValue $projectPlanDocument "assemblies") |
    ForEach-Object { ([string](Get-PropertyValue $_ "assemblyName")).Trim() } |
    Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
if ($planDheNames.Count -gt 0 -and ($planRecordNames -join ",") -ne ($planDheNames -join ",")) {
    throw "Project plan assembly records do not exactly match dheAotAssemblies metadata."
}

$sourcePreflightReference = [string](Get-PropertyValue $workflow "sourcePreflight")
$cleanCheckoutReference = [string](Get-PropertyValue $workflow "cleanCheckoutGate")
$playerResultReference = [string](Get-PropertyValue $workflow "playerResult")
$runtimeManifestReference = [string](Get-PropertyValue $workflow "runtimeManifest")
$sourcePreflightPath = Resolve-InputFile $sourcePreflightReference "source preflight report"
$cleanCheckoutPath = Resolve-InputFile $cleanCheckoutReference "clean checkout gate report"
$playerResultPath = Resolve-InputFile $playerResultReference "Player result"
$runtimeManifestPath = Resolve-InputFile $runtimeManifestReference "runtime manifest"
$runtimeManifestDocument = Read-JsonFile $runtimeManifestPath "runtime manifest"
$archiveYooAssetBuildRelative = $null
$yooAssetBuildReference = Get-NullableStringProperty $workflow "yooAssetBuild"
if (-not [string]::IsNullOrWhiteSpace($yooAssetBuildReference)) {
    $yooAssetBuildPath = Resolve-InputFile $yooAssetBuildReference "YooAsset build evidence" $workflowDirectory
    $yooAssetBuildDocument = Read-JsonFile $yooAssetBuildPath "YooAsset build evidence"
    if ($null -eq $yooAssetBuildDocument -or
        $null -eq $yooAssetBuildDocument.PSObject.Properties["passed"] -or
        $yooAssetBuildDocument.passed -ne $true) {
        throw "YooAsset build evidence did not pass: $yooAssetBuildPath"
    }

    # The source evidence points at a machine-local package directory. Keep
    # the raw YooAsset report in the archive, but rewrite those references so
    # the archived evidence remains portable and hash-verifiable.
    $rawYooReportReference = Get-NullableStringProperty $yooAssetBuildDocument "buildReport"
    if (-not [string]::IsNullOrWhiteSpace($rawYooReportReference)) {
        $rawYooReportPath = Resolve-InputFile $rawYooReportReference "YooAsset raw build report" ([IO.Path]::GetDirectoryName($yooAssetBuildPath))
        Copy-ArchiveFile $rawYooReportPath "yooasset/build-report.json" | Out-Null
        Set-PropertyValue $yooAssetBuildDocument "buildReport" "yooasset/build-report.json"
    } else {
        Set-PropertyValue $yooAssetBuildDocument "buildReport" $null
    }
    Set-PropertyValue $yooAssetBuildDocument "packageDirectory" $null
    Set-PropertyValue $yooAssetBuildDocument "pathSemantics" "archive-relative-v1"
    $archiveYooAssetBuildRelative = Write-ArchiveJson "yooasset/dhe-yooasset-build.json" $yooAssetBuildDocument
}

$archiveAssemblyRecords = New-Object System.Collections.Generic.List[object]
$archiveAssemblyByName = @{}
$settingsReference = [string](Get-PropertyValue $projectPlanDocument "settingsFile")
if ([string]::IsNullOrWhiteSpace($settingsReference)) { throw "Project plan has no settingsFile reference." }
$settingsSource = if ([IO.Path]::IsPathRooted($settingsReference)) {
    [IO.Path]::GetFullPath($settingsReference)
} else {
    [IO.Path]::GetFullPath((Join-Path $planDirectory $settingsReference.Replace('/', [IO.Path]::DirectorySeparatorChar)))
}
if (-not [IO.File]::Exists($settingsSource)) { throw "Project plan settingsFile was not found: $settingsSource" }
$settingsName = [IO.Path]::GetFileName($settingsSource)
if ([string]::IsNullOrWhiteSpace($settingsName) -or $settingsName -in @('.', '..') -or $settingsName.Contains('/') -or $settingsName.Contains('\')) {
    throw "Project plan settingsFile has an unsafe file name: $settingsName"
}
Copy-ArchiveFile $settingsSource ("project-settings/" + $settingsName) | Out-Null
foreach ($assembly in @($projectPlanDocument.assemblies)) {
    $assemblyName = [string](Get-PropertyValue $assembly "assemblyName")
    Assert-SafeAssemblyName $assemblyName
    if ($archiveAssemblyByName.ContainsKey($assemblyName)) {
        throw "Project plan contains duplicate assembly '$assemblyName'."
    }
    $archiveAssemblyByName[$assemblyName] = $assembly
    $baselineReference = [string](Get-PropertyValue $assembly "baseline")
    $currentReference = [string](Get-PropertyValue $assembly "current")
    # Keep the assembly basename intact so the independent validator can map
    # archive payloads back to MV assemblyName values. Baseline/current are
    # separated by directory rather than by a filename suffix (which could
    # collide with a legitimate assembly name such as Foo.current).
    $assemblyBaseName = "payload/assemblies/$assemblyName"
    $baselineDestination = Copy-ArchiveFile (Resolve-InputFile $baselineReference "Baseline assembly '$assemblyName'") "payload/assemblies/baseline/$assemblyName.dll"
    $currentDestination = Copy-ArchiveFile (Resolve-InputFile $currentReference "Current assembly '$assemblyName'") "payload/assemblies/current/$assemblyName.dll"
    $mvJsonDestination = $null
    $mvBytesDestination = $null
    $mvJsonReference = [string](Get-PropertyValue $assembly "mvJson")
    $mvBytesReference = [string](Get-PropertyValue $assembly "mvBytes")
    if (-not [string]::IsNullOrWhiteSpace($mvJsonReference)) {
        $mvJsonSource = Resolve-InputFile $mvJsonReference "MV JSON '$assemblyName'"
        $mvDocument = Get-Content -Raw -LiteralPath $mvJsonSource | ConvertFrom-Json
        Set-PropertyValue $mvDocument.baseline "path" "baseline/$assemblyName.dll"
        Set-PropertyValue $mvDocument.current "path" "current/$assemblyName.dll"
        Set-PropertyValue $mvDocument "pathSemantics" "archive-relative-v1"
        $mvJsonDestination = "$assemblyBaseName.mv.json"
        Write-ArchiveJson $mvJsonDestination $mvDocument | Out-Null
    }
    if (-not [string]::IsNullOrWhiteSpace($mvBytesReference)) {
        $mvBytesDestination = Copy-ArchiveFile (Resolve-InputFile $mvBytesReference "MV binary '$assemblyName'") "$assemblyBaseName.mv.bytes"
    }
    Set-PropertyValue $assembly "baseline" $baselineDestination
    Set-PropertyValue $assembly "current" $currentDestination
    Set-PropertyValue $assembly "mvJson" $mvJsonDestination
    Set-PropertyValue $assembly "mvBytes" $mvBytesDestination
    $archiveAssemblyRecords.Add([ordered]@{
        assemblyName = $assemblyName
        status = [string](Get-PropertyValue $assembly "status")
        baseline = $baselineDestination
        current = $currentDestination
        mvJson = $mvJsonDestination
        mvBytes = $mvBytesDestination
        changedMethodCount = [int](Get-PropertyValue $assembly "changedMethodCount")
    })
}

# Rewrite the plan's top-level material references alongside its per-assembly
# paths. These paths are intentionally relative to the archive root so the
# plan remains valid after the archive is copied to another machine.
Set-PropertyValue $projectPlanDocument "settingsFile" ("project-settings/" + $settingsName)
Set-PropertyValue $projectPlanDocument "baselineRoot" "payload/assemblies/baseline"
Set-PropertyValue $projectPlanDocument "currentRoot" "payload/assemblies/current"
Set-PropertyValue $projectPlanDocument "batchReport" "batch/dhe-batch-summary.json"

# Keep the runtime plan payloads beside its rewritten plan. The payload is
# intentionally the exact digest/snapshot contract consumed by the Player.
$archiveRuntimeRecords = New-Object System.Collections.Generic.List[object]
foreach ($record in @($runtimePlanDocument.assemblies)) {
    $assemblyName = [string](Get-PropertyValue $record "assemblyName")
    Assert-SafeAssemblyName $assemblyName
    $archiveRecord = [ordered]@{}
    foreach ($field in @("current", "baseline", "mv", "snapshot")) {
        $reference = [string](Get-PropertyValue $record $field)
        if ([IO.Path]::IsPathRooted($reference) -or
            $reference.Replace('\', '/') -match '(^|/)\.\.(/|$)' -or
            $reference.Replace('\', '/').Contains('/')) {
            throw "Runtime plan payload must be a single file name for '$assemblyName': $reference"
        }
        $source = Resolve-InputFile $reference "Runtime plan $field '$assemblyName'" ([IO.Path]::GetDirectoryName($runtimePlanPath))
        $fileName = [IO.Path]::GetFileName($reference)
        if ([string]::IsNullOrWhiteSpace($fileName)) {
            throw "Runtime plan payload has an unsafe file name for '$assemblyName': $reference"
        }
        Copy-ArchiveFile $source "runtime-plan/$fileName" | Out-Null
        $archiveRecord[$field] = $fileName
    }
    foreach ($field in @("baselineSha256", "currentSha256")) {
        $archiveRecord[$field] = [string](Get-PropertyValue $record $field)
    }
    $archiveRecord.assemblyName = $assemblyName
    $archiveRuntimeRecords.Add($archiveRecord)
}
$archiveRuntimePlan = [ordered]@{
    schemaVersion = [int](Get-PropertyValue $runtimePlanDocument "schemaVersion")
    format = [string](Get-PropertyValue $runtimePlanDocument "format")
    assemblies = $archiveRuntimeRecords.ToArray()
}
Write-ArchiveJson "runtime-plan/dhe-runtime-plan.json" $archiveRuntimePlan | Out-Null

# Copy each generated C++ source exactly once and rewrite sourceFile paths in
# the native manifest to be relative to the archive root.
$nativeRootReference = [string](Get-PropertyValue $nativeManifestDocument "generatedCppRoot")
if ([string]::IsNullOrWhiteSpace($nativeRootReference)) {
    throw "Native manifest has no generatedCppRoot."
}
$nativeManifestDirectory = [IO.Path]::GetDirectoryName($nativeManifestPath)
$sourceRoot = if ([IO.Path]::IsPathRooted($nativeRootReference)) {
    [IO.Path]::GetFullPath($nativeRootReference)
} else {
    [IO.Path]::GetFullPath((Join-Path $nativeManifestDirectory $nativeRootReference))
}
if (-not [IO.Directory]::Exists($sourceRoot)) {
    throw "Native generated C++ root was not found: $sourceRoot"
}
$sourceMap = @{}
$archiveGeneratedPaths = New-Object System.Collections.Generic.List[string]
foreach ($method in @($nativeManifestDocument.methods)) {
    $sourceReference = [string](Get-PropertyValue $method "sourceFile")
    if ([string]::IsNullOrWhiteSpace($sourceReference)) {
        throw "Native manifest method has no sourceFile."
    }
    $sourcePath = Resolve-GeneratedSource $sourceReference $nativeManifestDirectory
    $sourcePrefix = $sourceRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $sourcePath.StartsWith($sourcePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Native source is outside generatedCppRoot: $sourcePath"
    }
    if (-not [IO.File]::Exists($sourcePath)) {
        throw "Native generated C++ source was not found: $sourcePath"
    }
    if (-not $sourceMap.ContainsKey($sourcePath)) {
        $relativeSource = $sourcePath.Substring($sourceRoot.Length).TrimStart('\', '/').Replace('\', '/')
        $archiveRelative = Copy-ArchiveFile $sourcePath ("generated-cpp/" + $relativeSource)
        $sourceMap[$sourcePath] = $archiveRelative
        $archiveGeneratedPaths.Add($archiveRelative)
    }
    Set-PropertyValue $method "sourceFile" $sourceMap[$sourcePath]
}
Set-PropertyValue $nativeManifestDocument "generatedCppRoot" "generated-cpp"
Write-ArchiveJson "dhe-native-manifest.json" $nativeManifestDocument | Out-Null

$archiveGeneratedPaths = @($archiveGeneratedPaths | Sort-Object)
Set-PropertyValue $identity "generatedCppRoot" "generated-cpp"
Set-PropertyValue $identity "generatedCppPaths" $archiveGeneratedPaths
Set-PropertyValue $identity "generatedCppPath" $(if ($archiveGeneratedPaths.Count -eq 0) { $null } else { $archiveGeneratedPaths[0] })
Set-PropertyValue $identity "nativeManifestPath" "dhe-native-manifest.json"
Set-PropertyValue $identity "runtimeManifest" "runtime-manifest.json"
Set-PropertyValue $identity "runtimePlan" "runtime-plan/dhe-runtime-plan.json"
Set-PropertyValue $identity "runtimeSource" $null
Set-PropertyValue $identity "pathSemantics" "archive-relative-v1"
$archiveGeneratedAbsolutePaths = @($archiveGeneratedPaths | ForEach-Object {
    Join-Path $archivePath ($_.Replace('/', [IO.Path]::DirectorySeparatorChar))
})
Set-PropertyValue $identity "nativeGuardSourceSha256" (Get-DheFileSetHashOrEmpty $archiveGeneratedAbsolutePaths (Join-Path $archivePath "generated-cpp"))
Write-ArchiveJson "build-identity.json" $identity | Out-Null
$archiveNativeManifestPath = Join-Path $archivePath "dhe-native-manifest.json"
Set-PropertyValue $identity "nativeManifestSha256" ((Get-FileHash -LiteralPath $archiveNativeManifestPath -Algorithm SHA256).Hash.ToLowerInvariant())
Write-ArchiveJson "build-identity.json" $identity | Out-Null

# The assembled runtime manifest contains absolute workspace paths for local
# preflight. Those paths are not valid on the handoff machine and the runtime
# tree itself is deliberately not part of this artifact archive. Preserve the
# immutable source/engine/hash facts, but rewrite path-bearing provenance so a
# copied archive cannot appear to reference usable local inputs.
Set-PropertyValue $runtimeManifestDocument "pathSemantics" "archive-relative-v1"
Set-PropertyValue $runtimeManifestDocument "stagedLibil2cpp" $null
Set-PropertyValue $runtimeManifestDocument "dheRuntimeLock" "provenance/dhe-runtime-lock.json"
if ($null -ne $runtimeManifestDocument.engine) {
    Set-PropertyValue $runtimeManifestDocument.engine "executablePath" $null
}
if ($null -ne $runtimeManifestDocument.externalHeaders) {
    Set-PropertyValue $runtimeManifestDocument.externalHeaders "sourcePath" $null
    Set-PropertyValue $runtimeManifestDocument.externalHeaders "editorAvailable" $false
}
foreach ($sourceName in @("hybridclr", "il2cpp_plus", "hybridclr_unity")) {
    $sourceRoot = Get-PropertyValue $runtimeManifestDocument "source"
    $sourceEntry = Get-PropertyValue $sourceRoot $sourceName
    if ($null -ne $sourceEntry) {
        Set-PropertyValue $sourceEntry "path" $null
    }
}
foreach ($patch in @($runtimeManifestDocument.dhePatches)) {
    if ($null -ne $patch) {
        Set-PropertyValue $patch "path" $null
    }
}
Write-ArchiveJson "runtime-manifest.json" $runtimeManifestDocument | Out-Null
$sourcePreflightDocument = Get-Content -Raw -LiteralPath $sourcePreflightPath | ConvertFrom-Json
Set-PropertyValue $sourcePreflightDocument "pathSemantics" "archive-relative-v1"
Set-PropertyValue $sourcePreflightDocument "labRoot" $null
Set-PropertyValue $sourcePreflightDocument "projectPath" $null
Set-PropertyValue $sourcePreflightDocument "runtimeSource" $null
Set-PropertyValue $sourcePreflightDocument "packageLockPath" $(if ($null -eq $packageLockPath) { $null } else { "provenance/dhe-package-lock.json" })
Set-PropertyValue $sourcePreflightDocument "identityTemplatePath" $null
foreach ($check in @($sourcePreflightDocument.checks)) {
    $details = [string](Get-PropertyValue $check "details")
    if ($details -match '(?i)(?:^|[^A-Z0-9])(?:[A-Z]:[\\/]|\\\\[^\\/])') {
        Set-PropertyValue $check "details" "workspace path omitted from archive"
    }
}
Write-ArchiveJson "source-preflight-report.json" $sourcePreflightDocument | Out-Null

$cleanCheckoutDocument = Get-Content -Raw -LiteralPath $cleanCheckoutPath | ConvertFrom-Json
Set-PropertyValue $cleanCheckoutDocument "pathSemantics" "archive-relative-v1"
Set-PropertyValue $cleanCheckoutDocument "gitRoot" $null
Set-PropertyValue $cleanCheckoutDocument "sourceBoundaryPath" $null
foreach ($nullableIdentityField in @("gitHead", "gitTree", "sourceBoundarySha256", "vcs", "vcsRoot", "vcsRevision", "vcsRepository", "projectGit", "toolGit")) {
    if ($null -eq $cleanCheckoutDocument.PSObject.Properties[$nullableIdentityField]) {
        Set-PropertyValue $cleanCheckoutDocument $nullableIdentityField $null
    }
}
foreach ($identityName in @("projectGit", "toolGit")) {
    $identity = Get-PropertyValue $cleanCheckoutDocument $identityName
    if ($null -ne $identity) {
        Set-PropertyValue $identity "root" $null
        Set-PropertyValue $identity "ownedPath" $null
        Set-PropertyValue $identity "sourceBoundaryPath" $null
        Set-PropertyValue $identity "warnings" @()
    }
}
Write-ArchiveJson "clean-checkout-gate-report.json" $cleanCheckoutDocument | Out-Null
Copy-ArchiveFile $playerResultPath "dhe-player-result.json" | Out-Null

$archiveBatch = $batchReportDocument
Set-PropertyValue $archiveBatch "baselineRoot" "../payload/assemblies/baseline"
Set-PropertyValue $archiveBatch "currentRoot" "../payload/assemblies/current"
foreach ($batchAssembly in @($archiveBatch.assemblies)) {
    $assemblyName = [string](Get-PropertyValue $batchAssembly "assemblyName")
    if (-not $archiveAssemblyByName.ContainsKey($assemblyName)) { throw "Batch contains an unknown assembly '$assemblyName'." }
    $archiveAssembly = $archiveAssemblyRecords | Where-Object { $_.assemblyName -eq $assemblyName } | Select-Object -First 1
    # Batch references are resolved relative to batch/dhe-batch-summary.json.
    Set-PropertyValue $batchAssembly "baseline" ("../" + $archiveAssembly.baseline)
    Set-PropertyValue $batchAssembly "current" ("../" + $archiveAssembly.current)
    Set-PropertyValue $batchAssembly "report" ("../" + $archiveAssembly.mvJson)
    Set-PropertyValue $batchAssembly "binary" ("../" + $archiveAssembly.mvBytes)
}
Write-ArchiveJson "batch/dhe-batch-summary.json" $archiveBatch | Out-Null

Write-ArchiveJson "project-plan.json" $projectPlanDocument | Out-Null
Set-PropertyValue $projectPlanValidationDocument "plan" "project-plan.json"
foreach ($validationAssembly in @($projectPlanValidationDocument.assemblies)) {
    $assemblyName = [string](Get-PropertyValue $validationAssembly "assemblyName")
    $validationName = "$assemblyName.json"
    if (Test-Path -LiteralPath (Join-Path $archivePath "plan-validation/$validationName") -PathType Leaf) {
        Set-PropertyValue $validationAssembly "validationReport" ("plan-validation/" + $validationName)
    }
}
Write-ArchiveJson "project-plan-validation.json" $projectPlanValidationDocument | Out-Null

# The archived plan must be independently valid against only archive-relative
# payloads. Re-run the plan validator after rewriting all source references so
# the portable archive does not rely on the original workspace paths.
$archivedPlanValidationPath = Join-Path $archivePath "project-plan-validation.json"
& (Resolve-DhePowerShellHost) -NoProfile -ExecutionPolicy Bypass -File (Join-Path $labPath "scripts/validate-dhe-project-plan.ps1") `
    -Plan (Join-Path $archivePath "project-plan.json") `
    -RequireCompleteCoverage `
    -Output $archivedPlanValidationPath | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Archived project plan failed independent validation. See $archivedPlanValidationPath"
}
$projectPlanValidationDocument = Get-Content -Raw -LiteralPath $archivedPlanValidationPath | ConvertFrom-Json
Set-PropertyValue $projectPlanValidationDocument "plan" "project-plan.json"
Set-PropertyValue $projectPlanValidationDocument "pathSemantics" "archive-relative-v1"
foreach ($validationAssembly in @($projectPlanValidationDocument.assemblies)) {
    $assemblyName = [string](Get-PropertyValue $validationAssembly "assemblyName")
    $validationRelative = "plan-validation/$assemblyName.json"
    $validationPath = Join-Path $archivePath $validationRelative.Replace('/', [IO.Path]::DirectorySeparatorChar)
    if (-not [IO.File]::Exists($validationPath)) {
        throw "Archived plan validation report was not produced for '$assemblyName': $validationPath"
    }
    $validationDocument = Get-Content -Raw -LiteralPath $validationPath | ConvertFrom-Json
    $archiveAssembly = @($archiveAssemblyRecords | Where-Object { $_.assemblyName -eq $assemblyName })
    if ($archiveAssembly.Count -ne 1) {
        throw "Archived plan validation references an unknown assembly '$assemblyName'."
    }
    Set-ArchiveValidationReferences -Document $validationDocument -AssemblyRecords $archiveAssembly -Prefix "../"
    Write-ArchiveJson $validationRelative $validationDocument | Out-Null
    Set-PropertyValue $validationAssembly "validationReport" $validationRelative
}
Write-ArchiveJson "project-plan-validation.json" $projectPlanValidationDocument | Out-Null

$archiveWorkflow = $workflow
$archiveWorkflow | Add-Member -NotePropertyName projectPlan -NotePropertyValue "project-plan.json" -Force
$archiveWorkflow | Add-Member -NotePropertyName projectPlanValidation -NotePropertyValue "project-plan-validation.json" -Force
$archiveWorkflow | Add-Member -NotePropertyName batchReport -NotePropertyValue "batch/dhe-batch-summary.json" -Force
Set-PropertyValue $archiveWorkflow "sourcePreflight" "source-preflight-report.json"
Set-PropertyValue $archiveWorkflow "cleanCheckoutGate" "clean-checkout-gate-report.json"
Set-PropertyValue $archiveWorkflow "playerResult" "dhe-player-result.json"
Set-PropertyValue $archiveWorkflow "runtimeManifest" "runtime-manifest.json"
Set-PropertyValue $archiveWorkflow "runtimeSource" $null
Set-PropertyValue $archiveWorkflow "pathSemantics" "archive-relative-v1"
Set-PropertyValue $archiveWorkflow "buildIdentity" "build-identity.json"
Set-PropertyValue $archiveWorkflow "nativeManifest" "dhe-native-manifest.json"
Set-PropertyValue $archiveWorkflow "runtimePlan" "runtime-plan/dhe-runtime-plan.json"
Set-PropertyValue $archiveWorkflow "runtimePlanProjectPath" "runtime-plan/dhe-runtime-plan.json"
Set-PropertyValue $archiveWorkflow "archiveManifest" "archive-manifest.json"
Set-PropertyValue $archiveWorkflow "archiveGate" $null
Set-PropertyValue $archiveWorkflow "yooAssetBuild" $archiveYooAssetBuildRelative
Set-PropertyValue $archiveWorkflow "mvJson" @($archiveAssemblyRecords | ForEach-Object { $_.mvJson })
Set-PropertyValue $archiveWorkflow "mvBytes" @($archiveAssemblyRecords | ForEach-Object { $_.mvBytes })
Set-PropertyValue $archiveWorkflow "artifactValidation" "artifact-validation.json"
Set-PropertyValue $archiveWorkflow "packageLock" $(if ($null -eq $packageLockPath) { $null } else { "provenance/dhe-package-lock.json" })
Set-PropertyValue $archiveWorkflow "identityVersion" ([int](Get-PropertyValue $identity "identityVersion"))
Set-PropertyValue $archiveWorkflow "aotSnapshotKind" ([string](Get-PropertyValue $identity "aotSnapshotKind"))
Set-PropertyValue $archiveWorkflow "nativeGuardSourceSha256" ([string](Get-PropertyValue $identity "nativeGuardSourceSha256"))
Set-PropertyValue $archiveWorkflow "nativeManifestSha256" ([string](Get-PropertyValue $identity "nativeManifestSha256"))
if ($null -ne $packageLockPath) {
    Copy-ArchiveFile $packageLockPath "provenance/dhe-package-lock.json" | Out-Null
}
if ([IO.File]::Exists((Join-Path $labPath "manifests/dhe-runtime-lock.json"))) {
    Copy-ArchiveFile (Join-Path $labPath "manifests/dhe-runtime-lock.json") "provenance/dhe-runtime-lock.json" | Out-Null
}
# The source workflow report may have been downgraded by a previous failed
# archive attempt. Artifact validation below is the authoritative check for
# this archive, so seed only the pre-archive dimensions before the first
# validator pass; the final values are written after that pass succeeds.
$sourceValidationPassed = Get-DheStrictBooleanProperty $workflow "validationPassed" "Workflow validationPassed"
$sourceCoverageGatePassed = Get-DheStrictBooleanProperty $workflow "coverageGatePassed" "Workflow coverageGatePassed"
Set-PropertyValue $archiveWorkflow "artifactValidationPassed" $true
Set-PropertyValue $archiveWorkflow "passed" ($sourceValidationPassed -and $sourceCoverageGatePassed)
Write-ArchiveJson "workflow-report.json" $archiveWorkflow | Out-Null

$validator = Join-Path $labPath "scripts/validate-dhe-artifacts.ps1"
$archiveMvJson = @($archiveAssemblyRecords | Where-Object { -not [string]::IsNullOrWhiteSpace($_.mvJson) } | ForEach-Object { Join-Path $archivePath $_.mvJson.Replace('/', [IO.Path]::DirectorySeparatorChar) })
$archiveMvBytes = @($archiveAssemblyRecords | Where-Object { -not [string]::IsNullOrWhiteSpace($_.mvBytes) } | ForEach-Object { Join-Path $archivePath $_.mvBytes.Replace('/', [IO.Path]::DirectorySeparatorChar) })
$archiveBaseline = @($archiveAssemblyRecords | ForEach-Object { Join-Path $archivePath $_.baseline.Replace('/', [IO.Path]::DirectorySeparatorChar) })
$archiveCurrent = @($archiveAssemblyRecords | ForEach-Object { Join-Path $archivePath $_.current.Replace('/', [IO.Path]::DirectorySeparatorChar) })
& $validator `
    -MvJsonList (ConvertTo-DheStringListArgument $archiveMvJson) `
    -MvBytesList (ConvertTo-DheStringListArgument $archiveMvBytes) `
    -BaselineAssemblyList (ConvertTo-DheStringListArgument $archiveBaseline) `
    -CurrentAssemblyList (ConvertTo-DheStringListArgument $archiveCurrent) `
    -NativeManifest (Join-Path $archivePath "dhe-native-manifest.json") `
    -BuildIdentity (Join-Path $archivePath "build-identity.json") `
    -WorkflowReport (Join-Path $archivePath "workflow-report.json") `
    -RuntimePlan (Join-Path $archivePath "runtime-plan/dhe-runtime-plan.json") `
    -BatchReport (Join-Path $archivePath "batch/dhe-batch-summary.json") `
    -Output (Join-Path $archivePath "artifact-validation.json")
$validatorExitCode = $LASTEXITCODE
if ($validatorExitCode -ne 0) {
    throw "Archived DHE artifacts failed independent validation. See $archivePath/artifact-validation.json"
}
$archiveValidation = Get-Content -Raw -LiteralPath (Join-Path $archivePath "artifact-validation.json") | ConvertFrom-Json
Set-PropertyValue $archiveWorkflow "artifactValidationPassed" (Get-DheStrictBooleanProperty $archiveValidation "passed" "Archived artifact validation passed")
Set-PropertyValue $archiveWorkflow "passed" ((Get-DheStrictBooleanProperty $archiveValidation "passed" "Archived artifact validation passed") -and $sourceValidationPassed -and $sourceCoverageGatePassed)
Write-ArchiveJson "workflow-report.json" $archiveWorkflow | Out-Null

# Validate the final rewritten workflow report once more after publishing it.
& $validator `
    -MvJsonList (ConvertTo-DheStringListArgument $archiveMvJson) `
    -MvBytesList (ConvertTo-DheStringListArgument $archiveMvBytes) `
    -BaselineAssemblyList (ConvertTo-DheStringListArgument $archiveBaseline) `
    -CurrentAssemblyList (ConvertTo-DheStringListArgument $archiveCurrent) `
    -NativeManifest (Join-Path $archivePath "dhe-native-manifest.json") `
    -BuildIdentity (Join-Path $archivePath "build-identity.json") `
    -WorkflowReport (Join-Path $archivePath "workflow-report.json") `
    -RuntimePlan (Join-Path $archivePath "runtime-plan/dhe-runtime-plan.json") `
    -BatchReport (Join-Path $archivePath "batch/dhe-batch-summary.json") `
    -Output (Join-Path $archivePath "artifact-validation.json")
if ($LASTEXITCODE -ne 0) {
    throw "Archived DHE workflow report failed independent validation."
}
$finalArtifactValidationPath = Join-Path $archivePath "artifact-validation.json"
$finalArtifactValidation = Get-Content -Raw -LiteralPath $finalArtifactValidationPath | ConvertFrom-Json
Set-ArchiveValidationReferences -Document $finalArtifactValidation -AssemblyRecords $archiveAssemblyRecords.ToArray() -IncludeCoreReports
Write-ArchiveJson "artifact-validation.json" $finalArtifactValidation | Out-Null

$archiveFiles = @(Get-ChildItem -LiteralPath $archivePath -Recurse -File -Force |
    Where-Object { $_.Name -ne "archive-manifest.json" })
$archiveFileRecords = New-Object System.Collections.Generic.List[object]
foreach ($file in $archiveFiles) {
    $relative = $file.FullName.Substring($archivePath.Length).TrimStart('\', '/').Replace('\', '/')
    $archiveFileRecords.Add([ordered]@{
        path = $relative
        size = [int64]$file.Length
        sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    })
}
$archiveFileRecords = @($archiveFileRecords | Sort-Object path)
$archiveFilePaths = @($archiveFiles | ForEach-Object { $_.FullName })
$archiveSourceIdentities = [ordered]@{}
foreach ($identityName in @("projectGit", "toolGit")) {
    $identity = Get-PropertyValue $cleanCheckoutDocument $identityName
    $archiveSourceIdentities[$identityName] = if ($null -eq $identity) {
        $null
    } else {
        [ordered]@{
            vcs = Get-NullableStringProperty $identity "vcs"
            head = Get-NullableStringProperty $identity "head"
            tree = Get-NullableStringProperty $identity "tree"
            revision = Get-NullableStringProperty $identity "revision"
            revisionSpec = Get-NullableStringProperty $identity "revisionSpec"
            repository = Get-NullableStringProperty $identity "repository"
            sourceBoundarySha256 = Get-NullableStringProperty $identity "sourceBoundarySha256"
        }
    }
}
$archiveManifest = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-archive-manifest.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    # The source workspace is not an archive payload. Keeping its absolute
    # path would make a copied handoff look locally usable when it is not.
    sourceWorkflowRoot = $null
    pathSemantics = "archive-relative-v1"
    sourceIdentities = $archiveSourceIdentities
    workflowReport = "workflow-report.json"
    artifactValidation = "artifact-validation.json"
    buildIdentity = "build-identity.json"
    nativeManifest = "dhe-native-manifest.json"
    runtimeManifest = "runtime-manifest.json"
    runtimePlan = "runtime-plan/dhe-runtime-plan.json"
    yooAssetBuild = $archiveYooAssetBuildRelative
    projectPlan = "project-plan.json"
    projectPlanValidation = "project-plan-validation.json"
    generatedCppRoot = "generated-cpp"
    generatedCppPaths = $archiveGeneratedPaths
    assemblies = $archiveAssemblyRecords.ToArray()
    files = $archiveFileRecords
    fileCount = $archiveFileRecords.Count
    fileSetSha256 = Get-DheFileSetHash $archiveFilePaths $archivePath
}
Write-ArchiveJson "archive-manifest.json" $archiveManifest | Out-Null
Write-Host "DHE artifact archive created: $(Join-Path $archivePath 'archive-manifest.json')"
exit 0
