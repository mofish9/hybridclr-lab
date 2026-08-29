Set-StrictMode -Version Latest

function Invoke-DheGitApplyAtRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root,
        [Parameter(Mandatory = $true)]
        [string]$PatchPath,
        [ValidateRange(0, 8)]
        [int]$StripComponents = 1,
        [switch]$Reverse,
        [switch]$Check
    )

    $resolvedRoot = [IO.Path]::GetFullPath($Root).TrimEnd('\', '/')
    $resolvedPatch = [IO.Path]::GetFullPath($PatchPath)
    if (-not [IO.Directory]::Exists($resolvedRoot)) {
        throw "DHE patch root was not found: $resolvedRoot"
    }
    if (-not [IO.File]::Exists($resolvedPatch)) {
        throw "DHE patch was not found: $resolvedPatch"
    }

    $rootParent = [IO.Path]::GetDirectoryName($resolvedRoot)
    if ([string]::IsNullOrWhiteSpace($rootParent)) {
        throw "DHE patch root may not be a filesystem root: $resolvedRoot"
    }

    $arguments = @("apply")
    if ($Reverse) { $arguments += "--reverse" }
    if ($Check) { $arguments += "--check" }
    $arguments += @("--unsafe-paths", "--whitespace=nowarn", "-p$StripComponents", $resolvedPatch)

    $oldCeiling = [Environment]::GetEnvironmentVariable("GIT_CEILING_DIRECTORIES", "Process")
    $oldErrorActionPreference = $ErrorActionPreference
    try {
        # git apply works outside a repository. Prevent a copied/staged patch
        # root from borrowing an unrelated ancestor repository and changing
        # the path semantics of forward/reverse checks.
        [Environment]::SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", $rootParent, "Process")
        # Windows PowerShell 5.1 promotes redirected native stderr to a
        # terminating NativeCommandError under Stop. Non-zero is an expected
        # result for one side of every forward/reverse probe, so preserve it as
        # output and let callers decide from exitCode.
        $ErrorActionPreference = "Continue"
        $output = @(& git -C $resolvedRoot @arguments 2>&1)
        $exitCode = [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $oldErrorActionPreference
        [Environment]::SetEnvironmentVariable("GIT_CEILING_DIRECTORIES", $oldCeiling, "Process")
    }

    return [pscustomobject]@{
        exitCode = $exitCode
        output = @($output)
    }
}

function Resolve-DheEmbeddedPackageRoot {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [string]$PackageLockPath = "",
        [switch]$AllowMissing
    )

    $projectPath = [IO.Path]::GetFullPath($ProjectRoot)
    if (-not [IO.Directory]::Exists($projectPath)) {
        if ($AllowMissing) { return $null }
        throw "Unity project root was not found: $projectPath"
    }

    # A project DHE package lock owns the package path. This is intentionally
    # resolved relative to the project root, so a lock can live under Assets
    # or in an external build-artifact directory without changing semantics.
    if (-not [string]::IsNullOrWhiteSpace($PackageLockPath)) {
        $lockPath = [IO.Path]::GetFullPath($PackageLockPath)
        if (-not [IO.File]::Exists($lockPath)) {
            throw "DHE package lock was not found: $lockPath"
        }
        try {
            $lock = Get-Content -Raw -LiteralPath $lockPath | ConvertFrom-Json
        } catch {
            throw "DHE package lock is not valid JSON: $lockPath ($($_.Exception.Message))"
        }
        $packageReferenceProperty = $lock.PSObject.Properties["packagePath"]
        $packageReference = if ($null -eq $packageReferenceProperty) { "" } else {
            [string]$packageReferenceProperty.Value
        }
        if ([string]::IsNullOrWhiteSpace($packageReference) -or
            [IO.Path]::IsPathRooted($packageReference) -or
            $packageReference.Replace('\', '/') -match '(^|/)\.\.(/|$)') {
            throw "DHE package lock packagePath must be a safe project-root-relative path: '$packageReference'."
        }
        return [IO.Path]::GetFullPath((Join-Path $projectPath $packageReference))
    }

    $packagesPath = Join-Path $projectPath "Packages"
    $unversionedPath = Join-Path $packagesPath "com.code-philosophy.hybridclr"
    if ([IO.Directory]::Exists($unversionedPath)) {
        return [IO.Path]::GetFullPath($unversionedPath)
    }
    $versionedPaths = @(
        if ([IO.Directory]::Exists($packagesPath)) {
            Get-ChildItem -LiteralPath $packagesPath -Directory -Force -ErrorAction SilentlyContinue |
                Where-Object { $_.Name.StartsWith("com.code-philosophy.hybridclr@", [StringComparison]::OrdinalIgnoreCase) } |
                ForEach-Object { [IO.Path]::GetFullPath($_.FullName) }
        }
    )
    if ($versionedPaths.Count -eq 1) {
        return $versionedPaths[0]
    }
    if ($versionedPaths.Count -gt 1) {
        throw "Multiple embedded HybridCLR package directories were found under $packagesPath. Supply -PackageLockPath to select one."
    }
    if ($AllowMissing) { return $null }
    throw "An embedded HybridCLR package was not found under $packagesPath."
}

function Resolve-DheDnlibPath {
    param(
        [string]$RequestedPath = "",
        [string]$ProjectRoot = "",
        [string]$PackageLockPath = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequested = [IO.Path]::GetFullPath($RequestedPath)
        if (-not [IO.File]::Exists($resolvedRequested)) {
            throw "dnlib.dll was not found: $resolvedRequested"
        }
        return $resolvedRequested
    }

    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $projectPath = [IO.Path]::GetFullPath($ProjectRoot)
        $packageRoot = Resolve-DheEmbeddedPackageRoot -ProjectRoot $projectPath -PackageLockPath $PackageLockPath -AllowMissing
        $resolvedCandidate = if ($null -eq $packageRoot) { "" } else {
            [IO.Path]::GetFullPath((Join-Path $packageRoot "Plugins/dnlib.dll"))
        }
        if (-not [string]::IsNullOrWhiteSpace($resolvedCandidate) -and [IO.File]::Exists($resolvedCandidate)) {
            return $resolvedCandidate
        }
        throw "dnlib.dll was not found in the project embedded HybridCLR package. Pass -DnlibPath explicitly for registry or externally managed packages: $projectPath"
    }
    throw "dnlib.dll was not found. Pass -DnlibPath explicitly or provide a project embedded HybridCLR package."
}

