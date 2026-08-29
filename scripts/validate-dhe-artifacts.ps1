[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string[]]$MvJson,

    [string]$MvJsonList = "",

    [string[]]$MvBytes,
    [string]$MvBytesList = "",
    [string[]]$BaselineAssembly,
    [string]$BaselineAssemblyList = "",
    [string[]]$CurrentAssembly,
    [string]$CurrentAssemblyList = "",
    [string]$NativeManifest,
    [string]$BuildIdentity,
    [string]$WorkflowReport,
    [string]$RuntimePlan,
    [string]$BatchReport,
    [switch]$RequireCompleteCoverage,
    [string]$Output = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

$errors = New-Object System.Collections.Generic.List[string]
$warnings = New-Object System.Collections.Generic.List[string]
$coverageErrors = New-Object System.Collections.Generic.List[string]

function Add-Error([string]$Message) { $errors.Add($Message) }
function Add-Warning([string]$Message) { $warnings.Add($Message) }
function Add-CoverageError([string]$Message) {
    $errors.Add($Message)
    $coverageErrors.Add($Message)
}

function Resolve-ReferencePath([string]$Path, [string]$BaseDirectory = "") {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    if ([IO.Path]::IsPathRooted($Path)) {
        return [IO.Path]::GetFullPath($Path)
    }
    if (-not [string]::IsNullOrWhiteSpace($BaseDirectory)) {
        return [IO.Path]::GetFullPath((Join-Path $BaseDirectory $Path))
    }
    return [IO.Path]::GetFullPath($Path)
}

function Resolve-File([string]$Path, [string]$Description, [string]$BaseDirectory = "") {
    $resolved = Resolve-ReferencePath $Path $BaseDirectory
    if (-not [IO.File]::Exists($resolved)) {
        Add-Error "$Description was not found: $resolved"
        return $null
    }
    return $resolved
}

function Read-JsonFile([string]$Path, [string]$Description) {
    $resolved = Resolve-File $Path $Description
    if ($null -eq $resolved) { return $null }
    try {
        return Get-Content -Raw -LiteralPath $resolved | ConvertFrom-Json
    }
    catch {
        Add-Error "$Description is not valid JSON: $resolved ($($_.Exception.Message))"
        return $null
    }
}

function Get-StringProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return [string]$property.Value
}

function Get-ObjectProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-IntProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    try { return [int]$property.Value } catch { return $null }
}

function Get-BoolProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    if ($property.Value -isnot [bool]) {
        Add-Error "Property '$Name' must be a JSON boolean."
        return $null
    }
    return [bool]$property.Value
}

function Get-NullableBoolProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    if ($null -eq $property.Value) { return $null }
    if ($property.Value -isnot [bool]) {
        Add-Error "Property '$Name' must be a JSON boolean or null."
        return $null
    }
    return [bool]$property.Value
}

function Get-Sha256Hex([byte[]]$Bytes) {
    return ([BitConverter]::ToString($Bytes)).Replace("-", "").ToLowerInvariant()
}

function Get-U32([byte[]]$Bytes, [int]$Offset) {
    if ($Offset -lt 0 -or $Offset + 4 -gt $Bytes.Length) {
        throw "u32 offset is outside the binary payload."
    }
    return [BitConverter]::ToUInt32($Bytes, $Offset)
}

function Validate-AssemblyHash([string]$Path, [string]$Expected, [string]$Description) {
    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }
    $resolved = Resolve-File $Path $Description
    if ($null -eq $resolved) {
        return $null
    }
    $actual = (Get-FileHash -LiteralPath $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    if (-not [StringComparer]::OrdinalIgnoreCase.Equals($actual, $Expected)) {
        Add-Error "$Description hash does not match MV JSON: expected $Expected, got $actual."
    }
    return $resolved
}

function Add-AssemblyPathMapping {
    param(
        [Parameter(Mandatory = $true)]
        [hashtable]$Map,
        [Parameter(Mandatory = $true)]
        [string]$Key,
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Description
    )

    if ([string]::IsNullOrWhiteSpace($Key)) {
        Add-Error "$Description has an empty assembly basename: $Path"
        return
    }
    if ($Map.ContainsKey($Key)) {
        Add-Error "$Description contain duplicate assembly basename '$Key': '$($Map[$Key])' and '$Path'."
        return
    }
    $Map[$Key] = $Path
}

$mvJsonArguments = @()
$mvBytesArguments = @()
$baselineAssemblyArguments = @()
$currentAssemblyArguments = @()
if ($null -ne $MvJson) { $mvJsonArguments += @($MvJson) }
if (-not [string]::IsNullOrWhiteSpace($MvJsonList)) {
    $mvJsonArguments += ConvertFrom-DheStringListArgument $MvJsonList
}
if ($null -ne $MvBytes) { $mvBytesArguments += @($MvBytes) }
if (-not [string]::IsNullOrWhiteSpace($MvBytesList)) {
    $mvBytesArguments += ConvertFrom-DheStringListArgument $MvBytesList
}
if ($null -ne $BaselineAssembly) { $baselineAssemblyArguments += @($BaselineAssembly) }
if (-not [string]::IsNullOrWhiteSpace($BaselineAssemblyList)) {
    $baselineAssemblyArguments += ConvertFrom-DheStringListArgument $BaselineAssemblyList
}
if ($null -ne $CurrentAssembly) { $currentAssemblyArguments += @($CurrentAssembly) }
if (-not [string]::IsNullOrWhiteSpace($CurrentAssemblyList)) {
    $currentAssemblyArguments += ConvertFrom-DheStringListArgument $CurrentAssemblyList
}
$mvJsonCount = $mvJsonArguments.Count
$mvBytesCount = $mvBytesArguments.Count
$baselineAssemblyCount = $baselineAssemblyArguments.Count
$currentAssemblyCount = $currentAssemblyArguments.Count

if ($RequireCompleteCoverage) {
    if ($mvBytesArguments.Count -eq 0) {
        Add-Error "Complete coverage validation requires an MV binary."
    }
    if ([string]::IsNullOrWhiteSpace($NativeManifest)) {
        Add-Error "Complete coverage validation requires a native guard manifest."
    }
    foreach ($pair in @(
        @($mvBytesArguments, "MV binary"),
        @($baselineAssemblyArguments, "baseline assembly"),
        @($currentAssemblyArguments, "current assembly")
    )) {
        if ($pair[0].Count -ne $mvJsonCount) {
            Add-Error "Complete coverage requires one $($pair[1]) per MV JSON ($mvJsonCount expected, got $($pair[0].Count))."
        }
    }
}
if ($mvJsonArguments.Count -eq 0) {
    Add-Error "At least one MV JSON is required (use -MvJson or -MvJsonList)."
}

