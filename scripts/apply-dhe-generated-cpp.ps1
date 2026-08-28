param(
    [Parameter(Mandatory = $false)]
    [string[]]$MvJson,
    [string]$MvJsonList = "",
    [Parameter(Mandatory = $true)]
    [string]$GeneratedCppRoot,
    [string]$ManifestFile = "",
    [string]$ResolvedManifestFile = "",
    [switch]$InPlace,
    [switch]$RequireCompleteCoverage,
    [string]$AotProbeDeclaringType = "",
    [string]$AotProbeMethodName = ""
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

if (-not [string]::IsNullOrWhiteSpace($MvJsonList)) {
    $MvJson = ConvertFrom-DheStringListArgument $MvJsonList
}
if (@($MvJson).Count -eq 0) {
    throw "At least one DHE MV JSON is required (use -MvJson or -MvJsonList)."
}
$root = [IO.Path]::GetFullPath($GeneratedCppRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) {
    throw "Generated C++ directory was not found: $root"
}

$mvPaths = @($MvJson | ForEach-Object {
    $path = [IO.Path]::GetFullPath($_)
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "DHE MV JSON was not found: $path"
    }
    $path
})
if ($mvPaths.Count -eq 0) {
    throw "At least one DHE MV JSON is required."
}
$mvDocuments = @($mvPaths | ForEach-Object { Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json })
$mvChangedTokenSets = @{}
$mvChangedCounts = @{}
$seenAssemblies = @{}
foreach ($mvDocument in $mvDocuments) {
    $assemblyName = [string]$mvDocument.assemblyName
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        throw "DHE MV JSON has no assemblyName."
    }
    if ($seenAssemblies.ContainsKey($assemblyName)) {
        throw "DHE MV JSON contains duplicate assembly '$assemblyName'."
    }
    $seenAssemblies[$assemblyName] = $true
    if ($null -eq $mvDocument.compatibility -or
        [string]$mvDocument.compatibility.status -ne "compatible" -or
        [string]$mvDocument.compatibility.mode -ne "method-body-only") {
        throw "DHE C++ injection requires a compatible method-body-only MV for '$assemblyName'."
    }
    $tokenSet = @{}
    $changedMethods = @($mvDocument.methods | Where-Object {
        $_.kind -eq "changed" -and $null -ne $_.currentToken
    })
    foreach ($mvMethod in $changedMethods) {
        $mvToken = [uint32]$mvMethod.currentToken
        if ($mvToken -eq 0 -or $tokenSet.ContainsKey($mvToken)) {
            throw "DHE MV contains a duplicate or invalid changed method token for '$assemblyName'."
        }
        $tokenSet[$mvToken] = $true
    }
    $mvChangedTokenSets[$assemblyName] = $tokenSet
    $mvChangedCounts[$assemblyName] = $changedMethods.Count
}

$manifestPath = if (-not [string]::IsNullOrWhiteSpace($ResolvedManifestFile)) {
    [IO.Path]::GetFullPath($ResolvedManifestFile)
} elseif ([string]::IsNullOrWhiteSpace($ManifestFile)) {
    Join-Path $root "dhe-native-manifest.json"
} else {
    [IO.Path]::GetFullPath($ManifestFile)
}