# HybridCLRSettings.asset uses a small scalar-list subset of YAML. Keep the
# parser explicit about that boundary while handling the quoting cases seen in
# real projects; this is not intended to parse arbitrary YAML documents.
function Remove-DheYamlInlineComment {
    param([string]$Value)

    if ($null -eq $Value) {
        return ""
    }
    $inSingle = $false
    $inDouble = $false
    $escaped = $false
    for ($index = 0; $index -lt $Value.Length; $index++) {
        $character = $Value[$index]
        if ($inDouble -and $escaped) {
            $escaped = $false
            continue
        }
        if ($inDouble -and $character -eq [char]92) {
            $escaped = $true
            continue
        }
        if ($character -eq "'" -and -not $inDouble) {
            $inSingle = -not $inSingle
            continue
        }
        if ($character -eq '"' -and -not $inSingle) {
            $inDouble = -not $inDouble
            continue
        }
        if ($character -eq '#' -and -not $inSingle -and -not $inDouble -and
            ($index -eq 0 -or [char]::IsWhiteSpace($Value[$index - 1]))) {
            return $Value.Substring(0, $index).TrimEnd()
        }
    }
    return $Value.TrimEnd()
}

function Split-DheYamlListItems {
    param([string]$Value)

    $parts = New-Object System.Collections.Generic.List[string]
    if ($null -eq $Value) {
        return $parts.ToArray()
    }
    $start = 0
    $inSingle = $false
    $inDouble = $false
    $escaped = $false
    for ($index = 0; $index -lt $Value.Length; $index++) {
        $character = $Value[$index]
        if ($inDouble -and $escaped) {
            $escaped = $false
            continue
        }
        if ($inDouble -and $character -eq [char]92) {
            $escaped = $true
            continue
        }
        if ($character -eq "'" -and -not $inDouble) {
            $inSingle = -not $inSingle
            continue
        }
        if ($character -eq '"' -and -not $inSingle) {
            $inDouble = -not $inDouble
            continue
        }
        if ($character -eq ',' -and -not $inSingle -and -not $inDouble) {
            $parts.Add($Value.Substring($start, $index - $start))
            $start = $index + 1
        }
    }
    $parts.Add($Value.Substring($start))
    return $parts.ToArray()
}

function Convert-DheYamlScalarList {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return @()
    }
    $text = (Remove-DheYamlInlineComment $Value).Trim()
    if ($text -eq "[]") {
        return @()
    }
    if ($text.StartsWith("[") -and $text.EndsWith("]")) {
        $text = $text.Substring(1, $text.Length - 2)
    }
    $values = New-Object System.Collections.Generic.List[string]
    foreach ($part in @(Split-DheYamlListItems $text)) {
        $item = $part.Trim()
        if ($item.Length -ge 2 -and
            (($item.StartsWith("'") -and $item.EndsWith("'")) -or
             ($item.StartsWith('"') -and $item.EndsWith('"')))) {
            $item = $item.Substring(1, $item.Length - 2)
        }
        if ($item.Length -gt 0) {
            $values.Add($item)
        }
    }
    return $values.ToArray()
}

function Get-DheYamlList {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $values = New-Object System.Collections.Generic.List[string]
    if (-not [IO.File]::Exists($Path)) {
        return $values.ToArray()
    }
    $inSection = $false
    $sectionIndent = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match "^(?<indent>\s*)$([regex]::Escape($Key)):\s*(?<inline>.*)$") {
            $inSection = $true
            $sectionIndent = $Matches.indent.Length
            foreach ($value in @(Convert-DheYamlScalarList $Matches.inline)) {
                $values.Add($value)
            }
            continue
        }
        if (-not $inSection) {
            continue
        }
        if ($line -match '^(?<indent>\s*)[A-Za-z_][A-Za-z0-9_]*:\s*' -and
            $Matches.indent.Length -le $sectionIndent) {
            break
        }
        if ($line -match '^\s*-\s*(.+?)\s*$') {
            foreach ($value in @(Convert-DheYamlScalarList $Matches[1])) {
                $values.Add($value)
            }
        }
    }
    return $values.ToArray()
}

