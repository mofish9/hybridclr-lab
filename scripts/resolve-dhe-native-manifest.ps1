param(
    [Parameter(Mandatory = $true)]
    [string]$MvJson,
    [Parameter(Mandatory = $true)]
    [string]$GeneratedCppRoot,
    [Parameter(Mandatory = $true)]
    [string]$OutputManifest
)

$ErrorActionPreference = "Stop"

function Get-OptionalManifestProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function ConvertTo-ManifestStringList($Value) {
    $items = New-Object 'System.Collections.Generic.List[string]'
    foreach ($item in @($Value)) {
        if ($null -ne $item) {
            $items.Add([string]$item)
        }
    }
    return ,$items
}

function Split-CppParameters([string]$text) {
    $parts = New-Object System.Collections.Generic.List[string]
    $start = 0
    $angleDepth = 0
    $parenDepth = 0
    for ($i = 0; $i -lt $text.Length; ++$i) {
        switch ($text[$i]) {
            '<' { ++$angleDepth }
            '>' { if ($angleDepth -gt 0) { --$angleDepth } }
            '(' { ++$parenDepth }
            ')' { if ($parenDepth -gt 0) { --$parenDepth } }
            ',' {
                if ($angleDepth -eq 0 -and $parenDepth -eq 0) {
                    $parts.Add($text.Substring($start, $i - $start).Trim())
                    $start = $i + 1
                }
            }
        }
    }
    if ($start -lt $text.Length) {
        $parts.Add($text.Substring($start).Trim())
    }
    return @($parts | Where-Object { $_.Length -gt 0 })
}

function Convert-Parameter([string]$text, [string]$functionName) {
    $parameter = $text.Trim()
    if ($parameter -match '^(?<type>.+\S)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)$') {
        return [ordered]@{
            type = $Matches.type.Trim()
            name = $Matches.name
        }
    }
    throw "Could not parse parameter '$parameter' in '$functionName'."
}

function Get-NativeAbiUnsupportedReason([string]$returnType, [object[]]$parameters, [bool]$hasThis, [bool]$declaringTypeIsValueType) {
    $offset = if ($hasThis) { 1 } else { 0 }
    $managedParameterTypes = @($parameters | Select-Object -Skip $offset |
        ForEach-Object { [string]$_.type })
    $isVoid = $returnType -eq "void"
    $isInt32 = $returnType -eq "int32_t"
    $isInt64 = $returnType -eq "int64_t"
    $isBool = $returnType -eq "bool"
    $nativeShape = "$returnType($($managedParameterTypes -join ', '))"
    $valueTypeByValue = $managedParameterTypes.Count -eq 1 -and
        $managedParameterTypes[0] -match '_t[0-9A-Fa-f]+$' -and
        $managedParameterTypes[0] -notmatch '\*\s*$'
    $valueTypeRefOut = -not $hasThis -and $isVoid -and $managedParameterTypes.Count -eq 2 -and
        $managedParameterTypes[0] -match '^(?:const\s+)?(?:struct\s+)?[A-Za-z_][A-Za-z0-9_]*_t[0-9A-Fa-f]+\s*\*$' -and
        $managedParameterTypes[1] -match '^int32_t\s*\*$'
    $pointerAndInt32 = -not $hasThis -and $isInt32 -and $managedParameterTypes.Count -eq 2 -and
        $managedParameterTypes[0] -match '\*\s*$' -and $managedParameterTypes[1] -eq 'int32_t'
    $genericValueReturn = -not $hasThis -and $managedParameterTypes.Count -eq 1 -and
        -not $isVoid -and -not $isInt32 -and -not $isInt64 -and -not $isBool -and
        $returnType -notmatch '\*\s*$' -and $managedParameterTypes[0] -notmatch '\*\s*$'
    $valueTypeReceiver = $declaringTypeIsValueType -and $hasThis
    $supported = if ($valueTypeReceiver) {
        $managedParameterTypes.Count -eq 0 -and $isVoid
    } else {
        ($hasThis -and $managedParameterTypes.Count -eq 0 -and ($isVoid -or $isBool)) -or
            $valueTypeRefOut -or $pointerAndInt32 -or
            ($valueTypeByValue -and -not $isVoid) -or $genericValueReturn -or
            $nativeShape -in @(
                'int32_t(int32_t)',
                'int32_t(int32_t, int32_t)',
                'int64_t(int64_t)',
                'void(int32_t)'
            )
    }
    if ($supported) { return $null }
    if ($valueTypeReceiver) { return "value-type-receiver" }
    return "unsupported-native-shape:$nativeShape"
}

