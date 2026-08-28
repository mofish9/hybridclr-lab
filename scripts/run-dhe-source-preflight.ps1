[CmdletBinding()]
param(
    [string]$LabRoot = "",
    [string]$ProjectPath = "",
    [string]$RuntimeSource = "",
    [string]$OutputRoot = "",
    [string]$PackageLockPath = "",
    [string]$IdentityTemplatePath = "",
    [switch]$RequireRuntime,
    [switch]$RequireDheEqualsHotUpdate,
    [switch]$RequireEmbeddedPackage,
    [switch]$RequireNonSurrogateExternalHeaders,
    [switch]$RequireCleanRuntimeSources,
    [switch]$RequireIdentityTemplate,
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
$ProjectPath = if ([string]::IsNullOrWhiteSpace($ProjectPath)) {
    Join-Path $LabRoot "unity2021-dhe-demo"
} else {
    [IO.Path]::GetFullPath($ProjectPath)
}
$OutputRoot = if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    Join-Path $LabRoot "artifacts/dhe-source-preflight"
} else {
    [IO.Path]::GetFullPath($OutputRoot)
}

Assert-DheSafeOutputRoot -Path $OutputRoot -ProtectedPaths @($ProjectPath, $RuntimeSource)
Assert-DheOutputNotAncestor -Path $OutputRoot -Root $LabRoot
if (Test-Path -LiteralPath $OutputRoot) {
    if (-not $ForceOutput -and
        ((Test-Path -LiteralPath $OutputRoot -PathType Leaf) -or
         @(Get-ChildItem -LiteralPath $OutputRoot -Force -ErrorAction SilentlyContinue).Count -gt 0)) {
        throw "OutputRoot is not empty: $OutputRoot. Pass -ForceOutput to replace a prior run."
    }
    if ($ForceOutput) {
        Remove-Item -LiteralPath $OutputRoot -Recurse -Force
    }
}
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$checks = New-Object System.Collections.Generic.List[object]

function Add-Check([string]$Name, [bool]$Passed, [string]$Details) {
    $checks.Add([ordered]@{
        name = $Name
        passed = $Passed
        details = $Details
    })
}

function Add-ErrorCheck([string]$Name, [string]$Message) {
    Add-Check $Name $false $Message
    $errors.Add($Message)
}

function Add-WarningCheck([string]$Name, [string]$Message) {
    Add-Check $Name $true $Message
    $warnings.Add($Message)
}

function Require-File([string]$Path, [string]$Description) {
    if (-not [IO.File]::Exists($Path)) {
        Add-ErrorCheck "file:$Description" "$Description was not found: $Path"
        return $false
    }
    Add-Check "file:$Description" $true $Path
    return $true
}

function Read-Json([string]$Path, [string]$Description) {
    if (-not [IO.File]::Exists($Path)) { return $null }
    try {
        return Get-Content -Raw -LiteralPath $Path | ConvertFrom-Json
    } catch {
        Add-ErrorCheck "json:$Description" "$Description is not valid JSON: $Path ($($_.Exception.Message))"
        return $null
    }
}

function Read-ProjectVersionValue([string]$Path, [string]$Key) {
    if (-not [IO.File]::Exists($Path)) { return $null }
    $pattern = '^(?:' + [regex]::Escape($Key) + '):\s*(.+?)\s*$'
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match $pattern) { return $Matches[1].Trim() }
    }
    return $null
}

function Invoke-GitValue([string]$Repository, [string[]]$Arguments) {
    if (-not (Test-Path -LiteralPath $Repository -PathType Container)) {
        return $null
    }
    $value = @(& git -C $Repository @Arguments 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }
    return (($value -join [Environment]::NewLine).Trim())
}

function Get-GitDirty([string]$Repository) {
    $status = @(& git -C $Repository status --porcelain 2>$null)
    if ($LASTEXITCODE -ne 0) { return $null }
    return [bool](@($status | Where-Object {
        $_ -notmatch [regex]::Escape("Editor/BuildProcessors/AddLil2cppSourceCodeToXcodeproj2023OrNewer.cs.meta")
    }).Count -gt 0)
}

function Test-WorkflowEngineMatch($Actual, $Expected) {
    if ($null -eq $Actual -or $null -eq $Expected) {
        return $false
    }
    foreach ($field in @("family", "version", "unityVersion", "unityVersionNumber", "tuanjieVersionNumber")) {
        $actualProperty = $Actual.PSObject.Properties[$field]
        $expectedProperty = $Expected.PSObject.Properties[$field]
        if ($null -eq $actualProperty -or $null -eq $expectedProperty -or
            ([string]$actualProperty.Value -ne [string]$expectedProperty.Value)) {
            return $false
        }
    }
    return $true
}

function Normalize-NameSet([string[]]$Values) {
    return @(@($Values) | Where-Object { $null -ne $_ } | ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 } |
        Sort-Object -Unique)
}