function Get-DheYamlDefinitionGuids {
    param([Parameter(Mandatory = $true)][string]$Path)

    $values = New-Object System.Collections.Generic.List[string]
    if (-not [IO.File]::Exists($Path)) {
        return $values.ToArray()
    }
    $inSection = $false
    $sectionIndent = 0
    foreach ($line in Get-Content -LiteralPath $Path) {
        $content = Remove-DheYamlInlineComment $line
        if ($line -match '^(?<indent>\s*)hotUpdateAssemblyDefinitions:\s*(?<inline>.*)$') {
            $inSection = $true
            $sectionIndent = $Matches.indent.Length
            continue
        }
        if (-not $inSection) {
            continue
        }
        if ($line -match '^(?<indent>\s*)[A-Za-z_][A-Za-z0-9_]*:\s*' -and
            $Matches.indent.Length -le $sectionIndent) {
            break
        }
        if ($content -match 'guid:\s*([0-9a-fA-F]{32})') {
            $values.Add($Matches[1].ToLowerInvariant())
        }
    }
    return @($values | Select-Object -Unique)
}

function Get-DheDuplicateNames {
    param([string[]]$Values)

    $seen = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
    $duplicates = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @($Values)) {
        $normalized = if ($null -eq $value) { "" } else { ([string]$value).Trim() }
        if ($normalized.Length -eq 0) {
            continue
        }
        if (-not $seen.Add($normalized)) {
            if (-not $duplicates.Contains($normalized)) {
                $duplicates.Add($normalized)
            }
        }
    }
    $sorted = Sort-DheOrdinal $duplicates.ToArray()
    return @($sorted | ForEach-Object { $_ })
}

function ConvertTo-DheStringArray {
    param([object[]]$Values)

    $result = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @($Values)) {
        if ($null -ne $value) {
            $result.Add([string]$value)
        }
    }
    return ,([string[]]$result.ToArray())
}

function Get-DheStrictBooleanProperty {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Description
    )

    if ($null -eq $Object) {
        throw "$Description is missing because its report is null."
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property -or $property.Value -isnot [bool]) {
        throw "$Description must be a JSON boolean."
    }
    return [bool]$property.Value
}

# Child PowerShell processes receive a single string for an array-valued
# parameter. Prefer a JSON array so valid Windows paths containing semicolons
# remain lossless; accept the historical semicolon form for older callers.
function ConvertFrom-DheStringListArgument {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ,([string[]]@())
    }
    $text = $Value.Trim()
    if ($text.StartsWith("[") -and $text.EndsWith("]")) {
        try {
            $parsed = ConvertFrom-Json -InputObject $text
        } catch {
            throw "DHE string-list argument is not valid JSON: $($_.Exception.Message)"
        }
        if ($null -eq $parsed) {
            return ,([string[]]@())
        }
        $items = @($parsed | ForEach-Object { if ($null -ne $_) { ([string]$_).Trim() } } |
            Where-Object { $_.Length -gt 0 })
        return ,([string[]]$items)
    }
    return ,([string[]](@($Value -split ';' |
        ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })))
}

function ConvertTo-DheStringListArgument {
    param([string[]]$Values)

    $items = @($Values | ForEach-Object { if ($null -ne $_) { ([string]$_).Trim() } } |
        Where-Object { $_.Length -gt 0 })
    return ConvertTo-Json -InputObject ([object[]]$items) -Compress
}

function Resolve-DheSettingsAssemblySets {
    param(
        [Parameter(Mandatory = $true)][string]$SettingsFile,
        [string]$ProjectRoot = ""
    )

    $settingsPath = [IO.Path]::GetFullPath($SettingsFile)
    if (-not [IO.File]::Exists($settingsPath)) {
        throw "HybridCLR settings file was not found: $settingsPath"
    }
    $projectRootPath = if ([string]::IsNullOrWhiteSpace($ProjectRoot)) {
        Split-Path -Parent (Split-Path -Parent $settingsPath)
    } else {
        [IO.Path]::GetFullPath($ProjectRoot)
    }

    $hotUpdate = New-Object System.Collections.Generic.List[string]
    foreach ($name in @(Get-DheYamlList $settingsPath "hotUpdateAssemblies")) {
        $hotUpdate.Add($name)
    }
    $definitionGuids = @(Get-DheYamlDefinitionGuids $settingsPath)
    if ($definitionGuids.Count -gt 0) {
        $assetsRoot = Join-Path $projectRootPath "Assets"
        if (-not [IO.Directory]::Exists($assetsRoot)) {
            throw "HybridCLR settings contain hotUpdateAssemblyDefinitions, but project Assets was not found: $assetsRoot"
        }
        $definitionByGuid = @{}
        foreach ($metaPath in @(Get-ChildItem -LiteralPath $assetsRoot -Recurse -File -Filter "*.asmdef.meta")) {
            $metaText = [IO.File]::ReadAllText($metaPath.FullName)
            if ($metaText -match '(?m)^guid:\s*([0-9a-fA-F]{32})\s*$') {
                $asmdefPath = $metaPath.FullName.Substring(0, $metaPath.FullName.Length - 5)
                if ([IO.File]::Exists($asmdefPath)) {
                    $definitionByGuid[$Matches[1].ToLowerInvariant()] = $asmdefPath
                }
            }
        }
        foreach ($guid in $definitionGuids) {
            if (-not $definitionByGuid.ContainsKey($guid)) {
                throw "Hot-update assembly definition GUID was not found under Assets: $guid"
            }
            $definitionJson = Get-Content -Raw -LiteralPath $definitionByGuid[$guid] | ConvertFrom-Json
            if ($null -eq $definitionJson.name -or [string]::IsNullOrWhiteSpace([string]$definitionJson.name)) {
                throw "Assembly definition has no name: $($definitionByGuid[$guid])"
            }
            $hotUpdate.Add([string]$definitionJson.name)
        }
    }
    $resolvedHotUpdate = @($hotUpdate | ForEach-Object { $_.Trim() } |
        Where-Object { $_.Length -gt 0 } | Select-Object -Unique)
    $configuredDheRaw = @(Get-DheYamlList $settingsPath "dheAotAssemblies")
    $configuredDhe = @($configuredDheRaw |
        ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 } | Select-Object -Unique)
    $resolvedDhe = if ($configuredDhe.Count -gt 0) { $configuredDhe } else { @($resolvedHotUpdate) }
    return [ordered]@{
        settingsPath = $settingsPath
        projectRoot = $projectRootPath
        hotUpdateAssemblies = ConvertTo-DheStringArray $resolvedHotUpdate
        dheAotAssemblies = ConvertTo-DheStringArray $resolvedDhe
        dheAotConfigured = $configuredDhe.Count -gt 0
        hotUpdateDuplicates = ConvertTo-DheStringArray (Get-DheDuplicateNames $hotUpdate.ToArray())
        dheAotDuplicates = ConvertTo-DheStringArray (Get-DheDuplicateNames $configuredDheRaw)
        externalHotUpdateAssemblyDirs = ConvertTo-DheStringArray (Get-DheYamlList $settingsPath "externalHotUpdateAssembliyDirs")
    }
}

