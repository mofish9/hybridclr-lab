[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$OutputRoot = "",
    [string[]]$InputRoot = @(),
    [string]$InputRootList = "",
    [string]$InputRootListBase64 = "",
    [string[]]$AdditionalSchemaRoot = @(),
    [string]$AdditionalSchemaRootList = "",
    [string]$AdditionalSchemaRootListBase64 = "",
    [switch]$ForceOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$LabRoot = if ([string]::IsNullOrWhiteSpace($LabRoot)) {
    Split-Path -Parent $PSScriptRoot
} else {
    [IO.Path]::GetFullPath($LabRoot)
}
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-schema-gate"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}
if (-not [string]::IsNullOrWhiteSpace($InputRootList) -and
    -not [string]::IsNullOrWhiteSpace($InputRootListBase64)) {
    throw "Pass only one of -InputRootList or -InputRootListBase64."
}
if (-not [string]::IsNullOrWhiteSpace($InputRootListBase64)) {
    try {
        $InputRootList = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($InputRootListBase64))
    } catch {
        throw "DHE schema input-root list is not valid base64: $($_.Exception.Message)"
    }
}
if (-not [string]::IsNullOrWhiteSpace($InputRootList)) {
    $InputRoot = ConvertFrom-DheStringListArgument -Value $InputRootList
}
$InputRoot = @($InputRoot | ForEach-Object { [IO.Path]::GetFullPath($_) } | Select-Object -Unique)
if (-not [string]::IsNullOrWhiteSpace($AdditionalSchemaRootList) -and
    -not [string]::IsNullOrWhiteSpace($AdditionalSchemaRootListBase64)) {
    throw "Pass only one of -AdditionalSchemaRootList or -AdditionalSchemaRootListBase64."
}
if ($AdditionalSchemaRoot.Count -gt 0 -and
    (-not [string]::IsNullOrWhiteSpace($AdditionalSchemaRootList) -or
     -not [string]::IsNullOrWhiteSpace($AdditionalSchemaRootListBase64))) {
    throw "Pass either -AdditionalSchemaRoot or an additional-schema-root list, not both."
}
if (-not [string]::IsNullOrWhiteSpace($AdditionalSchemaRootListBase64)) {
    try {
        $AdditionalSchemaRootList = [Text.Encoding]::UTF8.GetString(
            [Convert]::FromBase64String($AdditionalSchemaRootListBase64))
    } catch {
        throw "DHE additional-schema-root list is not valid base64: $($_.Exception.Message)"
    }
}
if (-not [string]::IsNullOrWhiteSpace($AdditionalSchemaRootList)) {
    $AdditionalSchemaRoot = ConvertFrom-DheStringListArgument -Value $AdditionalSchemaRootList
}
$AdditionalSchemaRoot = @($AdditionalSchemaRoot | ForEach-Object {
        [IO.Path]::GetFullPath($_)
    } | Select-Object -Unique)

# Test-Json's draft 2020-12 implementation and duplicate-property-aware
# Newtonsoft parser are guaranteed by pwsh, not Windows PowerShell 5.1.
if ($PSVersionTable.PSEdition -ne "Core") {
    $pwsh = Get-Command pwsh -ErrorAction SilentlyContinue
    if ($null -eq $pwsh) {
        throw "DHE schema validation requires PowerShell 7 (pwsh)."
    }
    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $PSCommandPath,
        "-LabRoot", $LabRoot,
        "-OutputRoot", $OutputRoot
    )
    if ($InputRoot.Count -gt 0) {
        $inputRootJson = ConvertTo-DheStringListArgument -Values $InputRoot
        $inputRootBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($inputRootJson))
        $arguments += @("-InputRootListBase64", $inputRootBase64)
    }
    if ($AdditionalSchemaRoot.Count -gt 0) {
        $schemaRootJson = ConvertTo-DheStringListArgument -Values $AdditionalSchemaRoot
        $schemaRootBase64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($schemaRootJson))
        $arguments += @("-AdditionalSchemaRootListBase64", $schemaRootBase64)
    }
    if ($ForceOutput) { $arguments += "-ForceOutput" }
    & $pwsh.Source @arguments
    exit $LASTEXITCODE
}

Assert-DheSafeOutputRoot -Path $OutputRoot
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
$null = Initialize-DheOutputRoot -Path $OutputRoot -Force:$ForceOutput

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$schemaRecords = New-Object System.Collections.Generic.List[object]
$documentRecords = New-Object System.Collections.Generic.List[object]
$formatSchemas = New-Object 'System.Collections.Generic.Dictionary[string,string]'([StringComparer]::Ordinal)
$schemaByName = @{}
$jsonLoadSettings = New-Object Newtonsoft.Json.Linq.JsonLoadSettings
$jsonLoadSettings.DuplicatePropertyNameHandling = [Newtonsoft.Json.Linq.DuplicatePropertyNameHandling]::Error