$mvPaths = @()
$mvDocuments = New-Object System.Collections.Generic.List[object]
$baselineAssemblyPaths = New-Object System.Collections.Generic.List[object]
$currentAssemblyPaths = New-Object System.Collections.Generic.List[object]
$mvChangedTokens = New-Object System.Collections.Generic.List[uint32]
$mvBinaryByAssembly = @{}
$seenMvAssemblies = New-Object System.Collections.Generic.HashSet[string]([StringComparer]::Ordinal)
$mvChangedCount = 0
$mv = $null
$mvPath = $null
$baselineAssemblyByName = @{}
foreach ($assemblyPathArgument in @($baselineAssemblyArguments)) {
    $assemblyKey = [IO.Path]::GetFileNameWithoutExtension([string]$assemblyPathArgument)
    Add-AssemblyPathMapping $baselineAssemblyByName $assemblyKey ([string]$assemblyPathArgument) "Baseline assembly inputs"
}
$currentAssemblyByName = @{}
foreach ($assemblyPathArgument in @($currentAssemblyArguments)) {
    $assemblyKey = [IO.Path]::GetFileNameWithoutExtension([string]$assemblyPathArgument)
    Add-AssemblyPathMapping $currentAssemblyByName $assemblyKey ([string]$assemblyPathArgument) "Current assembly inputs"
}
$mvBytesByName = @{}
foreach ($binaryPathArgument in @($mvBytesArguments)) {
    $binaryName = [IO.Path]::GetFileName([string]$binaryPathArgument)
    if ($binaryName -notmatch '(?i)\.mv\.bytes$') {
        Add-Error "MV binary input does not use the expected '<assembly>.mv.bytes' name: $binaryPathArgument"
        continue
    }
    $assemblyKey = $binaryName -replace '(?i)\.mv\.bytes$', ''
    Add-AssemblyPathMapping $mvBytesByName $assemblyKey ([string]$binaryPathArgument) "MV binary inputs"
}
for ($mvIndex = 0; $mvIndex -lt $mvJsonCount; $mvIndex++) {
    $path = Resolve-File $mvJsonArguments[$mvIndex] "MV JSON[$mvIndex]"
    if ($null -eq $path) { continue }
    $document = Read-JsonFile $path "MV JSON[$mvIndex]"
    if ($null -eq $document) { continue }
    $documentAssemblyName = Get-StringProperty $document "assemblyName"
    if ([string]::IsNullOrWhiteSpace($documentAssemblyName)) {
        Add-Error "MV assemblyName is missing: $path"
    } elseif (-not $seenMvAssemblies.Add($documentAssemblyName)) {
        Add-Error "MV JSON inputs contain duplicate assembly '$documentAssemblyName'."
    }
    if ($null -eq $mv) { $mv = $document; $mvPath = $path }
    $mvDocuments.Add($document)
    $mvPaths += $path
    if ((Get-IntProperty $document "schemaVersion") -ne 1) { Add-Error "MV schemaVersion must be 1: $path" }
    if ((Get-StringProperty $document "format") -ne "hybridclr.dhe-lite.mv.json") { Add-Error "MV format is invalid: $path" }
    $methods = @($document.methods)
    $summaryMethodCount = Get-IntProperty $document.summary "methodCount"
    if ($null -eq $summaryMethodCount -or $summaryMethodCount -ne $methods.Count) { Add-Error "MV summary.methodCount does not match methods[]: $path" }
    $changedMethods = @($methods | Where-Object { [string]$_.kind -eq "changed" })
    $localChangedCount = $changedMethods.Count
    $mvChangedCount += $localChangedCount
    $summaryChangedCount = Get-IntProperty $document.summary "changedMethodCount"
    if ($null -eq $summaryChangedCount -or $summaryChangedCount -ne $localChangedCount) { Add-Error "MV summary.changedMethodCount does not match changed methods: $path" }
    $compatibilityStatus = Get-StringProperty $document.compatibility "status"
    $compatibilityMode = Get-StringProperty $document.compatibility "mode"
    if ($compatibilityStatus -eq "compatible" -and $compatibilityMode -ne "method-body-only") { Add-Error "A compatible MV must use method-body-only mode: $path" }
    if ($RequireCompleteCoverage -and $compatibilityStatus -ne "compatible") { Add-Error "Complete coverage requires a compatible MV: $path" }
    $baselineHash = Get-StringProperty $document.baseline "sha256"
    $currentHash = Get-StringProperty $document.current "sha256"
    if ($baselineHash -notmatch '^[0-9a-fA-F]{64}$') { Add-Error "MV baseline.sha256 is invalid: $path" }
    if ($currentHash -notmatch '^[0-9a-fA-F]{64}$') { Add-Error "MV current.sha256 is invalid: $path" }
    $assemblyKey = [string]$documentAssemblyName
    $baselineArg = if ($baselineAssemblyByName.ContainsKey($assemblyKey)) {
        $baselineAssemblyByName[$assemblyKey]
    } else {
        if ($baselineAssemblyCount -gt 0) {
            Add-Error "No baseline assembly input matches MV assembly '$assemblyKey'."
        }
        $null
    }
    $currentArg = if ($currentAssemblyByName.ContainsKey($assemblyKey)) {
        $currentAssemblyByName[$assemblyKey]
    } else {
        if ($currentAssemblyCount -gt 0) {
            Add-Error "No current assembly input matches MV assembly '$assemblyKey'."
        }
        $null
    }
    $baselineAssemblyPaths.Add((Validate-AssemblyHash $baselineArg $baselineHash "Baseline assembly[$mvIndex]"))
    $currentAssemblyPaths.Add((Validate-AssemblyHash $currentArg $currentHash "Current assembly[$mvIndex]"))
    $tokenSet = @{}
    foreach ($method in $changedMethods) {
        $token = Get-IntProperty $method "currentToken"
        if ($null -eq $token -or $token -le 0) { Add-Error "Changed MV method has an invalid currentToken: $($method.id)"; continue }
        if ($tokenSet.ContainsKey($token)) { Add-Error "MV contains duplicate changed currentToken: $token ($path)" }
        $tokenSet[$token] = $true
        $mvChangedTokens.Add([uint32]$token)
    }
    $binaryArg = if ($mvBytesByName.ContainsKey($assemblyKey)) {
        $mvBytesByName[$assemblyKey]
    } else {
        if ($mvBytesCount -gt 0) {
            Add-Error "No MV binary input matches MV assembly '$assemblyKey'."
        }
        $null
    }
    if (-not [string]::IsNullOrWhiteSpace($binaryArg)) {
        $binaryPath = Resolve-File $binaryArg "MV binary[$mvIndex]"
        if ($null -ne $binaryPath) {
            try {
                $bytes = [IO.File]::ReadAllBytes($binaryPath)
                if ($bytes.Length -lt 88) { throw "payload is shorter than the fixed header." }
                if ([Text.Encoding]::ASCII.GetString($bytes, 0, 8) -ne "DHEMVLT1") { throw "invalid magic." }
                $schema = Get-U32 $bytes 8; $assemblyNameSize = Get-U32 $bytes 12; $methodCount = Get-U32 $bytes 16; $flags = Get-U32 $bytes 20
                if ($schema -ne 1) { Add-Error "MV binary schema must be 1: $binaryPath" }
                if ($flags -band 1 -eq 0) { Add-Error "MV binary is not strict-compatible: $binaryPath" }
                if (($flags -band 0xFFFFFFFE) -ne 0) { Add-Error "MV binary contains unknown flags: $binaryPath" }
                $headerEnd = 24 + 64 + [int]$assemblyNameSize
                if ($assemblyNameSize -eq 0 -or $headerEnd + 4 * [int]$methodCount -ne $bytes.Length) { Add-Error "MV binary length is invalid: $binaryPath" }
                else {
                    $binaryAssemblyName = [Text.Encoding]::UTF8.GetString($bytes, 88, [int]$assemblyNameSize)
                    if (-not $mvBinaryByAssembly.ContainsKey($binaryAssemblyName)) {
                        $mvBinaryByAssembly[$binaryAssemblyName] = $binaryPath
                    } else {
                        Add-Error "MV binaries contain duplicate assembly names: $binaryAssemblyName"
                    }
                    if ((Get-Sha256Hex ($bytes[24..55])) -ne $baselineHash.ToLowerInvariant()) { Add-Error "MV binary baseline hash mismatch: $binaryPath" }
                    if ((Get-Sha256Hex ($bytes[56..87])) -ne $currentHash.ToLowerInvariant()) { Add-Error "MV binary current hash mismatch: $binaryPath" }
                    if ($binaryAssemblyName -ne (Get-StringProperty $document "assemblyName")) { Add-Error "MV binary assembly name mismatch: $binaryPath" }
                    if ($methodCount -ne $localChangedCount) { Add-Error "MV binary method count mismatch: $binaryPath" }
                    $actualTokens = if ([int]$methodCount -eq 0) {
                        @()
                    } else {
                        @((0..([int]$methodCount - 1)) | ForEach-Object { [uint32](Get-U32 $bytes ($headerEnd + 4 * $_)) } | Sort-Object)
                    }
                    $expectedTokens = @($tokenSet.Keys | ForEach-Object { [uint32]$_ } | Sort-Object)
                    if (($actualTokens -join ',') -ne ($expectedTokens -join ',')) { Add-Error "MV binary changed token set mismatch: $binaryPath" }
                }
            } catch { Add-Error "MV binary is invalid: $($_.Exception.Message)" }
        }
    }
}
$baselineAssemblyPath = if ($baselineAssemblyPaths.Count -gt 0) { $baselineAssemblyPaths[0] } else { $null }
$currentAssemblyPath = if ($currentAssemblyPaths.Count -gt 0) { $currentAssemblyPaths[0] } else { $null }
$binaryPath = if ($mvBytesCount -gt 0 -and -not [string]::IsNullOrWhiteSpace($mvBytesArguments[0])) { [IO.Path]::GetFullPath($mvBytesArguments[0]) } else { $null }