function Assert-DheAdapterPrepareReport {
    param(
        [Parameter(Mandatory = $true)]
        [object]$Report,
        [Parameter(Mandatory = $true)]
        [int]$ToolchainContractVersion,
        [Parameter(Mandatory = $true)]
        [string]$Target,
        [Parameter(Mandatory = $true)]
        [string]$ProjectPath,
        [Parameter(Mandatory = $true)]
        [string]$SettingsFile,
        [ValidateSet("Release", "Exploratory")]
        [string]$Mode = "Exploratory",
        [string]$BaselineAotRoot = ""
    )

    if ([int]$Report.schemaVersion -ne 1 -or
        [string]$Report.format -ne "hybridclr.dhe-project-adapter-prepare.json") {
        throw "DHE adapter prepare report has an invalid schema or format."
    }
    if ([string]$Report.target -ne $Target -or
        [string]$Report.pathSemantics -ne "workspace-absolute-v1") {
        throw "DHE adapter prepare report has an unsupported target or path semantics."
    }
    if ([int]$Report.toolchainContractVersion -ne $ToolchainContractVersion) {
        throw "DHE adapter prepare report does not match toolchain contract version $ToolchainContractVersion."
    }
    if (-not (Get-DheStrictBooleanProperty $Report "passed" "DHE adapter prepare passed")) {
        throw "DHE adapter prepare report did not pass."
    }

    $references = [ordered]@{}
    foreach ($propertyName in @("projectPath", "settingsFile", "baselineRoot", "currentRoot")) {
        $property = $Report.PSObject.Properties[$propertyName]
        $value = if ($null -eq $property) { "" } else { [string]$property.Value }
        if ([string]::IsNullOrWhiteSpace($value) -or -not [IO.Path]::IsPathRooted($value)) {
            throw "DHE adapter prepare $propertyName must be an absolute path under workspace-absolute-v1."
        }
        $references[$propertyName] = [IO.Path]::GetFullPath($value)
    }
    foreach ($propertyName in @("baselineSourceRoot")) {
        $property = $Report.PSObject.Properties[$propertyName]
        $value = if ($null -eq $property) { "" } else { [string]$property.Value }
        if ([string]::IsNullOrWhiteSpace($value) -or -not [IO.Path]::IsPathRooted($value)) {
            throw "DHE adapter prepare $propertyName must be an absolute path under workspace-absolute-v1."
        }
        $references[$propertyName] = [IO.Path]::GetFullPath($value)
    }
    $baselineGeneratedProperty = $Report.PSObject.Properties["baselineGeneratedFromCurrent"]
    if ($null -eq $baselineGeneratedProperty -or $baselineGeneratedProperty.Value -isnot [bool]) {
        throw "DHE adapter prepare baselineGeneratedFromCurrent must be a JSON boolean."
    }
    $baselineGenerated = [bool]$baselineGeneratedProperty.Value
    if ($Mode -eq "Release") {
        if ([string]::IsNullOrWhiteSpace($BaselineAotRoot)) {
            throw "DHE Release requires an explicit BaselineAotRoot from a previous stripped-AOT release."
        }
        $expectedBaselineRoot = Normalize-DhePath ([IO.Path]::GetFullPath($BaselineAotRoot))
        if ($baselineGenerated -or (Normalize-DhePath $references.baselineSourceRoot) -ne $expectedBaselineRoot) {
            throw "DHE Release adapter prepare did not bind baselineSourceRoot to the supplied previous release baseline."
        }
    }
    $references.baselineGeneratedFromCurrent = $baselineGenerated
    if ((Normalize-DhePath $references.projectPath) -ne (Normalize-DhePath $ProjectPath) -or
        (Normalize-DhePath $references.settingsFile) -ne (Normalize-DhePath $SettingsFile)) {
        throw "DHE adapter prepare report is bound to a different project or settings file."
    }
    return [pscustomobject]$references
}

function Resolve-DhePowerShellHost {
    $preferredNames = if ($PSVersionTable.PSEdition -eq "Core") {
        @("pwsh", "powershell")
    } else {
        @("powershell", "pwsh")
    }

    foreach ($name in $preferredNames) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace([string]$command.Source)) {
            return [string]$command.Source
        }
    }
    throw "Neither pwsh nor powershell was found for invoking a DHE child script."
}