function Read-StrictJson([string]$Path, [string]$Description) {
    $raw = [IO.File]::ReadAllText($Path)
    try {
        $null = [Newtonsoft.Json.Linq.JToken]::Parse($raw, $jsonLoadSettings)
    } catch {
        throw "$Description contains invalid JSON or a duplicate property: $Path ($($_.Exception.Message))"
    }
    try {
        return $raw | ConvertFrom-Json
    } catch {
        throw "$Description cannot be materialized as JSON: $Path ($($_.Exception.Message))"
    }
}

function Get-JsonProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

$schemaRoot = [IO.Path]::GetFullPath((Join-Path $LabRoot "schemas"))
$schemaRoots = @($schemaRoot) + @($AdditionalSchemaRoot | Where-Object {
        -not $_.Equals($schemaRoot, [StringComparison]::OrdinalIgnoreCase)
    })
foreach ($currentSchemaRoot in $schemaRoots) {
    if (-not [IO.Directory]::Exists($currentSchemaRoot)) {
        $errors.Add("DHE schema root was not found: $currentSchemaRoot")
        continue
    }
    foreach ($schemaFile in @(Get-ChildItem -LiteralPath $currentSchemaRoot -File -Filter "dhe-*.schema.json" | Sort-Object Name)) {
    $passed = $false
    $detail = ""
    try {
        $schema = Read-StrictJson $schemaFile.FullName "DHE schema"
        if ([string](Get-JsonProperty $schema '$schema') -ne "https://json-schema.org/draft/2020-12/schema" -or
            [string]::IsNullOrWhiteSpace([string](Get-JsonProperty $schema '$id')) -or
            [string](Get-JsonProperty $schema "type") -ne "object") {
            throw "Schema must declare draft 2020-12, a non-empty `$id, and type=object."
        }
        if ($schemaByName.ContainsKey($schemaFile.Name)) {
            $existingSchemaPath = [string]$schemaByName[$schemaFile.Name]
            $existingSchemaHash = (Get-FileHash -LiteralPath $existingSchemaPath -Algorithm SHA256).Hash
            $candidateSchemaHash = (Get-FileHash -LiteralPath $schemaFile.FullName -Algorithm SHA256).Hash
            if ([StringComparer]::OrdinalIgnoreCase.Equals($existingSchemaHash, $candidateSchemaHash)) {
                continue
            }
            throw "Schema file name '$($schemaFile.Name)' conflicts with $existingSchemaPath."
        }
        $schemaByName[$schemaFile.Name] = $schemaFile.FullName
        $formatDefinition = Get-JsonProperty (Get-JsonProperty $schema "properties") "format"
        $formatConst = [string](Get-JsonProperty $formatDefinition "const")
        if (-not [string]::IsNullOrWhiteSpace($formatConst)) {
            if ($formatSchemas.ContainsKey($formatConst)) {
                throw "Schema format '$formatConst' is already owned by $($formatSchemas[$formatConst])."
            }
            $formatSchemas.Add($formatConst, $schemaFile.FullName)
        }
        $passed = $true
        $detail = if ([string]::IsNullOrWhiteSpace($formatConst)) { "strict JSON; structural mapping" } else { "format=$formatConst" }
    } catch {
        $detail = $_.Exception.Message
        $errors.Add($detail)
    }
    $schemaRecords.Add([ordered]@{
        path = $schemaFile.FullName
        passed = $passed
        detail = $detail
    })
    }
}

$requiredSchemaNames = @(
    "dhe-build-identity.schema.json",
    "dhe-native-manifest.schema.json",
    "dhe-runtime-manifest.schema.json",
    "dhe-workflow-report.schema.json"
)
foreach ($requiredSchemaName in $requiredSchemaNames) {
    if (-not $schemaByName.ContainsKey($requiredSchemaName)) {
        $errors.Add("Required structural DHE schema is missing: $requiredSchemaName")
    }
}