$requiredScripts = @(
    "dhe-workflow-common.ps1",
    "runtime-provenance.ps1",
    "resolve-repos-root.ps1",
    "assemble-runtime.ps1",
    "apply-dhe-runtime-patches.ps1",
    "generate-dhe-mv.ps1",
    "generate-dhe-batch.ps1",
    "run-dhe-project-preflight.ps1",
    "run-dhe-project-workflow.ps1",
    "validate-dhe-project-plan.ps1",
    "resolve-dhe-native-manifest.ps1",
    "inject-dhe-guard.ps1",
    "apply-dhe-generated-cpp.ps1",
    "run-dhe-deterministic-player-build.ps1",
    "validate-dhe-artifacts.ps1",
    "run-dhe-release-gate.ps1",
    "run-dhe-schema-gate.ps1",
    "run-dhe-capability-gate.ps1",
    "run-dhe-compatibility-negative-gate.ps1",
    "run-dhe-script-fixture-gate.ps1",
    "run-dhe-clean-checkout-gate.ps1",
    "run-dhe-source-boundary-gate.ps1",
    "run-dhe-source-preflight.ps1",
    "archive-dhe-artifacts.ps1",
    "run-dhe-archive-gate.ps1"
)
foreach ($scriptName in $requiredScripts) {
    Require-File (Join-Path $LabRoot "scripts/$scriptName") "formal DHE script '$scriptName'" | Out-Null
}

$requiredSchemas = @(
    "dhe-mv.schema.json",
    "dhe-batch-report.schema.json",
    "dhe-artifact-validation.schema.json",
    "dhe-build-identity.schema.json",
    "dhe-native-manifest.schema.json",
    "dhe-runtime-plan.schema.json",
    "dhe-project-plan.schema.json",
    "dhe-project-plan-validation.schema.json",
    "dhe-project-adapter-prepare.schema.json",
    "dhe-project-workflow.schema.json",
    "dhe-project-workflow-failure.schema.json",
    "dhe-project-preflight.schema.json",
    "dhe-project-preflight-failure.schema.json",
    "dhe-workflow-report.schema.json",
    "dhe-release-gate.schema.json",
    "dhe-workflow-failure.schema.json",
    "dhe-script-fixture-gate.schema.json",
    "dhe-schema-gate.schema.json",
    "dhe-capability-gate.schema.json",
    "dhe-compatibility-negative-gate.schema.json",
    "dhe-runtime-lock.schema.json",
    "dhe-package-lock.schema.json",
    "dhe-source-preflight.schema.json",
    "dhe-clean-checkout-gate.schema.json",
    "dhe-source-boundary.schema.json",
    "dhe-source-boundary-gate.schema.json",
    "dhe-archive-manifest.schema.json",
    "dhe-archive-gate.schema.json"
)
foreach ($schemaName in $requiredSchemas) {
    Require-File (Join-Path $LabRoot "schemas/$schemaName") "formal DHE schema '$schemaName'" | Out-Null
}

$settingsPath = Join-Path $ProjectPath "ProjectSettings/HybridCLRSettings.asset"
$packageRoot = Join-Path $ProjectPath "Packages/com.code-philosophy.hybridclr"
$embeddedPackagePresent = Test-Path -LiteralPath $packageRoot -PathType Container
$defaultPackageLockPath = Join-Path $LabRoot "manifests/dhe-package-lock.json"
$defaultDemoProjectPath = Normalize-DhePath (Join-Path $LabRoot "unity2021-dhe-demo")
$identityTemplatePath = if (-not [string]::IsNullOrWhiteSpace($IdentityTemplatePath)) {
    [IO.Path]::GetFullPath($IdentityTemplatePath)
} elseif ((Normalize-DhePath $ProjectPath).Equals($defaultDemoProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
    Join-Path $ProjectPath "Assets/Runtime/HybridCLRDheBuildIdentity.cs"
} else {
    ""
}
$packageLockPath = if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
    [IO.Path]::GetFullPath($PackageLockPath)
} elseif ((Normalize-DhePath $ProjectPath).Equals($defaultDemoProjectPath, [StringComparison]::OrdinalIgnoreCase)) {
    $defaultPackageLockPath
} else {
    ""
}
$runtimeLockPath = Join-Path $LabRoot "manifests/dhe-runtime-lock.json"
$packageManifestPath = Join-Path $ProjectPath "Packages/manifest.json"
$packagePackagesLockPath = Join-Path $ProjectPath "Packages/packages-lock.json"

$null = Require-File $runtimeLockPath "DHE runtime lock"
$packageLockAvailable = -not [string]::IsNullOrWhiteSpace($packageLockPath) -and
    (Test-Path -LiteralPath $packageLockPath -PathType Leaf)