if ([string]::IsNullOrWhiteSpace($ResolvedManifestFile)) {
    $resolvedManifests = New-Object System.Collections.Generic.List[string]
    for ($mvIndex = 0; $mvIndex -lt $mvPaths.Count; ++$mvIndex) {
        $resolvedPath = if ($mvPaths.Count -eq 1) {
            $manifestPath
        } else {
            Join-Path ([IO.Path]::GetDirectoryName($manifestPath)) ("dhe-resolved-mv-{0:D3}.json" -f $mvIndex)
        }
        & (Join-Path $PSScriptRoot "resolve-dhe-native-manifest.ps1") `
            -MvJson $mvPaths[$mvIndex] `
            -GeneratedCppRoot $root `
            -OutputManifest $resolvedPath
        if (-not $?) {
            throw "Failed to resolve generated DHE native methods for '$($mvDocuments[$mvIndex].assemblyName)'."
        }
        $resolvedManifests.Add($resolvedPath)
    }
    if ($mvPaths.Count -gt 1) {
        $nativeManifests = @($resolvedManifests | ForEach-Object {
            Get-Content -Raw -LiteralPath $_ | ConvertFrom-Json
        })
        foreach ($nativeManifest in $nativeManifests) {
            if ([int]$nativeManifest.resolverVersion -ne 2 -or
                [string]$nativeManifest.abiContract -ne "il2cpp-generated-cpp-signature-v2") {
                throw "DHE native manifest uses an unsupported resolver/ABI contract."
            }
        }
        $aggregateMethods = New-Object System.Collections.Generic.List[object]
        $aggregateUnsupported = New-Object System.Collections.Generic.List[object]
        $aggregateChangedCount = 0
        $aggregateSupportedCount = 0
        $aggregateNativeEntryCount = 0
        foreach ($nativeManifest in $nativeManifests) {
            $aggregateChangedCount += [int]$nativeManifest.changedMethodCount
            $aggregateSupportedCount += [int]$nativeManifest.supportedChangedMethodCount
            $aggregateNativeEntryCount += [int]$nativeManifest.nativeEntryCount
            foreach ($method in @($nativeManifest.methods)) { $aggregateMethods.Add($method) }
            foreach ($method in @($nativeManifest.unsupportedChangedMethods)) { $aggregateUnsupported.Add($method) }
        }
        $aggregate = [ordered]@{
            schemaVersion = 1
            resolverVersion = 2
            abiContract = "il2cpp-generated-cpp-signature-v2"
            generatedCppRoot = $root
            changedMethodCount = $aggregateChangedCount
            supportedChangedMethodCount = $aggregateSupportedCount
            unsupportedChangedMethodCount = $aggregateUnsupported.Count
            nativeEntryCount = $aggregateNativeEntryCount
            unsupportedChangedMethods = $aggregateUnsupported.ToArray()
            methods = $aggregateMethods.ToArray()
        }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($manifestPath)) | Out-Null
        [IO.File]::WriteAllText($manifestPath, ($aggregate | ConvertTo-Json -Depth 10), (New-Object Text.UTF8Encoding($false)))
    }
} elseif (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw "Resolved DHE native manifest was not found: $manifestPath"
}

$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$manifestResolverVersion = if ($null -ne $manifest.PSObject.Properties["resolverVersion"]) {
    [int]$manifest.resolverVersion
} else { 0 }
if ($manifestResolverVersion -ne 2 -or
    [string]$manifest.abiContract -ne "il2cpp-generated-cpp-signature-v2") {
    throw "DHE native manifest uses an unsupported resolver/ABI contract."
}
$manifestRootProperty = $manifest.PSObject.Properties["generatedCppRoot"]
if ($null -eq $manifestRootProperty -or
    [IO.Path]::GetFullPath([string]$manifest.generatedCppRoot) -ne $root) {
    throw "DHE native manifest was generated for a different C++ root."
}
$mvChangedMethodCount = 0
foreach ($assemblyName in $mvChangedCounts.Keys) {
    $mvChangedMethodCount += [int]$mvChangedCounts[$assemblyName]
}
${unsupportedCount} = if ($null -ne $manifest.PSObject.Properties["unsupportedChangedMethodCount"]) {
    [int]$manifest.unsupportedChangedMethodCount
} else {
    0
}
$manifestMethods = @($manifest.methods)
$nativeEntryCount = if ($null -ne $manifest.PSObject.Properties["nativeEntryCount"]) {
    [int]$manifest.nativeEntryCount
} else { -1 }
if ($manifestMethods.Count -ne $nativeEntryCount) {
    throw "DHE native manifest native entry count does not match methods[]."
}
if ($unsupportedCount -lt 0 -or
    [int]$manifest.supportedChangedMethodCount + $unsupportedCount -ne $mvChangedMethodCount) {
    throw "DHE native manifest coverage counts do not match the aggregate MV set."
}
if ($null -ne $manifest.PSObject.Properties["changedMethodCount"] -and
    [int]$manifest.changedMethodCount -ne $mvChangedMethodCount) {
    throw "DHE native manifest changed method count does not match MV."
}
foreach ($manifestMethod in $manifestMethods) {
    $manifestToken = [uint32]$manifestMethod.methodToken
    $manifestAssembly = [string]$manifestMethod.assemblyName
    if ($manifestToken -eq 0 -or -not $mvChangedTokenSets.ContainsKey($manifestAssembly) -or
        -not $mvChangedTokenSets[$manifestAssembly].ContainsKey($manifestToken)) {
        throw "DHE native manifest contains a token not present in the matching MV changed methods: $manifestAssembly/$manifestToken"
    }
}
$supportedManifestTokens = @($manifestMethods | ForEach-Object {
    ([string]$_.assemblyName) + "/" + ([uint32]$_.methodToken).ToString()
} | Sort-Object -Unique)
if ($supportedManifestTokens.Count -ne [int]$manifest.supportedChangedMethodCount) {
    throw "DHE native manifest unique supported token count does not match supportedChangedMethodCount."
}
if ($RequireCompleteCoverage -and $unsupportedCount -gt 0) {
    throw "DHE C++ injection does not cover all changed methods: $unsupportedCount unsupported methods."
}
$methodsByFile = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($method in @($manifest.methods)) {
    $sourceFile = [IO.Path]::GetFullPath([string]$method.sourceFile)
    $rootPrefix = $root.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not $sourceFile.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolved generated C++ file is outside the generated root: $sourceFile"
    }
    if (-not $methodsByFile.ContainsKey($sourceFile)) {
        $methodsByFile[$sourceFile] = New-Object System.Collections.Generic.List[object]
    }
    $methodsByFile[$sourceFile].Add($method)
}
$duplicateFunctions = @($manifest.methods | Group-Object functionName | Where-Object Count -gt 1)
if ($duplicateFunctions.Count -gt 0) {
    throw "DHE native manifest contains duplicate generated functions: $(($duplicateFunctions | ForEach-Object Name) -join ', ')"
}

$transformed = 0
$manifestIndex = 0
$transactionRoot = Join-Path ([IO.Path]::GetDirectoryName($manifestPath)) (".dhe-transform-" + [Guid]::NewGuid().ToString("N"))
$destinationRoot = Join-Path ([IO.Path]::GetDirectoryName($manifestPath)) "generated-cpp-patched"
$stagedMappings = New-Object System.Collections.Generic.List[object]
$backupMappings = New-Object System.Collections.Generic.List[object]
$probeBackupMappings = New-Object System.Collections.Generic.List[object]
$commitStarted = $false
$transactionCommitted = $false

try {
    New-Item -ItemType Directory -Force -Path $transactionRoot | Out-Null

    # First generate and validate every transformed file in an isolated tree.
    # InPlace therefore means "commit all files after validation", rather than
    # exposing a partially transformed generated-code directory to the caller.
    foreach ($sourceFile in @($methodsByFile.Keys | Sort-Object)) {
        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            throw "Resolved generated C++ file was not found: $sourceFile"
        }
        # Basename-only names collide when IL2CPP emits the same source filename
        # in different output directories. Include the relative path and a stable
        # index so each source file gets its own manifest.
        $relativeSource = $sourceFile.Substring($root.Length).TrimStart('\', '/')
        $safeRelativeSource = $relativeSource -replace '[^A-Za-z0-9_.-]', '_'
        $fileManifest = Join-Path $transactionRoot ("dhe-{0:D3}-{1}.json" -f $manifestIndex, $safeRelativeSource)
        $manifestDirectory = [IO.Path]::GetDirectoryName($fileManifest)
        if (-not [string]::IsNullOrWhiteSpace($manifestDirectory)) {
            [IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
        }
        $manifestIndex++
        [IO.File]::WriteAllText(
            $fileManifest,
            ([ordered]@{ schemaVersion = 1; methods = $methodsByFile[$sourceFile].ToArray() } | ConvertTo-Json -Depth 10),
            (New-Object Text.UTF8Encoding($false)))

        $stagedFile = Join-Path $transactionRoot $relativeSource
        $stagedDirectory = [IO.Path]::GetDirectoryName($stagedFile)
        if (-not [string]::IsNullOrWhiteSpace($stagedDirectory)) {
            [IO.Directory]::CreateDirectory($stagedDirectory) | Out-Null
        }
        $reportFile = Join-Path $transactionRoot ("dhe-report-{0:D3}-{1}.json" -f $manifestIndex, $safeRelativeSource)
        & (Join-Path $PSScriptRoot "inject-dhe-guard.ps1") `
            -InputFile $sourceFile `
            -OutputFile $stagedFile `
            -ManifestFile $fileManifest `
            -ReportFile $reportFile
        if (-not $?) {
            throw "Failed to inject DHE guards into '$sourceFile'."
        }
        $report = Get-Content -Raw -LiteralPath $reportFile | ConvertFrom-Json
        $patchedText = [IO.File]::ReadAllText($stagedFile)
        foreach ($method in $methodsByFile[$sourceFile].ToArray()) {
            $marker = "HYBRIDCLR_DHE_GUARD_V4:$([string]$method.functionName):$([uint32]$method.methodToken)"
            if (-not $patchedText.Contains($marker)) {
                throw "DHE guard marker was not found after injection: $marker"
            }
        }
        $destinationFile = if ($InPlace) {
            $sourceFile
        } else {
            Join-Path $destinationRoot $relativeSource
        }
        $stagedMappings.Add([ordered]@{
            source = $sourceFile
            staged = $stagedFile
            destination = $destinationFile
            relative = $relativeSource
        })
        $transformed += [int]$report.transformedMethodCount
    }

    # Back up every destination before the commit. If a later copy fails, the
    # catch block restores all earlier copies, including InPlace input files.
    $backupRoot = Join-Path $transactionRoot "backup"
    $commitStarted = $true
    foreach ($mapping in $stagedMappings) {
        $destination = [string]$mapping.destination
        $existed = Test-Path -LiteralPath $destination -PathType Leaf
        $backupPath = Join-Path $backupRoot ([string]$mapping.relative)
        if ($existed) {
            $backupDirectory = [IO.Path]::GetDirectoryName($backupPath)
            if (-not [string]::IsNullOrWhiteSpace($backupDirectory)) {
                [IO.Directory]::CreateDirectory($backupDirectory) | Out-Null
            }
            Copy-Item -LiteralPath $destination -Destination $backupPath -Force
        }
        $backupMappings.Add([ordered]@{
            destination = $destination
            backup = $backupPath
            existed = $existed
        })
    }

    foreach ($mapping in $stagedMappings) {
        $destination = [string]$mapping.destination
        $destinationDirectory = [IO.Path]::GetDirectoryName($destination)
        if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
            [IO.Directory]::CreateDirectory($destinationDirectory) | Out-Null
        }
        Copy-Item -LiteralPath ([string]$mapping.staged) -Destination $destination -Force
    }
    $transactionCommitted = $true

    if (-not [string]::IsNullOrWhiteSpace($AotProbeDeclaringType) -or
        -not [string]::IsNullOrWhiteSpace($AotProbeMethodName)) {
        $probeMethodNames = @($AotProbeMethodName -split '[,;]' | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ([string]::IsNullOrWhiteSpace($AotProbeDeclaringType) -or $probeMethodNames.Count -eq 0) {
            throw "AOT probe requires both -AotProbeDeclaringType and -AotProbeMethodName."
        }
        # The probe helper edits generated C++ in place. Snapshot the whole
        # generated tree after guard commit and before the first probe so a
        # failed or partially applied diagnostic probe can be rolled back
        # before the guard transaction restores its own inputs.
        $probeBackupRoot = Join-Path $transactionRoot "probe-backup"
        $transactionPrefix = $transactionRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
        foreach ($probeSource in @(Get-ChildItem -LiteralPath $root -Recurse -File -Filter *.cpp |
                Where-Object { -not $_.FullName.StartsWith($transactionPrefix, [StringComparison]::OrdinalIgnoreCase) })) {
            $probeRelative = $probeSource.FullName.Substring($root.Length).TrimStart('\', '/')
            $probeBackupPath = Join-Path $probeBackupRoot $probeRelative
            $probeBackupDirectory = [IO.Path]::GetDirectoryName($probeBackupPath)
            if (-not [string]::IsNullOrWhiteSpace($probeBackupDirectory)) {
                [IO.Directory]::CreateDirectory($probeBackupDirectory) | Out-Null
            }
            Copy-Item -LiteralPath $probeSource.FullName -Destination $probeBackupPath -Force
            $probeBackupMappings.Add([ordered]@{
                source = $probeSource.FullName
                backup = $probeBackupPath
            })
        }
        foreach ($probeMethodName in $probeMethodNames) {
            & (Join-Path $PSScriptRoot "inject-dhe-aot-probe.ps1") `
                -GeneratedCppRoot $root `
                -DeclaringType $AotProbeDeclaringType `
                -MethodName $probeMethodName
            if (-not $?) {
                throw "Failed to inject DHE AOT probe for '$probeMethodName'."
            }
        }
    }
}
catch {
    # Probe edits happen after the guard commit. Restore that snapshot first,
    # then roll back the guard copies so the original generated tree is
    # restored even when probe injection fails midway through its method list.
    foreach ($probeBackup in $probeBackupMappings) {
        if (Test-Path -LiteralPath ([string]$probeBackup.backup) -PathType Leaf) {
            Copy-Item -LiteralPath ([string]$probeBackup.backup) -Destination ([string]$probeBackup.source) -Force
        }
    }
    if ($commitStarted) {
        foreach ($backup in @($backupMappings | Sort-Object { $_.destination } -Descending)) {
            if ([bool]$backup.existed) {
                Copy-Item -LiteralPath ([string]$backup.backup) -Destination ([string]$backup.destination) -Force
            } elseif (Test-Path -LiteralPath ([string]$backup.destination) -PathType Leaf) {
                Remove-Item -LiteralPath ([string]$backup.destination) -Force
            }
        }
    }
    throw
}
finally {
    if (Test-Path -LiteralPath $transactionRoot) {
        Remove-Item -LiteralPath $transactionRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

Write-Host ("Applied DHE guards to {0} generated files; methods transformed: {1}" -f $methodsByFile.Count, $transformed)
exit 0