function Enter-DheWorkflowLock {
    param(
        [Parameter(Mandatory = $true)][string]$LabRoot,
        [ValidateRange(0, 3600)][int]$TimeoutSeconds = 0
    )

    $normalizedRoot = ([IO.Path]::GetFullPath($LabRoot)).TrimEnd('\', '/').ToUpperInvariant()
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalizedRoot))
    } finally {
        $sha.Dispose()
    }
    $lockId = ([BitConverter]::ToString($digest, 0, 16)).Replace('-', '')
    $mutexName = "HybridCLR.DHE.Workflow.$lockId"
    $mutex = New-Object Threading.Mutex($false, $mutexName)
    $acquired = $false
    try {
        try {
            $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds($TimeoutSeconds))
        } catch [Threading.AbandonedMutexException] {
            $acquired = $true
        }
        if (-not $acquired) {
            throw "Another DHE workflow is already using this worktree: $normalizedRoot. Wait for it to finish or use a separate Git worktree."
        }
        return ,([PSCustomObject]@{
            name = $mutexName
            root = $normalizedRoot
            mutex = $mutex
            acquired = $true
        })
    } catch {
        if (-not $acquired) {
            $mutex.Dispose()
        }
        throw
    }
}

function Exit-DheWorkflowLock {
    param([object]$Lock)

    if ($null -eq $Lock) { return }
    $mutex = $Lock.mutex
    if ($null -eq $mutex) { return }
    try {
        if ([bool]$Lock.acquired) {
            $mutex.ReleaseMutex()
            $Lock.acquired = $false
        }
    } finally {
        $mutex.Dispose()
    }
}

function Normalize-DhePath([string]$Path) {
    return [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
}

function Sort-DheOrdinal([object[]]$Values) {
    $list = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @($Values)) {
        if ($null -ne $value) {
            $list.Add([string]$value)
        }
    }
    $list.Sort([StringComparer]::Ordinal)
    return ,$list.ToArray()
}

function Get-DheFileSetHash {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Paths,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedRoot = Normalize-DhePath $Root
    if (-not [IO.Directory]::Exists($resolvedRoot)) {
        throw "DHE file-set hash root was not found: $resolvedRoot"
    }

    $entries = New-Object 'System.Collections.Generic.Dictionary[string,string]'([StringComparer]::Ordinal)
    $relativePaths = New-Object 'System.Collections.Generic.List[string]'
    $prefix = $resolvedRoot + [IO.Path]::DirectorySeparatorChar
    foreach ($inputPath in @($Paths)) {
        if ([string]::IsNullOrWhiteSpace($inputPath)) {
            throw "DHE file-set hash contains an empty path."
        }
        $resolvedPath = [IO.Path]::GetFullPath($inputPath)
        if (-not [IO.File]::Exists($resolvedPath)) {
            throw "DHE file-set hash input was not found: $resolvedPath"
        }
        if (-not $resolvedPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "DHE file-set hash input is outside its root: $resolvedPath"
        }
        $relative = $resolvedPath.Substring($prefix.Length).Replace('\', '/')
        if ($entries.ContainsKey($relative)) {
            throw "DHE file-set hash contains duplicate relative path: $relative"
        }
        $entries.Add($relative, $resolvedPath)
        $relativePaths.Add($relative)
    }
    if ($relativePaths.Count -eq 0) {
        throw "DHE file-set hash cannot be computed for an empty file set."
    }
    $relativePaths.Sort([StringComparer]::Ordinal)

    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        foreach ($relative in $relativePaths) {
            $nameBytes = [Text.Encoding]::UTF8.GetBytes("$relative`n")
            [void]$sha.TransformBlock($nameBytes, 0, $nameBytes.Length, $nameBytes, 0)
            $stream = [IO.File]::OpenRead([string]$entries[$relative])
            try {
                $buffer = New-Object byte[] 1048576
                while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    [void]$sha.TransformBlock($buffer, 0, $read, $buffer, 0)
                }
            } finally {
                $stream.Dispose()
            }
            $separator = [Text.Encoding]::UTF8.GetBytes("`n")
            [void]$sha.TransformBlock($separator, 0, $separator.Length, $separator, 0)
        }
        [void]$sha.TransformFinalBlock([byte[]]::new(0), 0, 0)
        return ([BitConverter]::ToString($sha.Hash) -replace '-', '').ToLowerInvariant()
    } finally {
        $sha.Dispose()
    }
}

function Get-DheEmptyFileSetHash {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash((New-Object byte[] 0))) -replace '-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-DheFileSetHashOrEmpty {
    param(
        [Parameter(Mandatory = $true)]
        [AllowEmptyCollection()]
        [string[]]$Paths,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    if (@($Paths).Count -eq 0) {
        # An empty native guard set is a valid no-op build. Keep a stable
        # digest in build identity instead of making callers invent a path.
        return Get-DheEmptyFileSetHash
    }
    return Get-DheFileSetHash $Paths $Root
}