$candidateFiles = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
foreach ($manifestPath in @(
    (Join-Path $LabRoot "manifests/dhe-runtime-lock.json"),
    (Join-Path $LabRoot "manifests/dhe-package-lock.json"),
    (Join-Path $LabRoot "manifests/dhe-source-boundary.json"),
    (Join-Path $LabRoot "manifests/dhe-toolchain-layout.json"),
    (Join-Path $LabRoot "dhe-toolchain-manifest.json"),
    (Join-Path $LabRoot "dhe-source-boundary.json")
)) {
    if ([IO.File]::Exists($manifestPath)) { $null = $candidateFiles.Add([IO.Path]::GetFullPath($manifestPath)) }
}
foreach ($inputPath in $InputRoot) {
    if (-not [IO.Directory]::Exists($inputPath)) {
        $errors.Add("DHE schema input root was not found: $inputPath")
        continue
    }
    foreach ($jsonFile in @(Get-ChildItem -LiteralPath $inputPath -Recurse -File -Filter "*.json" -ErrorAction SilentlyContinue)) {
        if (-not $jsonFile.FullName.StartsWith($OutputRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
            $null = $candidateFiles.Add($jsonFile.FullName)
        }
    }
}

$skippedDocumentCount = 0
foreach ($documentPath in @($candidateFiles | Sort-Object)) {
    $document = $null
    try {
        $document = Read-StrictJson $documentPath "DHE JSON document"
    } catch {
        $errors.Add($_.Exception.Message)
        $documentRecords.Add([ordered]@{ path = $documentPath; schema = $null; passed = $false; detail = $_.Exception.Message })
        continue
    }

    $schemaPath = $null
    $format = [string](Get-JsonProperty $document "format")
    if (-not [string]::IsNullOrWhiteSpace($format)) {
        if ($formatSchemas.ContainsKey($format)) {
            $schemaPath = $formatSchemas[$format]
        } elseif ($format -match '^hybridclr\.dhe-[A-Za-z0-9._-]+-workflow\.json$') {
            $schemaPath = $schemaByName["dhe-workflow-report.schema.json"]
        } elseif ($format -match '^hybridclr\.dhe-[A-Za-z0-9._-]+-workflow-failure\.json$') {
            $schemaPath = $schemaByName["dhe-workflow-failure.schema.json"]
        } elseif ($format.StartsWith("hybridclr.dhe", [StringComparison]::Ordinal)) {
            $errors.Add("No DHE schema is registered for format '$format': $documentPath")
            $documentRecords.Add([ordered]@{ path = $documentPath; schema = $null; passed = $false; detail = "unknown DHE format: $format" })
            continue
        }
    } elseif ($null -ne (Get-JsonProperty $document "identityVersion") -and
        $null -ne (Get-JsonProperty $document "aotSnapshotKind") -and
        $null -ne (Get-JsonProperty $document "workflow") -and
        $null -ne (Get-JsonProperty $document "generatedCppPaths") -and
        $null -ne (Get-JsonProperty $document "nativeManifestPath")) {
        $schemaPath = $schemaByName["dhe-build-identity.schema.json"]
    } elseif ($null -ne (Get-JsonProperty $document "resolverVersion") -and
        $null -ne (Get-JsonProperty $document "abiContract")) {
        $schemaPath = $schemaByName["dhe-native-manifest.schema.json"]
    } elseif ($null -ne (Get-JsonProperty $document "dheEnabled") -and
        $null -ne (Get-JsonProperty $document "engineWorkflow")) {
        $schemaPath = $schemaByName["dhe-runtime-manifest.schema.json"]
    }

    if ([string]::IsNullOrWhiteSpace([string]$schemaPath)) {
        $skippedDocumentCount++
        continue
    }

    $valid = $false
    $detail = ""
    try {
        $validationErrors = @()
        $valid = [bool](Test-Json -LiteralPath $documentPath -SchemaFile $schemaPath -ErrorVariable validationErrors 2>$null)
        if (-not $valid) {
            $detail = (@($validationErrors | ForEach-Object { $_.Exception.Message }) -join " | ")
            if ([string]::IsNullOrWhiteSpace($detail)) { $detail = "JSON Schema validation returned false." }
            throw $detail
        }
        $detail = "schema validation passed"
    } catch {
        $valid = $false
        if ([string]::IsNullOrWhiteSpace($detail)) { $detail = $_.Exception.Message }
        $errors.Add("DHE document failed schema validation: $documentPath ($detail)")
    }
    $documentRecords.Add([ordered]@{
        path = $documentPath
        schema = $schemaPath
        passed = $valid
        detail = $detail
    })
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-schema-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    schemaCount = $schemaRecords.Count
    documentCount = $documentRecords.Count
    skippedDocumentCount = $skippedDocumentCount
    inputRoots = $InputRoot
    schemaRoots = $schemaRoots
    schemas = $schemaRecords.ToArray()
    documents = $documentRecords.ToArray()
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
}
$reportPath = Join-Path $OutputRoot "schema-gate-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE schema gate: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE schema gate failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host ("DHE schemas: {0}; validated documents: {1}; skipped JSON: {2}" -f $report.schemaCount, $report.documentCount, $report.skippedDocumentCount)
exit 0
