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

function Resolve-DheDnlibPath {
    param(
        [string]$RequestedPath = "",
        [string]$ProjectRoot = "",
        [string]$LabRoot = ""
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolvedRequested = [IO.Path]::GetFullPath($RequestedPath)
        if (-not [IO.File]::Exists($resolvedRequested)) {
            throw "dnlib.dll was not found: $resolvedRequested"
        }
        return $resolvedRequested
    }

    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($ProjectRoot)) {
        $projectPath = [IO.Path]::GetFullPath($ProjectRoot)
        $candidates.Add((Join-Path $projectPath "Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll"))
        foreach ($candidate in $candidates) {
            $resolvedCandidate = [IO.Path]::GetFullPath($candidate)
            if ([IO.File]::Exists($resolvedCandidate)) {
                return $resolvedCandidate
            }
        }
        throw "dnlib.dll was not found in the project embedded HybridCLR package. Pass -DnlibPath explicitly for registry or externally managed packages: $projectPath"
    }

    if (-not [string]::IsNullOrWhiteSpace($LabRoot)) {
        $labPath = [IO.Path]::GetFullPath($LabRoot)
        $candidates.Add((Join-Path $labPath "unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll"))
    }
    $candidates.Add((Join-Path $PSScriptRoot "../unity2021-dhe-demo/Packages/com.code-philosophy.hybridclr/Plugins/dnlib.dll"))
    $candidates.Add((Join-Path $PSScriptRoot "../../repos/hybridclr_unity/Plugins/dnlib.dll"))
    $candidates.Add((Join-Path $PSScriptRoot "../../../repos/hybridclr_unity/Plugins/dnlib.dll"))

    foreach ($candidate in $candidates) {
        $resolvedCandidate = [IO.Path]::GetFullPath($candidate)
        if ([IO.File]::Exists($resolvedCandidate)) {
            return $resolvedCandidate
        }
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

    # A caller can otherwise pass a tracked source directory (for example
    # `scripts` or `schemas`) with -ForceOutput and recursively delete the
    # repository.  Resolve the nearest Git worktree when available and reject
    # an output root that is itself a tracked directory.  Descendant output
    # directories remain valid, which preserves the normal `artifacts/...`
    # layout even when the parent is tracked only through other files.
    $gitRoot = $resolved
    while (-not [string]::IsNullOrWhiteSpace($gitRoot)) {
        if (Test-Path -LiteralPath (Join-Path $gitRoot ".git")) { break }
        $parent = Split-Path -Parent $gitRoot
        if ([string]::IsNullOrWhiteSpace($parent) -or
            $parent.Equals($gitRoot, [StringComparison]::OrdinalIgnoreCase)) {
            $gitRoot = ""
            break
        }
        $gitRoot = $parent.TrimEnd('\', '/')
    }
    if (-not [string]::IsNullOrWhiteSpace($gitRoot) -and
        (Test-Path -LiteralPath $gitRoot -PathType Container)) {
        try {
            $relative = $resolved.Substring($gitRoot.Length).TrimStart('\', '/').Replace('\', '/')
            if ([string]::IsNullOrWhiteSpace($relative)) {
                throw "DHE output root may not be the Git worktree root: $resolved"
            }
            $tracked = if ([string]::IsNullOrWhiteSpace($relative)) {
                @()
            } else {
                @(& git -C $gitRoot ls-files --cached -- "$relative" 2>$null)
            }
            if ($LASTEXITCODE -eq 0 -and $tracked.Count -gt 0) {
                throw "DHE output root overlaps Git-tracked source content; refusing recursive cleanup: $resolved"
            }
        } catch {
            if ($_.Exception.Message -like "DHE output root*") { throw }
            # Non-Git paths and older Git versions should not make an output
            # safety check fail for unrelated reasons.
        }
    }

    # Formal sources may be newly added and therefore not yet present in the
    # Git index. The checked-in boundary manifest still protects those paths
    # (notably patches/, schemas/, and fixture trees) from -ForceOutput.
    if (-not [string]::IsNullOrWhiteSpace($gitRoot)) {
        $boundaryPath = Join-Path $gitRoot "manifests/dhe-source-boundary.json"
        if (Test-Path -LiteralPath $boundaryPath -PathType Leaf) {
            try {
                $boundary = Get-Content -Raw -LiteralPath $boundaryPath | ConvertFrom-Json
                $boundaryPaths = @()
                foreach ($exact in @($boundary.exactPaths)) {
                    $boundaryPaths += [IO.Path]::GetFullPath((Join-Path $gitRoot ([string]$exact).Replace('/', [IO.Path]::DirectorySeparatorChar)))
                }
                foreach ($prefix in @($boundary.prefixes)) {
                    $boundaryPaths += [IO.Path]::GetFullPath((Join-Path $gitRoot ([string]$prefix).TrimEnd('/', '\').Replace('/', [IO.Path]::DirectorySeparatorChar)))
                }
                foreach ($boundaryPathEntry in $boundaryPaths) {
                    if (Test-DhePathRelation $resolved $boundaryPathEntry) {
                        throw "DHE output root overlaps the declared formal source boundary; refusing recursive cleanup: $resolved"
                    }
                }
            } catch {
                if ($_.Exception.Message -like "DHE output root*") { throw }
                # A malformed boundary is reported by the dedicated boundary
                # gate; do not turn unrelated exploratory output creation into
                # a parser failure here.
            }
        }
    }

    # Reject existing junctions/symlinks anywhere in the output path chain.
    # This protects recursive cleanup when the leaf itself has not been created.
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