function New-DheNativeGuardCoverage {
    param(
        [AllowNull()]
        [object]$NativeManifest,
        [Parameter(Mandatory = $true)]
        [ValidateRange(0, [int]::MaxValue)]
        [int]$ChangedMethodCount
    )

    if ($null -eq $NativeManifest) {
        return [ordered]@{
            manifestAvailable = $false
            changedMethodCount = $ChangedMethodCount
            supportedChangedMethodCount = 0
            unsupportedChangedMethodCount = 0
            nativeEntryCount = 0
            guardedMethodCount = 0
            complete = $false
        }
    }

    foreach ($propertyName in @(
        "supportedChangedMethodCount",
        "unsupportedChangedMethodCount",
        "nativeEntryCount")) {
        $property = $NativeManifest.PSObject.Properties[$propertyName]
        if ($null -eq $property) {
            throw "DHE native manifest is missing $propertyName."
        }
    }

    $supported = [int]$NativeManifest.supportedChangedMethodCount
    $unsupported = [int]$NativeManifest.unsupportedChangedMethodCount
    $nativeEntries = [int]$NativeManifest.nativeEntryCount
    if ($supported -lt 0 -or $unsupported -lt 0 -or $nativeEntries -lt 0) {
        throw "DHE native manifest coverage counts must be non-negative."
    }
    $complete = $supported + $unsupported -eq $ChangedMethodCount -and
        $unsupported -eq 0 -and $nativeEntries -ge $supported

    return [ordered]@{
        manifestAvailable = $true
        changedMethodCount = $ChangedMethodCount
        supportedChangedMethodCount = $supported
        unsupportedChangedMethodCount = $unsupported
        nativeEntryCount = $nativeEntries
        # A guard is a managed-token decision. Generic sharing can emit more
        # than one native entry for the same guarded managed method.
        guardedMethodCount = $supported
        complete = $complete
    }
}