if (-not (Test-Path -LiteralPath $MvJson -PathType Leaf)) {
    throw "mv JSON was not found: $MvJson"
}
if (-not (Test-Path -LiteralPath $GeneratedCppRoot -PathType Container)) {
    throw "Generated C++ directory was not found: $GeneratedCppRoot"
}

$mv = Get-Content -Raw -LiteralPath $MvJson | ConvertFrom-Json
$allChangedMethods = @($mv.methods | Where-Object {
    $_.kind -eq "changed" -and $null -ne $_.currentToken
})
$changedMethods = @($allChangedMethods | Where-Object {
    -not [bool]$_.isAbstract -and -not [bool]$_.isPInvoke
})
$unsupportedMethods = New-Object System.Collections.Generic.List[object]
foreach ($method in @($allChangedMethods | Where-Object { -not ($changedMethods -contains $_) })) {
    $reasons = New-Object System.Collections.Generic.List[string]
    if ([bool]$method.isAbstract) { $reasons.Add("abstract") }
    if ([bool]$method.isPInvoke) { $reasons.Add("pinvoke") }
    $unsupportedMethods.Add([ordered]@{
        id = [string]$method.id
        name = [string]$method.name
        assemblyName = [string]$mv.assemblyName
        declaringType = [string]$method.declaringType
        methodToken = [uint32]$method.currentToken
        returnType = [string](Get-OptionalManifestProperty $method "returnType")
        parameterTypes = ConvertTo-ManifestStringList (Get-OptionalManifestProperty $method "parameterTypes")
        hasThis = [bool](Get-OptionalManifestProperty $method "hasThis")
        declaringTypeIsValueType = [bool](Get-OptionalManifestProperty $method "declaringTypeIsValueType")
        genericParameterCount = [uint32](Get-OptionalManifestProperty $method "genericParameterCount")
        declaringTypeGenericParameterCount = [uint32](Get-OptionalManifestProperty $method "declaringTypeGenericParameterCount")
        reasons = $reasons.ToArray()
    })
}
$ambiguousMethods = @($changedMethods |
    Group-Object { "$( [string]$_.declaringType )::$( [string]$_.name )" } |
    Where-Object Count -gt 1)
if ($ambiguousMethods.Count -gt 0) {
    throw "Changed overloads cannot be resolved from generated C++ comments: $(($ambiguousMethods | ForEach-Object Name) -join ', ')"
}