$releasePackageLockRequired = $RequireCleanRuntimeSources -and $embeddedPackagePresent
if ($RequireEmbeddedPackage -and -not $embeddedPackagePresent) {
    Add-ErrorCheck "package:directory" "An embedded HybridCLR package is required but was not found: $packageRoot"
} elseif (-not $embeddedPackagePresent) {
    Add-WarningCheck "package:directory" "No embedded HybridCLR package was found; package tree validation is skipped."
}
$packageLock = if ($packageLockAvailable) {
    Read-Json $packageLockPath "DHE package lock"
} else {
    $packageLockDescription = if ([string]::IsNullOrWhiteSpace($packageLockPath)) {
        "Pass -PackageLockPath for an embedded package"
    } else {
        $packageLockPath
    }
    if ($RequireEmbeddedPackage -or $releasePackageLockRequired) {
        Add-ErrorCheck "package:lock" "DHE package lock was not found: $packageLockDescription"
    } else {
        Add-WarningCheck "package:lock" "DHE package lock was not supplied; embedded package provenance is not verified."
    }
    $null
}
$runtimeLock = Read-Json $runtimeLockPath "DHE runtime lock"
if ($null -ne $runtimeLock) {
    if ([int]$runtimeLock.schemaVersion -ne 1 -or
        [string]$runtimeLock.format -ne "hybridclr.dhe-runtime-lock.json" -or
        @($runtimeLock.patches).Count -eq 0) {
        Add-ErrorCheck "runtime-lock:schema" "DHE runtime lock has an invalid schema or empty patch set."
    } else {
        Add-Check "runtime-lock:schema" $true "schemaVersion=1; patches=$(@($runtimeLock.patches).Count)"
    }
}
if ($null -ne $packageLock) {
    if ([int]$packageLock.schemaVersion -ne 1 -or
        [string]$packageLock.format -ne "hybridclr.dhe-package-lock.json" -or
        [string]$packageLock.repository -ne "hybridclr_unity" -or
        [string]$packageLock.baseCommit -notmatch '^[0-9a-f]{40}$') {
        Add-ErrorCheck "package-lock:schema" "DHE package lock has an invalid schema or repository identity."
    } else {
        Add-Check "package-lock:schema" $true "schemaVersion=1; baseCommit=$($packageLock.baseCommit)"
    }
    if ($embeddedPackagePresent) {
        $packagePathProperty = $packageLock.PSObject.Properties["packagePath"]
        $lockedPackageReference = if ($null -eq $packagePathProperty) { "" } else { [string]$packagePathProperty.Value }
        if ([string]::IsNullOrWhiteSpace($lockedPackageReference)) {
            Add-ErrorCheck "package:path" "DHE package lock is missing packagePath for the embedded package: $packageRoot"
        } else {
            $packageLockDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($packageLockPath))
            $packagePathCandidates = if ([IO.Path]::IsPathRooted($lockedPackageReference)) {
                @([IO.Path]::GetFullPath($lockedPackageReference))
            } else {
                @(
                    [IO.Path]::GetFullPath((Join-Path $ProjectPath $lockedPackageReference)),
                    [IO.Path]::GetFullPath((Join-Path $packageLockDirectory $lockedPackageReference)),
                    [IO.Path]::GetFullPath((Join-Path $LabRoot $lockedPackageReference))
                )
            }
            $packagePathMatches = @($packagePathCandidates | Where-Object {
                $_.Equals([IO.Path]::GetFullPath($packageRoot), [StringComparison]::OrdinalIgnoreCase)
            })
            if (@($packagePathMatches).Count -eq 0) {
                Add-ErrorCheck "package:path" "DHE package lock packagePath '$lockedPackageReference' does not resolve to the embedded package: $packageRoot"
            } else {
                Add-Check "package:path" $true "embedded package path is locked"
            }
        }
    }
}

if ($null -ne $runtimeLock -and $null -ne $packageLock) {
    $runtimePackageIds = @($runtimeLock.patches |
        Where-Object { [string]$_.applyRoot -eq "package" } |
        ForEach-Object { [string]$_.id } | Sort-Object)
    $lockedPackageIds = @($packageLock.patches | ForEach-Object { [string]$_ } | Sort-Object)
    if (($runtimePackageIds -join ",") -ne ($lockedPackageIds -join ",")) {
        Add-ErrorCheck "locks:package-patches" "Package lock patch IDs do not match runtime lock package patches."
    } else {
        Add-Check "locks:package-patches" $true "package patch IDs match"
    }
}

if ($embeddedPackagePresent -and $null -ne $packageLock) {
    $packageTreeHash = Get-TreeHash $packageRoot
    if ($null -eq $packageLock -or
        -not [StringComparer]::OrdinalIgnoreCase.Equals($packageTreeHash, [string]$packageLock.treeSha256)) {
        Add-ErrorCheck "package:tree-hash" "Embedded HybridCLR package tree hash does not match dhe-package-lock.json."
    } else {
        Add-Check "package:tree-hash" $true $packageTreeHash
    }
} elseif ($embeddedPackagePresent) {
    Add-WarningCheck "package:tree-hash" "Embedded package exists but no package lock was supplied: $packageRoot"
} else {
    Add-Check "package:tree-hash" $true "no embedded package to validate"
}
if ($embeddedPackagePresent) {
    Require-File (Join-Path $packageRoot "Plugins/dnlib.dll") "embedded dnlib.dll" | Out-Null
}

if ($null -ne $runtimeLock) {
    foreach ($entry in @($runtimeLock.patches)) {
        $patchPath = Join-Path $LabRoot ([string]$entry.path)
        if (-not (Require-File $patchPath "DHE patch '$([string]$entry.id)'")) { continue }
        $patchHash = (Get-FileHash -LiteralPath $patchPath -Algorithm SHA256).Hash
        if (-not [StringComparer]::OrdinalIgnoreCase.Equals($patchHash, [string]$entry.sha256)) {
            Add-ErrorCheck "patch:$([string]$entry.id)" "DHE patch hash mismatch: $patchPath"
        } else {
            Add-Check "patch:$([string]$entry.id)" $true $patchHash
        }
    }
}