function Test-DhePathRelation([string]$Left, [string]$Right) {
    $leftPath = Normalize-DhePath $Left
    $rightPath = Normalize-DhePath $Right
    if ($leftPath.Equals($rightPath, [StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }
    $leftPrefix = $leftPath + [IO.Path]::DirectorySeparatorChar
    $rightPrefix = $rightPath + [IO.Path]::DirectorySeparatorChar
    return $leftPath.StartsWith($rightPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        $rightPath.StartsWith($leftPrefix, [StringComparison]::OrdinalIgnoreCase)
}

function Test-DhePathWithinRoot([string]$Path, [string]$Root) {
    $resolvedPath = Normalize-DhePath $Path
    $resolvedRoot = Normalize-DhePath $Root
    return $resolvedPath.Equals($resolvedRoot, [StringComparison]::OrdinalIgnoreCase) -or
        $resolvedPath.StartsWith($resolvedRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)
}

function Get-DheRegularTreeFiles {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $resolvedRoot = [IO.Path]::GetFullPath($Root)
    if (-not (Test-Path -LiteralPath $resolvedRoot)) {
        throw "DHE file tree root was not found: $resolvedRoot"
    }
    $rootItem = Get-Item -LiteralPath $resolvedRoot -Force
    if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "DHE file tree may not contain a junction or symbolic link: $resolvedRoot"
    }
    if (-not $rootItem.PSIsContainer) {
        return $rootItem
    }

    $pending = New-Object 'System.Collections.Generic.Queue[string]'
    $pending.Enqueue($resolvedRoot)
    while ($pending.Count -gt 0) {
        $directory = $pending.Dequeue()
        foreach ($item in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "DHE file tree may not contain a junction or symbolic link: $($item.FullName)"
            }
            if ($item.PSIsContainer) {
                $pending.Enqueue($item.FullName)
            } else {
                $item
            }
        }
    }
}

function Find-DheContainingGitRoot([string]$Path) {
    $cursor = Normalize-DhePath $Path
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath (Join-Path $cursor ".git")) {
            return $cursor
        }
        $cursorRoot = [IO.Path]::GetPathRoot($cursor)
        if (-not [string]::IsNullOrWhiteSpace($cursorRoot) -and
            $cursor.Equals($cursorRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($cursor, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $cursor = $parent.TrimEnd('\', '/')
    }
    return $null
}

function Assert-DheBasicOutputRootSafety {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ProtectedPaths = @()
    )

    $resolved = Normalize-DhePath $Path
    $pathRoot = [IO.Path]::GetPathRoot($resolved)
    if (-not [string]::IsNullOrWhiteSpace($pathRoot) -and
        $resolved.Equals($pathRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE output root may not be a filesystem root: $resolved"
    }
    foreach ($protected in @($ProtectedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if (Test-DhePathRelation $resolved $protected) {
            throw "DHE output root must not overlap protected path '$([IO.Path]::GetFullPath($protected))': $resolved"
        }
    }

    # Reject existing junctions/symlinks anywhere in the output path chain.
    # This protects both recursive cleanup and verified package replacement.
    $cursor = $resolved
    while (-not [string]::IsNullOrWhiteSpace($cursor)) {
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "DHE output root may not contain a junction or symbolic link: $cursor"
            }
        }
        $cursorRoot = [IO.Path]::GetPathRoot($cursor)
        if (-not [string]::IsNullOrWhiteSpace($cursorRoot) -and
            $cursor.Equals($cursorRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $parent = Split-Path -Parent $cursor
        if ([string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($cursor, [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
        $cursor = $parent.TrimEnd('\', '/')
    }
}

function Assert-DheSafeVerifiedReplacementRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ProtectedPaths = @()
    )

    $resolved = Normalize-DhePath $Path
    Assert-DheBasicOutputRootSafety -Path $resolved -ProtectedPaths $ProtectedPaths
    $gitRoot = Find-DheContainingGitRoot $resolved
    if (-not [string]::IsNullOrWhiteSpace([string]$gitRoot) -and
        $resolved.Equals($gitRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE verified replacement root may not be the Git worktree root: $resolved"
    }
}

function New-DheTemporaryReportPath([string]$Name) {
    $safeName = if ([string]::IsNullOrWhiteSpace($Name)) { "report" } else {
        [regex]::Replace($Name, '[^A-Za-z0-9._-]', '-')
    }
    $reportRoot = Join-Path ([IO.Path]::GetTempPath()) "HybridCLRDhe/reports"
    return Join-Path $reportRoot ("{0}-{1}-{2}.json" -f $safeName, $PID, [Guid]::NewGuid().ToString("N"))
}

function Get-DheToolchainPackageId {
    param(
        [Parameter(Mandatory = $true)][string]$ToolchainVersion,
        [Parameter(Mandatory = $true)][int]$ContractVersion,
        [Parameter(Mandatory = $true)][string]$Mode,
        [string]$SourceHead = "",
        [string]$SourceTree = "",
        [Parameter(Mandatory = $true)][string]$LayoutSha256,
        [Parameter(Mandatory = $true)][object[]]$Files
    )

    $recordsByPath = New-Object 'System.Collections.Generic.Dictionary[string,object]'([StringComparer]::Ordinal)
    foreach ($record in @($Files)) {
        $path = ([string]$record.path).Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($path) -or $recordsByPath.ContainsKey($path)) {
            throw "DHE toolchain package ID requires unique non-empty file paths."
        }
        $recordsByPath.Add($path, $record)
    }
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("hybridclr-dhe-toolchain-package-v1")
    $lines.Add($ToolchainVersion)
    $lines.Add($ContractVersion.ToString([Globalization.CultureInfo]::InvariantCulture))
    $lines.Add($Mode)
    $lines.Add(([string]$SourceHead).ToLowerInvariant())
    $lines.Add(([string]$SourceTree).ToLowerInvariant())
    $lines.Add($LayoutSha256.ToLowerInvariant())
    foreach ($path in (Sort-DheOrdinal ([string[]]@($recordsByPath.Keys)))) {
        $record = $recordsByPath[$path]
        $lines.Add(("{0}`0{1}`0{2}" -f $path, ([int64]$record.size).ToString([Globalization.CultureInfo]::InvariantCulture), ([string]$record.sha256).ToLowerInvariant()))
    }
    $payload = [Text.Encoding]::UTF8.GetBytes(($lines -join "`n"))
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return (($sha.ComputeHash($payload) | ForEach-Object { $_.ToString("x2") }) -join "")
    } finally {
        $sha.Dispose()
    }
}

function Assert-DheSafeReportPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ProtectedPaths = @()
    )

    $resolved = [IO.Path]::GetFullPath($Path)
    if (Test-Path -LiteralPath $resolved) {
        $existingReportItem = Get-Item -LiteralPath $resolved -Force
        if (($existingReportItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "DHE report path may not be a junction or symbolic link: $resolved"
        }
        if ($existingReportItem.PSIsContainer) {
            throw "DHE report path may not be an existing directory: $resolved"
        }
    }
    foreach ($protected in @($ProtectedPaths | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })) {
        if (Test-DhePathRelation $resolved $protected) {
            throw "DHE report path must not overlap protected path '$([IO.Path]::GetFullPath($protected))': $resolved"
        }
    }

    $parent = [IO.Path]::GetDirectoryName($resolved)
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "DHE report path has no safe parent directory: $resolved"
    }
    Assert-DheBasicOutputRootSafety -Path $parent

    $gitRoot = Find-DheContainingGitRoot $parent
    if (-not [string]::IsNullOrWhiteSpace([string]$gitRoot)) {
        $relative = $resolved.Substring($gitRoot.Length).TrimStart('\', '/').Replace('\', '/')
        $oldErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            [object[]]$tracked = @(& git -C $gitRoot ls-files --cached -- "$relative" 2>&1)
            $trackedExitCode = [int]$LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldErrorActionPreference
        }
        if ($trackedExitCode -ne 0) {
            throw "DHE report path Git inspection failed for '$resolved' (exit=$trackedExitCode)."
        }
        if (@($tracked).Count -gt 0) {
            throw "DHE report path may not overwrite Git-tracked content: $resolved"
        }
    }
    return $resolved
}

function Assert-DheOutputNotAncestor {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Root
    )

    $outputPath = Normalize-DhePath $Path
    $rootPath = Normalize-DhePath $Root
    if ($outputPath.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase) -or
        $rootPath.StartsWith($outputPath + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "DHE output root must not be the protected root or one of its ancestors: $outputPath"
    }
}

function Assert-DheSafeOutputRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [string[]]$ProtectedPaths = @()
    )

    $resolved = Normalize-DhePath $Path
    Assert-DheBasicOutputRootSafety -Path $resolved -ProtectedPaths $ProtectedPaths

    # A caller can otherwise pass a tracked source directory (for example
    # `scripts` or `schemas`) with -ForceOutput and recursively delete the
    # repository.  Resolve the nearest Git worktree when available and reject
    # an output root that is itself a tracked directory.  Descendant output
    # directories remain valid, which preserves the normal `artifacts/...`
    # layout even when the parent is tracked only through other files.
    $gitRoot = Find-DheContainingGitRoot $resolved
    if (-not [string]::IsNullOrWhiteSpace($gitRoot) -and
        (Test-Path -LiteralPath $gitRoot -PathType Container)) {
        $relative = $resolved.Substring($gitRoot.Length).TrimStart('\', '/').Replace('\', '/')
        if ([string]::IsNullOrWhiteSpace($relative)) {
            throw "DHE output root may not be the Git worktree root: $resolved"
        }
        $oldErrorActionPreference = $ErrorActionPreference
        try {
            # Windows PowerShell 5.1 can promote redirected native stderr to a
            # terminating error. Capture the exit code explicitly and fail
            # closed when Git cannot prove that the path is untracked.
            $ErrorActionPreference = "Continue"
            [object[]]$tracked = @(& git -C $gitRoot ls-files --cached -- "$relative" 2>&1)
            $trackedExitCode = [int]$LASTEXITCODE
        } finally {
            $ErrorActionPreference = $oldErrorActionPreference
        }
        if ($trackedExitCode -ne 0) {
            throw "DHE output root Git inspection failed for '$resolved' (exit=$trackedExitCode)."
        }
        if (@($tracked).Count -gt 0) {
            throw "DHE output root overlaps Git-tracked source content; refusing recursive cleanup: $resolved"
        }
    }

    # Formal sources may be newly added and therefore not yet present in the
    # Git index. The checked-in boundary manifest still protects those paths
    # (notably patches/, schemas/, and fixture trees) from -ForceOutput.
    if (-not [string]::IsNullOrWhiteSpace($gitRoot)) {
        $boundaryCandidates = New-Object 'System.Collections.Generic.HashSet[string]'([StringComparer]::OrdinalIgnoreCase)
        $null = $boundaryCandidates.Add((Join-Path $gitRoot "manifests/dhe-source-boundary.json"))
        $boundaryCursor = $resolved
        while (-not [string]::IsNullOrWhiteSpace($boundaryCursor) -and
            (Test-DhePathWithinRoot $boundaryCursor $gitRoot)) {
            $null = $boundaryCandidates.Add((Join-Path $boundaryCursor "dhe-source-boundary.json"))
            if ($boundaryCursor.Equals($gitRoot, [StringComparison]::OrdinalIgnoreCase)) { break }
            $parent = Split-Path -Parent $boundaryCursor
            if ([string]::IsNullOrWhiteSpace($parent) -or
                $parent.Equals($boundaryCursor, [StringComparison]::OrdinalIgnoreCase)) { break }
            $boundaryCursor = $parent.TrimEnd('\', '/')
        }
        foreach ($sourceBoundaryPath in $boundaryCandidates) {
            if (-not (Test-Path -LiteralPath $sourceBoundaryPath -PathType Leaf)) { continue }
            try {
                $boundary = Get-Content -Raw -LiteralPath $sourceBoundaryPath | ConvertFrom-Json
                $pathBase = if ($null -eq $boundary.PSObject.Properties["pathBase"]) { "" } else { [string]$boundary.pathBase }
                $boundaryBase = if ($pathBase -eq "manifest-directory-v1") {
                    [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($sourceBoundaryPath))
                } elseif ($pathBase -eq "git-root-v1") {
                    $gitRoot
                } else { throw "DHE source boundary has an unsupported pathBase: $sourceBoundaryPath" }
                $boundaryPaths = @()
                foreach ($exact in @($boundary.exactPaths)) {
                    $relative = ([string]$exact).Replace('\', '/').TrimStart('/')
                    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.(/|$)') {
                        throw "DHE source boundary contains an unsafe exact path: $sourceBoundaryPath"
                    }
                    $boundaryPaths += [IO.Path]::GetFullPath((Join-Path $boundaryBase $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
                }
                foreach ($prefix in @($boundary.prefixes)) {
                    $relative = ([string]$prefix).Replace('\', '/').TrimStart('/')
                    $wildcard = $relative.IndexOf('*')
                    if ($wildcard -ge 0) {
                        $relative = $relative.Substring(0, $wildcard).TrimEnd('/')
                    } else { $relative = $relative.TrimEnd('/') }
                    if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or $relative -match '(^|/)\.\.(/|$)') {
                        throw "DHE source boundary contains an unsafe prefix: $sourceBoundaryPath"
                    }
                    $boundaryPaths += [IO.Path]::GetFullPath((Join-Path $boundaryBase $relative.Replace('/', [IO.Path]::DirectorySeparatorChar)))
                }
                foreach ($boundaryPathEntry in $boundaryPaths) {
                    if (Test-DhePathRelation $resolved $boundaryPathEntry) {
                        throw "DHE output root overlaps the declared formal source boundary; refusing recursive cleanup: $resolved"
                    }
                }
            } catch {
                if ($_.Exception.Message -like "DHE output root*" -or
                    $_.Exception.Message -like "DHE source boundary*") { throw }
                throw "DHE source boundary inspection failed: $sourceBoundaryPath ($($_.Exception.Message))"
            }
        }
    }
}

function Initialize-DheOutputRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [switch]$Force,
        [string[]]$ProtectedPaths = @()
    )

    $resolved = Normalize-DhePath $Path
    Assert-DheSafeOutputRoot -Path $resolved -ProtectedPaths $ProtectedPaths
    if (Test-Path -LiteralPath $resolved) {
        if (Test-Path -LiteralPath $resolved -PathType Leaf) {
            if (-not $Force) {
                throw "OutputRoot is an existing file: $resolved. Pass -ForceOutput to replace it."
            }
            Remove-Item -LiteralPath $resolved -Force
            New-Item -ItemType Directory -Force -Path $resolved | Out-Null
            return $resolved
        }
        $existingItems = @(Get-ChildItem -LiteralPath $resolved -Force -ErrorAction SilentlyContinue)
        if ($existingItems.Count -gt 0 -and -not $Force) {
            throw "OutputRoot is not empty: $resolved. Pass -ForceOutput to replace a prior run."
        }
        if ($Force) {
            Remove-Item -LiteralPath $resolved -Recurse -Force
        }
    }
    New-Item -ItemType Directory -Force -Path $resolved | Out-Null
    return $resolved
}