$cppFiles = @(Get-ChildItem -LiteralPath $GeneratedCppRoot -Recurse -File -Filter *.cpp)
$commentDefinitions = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
$allDefinitions = New-Object System.Collections.Generic.List[object]
$definitionPattern = "(?m)^// (?<comment>[^\r\n]*)\r?\n(?<signature>[^\r\n{};]*\b(?<function>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<parameters>[^\r\n{};]*)\)\s*)\{"
$fallbackDefinitionPattern = "(?m)^(?<signature>[^\r\n{};]*\b(?<function>[A-Za-z_][A-Za-z0-9_]*)\s*\((?<parameters>[^\r\n{};]*)\)\s*)\{"
foreach ($file in $cppFiles) {
    # Generated C++ can contain tens of thousands of definitions. Read each
    # file once and build an index so resolving N MV methods is independent of
    # N * file-count disk reads.
    $source = [IO.File]::ReadAllText($file.FullName)
    foreach ($match in [regex]::Matches($source, $definitionPattern)) {
        $comment = [string]$match.Groups["comment"].Value
        $separator = $comment.LastIndexOf("::")
        $openParen = $comment.IndexOf("(", $separator + 2)
        if ($separator -lt 0 -or $openParen -lt 0) {
            continue
        }
        $commentId = $comment.Substring(0, $openParen).Trim()
        $space = $commentId.LastIndexOf(" ")
        if ($space -ge 0) { $commentId = $commentId.Substring($space + 1) }
        $definition = [ordered]@{
            file = $file.FullName
            signature = $match.Groups["signature"].Value
            functionName = $match.Groups["function"].Value
            parameters = $match.Groups["parameters"].Value
        }
        if (-not $commentDefinitions.ContainsKey($commentId)) {
            $commentDefinitions[$commentId] = @($definition)
        } else {
            $commentDefinitions[$commentId] = @($commentDefinitions[$commentId]) + @($definition)
        }
    }
    foreach ($match in [regex]::Matches($source, $fallbackDefinitionPattern)) {
        $allDefinitions.Add([ordered]@{
            file = $file.FullName
            signature = $match.Groups["signature"].Value
            functionName = $match.Groups["function"].Value
            parameters = $match.Groups["parameters"].Value
        })
    }
}
$assemblyStem = [string]$mv.assemblyName
if ($assemblyStem.EndsWith('.dll', [StringComparison]::OrdinalIgnoreCase)) {
    $assemblyStem = $assemblyStem.Substring(0, $assemblyStem.Length - 4)
}
$assemblyFilePattern = '^' + [regex]::Escape($assemblyStem) + '(?:__\d+)?\.cpp$'
$assemblyDefinitions = @($allDefinitions | Where-Object {
    [regex]::IsMatch([IO.Path]::GetFileName([string]$_.file), $assemblyFilePattern, [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
})
# IL2CPP emits forward declarations and call sites into Assembly-CSharp.cpp,
# while the owning assembly's definitions normally live in a file named after
# that assembly. Restrict the name-only fallback to those owning files so two
# assemblies containing the same short type/method name cannot collide.
$fallbackDefinitions = New-Object System.Collections.Generic.List[object]
if ($assemblyDefinitions.Count -gt 0) {
    foreach ($definition in $assemblyDefinitions) { $fallbackDefinitions.Add($definition) }
} else {
    foreach ($definition in $allDefinitions) { $fallbackDefinitions.Add($definition) }
}
$guards = New-Object System.Collections.Generic.List[object]
$supportedMethodCount = 0
foreach ($method in $changedMethods) {
    $commentId = "$( [string]$method.declaringType )::$( [string]$method.name )"
    $genericParameterCount = [uint32](Get-OptionalManifestProperty $method "genericParameterCount")
    $declaringTypeGenericParameterCount = [uint32](Get-OptionalManifestProperty $method "declaringTypeGenericParameterCount")
    $isGenericDefinition = $genericParameterCount -gt 0 -or $declaringTypeGenericParameterCount -gt 0
    $resolvedMatches = New-Object System.Collections.Generic.List[object]
    if ($commentDefinitions.ContainsKey($commentId)) {
        foreach ($definition in @($commentDefinitions[$commentId])) {
            $resolvedMatches.Add($definition)
        }
    } else {
        # Some Unity/IL2CPP versions omit the managed-name comment while
        # retaining the deterministic <Type>_<Method>_<hash> symbol.
        # Use the prefix only as a fallback and still require one unique
        # definition, so overloads remain an explicit unsupported case.
        $typeName = ([string]$method.declaringType -split '/')[-1]
        $typeName = ($typeName -split '\.')[-1] -replace '`([0-9]+)', '_$1'
        $typeName = $typeName.Replace('<', 'U3C').Replace('>', 'U3E')
        $functionPrefix = [regex]::Escape("${typeName}_$([string]$method.name)_")
        $definitionPool = if ($isGenericDefinition) { $allDefinitions } else { $fallbackDefinitions }
        foreach ($definition in @($definitionPool | Where-Object {
            [string]$_.functionName -match "^$functionPrefix[A-Za-z0-9_]+$" -and
            [string]$_.functionName -notmatch 'AdjustorThunk$'
        })) {
            $resolvedMatches.Add($definition)
        }
    }
    if ($resolvedMatches.Count -eq 0 -or (-not $isGenericDefinition -and $resolvedMatches.Count -ne 1)) {
        $expected = if ($isGenericDefinition) { "one or more" } else { "one" }
        throw "Expected $expected generated definition for '$commentId', found $($resolvedMatches.Count)."
    }

    $methodGuards = New-Object System.Collections.Generic.List[object]
    $methodUnsupportedReasons = New-Object System.Collections.Generic.List[string]
    foreach ($resolved in $resolvedMatches) {
        $signature = [string]$resolved.signature
        if ($signature -notmatch '\bconst\s+RuntimeMethod\s*\*\s*method\b') {
            throw "Generated definition '$($resolved.functionName)' has no RuntimeMethod parameter."
        }
        $returnType = $signature.Substring(0, $signature.IndexOf($resolved.functionName)).Trim()
        $returnType = $returnType -replace '^.*\bIL2CPP_METHOD_ATTR\s+', ''
        $returnType = $returnType -replace '^\s*inline\s+', ''
        if ([string]::IsNullOrWhiteSpace($returnType)) {
            throw "Could not resolve return type for '$($resolved.functionName)'."
        }

        $parameters = New-Object System.Collections.Generic.List[object]
        foreach ($parameterText in (Split-CppParameters ([string]$resolved.parameters))) {
            $parameter = Convert-Parameter $parameterText $resolved.functionName
            if ($parameter.name -eq "method") { continue }
            $parameters.Add($parameter)
        }

        $isStatic = [bool]$method.isStatic
        $hasThis = -not $isStatic
        if ($hasThis -and ($parameters.Count -eq 0 -or $parameters[0].name -ne "__this")) {
            throw "Generated definition '$($resolved.functionName)' has no leading __this parameter for an instance method."
        }
        $managedParameterOffset = if ($hasThis) { 1 } else { 0 }
        $generatedManagedParameters = @($parameters | Select-Object -Skip $managedParameterOffset)
        $usesHiddenReturnBuffer = $generatedManagedParameters.Count -gt 0 -and
            [string]$generatedManagedParameters[-1].name -eq "il2cppRetVal"
        if ($usesHiddenReturnBuffer) {
            $generatedManagedParameters = @($generatedManagedParameters | Select-Object -First ($generatedManagedParameters.Count - 1))
        }
        $managedParameterCount = $generatedManagedParameters.Count
        $declaringTypeIsValueType = [bool](Get-OptionalManifestProperty $method "declaringTypeIsValueType")
        $unsupportedAbiReason = $null
        $bridgeKind = "shape-helper-v1"
        $fullySharedParameterIndexes = New-Object System.Collections.Generic.List[int]
        if ($isGenericDefinition) {
            $bridgeKind = "invoke-args-v1"
            for ($parameterIndex = 0; $parameterIndex -lt $generatedManagedParameters.Count; ++$parameterIndex) {
                if ([string]$generatedManagedParameters[$parameterIndex].type -match '^Il2CppFullySharedGeneric(?:Any|Struct)$') {
                    $fullySharedParameterIndexes.Add($parameterIndex)
                }
            }
            $managedTypes = @($generatedManagedParameters | ForEach-Object { [string]$_.type })
            $hiddenReturnType = if ($usesHiddenReturnBuffer) { [string]$parameters[-1].type } else { "" }
            $managedParameterSignatures = @((Get-OptionalManifestProperty $method "parameterTypes") | ForEach-Object { [string]$_ })
            $managedReturnSignature = [string](Get-OptionalManifestProperty $method "returnType")
            $hasManagedByRef = @($managedParameterSignatures | Where-Object { $_ -match '[&*]\s*$' }).Count -gt 0
            $returnsManagedByRef = $managedReturnSignature -match '[&*]\s*$'
            # The invoke-args bridge uses the interpreter's own argument
            # descriptions, so concrete, shared-concrete, reference and FGS
            # by-value entries share one implementation. Managed ref/out and
            # byref returns need a distinct pointer contract and remain
            # fail-closed until that contract is implemented explicitly.
            $returnContractSupported = if ($usesHiddenReturnBuffer) {
                $returnType -eq 'void' -and $hiddenReturnType -match '\*\s*$'
            } else {
                $true
            }
            $genericShapeSupported = $managedParameterCount -eq $managedParameterSignatures.Count -and
                -not $hasManagedByRef -and -not $returnsManagedByRef -and $returnContractSupported
            if (-not $genericShapeSupported) {
                $unsupportedAbiReason = "unsupported-generic-native-shape:$returnType($($managedTypes -join ', '))"
            }
        } else {
            $unsupportedAbiReason = Get-NativeAbiUnsupportedReason $returnType $parameters.ToArray() $hasThis $declaringTypeIsValueType
        }
        if ($null -ne $unsupportedAbiReason) {
            $methodUnsupportedReasons.Add("$unsupportedAbiReason [$($resolved.functionName)]")
            continue
        }

        $methodGuards.Add([ordered]@{
            functionName = $resolved.functionName
            returnType = $returnType
            parameters = $parameters.ToArray()
            sourceFile = $resolved.file
            assemblyName = [string]$mv.assemblyName
            declaringType = [string]$method.declaringType
            methodName = [string]$method.name
            methodToken = [uint32]$method.currentToken
            managedId = [string]$method.id
            managedReturnType = [string](Get-OptionalManifestProperty $method "returnType")
            managedParameterTypes = ConvertTo-ManifestStringList (Get-OptionalManifestProperty $method "parameterTypes")
            managedHasThis = [bool](Get-OptionalManifestProperty $method "hasThis")
            declaringTypeIsValueType = $declaringTypeIsValueType
            genericParameterCount = $genericParameterCount
            declaringTypeGenericParameterCount = $declaringTypeGenericParameterCount
            bridgeKind = $bridgeKind
            usesHiddenReturnBuffer = $usesHiddenReturnBuffer
            fullySharedParameterIndexes = $fullySharedParameterIndexes.ToArray()
            isStatic = $isStatic
            hasThis = $hasThis
            managedParameterCount = $managedParameterCount
        })
    }

    if ($methodUnsupportedReasons.Count -gt 0) {
        $unsupportedMethods.Add([ordered]@{
            id = [string]$method.id
            name = [string]$method.name
            assemblyName = [string]$mv.assemblyName
            declaringType = [string]$method.declaringType
            methodToken = [uint32]$method.currentToken
            returnType = $returnType
            parameterTypes = ConvertTo-ManifestStringList (Get-OptionalManifestProperty $method "parameterTypes")
            hasThis = [bool](Get-OptionalManifestProperty $method "hasThis")
            declaringTypeIsValueType = $declaringTypeIsValueType
            genericParameterCount = [uint32](Get-OptionalManifestProperty $method "genericParameterCount")
            declaringTypeGenericParameterCount = [uint32](Get-OptionalManifestProperty $method "declaringTypeGenericParameterCount")
            reasons = $methodUnsupportedReasons.ToArray()
        })
        continue
    }
    foreach ($guard in $methodGuards) { $guards.Add($guard) }
    $supportedMethodCount++
}

$outputPath = [IO.Path]::GetFullPath($OutputManifest)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$manifest = [ordered]@{
    schemaVersion = 1
    resolverVersion = 2
    abiContract = "il2cpp-generated-cpp-signature-v2"
    generatedCppRoot = [IO.Path]::GetFullPath($GeneratedCppRoot)
    changedMethodCount = $allChangedMethods.Count
    supportedChangedMethodCount = $supportedMethodCount
    unsupportedChangedMethodCount = $unsupportedMethods.Count
    nativeEntryCount = $guards.Count
    unsupportedChangedMethods = $unsupportedMethods
    methods = $guards.ToArray()
}
[IO.File]::WriteAllText($outputPath, ($manifest | ConvertTo-Json -Depth 8), (New-Object Text.UTF8Encoding($false)))
Write-Host ("Resolved native DHE methods: {0}" -f $guards.Count)
exit 0