if ($embeddedPackagePresent) {
    $manifest = Read-Json $packageManifestPath "Unity package manifest"
    if ($null -ne $manifest -and $null -ne $manifest.dependencies -and
        $null -ne $manifest.dependencies.PSObject.Properties["com.code-philosophy.hybridclr"] -and
        [string]$manifest.dependencies."com.code-philosophy.hybridclr" -eq "file:com.code-philosophy.hybridclr") {
        Add-Check "package:manifest" $true "embedded package reference"
    } else {
        Add-ErrorCheck "package:manifest" "Unity package manifest does not reference the embedded HybridCLR package."
    }
    $packagesLock = Read-Json $packagePackagesLockPath "Unity packages lock"
    $packageLockEntry = if ($null -eq $packagesLock) { $null } else { $packagesLock.dependencies."com.code-philosophy.hybridclr" }
    if ($null -ne $packageLockEntry -and [string]$packageLockEntry.source -eq "embedded") {
        Add-Check "package:packages-lock" $true "embedded package lock entry"
    } else {
        Add-ErrorCheck "package:packages-lock" "Unity packages-lock.json has no embedded HybridCLR package entry."
    }
} else {
    Add-Check "package:manifest" $true "embedded package checks not applicable"
    Add-Check "package:packages-lock" $true "embedded package checks not applicable"
}

$settings = $null
try {
    $settings = Resolve-DheSettingsAssemblySets -SettingsFile $settingsPath -ProjectRoot $ProjectPath
} catch {
    Add-ErrorCheck "settings:parse" $_.Exception.Message
}
$hotUpdate = if ($null -eq $settings) { [string[]]@() } else { ConvertTo-DheStringArray $settings.hotUpdateAssemblies }
$dheAot = if ($null -eq $settings) { [string[]]@() } else { ConvertTo-DheStringArray $settings.dheAotAssemblies }
$externalDirs = if ($null -eq $settings) { [string[]]@() } else { ConvertTo-DheStringArray $settings.externalHotUpdateAssemblyDirs }
if (@($hotUpdate).Count -eq 0) {
    Add-ErrorCheck "settings:hot-update" "HybridCLR settings contain no hotUpdateAssemblies."
} else {
    Add-Check "settings:hot-update" $true ($hotUpdate -join ",")
}
if (@($dheAot).Count -eq 0) {
    Add-ErrorCheck "settings:dhe-aot" "DHE assembly set resolved to empty; configure hotUpdateAssemblies or dheAotAssemblies."
} else {
    $dheDetails = if ($null -ne $settings -and -not [bool]$settings.dheAotConfigured) {
        "dheAotAssemblies missing/empty; defaulted to hotUpdateAssemblies"
    } else {
        $dheAot -join ","
    }
    Add-Check "settings:dhe-aot" $true $dheDetails
}
$duplicateSettings = if ($null -eq $settings) {
    @()
} else {
    @(
        @($settings.hotUpdateDuplicates)
        @($settings.dheAotDuplicates)
    )
}
if (@($duplicateSettings).Count -gt 0) {
    Add-ErrorCheck "settings:duplicates" "HybridCLR settings contain duplicate assembly names."
} else {
    Add-Check "settings:duplicates" $true "assembly names are unique"
}
$normalizedHotUpdate = Normalize-NameSet $hotUpdate
$normalizedDheAot = Normalize-NameSet $dheAot
$missingDheCoverage = @($normalizedDheAot | Where-Object { $_ -notin $normalizedHotUpdate })
if (@($missingDheCoverage).Count -gt 0) {
    Add-ErrorCheck "settings:dhe-scope" "dheAotAssemblies contains assemblies that are not hot-update assemblies: $($missingDheCoverage -join ', ')"
} elseif ($RequireDheEqualsHotUpdate -and
    ($normalizedHotUpdate -join ",") -ne ($normalizedDheAot -join ",")) {
    Add-ErrorCheck "settings:dhe-scope" "dheAotAssemblies must exactly match hotUpdateAssemblies for this workflow."
} else {
    $scopeDetails = if ($RequireDheEqualsHotUpdate) {
        "DHE covers all hot-update assemblies"
    } else {
        "DHE scope is a subset of hot-update assemblies"
    }
    Add-Check "settings:dhe-scope" $true $scopeDetails
}
if (@($externalDirs).Count -gt 0) {
    $absoluteExternal = @($externalDirs | Where-Object {
        [IO.Path]::IsPathRooted($_) -or $_ -match '^[A-Za-z]:[\\/]'
    })
    if (@($absoluteExternal).Count -gt 0) {
        Add-ErrorCheck "settings:external-dirs" "HybridCLR settings contain machine-specific external assembly paths: $($absoluteExternal -join ', ')"
    } else {
        Add-WarningCheck "settings:external-dirs" "HybridCLR settings contain relative external assembly paths; the workflow will replace them at runtime."
    }
} else {
    Add-Check "settings:external-dirs" $true "empty source template"
}

