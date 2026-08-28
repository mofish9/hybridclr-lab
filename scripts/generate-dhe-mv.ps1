[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaselineAssembly,

    [Parameter(Mandatory = $true)]
    [string]$CurrentAssembly,

    [Parameter(Mandatory = $true)]
    [string]$Output,

    [string]$DnlibPath,

    [switch]$StrictCompatibility,

    [string]$BinaryOutput
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

function Resolve-ExistingPath([string]$Path, [string]$Description) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (-not [IO.File]::Exists($resolved)) {
        throw "$Description was not found: $resolved"
    }
    return $resolved
}

function Resolve-DnlibPath {
    return Resolve-DheDnlibPath -RequestedPath $DnlibPath -LabRoot (Split-Path -Parent $PSScriptRoot)
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-Sha256File {
    param([Parameter(Mandatory = $true)][string]$Path)
    $bytes = [IO.File]::ReadAllBytes($Path)
    return Get-Sha256 -Bytes $bytes
}

function Write-AtomicTextFile([string]$Path, [string]$Text) {
    $temporaryPath = "$Path.$([IO.Path]::GetRandomFileName()).tmp"
    try {
        [IO.File]::WriteAllText($temporaryPath, $Text, (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Assert-OutputPathDoesNotOverwriteInput([string]$OutputPath, [string]$Description, [string[]]$InputPaths) {
    if ([string]::IsNullOrWhiteSpace($OutputPath)) {
        return
    }
    $resolvedOutput = [IO.Path]::GetFullPath($OutputPath)
    foreach ($inputPath in @($InputPaths)) {
        if ([string]::IsNullOrWhiteSpace($inputPath)) {
            continue
        }
        $resolvedInput = [IO.Path]::GetFullPath($inputPath)
        if ($resolvedOutput.Equals($resolvedInput, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$Description must not overwrite an input assembly: $resolvedOutput"
        }
    }
}

function Convert-HexToBytes([string]$Hex) {
    if ([string]::IsNullOrWhiteSpace($Hex) -or ($Hex.Length % 2) -ne 0) {
        throw "Expected a non-empty even-length hexadecimal string."
    }
    $bytes = New-Object byte[] ($Hex.Length / 2)
    for ($i = 0; $i -lt $bytes.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($Hex.Substring($i * 2, 2), 16)
    }
    return $bytes
}

function Get-TypeFullName($Type) {
    if ($null -eq $Type) {
        return ""
    }
    return [string]$Type.FullName
}

function Get-OperandIdentity($Operand) {
    if ($null -eq $Operand) {
        return ""
    }

    if ($Operand -is [dnlib.DotNet.Emit.Instruction]) {
        return "target:" + [string]$Operand.Offset
    }

    if ($Operand -is [dnlib.DotNet.MethodDef] -or
        $Operand -is [dnlib.DotNet.FieldDef] -or
        $Operand -is [dnlib.DotNet.TypeDef]) {
        return [string]$Operand.FullName
    }

    $fullNameProperty = $Operand.GetType().GetProperty("FullName")
    if ($null -ne $fullNameProperty) {
        return [string]$fullNameProperty.GetValue($Operand, $null)
    }

    return [string]$Operand
}

function Get-OptionalProperty($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function New-StringList([object[]]$Values) {
    # Keep collection-valued metadata as a collection even when it contains
    # zero or one element. PowerShell 5.1 otherwise unwraps single values in
    # conditional expressions and ConvertTo-Json emits a scalar.
    $list = New-Object 'System.Collections.Generic.List[string]'
    foreach ($value in @($Values)) {
        if ($null -ne $value) {
            $list.Add([string]$value)
        }
    }
    return ,$list
}

function Get-CustomAttributeIdentity($Attribute) {
    $typeName = Get-TypeFullName (Get-OptionalProperty $Attribute "AttributeType")
    $constructorArguments = @((Get-OptionalProperty $Attribute "ConstructorArguments") | ForEach-Object { [string]$_ })
    $namedArguments = @((Get-OptionalProperty $Attribute "NamedArguments") | ForEach-Object { [string]$_ })
    return "$typeName|ctor=$($constructorArguments -join ',')|named=$($namedArguments -join ',')"
}

function Get-CustomAttributeIdentities($Owner) {
    return New-StringList (Sort-DheOrdinal @((Get-OptionalProperty $Owner "CustomAttributes") |
        ForEach-Object { Get-CustomAttributeIdentity $_ }))
}

function Get-GenericParameterIdentities($Owner) {
    return New-StringList @((Get-OptionalProperty $Owner "GenericParameters") | ForEach-Object {
        $constraints = Sort-DheOrdinal @((Get-OptionalProperty $_ "GenericParamConstraints") | ForEach-Object {
            Get-TypeFullName (Get-OptionalProperty $_ "Constraint")
        })
        $attrs = [string](Get-OptionalProperty $_ "Attributes")
        "index=$([int](Get-OptionalProperty $_ "Number"));attrs=$attrs;constraints=$($constraints -join ',')"
    })
}

function Get-MethodOverrideIdentity($Override) {
    $methodBody = Get-OptionalProperty $Override "MethodBody"
    $methodDeclaration = Get-OptionalProperty $Override "MethodDeclaration"
    return "body=$([string](Get-OptionalProperty $methodBody 'FullName'));declaration=$([string](Get-OptionalProperty $methodDeclaration 'FullName'))"
}

function Get-InstructionOffset($Instruction) {
    $offset = Get-OptionalProperty $Instruction "Offset"
    if ($null -eq $offset) { return "-" }
    return [string]$offset
}

function Get-MethodBodyHash($Method) {
    if (-not $Method.HasBody) {
        return ""
    }

    $body = $Method.Body
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("maxStack=$([string](Get-OptionalProperty $body 'MaxStack'))")
    $lines.Add("initLocals=$([string](Get-OptionalProperty $body 'InitLocals'))")
    $lines.Add("keepOldMaxStack=$([string](Get-OptionalProperty $body 'KeepOldMaxStack'))")
    # LocalVarSigTok is a metadata row token and can legitimately drift when
    # an otherwise unchanged assembly is rebuilt. Hash the normalized locals
    # below instead of the token itself.
    $locals = @((Get-OptionalProperty $body "Variables") | ForEach-Object {
        "index=$([string](Get-OptionalProperty $_ 'Index'));type=$(Get-TypeFullName (Get-OptionalProperty $_ 'Type'));pinned=$([string](Get-OptionalProperty $_ 'IsPinned'))"
    })
    $lines.Add("locals=$($locals -join ',')")
    foreach ($instruction in $Method.Body.Instructions) {
        $opcode = [string]$instruction.OpCode.Code
        $operand = Get-OperandIdentity $instruction.Operand
        $lines.Add("il:$([string](Get-InstructionOffset $instruction))|$opcode|$operand")
    }
    $handlers = @((Get-OptionalProperty $body "ExceptionHandlers") | ForEach-Object {
        $catchType = Get-TypeFullName (Get-OptionalProperty $_ "CatchType")
        "type=$([string](Get-OptionalProperty $_ 'HandlerType'));try=$(Get-InstructionOffset (Get-OptionalProperty $_ 'TryStart'))-$(Get-InstructionOffset (Get-OptionalProperty $_ 'TryEnd'));handler=$(Get-InstructionOffset (Get-OptionalProperty $_ 'HandlerStart'))-$(Get-InstructionOffset (Get-OptionalProperty $_ 'HandlerEnd'));filter=$(Get-InstructionOffset (Get-OptionalProperty $_ 'FilterStart'));catch=$catchType"
    })
    foreach ($handler in $handlers) {
        $lines.Add("eh:$handler")
    }
    return Get-Sha256 ([Text.Encoding]::UTF8.GetBytes(($lines -join "`n")))
}

function Get-MethodIdentity($Method) {
    $declaringType = Get-TypeFullName $Method.DeclaringType
    return "$declaringType::$($Method.Name)|$($Method.MethodSig)"
}

function Get-MethodRecord($Method) {
    $methodSig = $Method.MethodSig
    $parameterTypes = New-StringList ((Get-OptionalProperty $methodSig "Params") | ForEach-Object {
        Get-TypeFullName $_
    })
    $returnType = Get-TypeFullName (Get-OptionalProperty $methodSig "RetType")
    $declaringType = $Method.DeclaringType
    $declaringGenericParameterCount = if ($null -eq $declaringType) {
        0
    } else {
        [uint32](@((Get-OptionalProperty $declaringType "GenericParameters")).Count)
    }
    $overrides = New-StringList (Sort-DheOrdinal @((Get-OptionalProperty $Method "Overrides") |
        ForEach-Object { Get-MethodOverrideIdentity $_ }))
    return [ordered]@{
        id = Get-MethodIdentity $Method
        name = [string]$Method.Name
        declaringType = Get-TypeFullName $declaringType
        signature = [string]$Method.MethodSig
        returnType = $returnType
        parameterTypes = $parameterTypes
        hasThis = [bool](Get-OptionalProperty $methodSig "HasThis")
        declaringTypeIsValueType = if ($null -eq $declaringType) { $false } else { [bool]$declaringType.IsValueType }
        declaringTypeGenericParameterCount = $declaringGenericParameterCount
        token = [uint32]$Method.MDToken.Raw
        bodySha256 = Get-MethodBodyHash $Method
        hasBody = [bool]$Method.HasBody
        isStatic = [bool]$Method.IsStatic
        isVirtual = [bool]$Method.IsVirtual
        isAbstract = [bool]$Method.IsAbstract
        isPInvoke = [bool]$Method.IsPinvokeImpl
        genericParameterCount = [uint32]$Method.GenericParameters.Count
        attributes = [uint32]$Method.Attributes
        implementationAttributes = [uint16]$Method.ImplAttributes
        genericParameters = Get-GenericParameterIdentities $Method
        customAttributes = Get-CustomAttributeIdentities $Method
        overrides = $overrides
    }
}

function Add-TypeRecords($Type, $Records) {
    if ($Type.Name -ne "<Module>") {
        $fieldParts = New-StringList (Sort-DheOrdinal @($Type.Fields | ForEach-Object {
            $offset = Get-OptionalProperty $_ "FieldOffset"
            $constant = Get-OptionalProperty $_ "Constant"
            $constantIdentity = if ($null -eq $constant) {
                "none"
            } else {
                "type=$([string](Get-OptionalProperty $constant 'Type'));value=$([string](Get-OptionalProperty $constant 'Value'))"
            }
            "$(Get-TypeFullName $_.FieldType)|$($_.Name)|$($_.Attributes)|offset=$offset|constant=$constantIdentity|attrs=$(@(Get-CustomAttributeIdentities $_) -join ',')"
        }))
        $interfaceParts = New-StringList (Sort-DheOrdinal @($Type.Interfaces | ForEach-Object {
            Get-TypeFullName $_.Interface
        }))
        $baseType = Get-TypeFullName $Type.BaseType
        $classLayout = Get-OptionalProperty $Type "ClassLayout"
        $layoutProperties = @("PackingSize", "ClassSize", "AutoLayout", "ExplicitLayout", "SequentialLayout") |
            ForEach-Object { "$_=$([string](Get-OptionalProperty $classLayout $_))" }
        $genericParameters = Get-GenericParameterIdentities $Type
        $customAttributes = Get-CustomAttributeIdentities $Type
        $propertyParts = New-StringList (Sort-DheOrdinal @((Get-OptionalProperty $Type "Properties") | ForEach-Object {
            "name=$([string](Get-OptionalProperty $_ 'Name'));sig=$([string](Get-OptionalProperty $_ 'PropertySig'));attrs=$([string](Get-OptionalProperty $_ 'Attributes'));get=$([string](Get-OptionalProperty $_ 'GetMethod'));set=$([string](Get-OptionalProperty $_ 'SetMethod'));custom=$(@(Get-CustomAttributeIdentities $_) -join ',')"
        }))
        $eventParts = New-StringList (Sort-DheOrdinal @((Get-OptionalProperty $Type "Events") | ForEach-Object {
            "name=$([string](Get-OptionalProperty $_ 'Name'));type=$(Get-TypeFullName (Get-OptionalProperty $_ 'EventType'));attrs=$([string](Get-OptionalProperty $_ 'Attributes'));add=$([string](Get-OptionalProperty $_ 'AddMethod'));remove=$([string](Get-OptionalProperty $_ 'RemoveMethod'));raise=$([string](Get-OptionalProperty $_ 'InvokeMethod'));custom=$(@(Get-CustomAttributeIdentities $_) -join ',')"
        }))
        $layoutText = @(
            "base=$baseType"
            "interfaces=$($interfaceParts -join ',')"
            "fields=$($fieldParts -join ',')"
            "properties=$($propertyParts -join ',')"
            "events=$($eventParts -join ',')"
            "attributes=$($Type.Attributes)"
            "valueType=$([bool]$Type.IsValueType)"
            "classLayout=$($layoutProperties -join ',')"
            "genericParameters=$($genericParameters -join ',')"
            "customAttributes=$($customAttributes -join ',')"
        ) -join "`n"
        $Records[[string]$Type.FullName] = [ordered]@{
            id = [string]$Type.FullName
            layoutSha256 = Get-Sha256 ([Text.Encoding]::UTF8.GetBytes($layoutText))
            baseType = $baseType
            fields = $fieldParts
            properties = $propertyParts
            events = $eventParts
            interfaces = $interfaceParts
            isValueType = [bool]$Type.IsValueType
            classLayout = $layoutProperties
            genericParameters = $genericParameters
            customAttributes = $customAttributes
        }
    }

    foreach ($nested in $Type.NestedTypes) {
        Add-TypeRecords $nested $Records
    }
}

function Get-ModuleRecords([string]$Path) {
    $module = [dnlib.DotNet.ModuleDefMD]::Load($Path)
    try {
        $methods = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($Type in $module.Types) {
            foreach ($Method in $Type.Methods) {
                $record = Get-MethodRecord $Method
                $methods[$record.id] = $record
            }
            foreach ($nested in $Type.NestedTypes) {
                $nestedMethods = New-Object System.Collections.Generic.List[object]
                $stack = New-Object System.Collections.Generic.Stack[object]
                $stack.Push($nested)
                while ($stack.Count -gt 0) {
                    $currentType = $stack.Pop()
                    foreach ($Method in $currentType.Methods) {
                        $record = Get-MethodRecord $Method
                        $methods[$record.id] = $record
                    }
                    foreach ($child in $currentType.NestedTypes) {
                        $stack.Push($child)
                    }
                }
            }
        }

        $types = [System.Collections.Generic.Dictionary[string,object]]::new([StringComparer]::Ordinal)
        foreach ($Type in $module.Types) {
            Add-TypeRecords $Type $types
        }
        $assemblyRefs = New-StringList (Sort-DheOrdinal @((Get-OptionalProperty $module "AssemblyRefs") |
            ForEach-Object { [string]$_ }))
        $assembly = $module.Assembly
        $assemblyIdentity = if ($null -eq $assembly) {
            ""
        } else {
            "name=$([string]$assembly.Name.String);version=$([string]$assembly.Version);culture=$([string]$assembly.Culture);attributes=$([string]$assembly.Attributes);hashAlgorithm=$([string]$assembly.HashAlgorithm);publicKey=$([string]$assembly.PublicKey)"
        }

        return [ordered]@{
            assemblyName = [string]$module.Assembly.Name.String
            mvid = [string]$module.Mvid
            methods = $methods
            types = $types
            assemblyRefs = $assemblyRefs
            assemblyAttributes = Get-CustomAttributeIdentities $module.Assembly
            assemblyIdentity = $assemblyIdentity
        }
    }
    finally {
        $module.Dispose()
    }
}

$baselinePath = Resolve-ExistingPath $BaselineAssembly "Baseline assembly"
$currentPath = Resolve-ExistingPath $CurrentAssembly "Current assembly"
$dnlib = Resolve-DnlibPath
Add-Type -Path $dnlib

$baseline = Get-ModuleRecords $baselinePath
$current = Get-ModuleRecords $currentPath
if ($baseline.assemblyName -ne $current.assemblyName) {
    throw "Assembly names differ: '$($baseline.assemblyName)' vs '$($current.assemblyName)'."
}

$methodChanges = New-Object System.Collections.Generic.List[object]
$methodIds = Sort-DheOrdinal @($baseline.methods.Keys + $current.methods.Keys | Select-Object -Unique)
foreach ($methodId in $methodIds) {
    $old = $baseline.methods[$methodId]
    $new = $current.methods[$methodId]
    $tokenStable = ($null -ne $old -and $null -ne $new -and $old.token -eq $new.token)
    $shapeStable = ($null -ne $old -and $null -ne $new -and
        $old.isStatic -eq $new.isStatic -and
        $old.isVirtual -eq $new.isVirtual -and
        $old.isAbstract -eq $new.isAbstract -and
        $old.isPInvoke -eq $new.isPInvoke -and
        $old.attributes -eq $new.attributes -and
        $old.implementationAttributes -eq $new.implementationAttributes -and
        (($old.genericParameters -join "`n") -eq ($new.genericParameters -join "`n")) -and
        (($old.customAttributes -join "`n") -eq ($new.customAttributes -join "`n")) -and
        (($old.overrides -join "`n") -eq ($new.overrides -join "`n")))
    $changeKind = if ($null -eq $old) {
        "added"
    }
    elseif ($null -eq $new) {
        "removed"
    }
    elseif ($old.bodySha256 -ne $new.bodySha256) {
        "changed"
    }
    elseif ($old.token -ne $new.token) {
        "tokenChanged"
    }
    elseif ($old.isStatic -ne $new.isStatic -or
            $old.isVirtual -ne $new.isVirtual -or
            $old.isAbstract -ne $new.isAbstract -or
            $old.isPInvoke -ne $new.isPInvoke -or
            $old.attributes -ne $new.attributes -or
            $old.implementationAttributes -ne $new.implementationAttributes -or
            (($old.genericParameters -join "`n") -ne ($new.genericParameters -join "`n")) -or
            (($old.customAttributes -join "`n") -ne ($new.customAttributes -join "`n")) -or
            (($old.overrides -join "`n") -ne ($new.overrides -join "`n"))) {
        "shapeChanged"
    }
    else {
        "unchanged"
    }
    $methodChanges.Add([ordered]@{
        id = $methodId
        kind = $changeKind
         name = if ($null -eq $new) { $old.name } else { $new.name }
         declaringType = if ($null -eq $new) { $old.declaringType } else { $new.declaringType }
         signature = if ($null -eq $new) { $old.signature } else { $new.signature }
         returnType = if ($null -eq $new) { $old.returnType } else { $new.returnType }
        parameterTypes = if ($null -eq $new) {
            New-StringList $old.parameterTypes
        } else {
            New-StringList $new.parameterTypes
        }
         hasThis = if ($null -eq $new) { $old.hasThis } else { $new.hasThis }
         declaringTypeIsValueType = if ($null -eq $new) { $old.declaringTypeIsValueType } else { $new.declaringTypeIsValueType }
         declaringTypeGenericParameterCount = if ($null -eq $new) { $old.declaringTypeGenericParameterCount } else { $new.declaringTypeGenericParameterCount }
         isStatic = if ($null -eq $new) { $old.isStatic } else { $new.isStatic }
        isVirtual = if ($null -eq $new) { $old.isVirtual } else { $new.isVirtual }
        isAbstract = if ($null -eq $new) { $old.isAbstract } else { $new.isAbstract }
        isPInvoke = if ($null -eq $new) { $old.isPInvoke } else { $new.isPInvoke }
        genericParameterCount = if ($null -eq $new) { $old.genericParameterCount } else { $new.genericParameterCount }
        baselineToken = if ($null -eq $old) { $null } else { $old.token }
        currentToken = if ($null -eq $new) { $null } else { $new.token }
        tokenStable = $tokenStable
        shapeStable = $shapeStable
        baselineAttributes = if ($null -eq $old) { $null } else { $old.attributes }
        currentAttributes = if ($null -eq $new) { $null } else { $new.attributes }
        baselineImplementationAttributes = if ($null -eq $old) { $null } else { $old.implementationAttributes }
        currentImplementationAttributes = if ($null -eq $new) { $null } else { $new.implementationAttributes }
        baselineGenericParameters = if ($null -eq $old) { $null } else { New-StringList $old.genericParameters }
        currentGenericParameters = if ($null -eq $new) { $null } else { New-StringList $new.genericParameters }
        baselineCustomAttributes = if ($null -eq $old) { $null } else { New-StringList $old.customAttributes }
        currentCustomAttributes = if ($null -eq $new) { $null } else { New-StringList $new.customAttributes }
        baselineOverrides = if ($null -eq $old) { $null } else { New-StringList $old.overrides }
        currentOverrides = if ($null -eq $new) { $null } else { New-StringList $new.overrides }
        baselineBodySha256 = if ($null -eq $old) { $null } else { $old.bodySha256 }
        currentBodySha256 = if ($null -eq $new) { $null } else { $new.bodySha256 }
    })
}

$typeChanges = New-Object System.Collections.Generic.List[object]
$typeIds = Sort-DheOrdinal @($baseline.types.Keys + $current.types.Keys | Select-Object -Unique)
foreach ($typeId in $typeIds) {
    $old = $baseline.types[$typeId]
    $new = $current.types[$typeId]
    $changeKind = if ($null -eq $old) { "added" } elseif ($null -eq $new) { "removed" } elseif ($old.layoutSha256 -ne $new.layoutSha256) { "layoutChanged" } else { "unchanged" }
    if ($changeKind -ne "unchanged") {
        $typeChanges.Add([ordered]@{
            id = $typeId
            kind = $changeKind
            baselineLayoutSha256 = if ($null -eq $old) { $null } else { $old.layoutSha256 }
            currentLayoutSha256 = if ($null -eq $new) { $null } else { $new.layoutSha256 }
        })
    }
}

$outputPath = [IO.Path]::GetFullPath($Output)
$baselinePath = [IO.Path]::GetFullPath($baselinePath)
$currentPath = [IO.Path]::GetFullPath($currentPath)
Assert-OutputPathDoesNotOverwriteInput $outputPath "MV JSON output" @($baselinePath, $currentPath)
Assert-OutputPathDoesNotOverwriteInput $BinaryOutput "MV binary output" @($baselinePath, $currentPath)
if (-not [string]::IsNullOrWhiteSpace($BinaryOutput) -and
    $outputPath.Equals([IO.Path]::GetFullPath($BinaryOutput), [StringComparison]::OrdinalIgnoreCase)) {
    throw "MV JSON output and binary output must be different files: $outputPath"
}
$outputDirectory = [IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}

$binaryOutputPath = $null
if (-not [string]::IsNullOrWhiteSpace($BinaryOutput)) {
    $binaryOutputPath = [IO.Path]::GetFullPath($BinaryOutput)
}

$baselineAssemblySha256 = Get-Sha256File -Path $baselinePath
$currentAssemblySha256 = Get-Sha256File -Path $currentPath
$methodChangesArray = $methodChanges.ToArray()
$typeChangesArray = $typeChanges.ToArray()
$compatibilityReasons = New-Object System.Collections.Generic.List[string]
if (($baseline.assemblyRefs -join "`n") -ne ($current.assemblyRefs -join "`n")) {
    $compatibilityReasons.Add("assembly references changed")
}
if (($baseline.assemblyAttributes -join "`n") -ne ($current.assemblyAttributes -join "`n")) {
    $compatibilityReasons.Add("assembly custom attributes changed")
}
if ([string]$baseline.assemblyIdentity -ne [string]$current.assemblyIdentity) {
    $compatibilityReasons.Add("assembly identity changed")
}
foreach ($methodChange in $methodChangesArray) {
    switch ($methodChange.kind) {
        "added" {
            $compatibilityReasons.Add("method added: $($methodChange.id)")
        }
        "removed" {
            $compatibilityReasons.Add("method removed: $($methodChange.id)")
        }
        "tokenChanged" {
            $compatibilityReasons.Add("method token changed: $($methodChange.id) ($($methodChange.baselineToken) -> $($methodChange.currentToken))")
        }
        "shapeChanged" {
            $compatibilityReasons.Add("method ABI shape changed: $($methodChange.id)")
        }
        "changed" {
            if (-not $methodChange.tokenStable) {
                $compatibilityReasons.Add("changed method token is not stable: $($methodChange.id)")
            }
            if (-not $methodChange.shapeStable) {
                $compatibilityReasons.Add("changed method ABI shape is not stable: $($methodChange.id)")
            }
        }
    }
}
foreach ($typeChange in $typeChangesArray) {
    $compatibilityReasons.Add("type layout changed: $($typeChange.id) [$($typeChange.kind)]")
}
$compatibilityReasonsArray = $compatibilityReasons.ToArray()
$strictCompatible = ($compatibilityReasonsArray.Count -eq 0)
$changedMethodCount = 0
$unchangedMethodCount = 0
foreach ($methodChange in $methodChangesArray) {
    if ($methodChange.kind -eq "unchanged") {
        $unchangedMethodCount++
    }
    else {
        $changedMethodCount++
    }
}
$result = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-lite.mv.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    assemblyName = $baseline.assemblyName
    baseline = [ordered]@{
        path = $baselinePath
        sha256 = $baselineAssemblySha256
        mvid = $baseline.mvid
        assemblyRefs = $baseline.assemblyRefs
        identity = $baseline.assemblyIdentity
    }
    current = [ordered]@{
        path = $currentPath
        sha256 = $currentAssemblySha256
        mvid = $current.mvid
        assemblyRefs = $current.assemblyRefs
        identity = $current.assemblyIdentity
    }
    methods = $methodChangesArray
    typeChanges = $typeChangesArray
    compatibility = [ordered]@{
        mode = if ($StrictCompatibility) { "method-body-only" } else { "analysis" }
        status = if ($strictCompatible) { "compatible" } else { "incompatible" }
        reasons = $compatibilityReasonsArray
    }
    summary = [ordered]@{
        methodCount = $methodChangesArray.Count
        changedMethodCount = $changedMethodCount
        unchangedMethodCount = $unchangedMethodCount
        typeChangeCount = $typeChangesArray.Count
        compatibleMethodOnlyChange = $strictCompatible
    }
}

$json = $result | ConvertTo-Json -Depth 12
Write-AtomicTextFile $outputPath $json

if (-not [string]::IsNullOrWhiteSpace($BinaryOutput)) {
    if (-not $StrictCompatibility) {
        throw "-BinaryOutput requires -StrictCompatibility."
    }
    if (-not $strictCompatible) {
        throw "-BinaryOutput cannot be produced for an incompatible diff."
    }

    $binaryOutputDirectory = [IO.Path]::GetDirectoryName($binaryOutputPath)
    if (-not [string]::IsNullOrWhiteSpace($binaryOutputDirectory)) {
        [IO.Directory]::CreateDirectory($binaryOutputDirectory) | Out-Null
    }

    $changedTokenRecords = @($methodChangesArray |
        Where-Object { $_.kind -eq "changed" -and $null -ne $_.currentToken } |
        Sort-Object currentToken)
    $temporaryBinaryPath = "$binaryOutputPath.$([IO.Path]::GetRandomFileName()).tmp"
    $stream = [IO.File]::Open($temporaryBinaryPath, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $writer = New-Object IO.BinaryWriter($stream)
    $binaryWriteSucceeded = $false
    try {
        $writer.Write([Text.Encoding]::ASCII.GetBytes("DHEMVLT1"))
        $writer.Write([uint32]1)
        $assemblyNameBytes = [Text.Encoding]::UTF8.GetBytes($result.assemblyName)
        $writer.Write([uint32]$assemblyNameBytes.Length)
        $writer.Write([uint32]$changedTokenRecords.Count)
        $writer.Write([uint32]1) # strict method-body-only compatibility
        $writer.Write([byte[]](Convert-HexToBytes $baselineAssemblySha256))
        $writer.Write([byte[]](Convert-HexToBytes $currentAssemblySha256))
        $writer.Write($assemblyNameBytes)
        foreach ($record in $changedTokenRecords) {
            $writer.Write([uint32]$record.currentToken)
        }
        $binaryWriteSucceeded = $true
    }
    finally {
        $writer.Dispose()
        if (-not $binaryWriteSucceeded -and (Test-Path -LiteralPath $temporaryBinaryPath)) {
            Remove-Item -LiteralPath $temporaryBinaryPath -Force
        }
    }
    Move-Item -LiteralPath $temporaryBinaryPath -Destination $binaryOutputPath -Force
    if (Test-Path -LiteralPath $temporaryBinaryPath) {
        Remove-Item -LiteralPath $temporaryBinaryPath -Force
    }
    Write-Host "DHE mv binary: $binaryOutputPath"
}

Write-Host "DHE mv: $outputPath"
Write-Host ("Assembly: {0}; methods changed: {1}/{2}; type changes: {3}" -f `
    $result.assemblyName,
    $result.summary.changedMethodCount,
    $result.summary.methodCount,
    $result.summary.typeChangeCount)
Write-Host ("Compatibility: {0} ({1})" -f $result.compatibility.status, $result.compatibility.mode)

if ($StrictCompatibility -and -not $strictCompatible) {
    throw ("DHE strict compatibility rejected the diff:`n - " + ($compatibilityReasonsArray -join "`n - "))
}
exit 0
