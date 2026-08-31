param(
    [Parameter(Mandatory = $true)]
    [string]$InputFile,
    [Parameter(Mandatory = $true)]
    [string]$OutputFile,
    [Parameter(Mandatory = $true)]
    [string]$ManifestFile,
    [string]$ReportFile = ""
)

$ErrorActionPreference = "Stop"

function New-Guard([object]$method) {
    $returnType = [string]$method.returnType
    if ([string]::IsNullOrWhiteSpace($returnType)) {
        throw "DHE method '$($method.functionName)' has no returnType."
    }

    $parameters = @($method.parameters)
    $parameterTypes = @($parameters | ForEach-Object { [string]$_.type })
    $parameterNames = @($parameters | ForEach-Object { [string]$_.name })
    if ($parameterNames.Count -ne $parameterTypes.Count) {
        throw "DHE method '$($method.functionName)' has malformed parameters."
    }
    foreach ($name in $parameterNames) {
        if ($name -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
            throw "DHE method '$($method.functionName)' has an invalid parameter name '$name'."
        }
    }

    $callTypes = @($parameterTypes + @("const RuntimeMethod*"))
    $callArguments = @($parameterNames + @("method")) -join ", "
    $functionType = "$returnType (*)($($callTypes -join ', '))"
    $functionName = [string]$method.functionName
    $assemblyName = [string]$method.assemblyName
    if ([string]::IsNullOrWhiteSpace($assemblyName)) {
        throw "DHE method '$functionName' has no assemblyName."
    }
    $methodToken = [uint32]$method.methodToken
    if ($methodToken -eq 0) {
        throw "DHE method '$functionName' has no methodToken."
    }
    $declaringType = [string]$method.declaringType
    $methodName = [string]$method.methodName
    if ([string]::IsNullOrWhiteSpace($declaringType) -or [string]::IsNullOrWhiteSpace($methodName)) {
        throw "DHE method '$functionName' has no managed declaring type or method name."
    }
    $escapedAssemblyName = $assemblyName.Replace('\\', '\\\\').Replace('"', '\\"')
    $escapedDeclaringType = $declaringType.Replace('\\', '\\\\').Replace('"', '\\"')
    $escapedMethodName = $methodName.Replace('\\', '\\\\').Replace('"', '\\"')
    $isStatic = if ($null -ne $method.PSObject.Properties["isStatic"]) {
        if ($method.isStatic -isnot [bool]) { throw "DHE method '$functionName' isStatic must be a JSON boolean." }
        [bool]$method.isStatic
    } else {
        $true
    }
    $hasThis = if ($null -ne $method.PSObject.Properties["hasThis"]) {
        if ($method.hasThis -isnot [bool]) { throw "DHE method '$functionName' hasThis must be a JSON boolean." }
        [bool]$method.hasThis
    } else {
        -not $isStatic
    }
    if ($hasThis -and ($parameterNames.Count -eq 0 -or $parameterNames[0] -ne "__this")) {
        throw "DHE method '$functionName' has no leading __this parameter for an instance method."
    }
    $managedParameterOffset = if ($hasThis) { 1 } else { 0 }
    $managedParameterTypes = @($parameterTypes | Select-Object -Skip $managedParameterOffset)
    $managedParameterNames = @($parameterNames | Select-Object -Skip $managedParameterOffset)
    $bridgeKind = if ($null -ne $method.PSObject.Properties["bridgeKind"]) {
        [string]$method.bridgeKind
    } else {
        "shape-helper-v1"
    }
    $usesHiddenReturnBuffer = if ($null -ne $method.PSObject.Properties["usesHiddenReturnBuffer"]) {
        if ($method.usesHiddenReturnBuffer -isnot [bool]) {
            throw "DHE method '$functionName' usesHiddenReturnBuffer must be a JSON boolean."
        }
        [bool]$method.usesHiddenReturnBuffer
    } else {
        $false
    }
    $hiddenReturnName = $null
    if ($usesHiddenReturnBuffer) {
        if ($managedParameterNames.Count -eq 0 -or $managedParameterNames[-1] -ne "il2cppRetVal") {
            throw "DHE method '$functionName' has no trailing il2cppRetVal buffer."
        }
        $hiddenReturnName = $managedParameterNames[-1]
        $managedParameterNames = @($managedParameterNames | Select-Object -First ($managedParameterNames.Count - 1))
        $managedParameterTypes = @($managedParameterTypes | Select-Object -First ($managedParameterTypes.Count - 1))
    }
    $parameterCount = if ($null -ne $method.PSObject.Properties["managedParameterCount"]) {
        [int]$method.managedParameterCount
    } else {
        $managedParameterNames.Count
    }
    if ($parameterCount -ne $managedParameterNames.Count) {
        throw "DHE method '$functionName' has inconsistent managed parameter count."
    }
    $nativeShape = "$returnType($($managedParameterTypes -join ', '))"
    $receiverName = if ($hasThis) { $parameterNames[0] } else { $null }
    $isValueTypeReceiver = $false
    if ($null -ne $method.PSObject.Properties["declaringTypeIsValueType"]) {
        if ($method.declaringTypeIsValueType -isnot [bool]) {
            throw "DHE method '$functionName' declaringTypeIsValueType must be a JSON boolean."
        }
        $isValueTypeReceiver = $hasThis -and [bool]$method.declaringTypeIsValueType
    }
    $helperArguments = if ($hasThis) {
        @("dheMethod", $receiverName) + $managedParameterNames
    } else {
        @("dheMethod") + $managedParameterNames
    }
    $helperArgumentText = $helperArguments -join ", "
    $isVoid = $returnType -eq "void"
    $isInt32 = $returnType -eq "int32_t"
    $isInt64 = $returnType -eq "int64_t"
    $isBool = $returnType -eq "bool"
    $allManagedParametersArePointers = $managedParameterTypes.Count -gt 0 -and
        @($managedParameterTypes | Where-Object { $_ -notmatch '\*\s*$' }).Count -eq 0
    $firstManagedParameterIsValueTypePointer = $managedParameterTypes.Count -eq 2 -and
        $managedParameterTypes[0] -match '^(?:const\s+)?(?:struct\s+)?[A-Za-z_][A-Za-z0-9_]*_t[0-9A-Fa-f]+\s*\*$'
    $secondManagedParameterIsInt32Pointer = $managedParameterTypes.Count -eq 2 -and
        $managedParameterTypes[1] -match '^int32_t\s*\*$'
    $helperCall = if ($bridgeKind -eq "invoke-args-v1") {
        $declaredFullySharedIndexes = @($method.fullySharedParameterIndexes | ForEach-Object { [int]$_ })
        $inferredFullySharedIndexes = @()
        $invokeArguments = @()
        $invokeKinds = @()
        for ($parameterIndex = 0; $parameterIndex -lt $managedParameterTypes.Count; ++$parameterIndex) {
            $parameterType = $managedParameterTypes[$parameterIndex]
            $parameterName = $managedParameterNames[$parameterIndex]
            if ($parameterType -match '^Il2CppFullySharedGeneric(?:Any|Struct)$') {
                $inferredFullySharedIndexes += $parameterIndex
                $invokeArguments += "reinterpret_cast<void*>($parameterName)"
                $invokeKinds += "1u"
            } elseif ($parameterType -match '\*\s*$') {
                # A generated pointer for a supported generic by-value
                # parameter is the raw managed reference passed to Invoke.
                $invokeArguments += "reinterpret_cast<void*>($parameterName)"
                $invokeKinds += "1u"
            } else {
                $invokeArguments += "reinterpret_cast<void*>(&$parameterName)"
                $invokeKinds += "0u"
            }
        }
        if (($declaredFullySharedIndexes -join ',') -ne ($inferredFullySharedIndexes -join ',')) {
            throw "DHE method '$functionName' fully-shared parameter indexes do not match its generated signature."
        }
        $thisArgument = if ($hasThis) { "reinterpret_cast<void*>($receiverName)" } else { "nullptr" }
        $resultArgument = if ($usesHiddenReturnBuffer) {
            "reinterpret_cast<void*>($hiddenReturnName)"
        } elseif ($isVoid) {
            "nullptr"
        } else {
            "&dheResult"
        }
        $invokeLines = @(
            "void* dheInvokeArgs[] = { $($invokeArguments -join ', ') };"
            "const uint8_t dheInvokeArgKinds[] = { $($invokeKinds -join ', ') };"
        )
        if (-not $isVoid) {
            $invokeLines += "${returnType} dheResult{};"
        }
        $invokeLines += "hybridclr::dhe::ExecuteInterpreterInvokeArgs(dheMethod, $thisArgument, dheInvokeArgs, dheInvokeArgKinds, $parameterCount, $resultArgument);"
        if (-not $isVoid) {
            $invokeLines += "return dheResult;"
        } else {
            $invokeLines += "return;"
        }
        $invokeLines -join "`r`n        "
    }
    elseif ($bridgeKind -ne "shape-helper-v1") {
        throw "DHE method '$functionName' uses unsupported bridgeKind '$bridgeKind'."
    }
    elseif ($isValueTypeReceiver -and $managedParameterTypes.Count -eq 0 -and $isVoid) {
        "hybridclr::dhe::ExecuteInterpreterValueTypeInstanceVoidNoArgs(dheMethod, $receiverName);`r`n        return;"
    }
    elseif ($hasThis -and $managedParameterTypes.Count -eq 0 -and $isVoid) {
        "hybridclr::dhe::ExecuteInterpreterInstanceVoidNoArgs(dheMethod, $receiverName);`r`n        return;"
    }
    elseif ($hasThis -and $managedParameterTypes.Count -eq 0 -and $isBool) {
        "return hybridclr::dhe::ExecuteInterpreterInstanceBool(dheMethod, $receiverName);"
    }
    elseif (-not $hasThis -and $isVoid -and $firstManagedParameterIsValueTypePointer -and $secondManagedParameterIsInt32Pointer) {
        "hybridclr::dhe::ExecuteInterpreterRefValueI4Ref(dheMethod, $($managedParameterNames[0]), $($managedParameterNames[1]));`r`n        return;"
    }
    elseif (-not $hasThis -and $isInt32 -and $managedParameterTypes.Count -eq 2 -and
        $managedParameterTypes[0] -match '\*$' -and $managedParameterTypes[1] -eq 'int32_t') {
        "return hybridclr::dhe::ExecuteInterpreterPtrI4(dheMethod, $($managedParameterNames[0]), $($managedParameterNames[1]));"
    }
    elseif (-not $hasThis -and $managedParameterTypes.Count -eq 1 -and
        $managedParameterTypes[0] -match '_t[0-9A-F]+$' -and
        $managedParameterTypes[0] -notmatch '\*\s*$' -and
        -not $isVoid) {
        @(
            "${returnType} dheResult{};"
            "hybridclr::dhe::ExecuteInterpreterValue(dheMethod, &$($managedParameterNames[0]), sizeof($($managedParameterNames[0])), &dheResult);"
            "return dheResult;"
        ) -join "`r`n        "
    }
    elseif (-not $hasThis -and $managedParameterTypes.Count -eq 1 -and
        -not $isVoid -and -not $isInt32 -and -not $isInt64 -and -not $isBool -and
        $returnType -notmatch '\*$' -and $managedParameterTypes[0] -notmatch '\*$') {
        @(
            "${returnType} dheResult{};"
            "hybridclr::dhe::ExecuteInterpreterValue(dheMethod, &$($managedParameterNames[0]), sizeof($($managedParameterNames[0])), &dheResult);"
            "return dheResult;"
        ) -join "`r`n        "
    }
    else {
        switch ($nativeShape) {
        "int32_t(int32_t)" {
            if ($hasThis) { "return hybridclr::dhe::ExecuteInterpreterInstanceI4I4($helperArgumentText);" }
            else { "return hybridclr::dhe::ExecuteInterpreterI4I4($helperArgumentText);" }
            break
        }
        "int32_t(int32_t, int32_t)" {
            if ($hasThis) { throw "DHE method '$functionName' has unsupported instance shape '$nativeShape'." }
            "return hybridclr::dhe::ExecuteInterpreterI4I4I4($helperArgumentText);"
            break
        }
        "int64_t(int64_t)" {
            if ($hasThis) { "return hybridclr::dhe::ExecuteInterpreterInstanceI8I8($helperArgumentText);" }
            else { "return hybridclr::dhe::ExecuteInterpreterI8I8($helperArgumentText);" }
            break
        }
        "void(int32_t)" {
            if ($hasThis) { "hybridclr::dhe::ExecuteInterpreterInstanceVoidI4($helperArgumentText);`r`n        return;" }
            else { "hybridclr::dhe::ExecuteInterpreterVoidI4($helperArgumentText);`r`n        return;" }
            break
        }
        default { throw "DHE method '$functionName' has unsupported native shape '$nativeShape'." }
        }
    }
    $marker = "HYBRIDCLR_DHE_GUARD_V4:${functionName}:$methodToken"

    $lines = @(
    "    // $marker",
        "    const RuntimeMethod* dheMethod = method;",
        "    if (dheMethod == nullptr)",
        "    {",
        "        dheMethod = hybridclr::dhe::ResolveMethodByToken(`"$escapedAssemblyName`", $methodToken);",
        "    }",
        "    if (hybridclr::dhe::ShouldDispatchToInterpreter(dheMethod))",
        "    {"
    )
    $lines += "        $helperCall"
    $lines += @(
        "    }",
        "    // Count only the path that reaches the original generated AOT body.",
        "    hybridclr::dhe::RecordAotEntry();"
    )
    return ($lines -join "`r`n") + "`r`n"
}

if (-not (Test-Path -LiteralPath $InputFile -PathType Leaf)) {
    throw "Generated C++ input was not found: $InputFile"
}
if (-not (Test-Path -LiteralPath $ManifestFile -PathType Leaf)) {
    throw "DHE guard manifest was not found: $ManifestFile"
}

$spec = Get-Content -Raw -LiteralPath $ManifestFile | ConvertFrom-Json
$methods = @($spec.methods)
if ($methods.Count -eq 0) {
    throw "DHE guard manifest contains no methods."
}

$text = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $InputFile).Path)
$changed = 0
$changedFunctions = New-Object System.Collections.Generic.List[string]
    $includeBlock = '#include "hybridclr/DheRuntime.h"' + "`r`n" + '#include "hybridclr/Il2CppCompatibleDef.h"' + "`r`n"

foreach ($method in $methods) {
    $functionName = [string]$method.functionName
    if ($functionName -notmatch '^[A-Za-z_][A-Za-z0-9_]*$') {
        throw "Invalid DHE function name '$functionName'."
    }
    $marker = "HYBRIDCLR_DHE_GUARD_V4:${functionName}:$([uint32]$method.methodToken)"
    if ($text.Contains($marker)) {
        continue
    }

    $escapedName = [regex]::Escape($functionName)
    # Unity 2021 emits method signatures on one physical line. Keeping the
    # match line-bounded prevents a declaration/call elsewhere in the file
    # from being mistaken for the function definition.
    $pattern = "(?m)^(?<signature>[^\r\n{};]*\b$escapedName\s*\([^\r\n{};]*\)\s*)\{"
    $match = [regex]::Match($text, $pattern)
    if (-not $match.Success) {
        throw "Could not find a definition for DHE function '$functionName'."
    }
    $signature = $match.Groups["signature"].Value
    if ($signature -notmatch '\bconst\s+RuntimeMethod\s*\*\s*method\b') {
        throw "DHE function '$functionName' does not have the expected RuntimeMethod parameter."
    }

    foreach ($parameter in @($method.parameters)) {
        $parameterName = [string]$parameter.name
        if ($signature -notmatch ("\b" + [regex]::Escape($parameterName) + "\b")) {
            throw "DHE function '$functionName' does not contain parameter '$parameterName'."
        }
    }

    $guard = New-Guard $method
    $insertAt = $match.Index + $match.Length
    $text = $text.Insert($insertAt, "`r`n$guard")
    $changed++
    $changedFunctions.Add($functionName)
}

if ($changed -gt 0 -and $text -notmatch '(?m)^#include\s+"hybridclr/DheRuntime\.h"') {
    $text = $includeBlock + $text
}

$outputPath = [IO.Path]::GetFullPath($OutputFile)
$outputDirectory = [IO.Path]::GetDirectoryName($outputPath)
if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
    [IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
}
[IO.File]::WriteAllText($outputPath, $text, (New-Object Text.UTF8Encoding($false)))

$report = [ordered]@{
    schemaVersion = 1
    inputFile = [IO.Path]::GetFullPath($InputFile)
    outputFile = $outputPath
    requestedMethodCount = $methods.Count
    transformedMethodCount = $changed
    transformedFunctions = $changedFunctions.ToArray()
}
if (-not [string]::IsNullOrWhiteSpace($ReportFile)) {
    $reportPath = [IO.Path]::GetFullPath($ReportFile)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($reportPath)) | Out-Null
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
}
Write-Host ("DHE guards: {0}/{1}; output: {2}" -f $changed, $methods.Count, $outputPath)
exit 0