if ([string]::IsNullOrWhiteSpace($identityTemplatePath)) {
    if ($RequireIdentityTemplate) {
        Add-ErrorCheck "identity:template" "DHE build identity source path was not supplied for this project."
    } else {
        Add-WarningCheck "identity:template" "No DHE build identity source path was supplied; identity template validation is deferred to the project adapter."
    }
} elseif (Test-Path -LiteralPath $identityTemplatePath -PathType Leaf) {
    $identityText = [IO.File]::ReadAllText($identityTemplatePath)
    $identityFields = @("BaselineAssemblySha256", "AotSnapshotSha256", "NativeGuardSourceSha256", "NativeManifestSha256")
    $identityTemplateValid = $true
    foreach ($field in $identityFields) {
        $pattern = '(?m)public const string ' + [regex]::Escape($field) + ' = "([0-9a-fA-F]{64})";'
        $match = [regex]::Match($identityText, $pattern)
        if (-not $match.Success -or $match.Groups[1].Value -ne ("0" * 64)) {
            $identityTemplateValid = $false
        }
    }
    if ($identityText -notmatch 'AotSnapshotKind\s*=\s*"uninitialized-template"') {
        $identityTemplateValid = $false
    }
    if (-not $identityTemplateValid) {
        Add-ErrorCheck "identity:template" "DHE build identity source is not the zeroed uninitialized template: $identityTemplatePath"
    } else {
        Add-Check "identity:template" $true "zeroed uninitialized template"
    }
} else {
    Add-ErrorCheck "identity:template" "DHE build identity source was not found: $identityTemplatePath"
}