$runtimePlanPath = $null
if (-not [string]::IsNullOrWhiteSpace($RuntimePlan)) {
    $runtimePlanPath = Resolve-File $RuntimePlan "runtime plan"
    $runtimePlanDocument = if ($null -ne $runtimePlanPath) { Read-JsonFile $runtimePlanPath "runtime plan" } else { $null }
    if ($null -ne $runtimePlanDocument) {
        if ((Get-IntProperty $runtimePlanDocument "schemaVersion") -ne 1 -or
            (Get-StringProperty $runtimePlanDocument "format") -ne "hybridclr.dhe-runtime-plan.json") {
            Add-Error "Runtime plan schema or format is invalid: $runtimePlanPath"
        }
        $planDirectory = [IO.Path]::GetDirectoryName($runtimePlanPath)
        $planAssemblies = @($runtimePlanDocument.assemblies)
        if ($RequireCompleteCoverage -and $planAssemblies.Count -ne $mvJsonCount) {
            Add-Error "Complete coverage requires one runtime-plan assembly per MV JSON ($mvJsonCount expected, got $($planAssemblies.Count))."
        }
        $seenPlanAssemblies = @{}
        foreach ($assemblyPlan in $planAssemblies) {
            $assemblyName = Get-StringProperty $assemblyPlan "assemblyName"
            if ([string]::IsNullOrWhiteSpace($assemblyName) -or $seenPlanAssemblies.ContainsKey($assemblyName)) {
                Add-Error "Runtime plan contains a missing or duplicate assembly name: $runtimePlanPath"
                continue
            }
            $seenPlanAssemblies[$assemblyName] = $true
            foreach ($field in @("current", "baseline", "mv", "snapshot")) {
                $relative = Get-StringProperty $assemblyPlan $field
                if ([string]::IsNullOrWhiteSpace($relative) -or [IO.Path]::IsPathRooted($relative) -or
                    $relative -match '(^|[\\/])\.\.([\\/]|$)') {
                    Add-Error "Runtime plan has an unsafe $field path for '$assemblyName'."
                    continue
                }
                $payloadPath = [IO.Path]::GetFullPath((Join-Path $planDirectory ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
                $planPrefix = $planDirectory.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
                if (-not $payloadPath.StartsWith($planPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                    Add-Error "Runtime plan $field escapes its archive directory for '$assemblyName'."
                } elseif (-not [IO.File]::Exists($payloadPath)) {
                    Add-Error "Runtime plan payload is missing for '$assemblyName': $payloadPath"
                } elseif ($field -eq "mv" -and $mvBinaryByAssembly.ContainsKey($assemblyName)) {
                    $archiveMvHash = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
                    $inputMvHash = (Get-FileHash -LiteralPath $mvBinaryByAssembly[$assemblyName] -Algorithm SHA256).Hash.ToLowerInvariant()
                    if ($archiveMvHash -ne $inputMvHash) {
                        Add-Error "Runtime plan MV payload does not match the validated MV binary for '$assemblyName'."
                    }
                } elseif ($field -eq "snapshot") {
                    $snapshotHex = ([BitConverter]::ToString([IO.File]::ReadAllBytes($payloadPath)) -replace '-', '').ToLowerInvariant()
                    $expectedSnapshot = (Get-StringProperty $assemblyPlan "baselineSha256").ToLowerInvariant()
                    if ($snapshotHex -ne $expectedSnapshot) {
                        Add-Error "Runtime plan snapshot payload does not match baseline hash for '$assemblyName'."
                    }
                }
            }
            foreach ($field in @("baselineSha256", "currentSha256")) {
                $expected = Get-StringProperty $assemblyPlan $field
                if ($expected -notmatch '^[0-9a-fA-F]{64}$') {
                    Add-Error "Runtime plan $field is invalid for '$assemblyName'."
                    continue
                }
                $payloadField = if ($field -eq "baselineSha256") { "baseline" } else { "current" }
                $relative = Get-StringProperty $assemblyPlan $payloadField
                if (-not [string]::IsNullOrWhiteSpace($relative) -and -not [IO.Path]::IsPathRooted($relative)) {
                    $payloadPath = [IO.Path]::GetFullPath((Join-Path $planDirectory ($relative.Replace('/', [IO.Path]::DirectorySeparatorChar))))
                    if ([IO.File]::Exists($payloadPath)) {
                        $actual = (Get-FileHash -LiteralPath $payloadPath -Algorithm SHA256).Hash.ToLowerInvariant()
                        if ($actual -ne $expected.ToLowerInvariant()) {
                            Add-Error "Runtime plan $payloadField hash mismatch for '$assemblyName'."
                        }
                    }
                }
            }
        }
        $mvAssemblyNames = @($mvDocuments | ForEach-Object { Get-StringProperty $_ "assemblyName" } | Sort-Object -Unique)
        $planAssemblyNames = @($planAssemblies | ForEach-Object { Get-StringProperty $_ "assemblyName" } | Sort-Object -Unique)
        if (($mvAssemblyNames -join ',') -ne ($planAssemblyNames -join ',')) {
            Add-Error "Runtime plan assembly set does not match MV JSON inputs."
        }
    }
}

$nativePath = $null
$native = $null
if (-not [string]::IsNullOrWhiteSpace($NativeManifest)) {
    $nativePath = Resolve-File $NativeManifest "native manifest"
    $native = if ($null -ne $nativePath) { Read-JsonFile $nativePath "native manifest" } else { $null }
        if ($null -ne $native) {
            if ((Get-IntProperty $native "schemaVersion") -ne 1) { Add-Error "Native manifest schemaVersion must be 1." }
            if ((Get-IntProperty $native "resolverVersion") -ne 2 -or
                (Get-StringProperty $native "abiContract") -ne "il2cpp-generated-cpp-signature-v2") {
                Add-Error "Native manifest resolver/ABI contract is unsupported."
            }
            $nativeGeneratedCppRoot = Get-StringProperty $native "generatedCppRoot"
            if ([string]::IsNullOrWhiteSpace($nativeGeneratedCppRoot)) {
                Add-Error "Native manifest has no generatedCppRoot."
            } else {
                $nativeGeneratedCppRootPath = Resolve-ReferencePath $nativeGeneratedCppRoot ([IO.Path]::GetDirectoryName($nativePath))
                if (-not [IO.Directory]::Exists($nativeGeneratedCppRootPath)) {
                    Add-Error "Native manifest generatedCppRoot was not found: $nativeGeneratedCppRootPath"
                }
            }
            $nativeChanged = Get-IntProperty $native "changedMethodCount"
        $nativeSupported = Get-IntProperty $native "supportedChangedMethodCount"
        $nativeUnsupported = Get-IntProperty $native "unsupportedChangedMethodCount"
        $nativeEntryCount = Get-IntProperty $native "nativeEntryCount"
        $nativeMethods = @($native.methods)
        if ($nativeChanged -ne $mvChangedCount) { Add-Error "Native manifest changedMethodCount does not match MV JSON." }
        if ($null -eq $nativeSupported -or $null -eq $nativeUnsupported -or
            $nativeSupported -lt 0 -or $nativeUnsupported -lt 0 -or
            $nativeSupported + $nativeUnsupported -ne $nativeChanged) {
            Add-Error "Native manifest coverage counts are inconsistent."
        }
        if ($null -eq $nativeEntryCount -or $nativeEntryCount -lt 0 -or $nativeMethods.Count -ne $nativeEntryCount) {
            Add-Error "Native manifest methods[] does not match nativeEntryCount."
        }
        $nativeTokenValues = New-Object System.Collections.Generic.List[string]
        $validNativeTokenMethods = New-Object System.Collections.Generic.List[object]
            foreach ($nativeMethod in $nativeMethods) {
            $nativeToken = Get-IntProperty $nativeMethod "methodToken"
            if ($null -eq $nativeToken -or $nativeToken -le 0) {
                Add-Error "Native manifest method has an invalid methodToken."
                continue
            }
                $nativeAssembly = Get-StringProperty $nativeMethod "assemblyName"
                if ([string]::IsNullOrWhiteSpace($nativeAssembly)) {
                    Add-Error "Native manifest method has no assemblyName."
                    continue
                }
                $validNativeTokenMethods.Add($nativeMethod)
                $nativeSourceFile = Get-StringProperty $nativeMethod "sourceFile"
                if ([string]::IsNullOrWhiteSpace($nativeSourceFile)) {
                    Add-Error "Native manifest method has no sourceFile: $nativeAssembly/$nativeToken"
                } elseif (-not [string]::IsNullOrWhiteSpace($nativeGeneratedCppRoot)) {
                    $resolvedNativeSource = Resolve-ReferencePath $nativeSourceFile ([IO.Path]::GetDirectoryName($nativePath))
                    $nativeRootPrefix = $nativeGeneratedCppRootPath.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
                    if (-not $resolvedNativeSource.StartsWith($nativeRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
                        Add-Error "Native manifest sourceFile escapes generatedCppRoot: $resolvedNativeSource"
                    } elseif (-not [IO.File]::Exists($resolvedNativeSource)) {
                        Add-Error "Native manifest sourceFile was not found: $resolvedNativeSource"
                    }
                }
                $nativeTokenValues.Add($nativeAssembly + "/" + ([uint32]$nativeToken).ToString())
        }
        $duplicateNativeFunctions = @($nativeMethods | Group-Object { Get-StringProperty $_ "functionName" } | Where-Object Count -gt 1)
        if ($duplicateNativeFunctions.Count -gt 0) {
            Add-Error "Native manifest contains duplicate generated function entries."
        }
        $nativeTokenGroups = @($validNativeTokenMethods | Group-Object {
            (Get-StringProperty $_ "assemblyName") + "/" + ([uint32](Get-IntProperty $_ "methodToken")).ToString()
        })
        foreach ($nativeTokenGroup in $nativeTokenGroups) {
            $managedIds = @($nativeTokenGroup.Group | ForEach-Object { Get-StringProperty $_ "managedId" } | Sort-Object -Unique)
            if ($managedIds.Count -ne 1 -or [string]::IsNullOrWhiteSpace([string]$managedIds[0])) {
                Add-Error "Native entries for $($nativeTokenGroup.Name) do not share one managedId."
            }
        }
        $nativeTokens = @($nativeTokenValues | Sort-Object -Unique)
        if ($nativeTokens.Count -ne $nativeSupported) {
            Add-Error "Native manifest unique supported token count does not match supportedChangedMethodCount."
        }
        $expectedNativeTokenSet = @{}
        foreach ($document in $mvDocuments) {
            $assemblyName = Get-StringProperty $document "assemblyName"
            foreach ($method in @($document.methods | Where-Object { [string]$_.kind -eq "changed" })) {
                $token = Get-IntProperty $method "currentToken"
                if ($null -eq $token -or $token -le 0) {
                    Add-Error "MV changed method has an invalid currentToken for '$assemblyName'."
                    continue
                }
                $expectedNativeTokenSet[$assemblyName + "/" + ([uint32]$token).ToString()] = $true
            }
        }
        foreach ($nativeToken in $nativeTokens) {
            if (-not $expectedNativeTokenSet.ContainsKey([string]$nativeToken)) {
                Add-Error "Native manifest contains methodToken $nativeToken not present in MV changed methods."
            }
        }
        $unsupportedMethods = @($native.unsupportedChangedMethods)
        if ($unsupportedMethods.Count -ne $nativeUnsupported) {
            Add-Error "Native manifest unsupportedChangedMethods does not match unsupported count."
        }
        $coverageTokenValues = New-Object System.Collections.Generic.List[string]
        foreach ($nativeToken in $nativeTokens) {
            $coverageTokenValues.Add([string]$nativeToken)
        }
        foreach ($unsupportedMethod in $unsupportedMethods) {
            $unsupportedAssembly = Get-StringProperty $unsupportedMethod "assemblyName"
            $unsupportedToken = Get-IntProperty $unsupportedMethod "methodToken"
            if ([string]::IsNullOrWhiteSpace($unsupportedAssembly) -or $null -eq $unsupportedToken -or $unsupportedToken -le 0) {
                Add-Error "Native manifest unsupported method is missing a valid assemblyName/methodToken."
                continue
            }
            $coverageTokenValues.Add($unsupportedAssembly + "/" + ([uint32]$unsupportedToken).ToString())
        }
        $coverageTokens = @($coverageTokenValues | Sort-Object)
        if (($coverageTokens | Select-Object -Unique).Count -ne $coverageTokens.Count) {
            Add-Error "Native manifest supported and unsupported methodToken entries contain duplicates."
        }
        if (($expectedNativeTokenSet.Keys | Sort-Object) -join ',' -ne ($coverageTokens -join ',')) {
            if ($RequireCompleteCoverage -and $nativeUnsupported -gt 0) {
                Add-CoverageError "Native manifest supported+unsupported methodToken set does not match MV changed methods."
            } else {
                Add-Error "Native manifest supported+unsupported methodToken set does not match MV changed methods."
            }
        }
        if ($RequireCompleteCoverage -and $nativeUnsupported -ne 0) {
            Add-CoverageError "Complete coverage was required but native manifest has $nativeUnsupported unsupported methods."
        }
        elseif ($nativeUnsupported -gt 0) {
            Add-Warning "Native manifest has $nativeUnsupported unsupported changed methods."
        }
    }
}

$identityPath = $null
$identity = $null
if (-not [string]::IsNullOrWhiteSpace($BuildIdentity)) {
    $identityPath = Resolve-File $BuildIdentity "build identity"
    $identity = if ($null -ne $identityPath) { Read-JsonFile $identityPath "build identity" } else { $null }
    if ($null -ne $identity) {
        if ((Get-IntProperty $identity "identityVersion") -ne 2) {
            Add-Error "Build identity identityVersion must be 2."
        }
        if ((Get-StringProperty $identity "aotSnapshotKind") -ne "managed-assembly-plus-generated-cpp-v1") {
            Add-Error "Build identity aotSnapshotKind is invalid."
        }
        $identityPathSemantics = Get-StringProperty $identity "pathSemantics"
        if ($identityPathSemantics -notin @("workspace-absolute-v1", "archive-relative-v1")) {
            Add-Error "Build identity pathSemantics is missing or invalid."
        }
        foreach ($field in @("baselineAssemblySha256", "aotSnapshotSha256", "nativeGuardSourceSha256", "nativeManifestSha256")) {
            $value = Get-StringProperty $identity $field
            if ($value -notmatch '^[0-9a-fA-F]{64}$') {
                Add-Error "Build identity $field is not a SHA-256 digest."
            }
        }
        $identityBaselineValue = Get-StringProperty $identity "baselineAssemblySha256"
        $identitySnapshotValue = Get-StringProperty $identity "aotSnapshotSha256"
        if ($mvDocuments.Count -gt 0) {
            $snapshotAssemblyName = Get-StringProperty $identity "aotSnapshotAssembly"
            $identityBaseline = if ($identityBaselineValue -match '^[0-9a-fA-F]{64}$') {
                $identityBaselineValue.ToLowerInvariant()
            } else { "" }
            $identityCandidates = @()
            if (-not [string]::IsNullOrWhiteSpace($snapshotAssemblyName)) {
                $identityCandidates = @($mvDocuments | Where-Object {
                    (Get-StringProperty $_ "assemblyName") -eq $snapshotAssemblyName
                })
            } elseif (-not [string]::IsNullOrWhiteSpace($identityBaseline)) {
                # Identity v2 predates the explicit assembly name. Preserve
                # compatibility for old archives by resolving it from the
                # baseline hash, but reject ambiguity instead of using array
                # order as an implicit binding.
                $identityCandidates = @($mvDocuments | Where-Object {
                    [void]($candidateBaseline = Get-StringProperty $_.baseline "sha256")
                    -not [string]::IsNullOrWhiteSpace($candidateBaseline) -and
                        $candidateBaseline.ToLowerInvariant() -eq $identityBaseline
                })
            }
            if ([string]::IsNullOrWhiteSpace($snapshotAssemblyName) -and
                [string]::IsNullOrWhiteSpace($identityBaseline)) {
                # The digest error above is the actionable failure; do not
                # emit a second binding error when the selector is absent.
            } elseif (@($identityCandidates).Count -ne 1) {
                $selector = if ([string]::IsNullOrWhiteSpace($snapshotAssemblyName)) {
                    "baseline hash $identityBaseline"
                } else {
                    "aotSnapshotAssembly '$snapshotAssemblyName'"
                }
                Add-Error "Build identity could not be bound to exactly one MV ($selector); found $(@($identityCandidates).Count)."
            } else {
                $identityMvBaselineValue = Get-StringProperty $identityCandidates[0].baseline "sha256"
                $identityMvBaseline = if ([string]::IsNullOrWhiteSpace($identityMvBaselineValue)) {
                    ""
                } else { $identityMvBaselineValue.ToLowerInvariant() }
                if ($identityBaseline -ne $identityMvBaseline) {
                    Add-Error "Build identity baselineAssemblySha256 does not match the selected MV baseline.sha256."
                }
                if ($identitySnapshotValue -notmatch '^[0-9a-fA-F]{64}$' -or
                    $identitySnapshotValue.ToLowerInvariant() -ne $identityMvBaseline) {
                    Add-Error "Build identity aotSnapshotSha256 does not match the selected MV baseline.sha256."
                }
            }
        }
        if ($null -ne $nativePath) {
            $identityNativeManifestValue = Get-StringProperty $identity "nativeManifestSha256"
            if ($identityNativeManifestValue -match '^[0-9a-fA-F]{64}$') {
                $identityNativeManifest = $identityNativeManifestValue.ToLowerInvariant()
                $actualNativeManifest = (Get-FileHash -LiteralPath $nativePath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($identityNativeManifest -ne $actualNativeManifest) {
                    Add-Error "Build identity nativeManifestSha256 does not match native manifest bytes."
                }
            }
        }
        $generatedCppPath = Get-StringProperty $identity "generatedCppPath"
        $identityNativeGuardValue = Get-StringProperty $identity "nativeGuardSourceSha256"
        $generatedCppPathsProperty = $identity.PSObject.Properties["generatedCppPaths"]
        $generatedCppPaths = if ($null -ne $generatedCppPathsProperty) {
            @($generatedCppPathsProperty.Value | ForEach-Object { [string]$_ })
        } elseif (-not [string]::IsNullOrWhiteSpace($generatedCppPath)) {
            @($generatedCppPath)
        } else { @() }
        $generatedCppPaths = @($generatedCppPaths)
        $nativeChangedCount = if ($null -ne $native) {
            Get-IntProperty $native "changedMethodCount"
        } else {
            $null
        }
        if ($generatedCppPaths.Count -eq 0) {
            if ($nativeChangedCount -ne 0) {
                Add-Error "Build identity has no generated C++ source paths for a non-empty native manifest."
            } elseif ($identityNativeGuardValue -notmatch '^[0-9a-fA-F]{64}$' -or
                $identityNativeGuardValue.ToLowerInvariant() -ne (Get-DheEmptyFileSetHash)) {
                Add-Error "Empty generated C++ source set must use the canonical empty-set hash."
            }
        } elseif ($identityNativeGuardValue -match '^[0-9a-fA-F]{64}$') {
            $identityDirectory = [IO.Path]::GetDirectoryName($identityPath)
            $generatedCppRoot = Get-StringProperty $identity "generatedCppRoot"
            if ([string]::IsNullOrWhiteSpace($generatedCppRoot)) {
                $generatedCppRoot = if ($null -ne $native) { Get-StringProperty $native "generatedCppRoot" } else { $null }
            }
            if ([string]::IsNullOrWhiteSpace($generatedCppRoot)) {
                Add-Error "Build identity has no generatedCppRoot for source-set hashing."
            } else {
                try {
                    $resolvedGeneratedCppRoot = Resolve-ReferencePath $generatedCppRoot $identityDirectory
                    $resolvedGeneratedCppPaths = @($generatedCppPaths | ForEach-Object {
                        Resolve-ReferencePath $_ $identityDirectory
                    })
                    $actualGeneratedCpp = Get-DheFileSetHashOrEmpty $resolvedGeneratedCppPaths $resolvedGeneratedCppRoot
                    if ($actualGeneratedCpp -ne $identityNativeGuardValue.ToLowerInvariant()) {
                        Add-Error "Build identity nativeGuardSourceSha256 does not match generated C++ source set."
                    }
                } catch {
                    Add-Error "Unable to hash generated C++ source set: $($_.Exception.Message)"
                }
            }
        }
    }
}

$workflowPath = $null
if (-not [string]::IsNullOrWhiteSpace($WorkflowReport)) {
    $workflowPath = Resolve-File $WorkflowReport "workflow report"
    $workflow = if ($null -ne $workflowPath) { Read-JsonFile $workflowPath "workflow report" } else { $null }
    if ($null -ne $workflow) {
        if ((Get-IntProperty $workflow "schemaVersion") -ne 1) { Add-Error "Workflow report schemaVersion must be 1." }
        $workflowFormat = Get-StringProperty $workflow "format"
        if ($workflowFormat -notmatch '^hybridclr\.dhe-[A-Za-z0-9._-]+-workflow\.json$') {
            Add-Error "Workflow report format is invalid."
        }
        $workflowMode = Get-StringProperty $workflow "mode"
        if ($workflowMode -notin @("Release", "Exploratory")) {
            Add-Error "Workflow report mode must be Release or Exploratory."
        }
        $sourcePreflightValue = Get-StringProperty $workflow "sourcePreflight"
        if ([string]::IsNullOrWhiteSpace($sourcePreflightValue)) {
            Add-Error "Workflow report is missing sourcePreflight."
        } else {
            $workflowDirectory = [IO.Path]::GetDirectoryName($workflowPath)
            $sourcePreflightPath = Resolve-File $sourcePreflightValue "source preflight report" $workflowDirectory
            $sourcePreflight = if ($null -ne $sourcePreflightPath) {
                Read-JsonFile $sourcePreflightPath "source preflight report"
            } else { $null }
            if ($null -ne $sourcePreflight) {
                if ((Get-IntProperty $sourcePreflight "schemaVersion") -ne 1 -or
                    (Get-StringProperty $sourcePreflight "format") -ne "hybridclr.dhe-source-preflight.json") {
                    Add-Error "Workflow source preflight report schema or format is invalid."
                }
                if (-not (Get-BoolProperty $sourcePreflight "passed") -or
                    -not (Get-BoolProperty $sourcePreflight "runtimeReady")) {
                    Add-Error "Workflow source preflight did not pass runtime verification."
                }
            }
        }
        $cleanCheckoutValue = Get-StringProperty $workflow "cleanCheckoutGate"
        if ([string]::IsNullOrWhiteSpace($cleanCheckoutValue)) {
            Add-Error "Workflow report is missing cleanCheckoutGate."
        } else {
            $workflowDirectory = [IO.Path]::GetDirectoryName($workflowPath)
            $cleanCheckoutPath = Resolve-File $cleanCheckoutValue "clean checkout gate report" $workflowDirectory
            $cleanCheckout = if ($null -ne $cleanCheckoutPath) {
                Read-JsonFile $cleanCheckoutPath "clean checkout gate report"
            } else { $null }
            if ($null -ne $cleanCheckout) {
                if ((Get-IntProperty $cleanCheckout "schemaVersion") -ne 1 -or
                    (Get-StringProperty $cleanCheckout "format") -ne "hybridclr.dhe-clean-checkout-gate.json") {
                    Add-Error "Workflow clean checkout gate report schema or format is invalid."
                }
                $cleanStaleManifestTested = Get-BoolProperty $cleanCheckout "staleManifestTested"
                $cleanStaleManifestRejected = Get-NullableBoolProperty $cleanCheckout "staleManifestRejected"
                $cleanGitTested = Get-BoolProperty $cleanCheckout "gitTested"
                $cleanRequiresGit = Get-BoolProperty $cleanCheckout "gitCleanRequired"
                $cleanGitClean = Get-NullableBoolProperty $cleanCheckout "gitClean"
                $cleanTrackedSourcesTested = Get-BoolProperty $cleanCheckout "trackedSourcesTested"
                $cleanRequiresTrackedSources = Get-BoolProperty $cleanCheckout "trackedSourcesRequired"
                $cleanTrackedSourcesComplete = Get-NullableBoolProperty $cleanCheckout "trackedSourcesComplete"
                if ($cleanGitTested -and $null -eq $cleanGitClean) {
                    Add-Error "Clean checkout gitTested=true requires a boolean gitClean."
                }
                if (-not $cleanGitTested -and $null -ne $cleanGitClean) {
                    Add-Error "Clean checkout gitTested=false requires gitClean to be null."
                }
                if ($cleanTrackedSourcesTested -and $null -eq $cleanTrackedSourcesComplete) {
                    Add-Error "Clean checkout trackedSourcesTested=true requires a boolean trackedSourcesComplete."
                }
                if (-not $cleanTrackedSourcesTested -and $null -ne $cleanTrackedSourcesComplete) {
                    Add-Error "Clean checkout trackedSourcesTested=false requires trackedSourcesComplete to be null."
                }
                    $cleanGateInvalid = -not (Get-BoolProperty $cleanCheckout "passed") -or
                    -not (Get-BoolProperty $cleanCheckout "cleanSourcePreflightPassed") -or
                    -not (Get-BoolProperty $cleanCheckout "staleOutputRejected") -or
                    -not (Get-BoolProperty $cleanCheckout "missingRuntimeRejected") -or
                    ($cleanStaleManifestTested -and -not $cleanStaleManifestRejected) -or
                    (-not $cleanStaleManifestTested -and $null -ne $cleanStaleManifestRejected) -or
                    ($cleanRequiresGit -and -not $cleanGitClean)
                if ($cleanRequiresTrackedSources -and -not $cleanTrackedSourcesComplete) {
                    $cleanGateInvalid = $true
                }
                if ($workflowMode -eq "Release" -and -not $cleanStaleManifestTested) {
                    $cleanGateInvalid = $true
                }
                if ($workflowMode -eq "Release" -and
                    (-not $cleanRequiresTrackedSources -or -not $cleanTrackedSourcesComplete)) {
                    $cleanGateInvalid = $true
                }
                if ($workflowMode -eq "Release") {
                    $formalGitIdentities = @(
                        @("project", (Get-ObjectProperty $cleanCheckout "projectGit")),
                        @("tool", (Get-ObjectProperty $cleanCheckout "toolGit"))
                    )
                    foreach ($formalGitIdentity in $formalGitIdentities) {
                        $identityName = [string]$formalGitIdentity[0]
                        $gitIdentity = $formalGitIdentity[1]
                        $identityVcs = Get-StringProperty $gitIdentity "vcs"
                        if ([string]::IsNullOrWhiteSpace($identityVcs)) { $identityVcs = "git" }
                        $identityVcsValid = if ($identityVcs -eq "svn") {
                            (Get-StringProperty $gitIdentity "revision") -match '^[0-9]+$' -and
                                -not [string]::IsNullOrWhiteSpace((Get-StringProperty $gitIdentity "repository"))
                        } elseif ($identityVcs -eq "git") {
                            (Get-StringProperty $gitIdentity "head") -match '^[0-9a-fA-F]{40,64}$' -and
                                (Get-StringProperty $gitIdentity "tree") -match '^[0-9a-fA-F]{40,64}$'
                        } else { $false }
                        if ($null -eq $gitIdentity -or
                            (Get-StringProperty $gitIdentity "name") -ne $identityName -or
                            -not (Get-BoolProperty $gitIdentity "tested") -or
                            -not (Get-BoolProperty $gitIdentity "passed") -or
                            -not (Get-BoolProperty $gitIdentity "cleanRequired") -or
                            -not (Get-BoolProperty $gitIdentity "clean") -or
                            -not (Get-BoolProperty $gitIdentity "trackedSourcesTested") -or
                            -not (Get-BoolProperty $gitIdentity "trackedSourcesRequired") -or
                            -not (Get-BoolProperty $gitIdentity "trackedSourcesComplete") -or
                            -not $identityVcsValid -or
                            (Get-StringProperty $gitIdentity "sourceBoundarySha256") -notmatch '^[0-9a-fA-F]{64}$') {
                            Add-Error "Release workflow $identityName VCS identity is incomplete or not clean/tracked."
                            $cleanGateInvalid = $true
                        }
                    }
                    $projectGitIdentity = Get-ObjectProperty $cleanCheckout "projectGit"
                    $projectVcs = Get-StringProperty $projectGitIdentity "vcs"
                    if ([string]::IsNullOrWhiteSpace($projectVcs)) { $projectVcs = "git" }
                    $flatProjectIdentityMatches = if ($projectVcs -eq "svn") {
                        (Get-StringProperty $cleanCheckout "vcs") -eq "svn" -and
                            (Get-StringProperty $cleanCheckout "vcsRevision") -eq (Get-StringProperty $projectGitIdentity "revision") -and
                            (Get-StringProperty $cleanCheckout "vcsRepository") -eq (Get-StringProperty $projectGitIdentity "repository") -and
                            (Get-StringProperty $cleanCheckout "sourceBoundarySha256") -eq (Get-StringProperty $projectGitIdentity "sourceBoundarySha256")
                    } else {
                        (Get-StringProperty $cleanCheckout "gitHead") -eq (Get-StringProperty $projectGitIdentity "head") -and
                            (Get-StringProperty $cleanCheckout "gitTree") -eq (Get-StringProperty $projectGitIdentity "tree") -and
                            (Get-StringProperty $cleanCheckout "sourceBoundarySha256") -eq (Get-StringProperty $projectGitIdentity "sourceBoundarySha256")
                    }
                    if ($null -eq $projectGitIdentity -or -not $flatProjectIdentityMatches) {
                        Add-Error "Release workflow flat VCS identity does not match project identity."
                        $cleanGateInvalid = $true
                    }
                }
                if ($cleanGateInvalid) {
                    Add-Error "Workflow clean checkout gate did not pass all required checks."
                }
            }
        }
        $validationPassed = Get-BoolProperty $workflow "validationPassed"
        $coverageRequired = Get-BoolProperty $workflow "coverageRequired"
        $coverageGatePassed = Get-BoolProperty $workflow "coverageGatePassed"
        $releaseReady = Get-BoolProperty $workflow "releaseReady"
        $artifactValidationProperty = $workflow.PSObject.Properties["artifactValidationPassed"]
        if ($null -eq $artifactValidationProperty) {
            Add-Error "Workflow report is missing artifactValidationPassed."
            $artifactValidationPassed = $false
        } else {
            $artifactValidationPassed = Get-BoolProperty $workflow "artifactValidationPassed"
        }
        $passed = Get-BoolProperty $workflow "passed"
        $buildIdentityReady = Get-BoolProperty $workflow "buildIdentityReady"
        $workflowIdentityVersion = Get-IntProperty $workflow "identityVersion"
        $workflowSnapshotKind = Get-StringProperty $workflow "aotSnapshotKind"
        $workflowPathSemantics = Get-StringProperty $workflow "pathSemantics"
        $workflowNativeGuardHash = Get-StringProperty $workflow "nativeGuardSourceSha256"
        $workflowNativeManifestHash = Get-StringProperty $workflow "nativeManifestSha256"
        $transactionProperty = $workflow.PSObject.Properties["transaction"]
        if ($null -eq $transactionProperty -or $null -eq $workflow.transaction) {
            Add-Error "Workflow report is missing transaction evidence."
        } else {
            if (-not (Get-BoolProperty $workflow.transaction "retryValidated")) {
                Add-Error "Workflow transaction retry probe did not pass."
            }
            if ([string]::IsNullOrWhiteSpace((Get-StringProperty $workflow.transaction "retryAssemblyName"))) {
                Add-Error "Workflow transaction evidence has no retry assembly name."
            }
            if ((Get-StringProperty $workflow.transaction "retryFailure") -ne "DHE_MV_REGISTRATION_FAILED") {
                Add-Error "Workflow transaction evidence has an unexpected retry failure code."
            }
        }
        $complete = Get-BoolProperty $workflow.nativeGuardCoverage "complete"
        $unsupported = Get-IntProperty $workflow.nativeGuardCoverage "unsupportedChangedMethodCount"
        if ($null -ne $native) {
            $nativeSupportedForWorkflow = Get-IntProperty $native "supportedChangedMethodCount"
            $nativeUnsupportedForWorkflow = Get-IntProperty $native "unsupportedChangedMethodCount"
            $nativeChangedForWorkflow = Get-IntProperty $native "changedMethodCount"
            $nativeEntryCountForWorkflow = Get-IntProperty $native "nativeEntryCount"
            if ((Get-IntProperty $workflow.nativeGuardCoverage "changedMethodCount") -ne $nativeChangedForWorkflow -or
                (Get-IntProperty $workflow.nativeGuardCoverage "supportedChangedMethodCount") -ne $nativeSupportedForWorkflow -or
                $unsupported -ne $nativeUnsupportedForWorkflow -or
                (Get-IntProperty $workflow.nativeGuardCoverage "nativeEntryCount") -ne $nativeEntryCountForWorkflow -or
                (Get-IntProperty $workflow.nativeGuardCoverage "guardedMethodCount") -ne $nativeSupportedForWorkflow -or
                $complete -ne ($nativeUnsupportedForWorkflow -eq 0)) {
                Add-Error "Workflow native guard coverage does not match the native manifest."
            }
        }
        $expectedCoverageGate = -not $coverageRequired -or $complete
        $expectedReleaseReady = $workflowMode -eq "Release" -and $validationPassed -and $complete -and
            (Get-StringProperty $workflow.capability "compatibility") -eq "compatible" -and
            $artifactValidationPassed -and $buildIdentityReady -and
            $workflowIdentityVersion -eq 2 -and
            $workflowSnapshotKind -eq "managed-assembly-plus-generated-cpp-v1"
        if ($workflowPathSemantics -notin @("workspace-absolute-v1", "archive-relative-v1")) {
            Add-Error "Workflow pathSemantics is missing or invalid."
        }
        # `artifactValidationPassed` records the workflow's original artifact
        # pass. A caller may rerun this validator with the stricter complete
        # coverage switch; that adds coverage-only errors but must not rewrite
        # the meaning of the recorded artifact result.
        $artifactErrors = @($errors | Where-Object { -not $coverageErrors.Contains($_) })
        $expectedArtifactValidationPassed = $artifactErrors.Count -eq 0
        if ($coverageGatePassed -ne $expectedCoverageGate) { Add-Error "Workflow coverageGatePassed is inconsistent." }
        if ($releaseReady -ne $expectedReleaseReady) { Add-Error "Workflow releaseReady is inconsistent." }
        if ($artifactValidationPassed -ne $expectedArtifactValidationPassed) {
            Add-Error "Workflow artifactValidationPassed is inconsistent with the validated artifacts."
        }
        if ($passed -ne ($validationPassed -and $coverageGatePassed -and $artifactValidationPassed)) {
            Add-Error "Workflow passed is inconsistent."
        }
        if ($buildIdentityReady -ne ($null -ne $identity)) {
            Add-Error "Workflow buildIdentityReady is inconsistent with the supplied build identity."
        }
        if ($null -ne $identity) {
            if ($workflowIdentityVersion -ne (Get-IntProperty $identity "identityVersion") -or
                $workflowSnapshotKind -ne (Get-StringProperty $identity "aotSnapshotKind") -or
                $workflowNativeGuardHash -ne (Get-StringProperty $identity "nativeGuardSourceSha256") -or
                $workflowNativeManifestHash -ne (Get-StringProperty $identity "nativeManifestSha256")) {
                Add-Error "Workflow build identity fields do not match build-identity.json."
            }
        }
        if ($unsupported -lt 0) { Add-Error "Workflow unsupported coverage count is invalid." }
        if ($RequireCompleteCoverage -and -not $passed) { Add-CoverageError "Complete coverage was required but workflow report did not pass." }
        if ($null -ne $mv -and (Get-IntProperty $workflow.capability "changedMethodCount") -ne $mvChangedCount) {
            Add-Error "Workflow capability changedMethodCount does not match MV JSON."
        }
    }
}

$batchPath = $null
if (-not [string]::IsNullOrWhiteSpace($BatchReport)) {
    $batchPath = Resolve-File $BatchReport "batch report"
    $batch = if ($null -ne $batchPath) { Read-JsonFile $batchPath "batch report" } else { $null }
    if ($null -ne $batch) {
        if ((Get-IntProperty $batch "schemaVersion") -ne 1) { Add-Error "Batch report schemaVersion must be 1." }
        if ((Get-StringProperty $batch "format") -ne "hybridclr.dhe-lite.batch-report.json") {
            Add-Error "Batch report format is invalid."
        }
        $configurationPassedProperty = $batch.PSObject.Properties["configurationPassed"]
        $configurationErrorsProperty = $batch.PSObject.Properties["configurationErrors"]
        if ($null -eq $configurationPassedProperty -or $null -eq $configurationErrorsProperty) {
            Add-Error "Batch report is missing configurationPassed/configurationErrors."
        } else {
            $configurationPassed = Get-BoolProperty $batch "configurationPassed"
            $configurationErrors = @($batch.configurationErrors | ForEach-Object { [string]$_ } |
                Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
            if ($configurationPassed -ne ($configurationErrors.Count -eq 0)) {
                Add-Error "Batch configurationPassed is inconsistent with configurationErrors[]."
            }
            $batchRequiresDheCoverage = Get-BoolProperty $batch "requireDheEqualsHotUpdate"
            if ($batchRequiresDheCoverage) {
                $batchHotUpdateNames = @($batch.hotUpdateAssemblies | ForEach-Object { ([string]$_).Trim() } |
                    Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
                $batchDheNames = @($batch.dheAotAssemblies | ForEach-Object { ([string]$_).Trim() } |
                    Where-Object { $_.Length -gt 0 } | Sort-Object -Unique)
                if (($batchHotUpdateNames -join ",") -ne ($batchDheNames -join ",")) {
                    Add-Error "Batch DHE AOT assembly set does not exactly match hot-update assembly set."
                }
            }
        }
        $assemblies = @($batch.assemblies)
        $counts = $batch.counts
        $total = Get-IntProperty $counts "total"
        $compatible = Get-IntProperty $counts "compatible"
        $incompatible = Get-IntProperty $counts "incompatible"
        $missing = Get-IntProperty $counts "missing"
        $batchErrors = Get-IntProperty $counts "error"
        if ($null -eq $total -or $total -ne $assemblies.Count) { Add-Error "Batch counts.total does not match assemblies[]." }
        if ($null -eq $compatible -or $null -eq $incompatible -or $null -eq $missing -or $null -eq $batchErrors -or
            $compatible + $incompatible + $missing + $batchErrors -ne $assemblies.Count) {
            Add-Error "Batch status counts are inconsistent."
        }
        if ($RequireCompleteCoverage -and ($incompatible -ne 0 -or $missing -ne 0 -or $batchErrors -ne 0)) {
            Add-CoverageError "Complete batch compatibility was required but the batch contains rejected or missing assemblies."
        }
        elseif ($incompatible -gt 0 -or $missing -gt 0 -or $batchErrors -gt 0) {
            Add-Warning "Batch contains incompatible, missing, or error assemblies."
        }
    }
}

$outputPath = if ([string]::IsNullOrWhiteSpace($Output)) { $null } else { [IO.Path]::GetFullPath($Output) }
$result = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-artifact-validation.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    passed = $errors.Count -eq 0
    errors = $errors.ToArray()
    warnings = $warnings.ToArray()
    mvJson = $mvPath
    mvBytes = $binaryPath
    baselineAssembly = $baselineAssemblyPath
    currentAssembly = $currentAssemblyPath
    mvJsons = $mvPaths
    mvBytesList = @($mvBytesArguments | ForEach-Object { if ([string]::IsNullOrWhiteSpace($_)) { $null } else { [IO.Path]::GetFullPath($_) } })
    baselineAssemblies = $baselineAssemblyPaths.ToArray()
    currentAssemblies = $currentAssemblyPaths.ToArray()
    nativeManifest = $nativePath
    buildIdentity = $identityPath
    workflowReport = $workflowPath
    runtimePlan = $runtimePlanPath
    batchReport = $batchPath
}
if ($null -ne $outputPath) {
    # Validation reports must never replace one of the artifacts they are
    # validating. This is an easy CLI mistake to make when reusing a path from
    # a plan, and would otherwise destroy the only copy of the MV/DLL evidence
    # after all checks had already run.
    $validatorInputs = @(
        $mvPaths,
        $mvBytesArguments,
        $baselineAssemblyArguments,
        $currentAssemblyArguments,
        $NativeManifest,
        $BuildIdentity,
        $WorkflowReport,
        $RuntimePlan,
        $BatchReport
    ) | ForEach-Object {
        if (-not [string]::IsNullOrWhiteSpace([string]$_)) {
            try { [IO.Path]::GetFullPath([string]$_) } catch { }
        }
    }
    foreach ($validatorInput in @($validatorInputs)) {
        if ([IO.Path]::GetFullPath([string]$validatorInput).Equals($outputPath, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Artifact validation output must not overwrite an input artifact: $outputPath"
        }
    }
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
    [IO.File]::WriteAllText($outputPath, ($result | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
}

if ($result.passed) {
    Write-Host "DHE artifact validation passed."
    if ($warnings.Count -gt 0) { Write-Host ("Warnings: " + ($warnings -join "; ")) }
} else {
    Write-Error ("DHE artifact validation failed:`n - " + ($errors -join "`n - "))
    exit 1
}
