[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PackageRoot,
    [string]$Output = "",
    [switch]$RequireRelease,
    [ValidatePattern("^$|^[0-9a-fA-F]{64}$")]
    [string]$ExpectedPackageId = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$PackageRoot = [IO.Path]::GetFullPath($PackageRoot)
$Output = if ([string]::IsNullOrWhiteSpace($Output)) {
    New-DheTemporaryReportPath "package-gate"
} else { [IO.Path]::GetFullPath($Output) }
Assert-DheSafeReportPath -Path $Output -ProtectedPaths @($PackageRoot) | Out-Null

$manifestPath = Join-Path $PackageRoot "dhe-toolchain-manifest.json"
$layoutPath = Join-Path $PackageRoot "manifests/dhe-toolchain-layout.json"
$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$manifest = $null
$layout = $null
$hashesValid = $true
$scriptsValid = $true
$jsonValid = $true
$schemaValid = $true
$boundaryValid = $true
$layoutValid = $true
$releaseIdentityValid = $true
$packageIdValid = $true
$packageTreeSafe = $true
$expectedFileCount = 0
$actualFileCount = 0
$packageId = $null
$verifiedPackageFiles = @()

function Add-Error([string]$Message) { $errors.Add($Message) }
function Get-PackageProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}
function Is-SafePackagePath([string]$Value) {
    return -not [string]::IsNullOrWhiteSpace($Value) -and
        -not [IO.Path]::IsPathRooted($Value) -and
        $Value.Replace('\', '/') -notmatch '(^|/)\.\.(/|$)'
}
function Test-SequenceEqual([object[]]$Left, [object[]]$Right) {
    if (@($Left).Count -ne @($Right).Count) { return $false }
    for ($index = 0; $index -lt @($Left).Count; $index++) {
        if (-not [StringComparer]::Ordinal.Equals([string]$Left[$index], [string]$Right[$index])) { return $false }
    }
    return $true
}
function Find-MachineLocalJsonString($Value, [string]$JsonPath, [System.Collections.Generic.List[string]]$Matches) {
    if ($null -eq $Value) { return }
    if ($Value -is [string]) {
        $text = [string]$Value
        if ($text -match '^[A-Za-z]:[\\/]' -or $text -match '^(\\\\|//)[^\\/]') {
            $Matches.Add("${JsonPath}=$text")
        }
        return
    }
    $valueType = $Value.GetType()
    if ($valueType.IsPrimitive -or $Value -is [decimal] -or $Value -is [DateTime] -or
        $Value -is [DateTimeOffset] -or $Value -is [Guid]) {
        return
    }
    if ($Value -is [Collections.IDictionary]) {
        foreach ($key in $Value.Keys) {
            Find-MachineLocalJsonString $Value[$key] ("{0}.{1}" -f $JsonPath, [string]$key) $Matches
        }
        return
    }
    if ($Value -is [Collections.IEnumerable]) {
        $index = 0
        foreach ($item in $Value) {
            Find-MachineLocalJsonString $item ("{0}[{1}]" -f $JsonPath, $index) $Matches
            $index++
        }
        return
    }
    foreach ($property in @($Value.PSObject.Properties | Where-Object {
            $_.MemberType -in @("NoteProperty", "Property")
        })) {
        Find-MachineLocalJsonString $property.Value ("{0}.{1}" -f $JsonPath, $property.Name) $Matches
    }
}
function Test-LayoutSelectsPath($Layout, [string]$RelativePath) {
    $normalized = $RelativePath.Replace('\', '/')
    if ($normalized -in @($Layout.exactPaths) -or $normalized -in @($Layout.generatedPaths)) { return $true }
    foreach ($prefixValue in @($Layout.prefixes)) {
        $prefix = ([string]$prefixValue).Replace('\', '/')
        if ($prefix.Contains('*')) {
            if ($normalized -like $prefix) { return $true }
        } else {
            $trimmed = $prefix.TrimEnd('/')
            if ($normalized.Equals($trimmed, [StringComparison]::OrdinalIgnoreCase) -or
                $normalized.StartsWith($trimmed + '/', [StringComparison]::OrdinalIgnoreCase)) { return $true }
        }
    }
    return $false
}

if (-not (Test-Path -LiteralPath $PackageRoot -PathType Container)) {
    Add-Error "DHE toolchain package root was not found: $PackageRoot"
} else {
    try {
        $verifiedPackageFiles = @(Get-DheRegularTreeFiles -Root $PackageRoot)
    } catch {
        $packageTreeSafe = $false
        Add-Error $_.Exception.Message
    }
    if ($packageTreeSafe) {
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
            Add-Error "DHE toolchain manifest was not found: $manifestPath"
        } else {
            try { $manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json }
            catch { Add-Error "DHE toolchain manifest is not valid JSON: $manifestPath" }
        }
    }
}

$records = @()
$recordPaths = @()
$releaseReadyValue = $false
if ($null -ne $manifest) {
    $schemaVersion = Get-PackageProperty $manifest "schemaVersion"
    $format = [string](Get-PackageProperty $manifest "format")
    $toolchainVersion = [string](Get-PackageProperty $manifest "toolchainVersion")
    $contractVersion = Get-PackageProperty $manifest "contractVersion"
    $mode = [string](Get-PackageProperty $manifest "mode")
    $pathSemantics = [string](Get-PackageProperty $manifest "pathSemantics")
    $entryPoint = [string](Get-PackageProperty $manifest "entryPoint")
    $releaseReadyProperty = Get-PackageProperty $manifest "releaseReady"
    $releaseReadyValue = $releaseReadyProperty -is [bool] -and [bool]$releaseReadyProperty
    $packageId = [string](Get-PackageProperty $manifest "packageId")

    if (($schemaVersion -isnot [int] -and $schemaVersion -isnot [long]) -or
        [int]$schemaVersion -ne 1 -or $format -ne "hybridclr.dhe-toolchain-manifest.json" -or
        $toolchainVersion -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
        ($contractVersion -isnot [int] -and $contractVersion -isnot [long]) -or
        [int]$contractVersion -ne 1 -or $mode -notin @("Release", "Exploratory") -or
        $pathSemantics -ne "package-relative-v1" -or $entryPoint -ne "dhe.ps1" -or
        $packageId -notmatch '^[0-9a-fA-F]{64}$') {
        Add-Error "DHE toolchain manifest contract is invalid."
    }
    if ($mode -eq "Exploratory" -and $releaseReadyValue) {
        Add-Error "An Exploratory DHE toolchain package may not declare releaseReady=true."
    }
    if ($RequireRelease -and (-not $releaseReadyValue -or $mode -ne "Release")) {
        Add-Error "A Release-ready DHE toolchain package was required."
    }

    $sourceIdentity = Get-PackageProperty $manifest "sourceIdentity"
    if ($mode -eq "Release" -or $RequireRelease) {
        $sourceHead = [string](Get-PackageProperty $sourceIdentity "head")
        $sourceTree = [string](Get-PackageProperty $sourceIdentity "tree")
        $sourceClean = Get-PackageProperty $sourceIdentity "clean"
        $sourceTracked = Get-PackageProperty $sourceIdentity "tracked"
        $releaseIdentityValid = $releaseReadyValue -and $mode -eq "Release" -and
            $sourceClean -is [bool] -and [bool]$sourceClean -and
            $sourceTracked -is [bool] -and [bool]$sourceTracked -and
            $sourceHead -match '^[0-9a-fA-F]{40,64}$' -and $sourceTree -match '^[0-9a-fA-F]{40,64}$'
        if (-not $releaseIdentityValid) {
            Add-Error "DHE Release package source identity is incomplete, dirty, or untracked."
        }
    }

    $recordsValue = Get-PackageProperty $manifest "files"
    $records = if ($null -eq $recordsValue) { @() } else { @($recordsValue) }
    $expectedFileCount = $records.Count
    $manifestFileCount = Get-PackageProperty $manifest "fileCount"
    if (($manifestFileCount -isnot [int] -and $manifestFileCount -isnot [long]) -or
        [int]$manifestFileCount -ne $expectedFileCount) {
        Add-Error "DHE toolchain manifest fileCount does not match files[]."
    }
    $recordPaths = @($records | ForEach-Object { [string](Get-PackageProperty $_ "path") })
    if (@($recordPaths | Group-Object | Where-Object { $_.Count -gt 1 }).Count -gt 0 -or
        @($recordPaths | Where-Object { -not (Is-SafePackagePath $_) }).Count -gt 0) {
        Add-Error "DHE toolchain manifest contains duplicate or unsafe file paths."
        $hashesValid = $false
    }
    foreach ($record in $records) {
        $relative = [string](Get-PackageProperty $record "path")
        $declaredSize = Get-PackageProperty $record "size"
        $declaredHash = [string](Get-PackageProperty $record "sha256")
        if (-not (Is-SafePackagePath $relative) -or
            ($declaredSize -isnot [int] -and $declaredSize -isnot [long]) -or [int64]$declaredSize -lt 0 -or
            $declaredHash -notmatch '^[0-9a-fA-F]{64}$') {
            Add-Error "DHE toolchain manifest contains an invalid file record: $relative"
            $hashesValid = $false
            continue
        }
        $path = Join-Path $PackageRoot $relative
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            Add-Error "DHE toolchain package file is missing: $relative"
            $hashesValid = $false
            continue
        }
        $file = Get-Item -LiteralPath $path -Force
        $actualHash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
        if ([int64]$declaredSize -ne [int64]$file.Length -or
            -not [StringComparer]::OrdinalIgnoreCase.Equals($declaredHash, $actualHash)) {
            Add-Error "DHE toolchain package file hash or size mismatch: $relative"
            $hashesValid = $false
        }
    }

    $actualFiles = @($verifiedPackageFiles | Where-Object {
        -not $_.FullName.Equals($manifestPath, [StringComparison]::OrdinalIgnoreCase)
    })
    $actualFileCount = $actualFiles.Count
    $actualRelative = @($actualFiles | ForEach-Object {
        $_.FullName.Substring($PackageRoot.TrimEnd('\', '/').Length).TrimStart('\', '/').Replace('\', '/')
    })
    $extra = @($actualRelative | Where-Object { $_ -notin $recordPaths })
    if ($extra.Count -gt 0) {
        Add-Error "DHE toolchain package contains files not declared by the manifest: $($extra -join ', ')"
        $hashesValid = $false
    }

    try {
        $computedPackageId = Get-DheToolchainPackageId `
            -ToolchainVersion $toolchainVersion `
            -ContractVersion ([int]$contractVersion) `
            -Mode $mode `
            -SourceHead ([string](Get-PackageProperty $sourceIdentity "head")) `
            -SourceTree ([string](Get-PackageProperty $sourceIdentity "tree")) `
            -LayoutSha256 ([string](Get-PackageProperty $manifest "layoutSha256")) `
            -Files $records
        $packageIdValid = $packageId -match '^[0-9a-fA-F]{64}$' -and
            [StringComparer]::OrdinalIgnoreCase.Equals($packageId, $computedPackageId)
    } catch {
        $packageIdValid = $false
    }
    if (-not $packageIdValid) { Add-Error "DHE toolchain packageId does not match its canonical payload identity." }
    if (-not [string]::IsNullOrWhiteSpace($ExpectedPackageId) -and
        -not [StringComparer]::OrdinalIgnoreCase.Equals($ExpectedPackageId, $packageId)) {
        $packageIdValid = $false
        Add-Error "DHE toolchain packageId does not match -ExpectedPackageId."
    }

    foreach ($script in @($verifiedPackageFiles | Where-Object { $_.Extension -eq ".ps1" })) {
        $tokens = $null
        $parseErrors = $null
        [Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null
        if ($parseErrors.Count -gt 0) {
            Add-Error "DHE toolchain PowerShell parse failure: $($script.FullName)"
            $scriptsValid = $false
        }
    }

    foreach ($jsonFile in @($verifiedPackageFiles | Where-Object { $_.Extension -eq ".json" })) {
        try {
            $document = Get-Content -Raw -LiteralPath $jsonFile.FullName | ConvertFrom-Json
            $machinePaths = New-Object System.Collections.Generic.List[string]
            Find-MachineLocalJsonString $document '$' $machinePaths
            if ($machinePaths.Count -gt 0) {
                Add-Error "DHE toolchain package JSON contains a machine-local absolute path: $($jsonFile.FullName) ($($machinePaths -join ', '))"
                $jsonValid = $false
            }
        } catch {
            Add-Error "DHE toolchain package JSON is invalid: $($jsonFile.FullName)"
            $jsonValid = $false
        }
    }

    if (-not (Test-Path -LiteralPath $layoutPath -PathType Leaf)) {
        Add-Error "DHE toolchain package layout is missing."
        $layoutValid = $false
    } else {
        try { $layout = Get-Content -Raw -LiteralPath $layoutPath | ConvertFrom-Json }
        catch { $layout = $null }
        if ($null -eq $layout -or [int](Get-PackageProperty $layout "schemaVersion") -ne 1 -or
            [string](Get-PackageProperty $layout "format") -ne "hybridclr.dhe-toolchain-layout.json") {
            Add-Error "DHE toolchain package layout contract is invalid."
            $layoutValid = $false
        } else {
            $actualLayoutHash = (Get-FileHash -LiteralPath $layoutPath -Algorithm SHA256).Hash
            $layoutCommands = @((Get-PackageProperty $layout "commands") | ForEach-Object { [string]$_ })
            $manifestCommands = @((Get-PackageProperty $manifest "commands") | ForEach-Object { [string]$_ })
            if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualLayoutHash, [string](Get-PackageProperty $manifest "layoutSha256")) -or
                [string](Get-PackageProperty $layout "toolchainVersion") -ne $toolchainVersion -or
                [int](Get-PackageProperty $layout "contractVersion") -ne [int]$contractVersion -or
                -not (Test-SequenceEqual $layoutCommands $manifestCommands)) {
                Add-Error "DHE toolchain manifest does not match its packaged layout, version, contract, or commands."
                $layoutValid = $false
            }
            $allPackageRelative = @($actualRelative) + @("dhe-toolchain-manifest.json")
            foreach ($requiredPath in @($layout.exactPaths) + @($layout.generatedPaths)) {
                if ([string]$requiredPath -notin $allPackageRelative) {
                    Add-Error "DHE toolchain layout required path is missing from the package: $requiredPath"
                    $layoutValid = $false
                }
            }
            foreach ($prefix in @($layout.prefixes)) {
                $prefixLayout = [pscustomobject]@{ exactPaths = @(); generatedPaths = @(); prefixes = @($prefix) }
                if (@($allPackageRelative | Where-Object { Test-LayoutSelectsPath $prefixLayout $_ }).Count -eq 0) {
                    Add-Error "DHE toolchain layout prefix matched no package files: $prefix"
                    $layoutValid = $false
                }
            }
            $unselected = @($allPackageRelative | Where-Object { -not (Test-LayoutSelectsPath $layout $_) })
            if ($unselected.Count -gt 0) {
                Add-Error "DHE toolchain package contains files outside its declared layout: $($unselected -join ', ')"
                $layoutValid = $false
            }
        }
    }

    $boundaryPath = Join-Path $PackageRoot "dhe-source-boundary.json"
    if (-not (Test-Path -LiteralPath $boundaryPath -PathType Leaf)) {
        Add-Error "DHE toolchain package source boundary is missing."
        $boundaryValid = $false
    } else {
        try { $boundary = Get-Content -Raw -LiteralPath $boundaryPath | ConvertFrom-Json }
        catch { $boundary = $null }
        if ($null -eq $boundary -or [int](Get-PackageProperty $boundary "schemaVersion") -ne 1 -or
            [string](Get-PackageProperty $boundary "format") -ne "hybridclr.dhe-source-boundary.json" -or
            [string](Get-PackageProperty $boundary "pathBase") -ne "manifest-directory-v1") {
            Add-Error "DHE toolchain package source boundary contract is invalid."
            $boundaryValid = $false
        }
    }

    $schemaGateRoot = Join-Path ([IO.Path]::GetTempPath()) ("HybridCLRDhe/package-schema-" + [Guid]::NewGuid().ToString("N"))
    if ($errors.Count -eq 0) {
        try {
            $schemaHost = Resolve-DhePowerShellHost
            $oldErrorActionPreference = $ErrorActionPreference
            try {
                $ErrorActionPreference = "Continue"
                # Package verification is a trust boundary. Always execute the
                # verifier-side schema gate; candidate package scripts remain
                # inert data until integrity and an optional external pin pass.
                & $schemaHost -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "run-dhe-schema-gate.ps1") `
                    -LabRoot $PackageRoot -InputRoot $PackageRoot -OutputRoot $schemaGateRoot -ForceOutput 2>&1 | Out-Null
                $schemaExitCode = [int]$LASTEXITCODE
            } finally {
                $ErrorActionPreference = $oldErrorActionPreference
            }
            $schemaReportPath = Join-Path $schemaGateRoot "schema-gate-report.json"
            $schemaReport = if (Test-Path -LiteralPath $schemaReportPath -PathType Leaf) {
                Get-Content -Raw -LiteralPath $schemaReportPath | ConvertFrom-Json
            } else { $null }
            $schemaPassed = Get-PackageProperty $schemaReport "passed"
            $schemaValid = $schemaExitCode -eq 0 -and $schemaPassed -is [bool] -and [bool]$schemaPassed
            if (-not $schemaValid) { Add-Error "DHE toolchain package JSON Schema validation failed." }
        } catch {
            $schemaValid = $false
            Add-Error "DHE toolchain package JSON Schema validation failed: $($_.Exception.Message)"
        } finally {
            if (Test-Path -LiteralPath $schemaGateRoot) { Remove-Item -LiteralPath $schemaGateRoot -Recurse -Force }
        }
    } else {
        $schemaValid = $false
        $warnings.Add("JSON Schema validation was skipped because package integrity or identity checks failed.")
    }
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-toolchain-gate.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    packageRoot = $PackageRoot
    manifest = if ($null -eq $manifest) { $null } else { $manifestPath }
    toolchainVersion = if ($null -eq $manifest) { $null } else { [string](Get-PackageProperty $manifest "toolchainVersion") }
    contractVersion = if ($null -eq $manifest) { $null } else { Get-PackageProperty $manifest "contractVersion" }
    packageId = $packageId
    expectedPackageId = if ([string]::IsNullOrWhiteSpace($ExpectedPackageId)) { $null } else { $ExpectedPackageId.ToLowerInvariant() }
    packageIdValid = $packageIdValid
    packageTreeSafe = $packageTreeSafe
    releaseReady = $releaseReadyValue
    releaseIdentityValid = $releaseIdentityValid
    requireRelease = [bool]$RequireRelease
    expectedFileCount = $expectedFileCount
    actualFileCount = $actualFileCount
    hashesValid = $hashesValid
    scriptsValid = $scriptsValid
    jsonValid = $jsonValid
    schemaValid = $schemaValid
    layoutValid = $layoutValid
    boundaryValid = $boundaryValid
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
}
$outputParent = [IO.Path]::GetDirectoryName($Output)
if (-not [string]::IsNullOrWhiteSpace($outputParent)) { $null = New-Item -ItemType Directory -Force -Path $outputParent }
[IO.File]::WriteAllText($Output, ($report | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE toolchain package gate: $Output"
if (-not $report.passed) {
    Write-Error ("DHE toolchain package gate failed:" + [Environment]::NewLine + " - " + ($errors -join ([Environment]::NewLine + " - ")))
    exit 1
}
Write-Host ("DHE toolchain package passed: version={0}; packageId={1}; files={2}" -f $report.toolchainVersion, $report.packageId, $actualFileCount)
exit 0