$runtimeReady = $false
$runtimePath = $null
$runtimeManifest = $null
if (-not [string]::IsNullOrWhiteSpace($RuntimeSource)) {
    $runtimePath = [IO.Path]::GetFullPath($RuntimeSource)
    $runtimeManifestPath = Join-Path ([IO.Path]::GetDirectoryName($runtimePath)) "runtime-manifest.json"
    $runtimeDirectoryValid = $false
    if (-not (Test-Path -LiteralPath $runtimePath -PathType Container)) {
        Add-ErrorCheck "runtime:directory" "DHE runtime source was not found: $runtimePath"
    } elseif (-not (Test-Path -LiteralPath (Join-Path $runtimePath "hybridclr/DheRuntime.cpp") -PathType Leaf) -or
        -not (Test-Path -LiteralPath (Join-Path $runtimePath "hybridclr/DheRuntime.h") -PathType Leaf)) {
        Add-ErrorCheck "runtime:directory" "DHE runtime source must contain both hybridclr/DheRuntime.cpp and hybridclr/DheRuntime.h: $runtimePath"
    } else {
        Add-Check "runtime:directory" $true $runtimePath
        $runtimeDirectoryValid = $true
    }
    if (-not (Require-File $runtimeManifestPath "DHE runtime manifest")) {
        $runtimeManifest = $null
    } else {
        $runtimeManifest = Read-Json $runtimeManifestPath "DHE runtime manifest"
    }
    if ($runtimeDirectoryValid -and $null -ne $runtimeManifest) {
        $runtimeHash = Get-TreeHash $runtimePath
        $lockHash = if (Test-Path -LiteralPath $runtimeLockPath -PathType Leaf) {
            (Get-FileHash -LiteralPath $runtimeLockPath -Algorithm SHA256).Hash.ToLowerInvariant()
        } else { "" }
        $runtimeReady = $true
        $runtimePathSemantics = if ($null -ne $runtimeManifest.PSObject.Properties["pathSemantics"]) {
            [string]$runtimeManifest.pathSemantics
        } else { "" }
        if ($runtimePathSemantics -ne "workspace-absolute-v1") {
            Add-ErrorCheck "runtime:path-semantics" "DHE runtime manifest must declare workspace-absolute-v1 path semantics."
            $runtimeReady = $false
        } else {
            Add-Check "runtime:path-semantics" $true "workspace-absolute-v1"
        }
        if ($runtimeHash -ne [string]$runtimeManifest.stagedRuntimeSha256) {
            Add-ErrorCheck "runtime:tree-hash" "DHE runtime tree does not match runtime-manifest.json."
            $runtimeReady = $false
        } else {
            Add-Check "runtime:tree-hash" $true $runtimeHash
        }
        $dheEnabled = Get-DheStrictBooleanProperty $runtimeManifest "dheEnabled" "DHE runtime manifest dheEnabled"
        if (-not $dheEnabled -or
            -not [StringComparer]::OrdinalIgnoreCase.Equals([string]$runtimeManifest.dheRuntimeLockSha256, $lockHash)) {
            Add-ErrorCheck "runtime:lock" "DHE runtime manifest is not enabled or was produced from a different runtime lock."
            $runtimeReady = $false
        } else {
            Add-Check "runtime:lock" $true $lockHash
        }

        $externalHeadersMetadataValid = $null -ne $runtimeManifest.PSObject.Properties["externalHeaders"] -and
            $null -ne $runtimeManifest.externalHeaders -and
            $null -ne $runtimeManifest.externalHeaders.PSObject.Properties["surrogate"]
        $externalHeadersSurrogate = $false
        if ($externalHeadersMetadataValid) {
            $externalHeadersSurrogate = Get-DheStrictBooleanProperty $runtimeManifest.externalHeaders "surrogate" "DHE runtime manifest externalHeaders.surrogate"
        }
        if ($RequireNonSurrogateExternalHeaders -and -not $externalHeadersMetadataValid) {
            Add-ErrorCheck "runtime:external-headers" "DHE runtime manifest is missing externalHeaders.surrogate provenance."
            $runtimeReady = $false
        } elseif ($RequireNonSurrogateExternalHeaders -and $externalHeadersSurrogate) {
            Add-ErrorCheck "runtime:external-headers" "DHE runtime uses surrogate external headers; Release validation requires headers from the matching engine installation."
            $runtimeReady = $false
        } elseif ($RequireNonSurrogateExternalHeaders) {
            Add-Check "runtime:external-headers" $true "matching engine external headers"
        } elseif (-not $externalHeadersMetadataValid) {
            Add-WarningCheck "runtime:external-headers" "externalHeaders.surrogate provenance is missing; exploratory validation did not require it"
        } else {
            Add-WarningCheck "runtime:external-headers" $(if ($externalHeadersSurrogate) { "surrogate headers allowed for exploratory validation" } else { "matching engine external headers" })
        }

        $externalRootPath = Join-Path ([IO.Path]::GetDirectoryName($runtimePath)) "external"
        $externalHeadersObject = if ($null -ne $runtimeManifest.PSObject.Properties["externalHeaders"]) {
            $runtimeManifest.externalHeaders
        } else { $null }
        $externalHashProperty = if ($null -ne $externalHeadersObject) {
            $externalHeadersObject.PSObject.Properties["stagedTreeSha256"]
        } else { $null }
        $expectedExternalHash = if ($null -eq $externalHashProperty) { "" } else { [string]$externalHashProperty.Value }
        $actualExternalHash = ""
        if (-not [IO.Directory]::Exists($externalRootPath)) {
            Add-ErrorCheck "runtime:external-headers-hash" "Staged external headers directory was not found: $externalRootPath"
            $runtimeReady = $false
        } elseif ($expectedExternalHash -notmatch '^[0-9a-fA-F]{64}$') {
            Add-ErrorCheck "runtime:external-headers-hash" "DHE runtime manifest is missing a valid externalHeaders.stagedTreeSha256 value."
            $runtimeReady = $false
        } else {
            $actualExternalHash = Get-TreeHash $externalRootPath
            if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualExternalHash, $expectedExternalHash)) {
                Add-ErrorCheck "runtime:external-headers-hash" "Staged external headers do not match runtime manifest: expected $expectedExternalHash, got $actualExternalHash."
                $runtimeReady = $false
            } else {
                Add-Check "runtime:external-headers-hash" $true $actualExternalHash
            }
        }

        # Runtime provenance is bound to the checked-in workflow and the
        # actual Git worktrees, rather than trusting the copied manifest's
        # commit/dirty fields. This makes a stale or locally modified runtime
        # fail before Unity generation can consume it.
        $repoLockPath = Join-Path $LabRoot "manifests/repo-lock.json"
        $workflowManifestPath = Join-Path $LabRoot "manifests/runtime-workflows.json"
        $repoLock = Read-Json $repoLockPath "repository lock"
        $workflowManifest = Read-Json $workflowManifestPath "runtime workflow manifest"
        $runtimeWorkflowId = if ($null -ne $runtimeManifest.PSObject.Properties["engineWorkflow"]) {
            [string]$runtimeManifest.engineWorkflow
        } else { "" }
        $workflowEntries = if ($null -ne $workflowManifest -and
            $null -ne $workflowManifest.PSObject.Properties["workflows"]) {
            @($workflowManifest.workflows | Where-Object { [string]$_.id -eq $runtimeWorkflowId })
        } else { @() }
        if ([string]::IsNullOrWhiteSpace($runtimeWorkflowId) -or @($workflowEntries).Count -ne 1) {
            Add-ErrorCheck "runtime:workflow-provenance" "DHE runtime manifest engineWorkflow '$runtimeWorkflowId' does not resolve to exactly one current runtime workflow."
            $workflowEntry = $null
            $runtimeReady = $false
        } else {
            $workflowEntry = $workflowEntries[0]
            $runtimeEngineObject = if ($null -ne $runtimeManifest.PSObject.Properties["engine"]) {
                $runtimeManifest.engine
            } else { $null }
            $expectedEngineObject = if ($null -ne $workflowEntry.PSObject.Properties["engine"]) {
                $workflowEntry.engine
            } else { $null }
            if ($null -eq $runtimeEngineObject -or -not (Test-WorkflowEngineMatch $runtimeEngineObject $expectedEngineObject)) {
                Add-ErrorCheck "runtime:workflow-provenance" "DHE runtime engine metadata does not match the current runtime workflow '$runtimeWorkflowId'."
                $runtimeReady = $false
            } else {
                Add-Check "runtime:workflow-provenance" $true "workflow=$runtimeWorkflowId"
            }

            $expectedCommits = [ordered]@{
                hybridclr = if ($null -ne $repoLock) { [string]$repoLock.repositories.hybridclr.commit } else { "" }
                il2cpp_plus = if ($null -ne $workflowEntry.PSObject.Properties["il2cppPlus"] -and
                    $null -ne $workflowEntry.il2cppPlus.PSObject.Properties["commit"]) {
                    [string]$workflowEntry.il2cppPlus.commit
                } else { "" }
                hybridclr_unity = if ($null -ne $repoLock) { [string]$repoLock.repositories.hybridclr_unity.commit } else { "" }
            }
            foreach ($sourceName in @("hybridclr", "il2cpp_plus", "hybridclr_unity")) {
                $sourceRoot = if ($null -ne $runtimeManifest.PSObject.Properties["source"]) { $runtimeManifest.source } else { $null }
                $sourceEntry = if ($null -eq $sourceRoot) { $null } else { $sourceRoot.PSObject.Properties[$sourceName] }
                if ($null -eq $sourceEntry -or $null -eq $sourceEntry.Value) {
                    Add-ErrorCheck "runtime:source-$sourceName" "DHE runtime manifest is missing '$sourceName' source provenance."
                    $runtimeReady = $false
                    continue
                }
                $sourceValue = $sourceEntry.Value
                $sourcePath = if ($null -ne $sourceValue.PSObject.Properties["path"]) { [string]$sourceValue.path } else { "" }
                $expectedCommit = [string]$expectedCommits[$sourceName]
                $actualCommit = if ([string]::IsNullOrWhiteSpace($sourcePath)) { $null } else { Invoke-GitValue $sourcePath @("rev-parse", "HEAD") }
                if ([string]::IsNullOrWhiteSpace($actualCommit) -or
                    [string]::IsNullOrWhiteSpace($expectedCommit) -or
                    -not $actualCommit.Equals($expectedCommit, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-ErrorCheck "runtime:source-$sourceName" "Actual '$sourceName' source commit '$actualCommit' does not match locked commit '$expectedCommit'."
                    $runtimeReady = $false
                    continue
                }
                $actualDirty = Get-GitDirty $sourcePath
                if ($null -eq $actualDirty) {
                    Add-ErrorCheck "runtime:source-$sourceName" "Unable to inspect Git status for '$sourceName': $sourcePath"
                    $runtimeReady = $false
                    continue
                }
                $manifestDirtyProperty = $sourceValue.PSObject.Properties["dirty"]
                if ($null -eq $manifestDirtyProperty -or $manifestDirtyProperty.Value -isnot [bool] -or
                    [bool]$manifestDirtyProperty.Value -ne $actualDirty) {
                    Add-ErrorCheck "runtime:source-$sourceName" "Runtime manifest dirty provenance for '$sourceName' does not match the actual worktree."
                    $runtimeReady = $false
                } elseif ($RequireCleanRuntimeSources -and $actualDirty) {
                    Add-ErrorCheck "runtime:source-$sourceName" "Release runtime source '$sourceName' is dirty: $sourcePath"
                    $runtimeReady = $false
                } else {
                    Add-Check "runtime:source-$sourceName" $true ("commit={0}; dirty={1}" -f $actualCommit, $actualDirty)
                }
            }
            $workflowExternalHashProperty = $workflowEntry.PSObject.Properties["externalHeadersTreeSha256"]
            if ($null -eq $workflowExternalHashProperty -and
                $null -ne $workflowEntry.PSObject.Properties["engine"] -and
                $null -ne $workflowEntry.engine) {
                $workflowExternalHashProperty = $workflowEntry.engine.PSObject.Properties["externalHeadersTreeSha256"]
            }
            $workflowExternalHash = if ($null -eq $workflowExternalHashProperty) { "" } else { [string]$workflowExternalHashProperty.Value }
            if ($workflowExternalHash -notmatch '^[0-9a-fA-F]{64}$') {
                if ($RequireNonSurrogateExternalHeaders) {
                    Add-ErrorCheck "runtime:workflow-external-headers" "Runtime workflow '$runtimeWorkflowId' has no locked externalHeadersTreeSha256 value."
                    $runtimeReady = $false
                } else {
                    Add-WarningCheck "runtime:workflow-external-headers" "Runtime workflow has no independent external-header tree hash."
                }
            } elseif (-not [StringComparer]::OrdinalIgnoreCase.Equals($actualExternalHash, $workflowExternalHash)) {
                Add-ErrorCheck "runtime:workflow-external-headers" "Staged external headers do not match the locked workflow hash for '$runtimeWorkflowId'."
                $runtimeReady = $false
            } else {
                Add-Check "runtime:workflow-external-headers" $true $workflowExternalHash
            }
        }

        # The generated C++ ABI is tied to the exact engine build, not merely
        # the Unity/Tuanjie family. Bind the runtime lock to the project's
        # ProjectVersion.txt before Unity generation starts.
        $projectVersionPath = Join-Path $ProjectPath "ProjectSettings/ProjectVersion.txt"
        if (-not [IO.File]::Exists($projectVersionPath)) {
            Add-ErrorCheck "runtime:engine-version" "ProjectVersion.txt was not found: $projectVersionPath"
            $runtimeReady = $false
        } else {
            $projectUnityVersion = Read-ProjectVersionValue $projectVersionPath "m_EditorVersion"
            $runtimeEngineObject = if ($null -ne $runtimeManifest.PSObject.Properties["engine"]) {
                $runtimeManifest.engine
            } else { $null }
            $expectedUnityVersion = if ($null -ne $runtimeEngineObject -and
                $null -ne $runtimeEngineObject.PSObject.Properties["unityVersion"]) {
                [string]$runtimeEngineObject.unityVersion
            } else { "" }
            if ([string]::IsNullOrWhiteSpace($projectUnityVersion) -or
                [string]::IsNullOrWhiteSpace($expectedUnityVersion) -or
                -not $projectUnityVersion.Equals($expectedUnityVersion, [StringComparison]::OrdinalIgnoreCase)) {
                Add-ErrorCheck "runtime:engine-version" "Project m_EditorVersion '$projectUnityVersion' does not match runtime engine unityVersion '$expectedUnityVersion'."
                $runtimeReady = $false
            } else {
                Add-Check "runtime:engine-version" $true "m_EditorVersion=$projectUnityVersion"
            }

            $runtimeEngineExecutable = if ($null -ne $runtimeEngineObject -and
                $null -ne $runtimeEngineObject.PSObject.Properties["executablePath"]) {
                [string]$runtimeEngineObject.executablePath
            } else { "" }
            if (-not [string]::IsNullOrWhiteSpace($runtimeEngineExecutable) -and
                (Test-Path -LiteralPath $runtimeEngineExecutable -PathType Leaf)) {
                $editorProductVersion = [string](Get-Item -LiteralPath $runtimeEngineExecutable -Force).VersionInfo.ProductVersion
                if ([string]::IsNullOrWhiteSpace($editorProductVersion) -or
                    -not ($editorProductVersion -match ('^' + [regex]::Escape($expectedUnityVersion) + '(?:_|$)'))) {
                    Add-ErrorCheck "runtime:editor-product-version" "Engine executable ProductVersion '$editorProductVersion' does not match workflow unityVersion '$expectedUnityVersion'."
                    $runtimeReady = $false
                } else {
                    Add-Check "runtime:editor-product-version" $true "ProductVersion=$editorProductVersion"
                }
            } elseif ($RequireNonSurrogateExternalHeaders) {
                Add-ErrorCheck "runtime:editor-product-version" "Matching engine executable was not found for Release ProductVersion verification: $runtimeEngineExecutable"
                $runtimeReady = $false
            } else {
                Add-WarningCheck "runtime:editor-product-version" "Engine executable was unavailable; ProductVersion verification is deferred to the Player adapter."
            }

            $runtimeEngineFamily = if ($null -ne $runtimeEngineObject -and
                $null -ne $runtimeEngineObject.PSObject.Properties["family"]) {
                [string]$runtimeEngineObject.family
            } else { "" }
            if ($runtimeEngineFamily.Equals("Tuanjie", [StringComparison]::OrdinalIgnoreCase)) {
                $projectTuanjieVersion = Read-ProjectVersionValue $projectVersionPath "m_TuanjieEditorVersion"
                $expectedTuanjieVersion = if ($null -ne $runtimeEngineObject -and
                    $null -ne $runtimeEngineObject.PSObject.Properties["version"]) {
                    [string]$runtimeEngineObject.version
                } else { "" }
                if ([string]::IsNullOrWhiteSpace($projectTuanjieVersion) -or
                    [string]::IsNullOrWhiteSpace($expectedTuanjieVersion) -or
                    -not $projectTuanjieVersion.Equals($expectedTuanjieVersion, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-ErrorCheck "runtime:engine-family-version" "Project m_TuanjieEditorVersion '$projectTuanjieVersion' does not match runtime engine version '$expectedTuanjieVersion'."
                    $runtimeReady = $false
                } else {
                    Add-Check "runtime:engine-family-version" $true "m_TuanjieEditorVersion=$projectTuanjieVersion"
                }
            }
        }
    }
} else {
    Add-WarningCheck "runtime:directory" "Runtime source was not supplied; source-only preflight passed without runtime verification."
}
if ($RequireRuntime -and -not $runtimeReady) {
    $errors.Add("DHE runtime verification is required but did not pass.")
}

$report = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-source-preflight.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    runtimeRequired = [bool]$RequireRuntime
    runtimeReady = $runtimeReady
    cleanRuntimeSourcesRequired = [bool]$RequireCleanRuntimeSources
    packageLockPath = if ($packageLockAvailable) { $packageLockPath } else { $null }
    identityTemplatePath = if ([string]::IsNullOrWhiteSpace($identityTemplatePath)) { $null } else { $identityTemplatePath }
    identityTemplateRequired = [bool]$RequireIdentityTemplate
    embeddedPackageRequired = [bool]$RequireEmbeddedPackage
    embeddedPackagePresent = $embeddedPackagePresent
    externalHeadersRequired = [bool]$RequireNonSurrogateExternalHeaders
    externalHeadersSurrogate = if ($null -ne $runtimeManifest -and
        $null -ne $runtimeManifest.PSObject.Properties["externalHeaders"] -and
        $null -ne $runtimeManifest.externalHeaders.PSObject.Properties["surrogate"]) {
        [bool]$runtimeManifest.externalHeaders.surrogate
    } else { $null }
    labRoot = $LabRoot
    projectPath = $ProjectPath
    runtimeSource = $runtimePath
    hotUpdateAssemblies = [string[]]$hotUpdate
    dheAotAssemblies = [string[]]$dheAot
    dheAotConfigured = if ($null -eq $settings) { $false } else { [bool]$settings.dheAotConfigured }
    externalHotUpdateAssemblyDirs = [string[]]$externalDirs
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
    checks = $checks.ToArray()
}
$reportPath = Join-Path $OutputRoot "source-preflight-report.json"
[IO.File]::WriteAllText($reportPath, ($report | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE source preflight: $reportPath"
if (-not $report.passed) {
    Write-Error ("DHE source preflight failed:`n - " + ($errors -join "`n - "))
    exit 1
}
Write-Host ("DHE source preflight passed; runtimeReady={0}" -f $runtimeReady)
exit 0
