[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]]$AssemblyPaths,

    [Parameter(Mandatory = $true)]
    [string]$Output,

    [string]$DnlibPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "dhe-workflow-common.ps1")

function Resolve-DnlibPath {
    return Resolve-DheDnlibPath -RequestedPath $DnlibPath -LabRoot (Split-Path -Parent $PSScriptRoot)
}

function New-Counters {
    return [ordered]@{
        methodCount = 0
        methodBodyCount = 0
        staticMethodCount = 0
        instanceMethodCount = 0
        virtualMethodCount = 0
        abstractMethodCount = 0
        genericMethodCount = 0
        genericDeclaringTypeMethodCount = 0
        asyncMethodCount = 0
        pinvokeMethodCount = 0
        valueTypeMethodCount = 0
        delegateTypeCount = 0
        valueTypeCount = 0
        referenceTypeCount = 0
        nestedTypeCount = 0
        interfaceTypeCount = 0
        fieldCount = 0
        opcodeCounts = @{}
    }
}

function Add-Opcode([System.Collections.IDictionary]$Counts, [string]$Opcode) {
    if ([string]::IsNullOrWhiteSpace($Opcode)) { return }
    if (-not $Counts.ContainsKey($Opcode)) { $Counts[$Opcode] = 0 }
    $Counts[$Opcode]++
}

function Visit-Type($Type, [System.Collections.IDictionary]$Counters, [System.Collections.Generic.List[object]]$Samples) {
    if ($Type.Name -eq "<Module>") { return }
    if ($Type.NestedTypes.Count -gt 0) { $Counters.nestedTypeCount += $Type.NestedTypes.Count }
    if ($Type.IsInterface) { $Counters.interfaceTypeCount++ }
    if ($Type.IsValueType) { $Counters.valueTypeCount++ } else { $Counters.referenceTypeCount++ }
    if ($Type.IsValueType) { $Counters.fieldCount += $Type.Fields.Count }
    if ($null -ne $Type.BaseType -and
        ([string]$Type.BaseType.FullName -eq "System.MulticastDelegate" -or
         [string]$Type.BaseType.FullName -eq "System.Delegate")) {
        $Counters.delegateTypeCount++
    }

    foreach ($Method in $Type.Methods) {
        $Counters.methodCount++
        if ($Method.HasBody) { $Counters.methodBodyCount++ }
        if ($Method.IsStatic) { $Counters.staticMethodCount++ } else { $Counters.instanceMethodCount++ }
        if ($Method.IsVirtual) { $Counters.virtualMethodCount++ }
        if ($Method.IsAbstract) { $Counters.abstractMethodCount++ }
        if ($Method.GenericParameters.Count -gt 0) { $Counters.genericMethodCount++ }
        if ($Type.GenericParameters.Count -gt 0) { $Counters.genericDeclaringTypeMethodCount++ }
        if ($Type.IsValueType) { $Counters.valueTypeMethodCount++ }
        if ($Method.IsPinvokeImpl) { $Counters.pinvokeMethodCount++ }
        $isAsync = @($Method.CustomAttributes | Where-Object {
            [string]$_.TypeFullName -eq "System.Runtime.CompilerServices.AsyncStateMachineAttribute"
        }).Count -gt 0
        if ($isAsync) { $Counters.asyncMethodCount++ }

        if ($Method.HasBody) {
            foreach ($Instruction in $Method.Body.Instructions) {
                Add-Opcode $Counters.opcodeCounts ([string]$Instruction.OpCode.Code)
            }
        }

        if ($Samples.Count -lt 20 -and ($Method.IsVirtual -or $Method.GenericParameters.Count -gt 0 -or
            $isAsync -or $Method.IsPinvokeImpl -or $Type.IsValueType)) {
            $Samples.Add([ordered]@{
                declaringType = [string]$Type.FullName
                method = [string]$Method.Name
                token = [uint32]$Method.MDToken.Raw
                virtual = [bool]$Method.IsVirtual
                generic = [bool]($Method.GenericParameters.Count -gt 0 -or $Type.GenericParameters.Count -gt 0)
                async = $isAsync
                pinvoke = [bool]$Method.IsPinvokeImpl
                valueType = [bool]$Type.IsValueType
            })
        }
    }

    foreach ($Nested in $Type.NestedTypes) {
        Visit-Type $Nested $Counters $Samples
    }
}

$dnlib = Resolve-DnlibPath
if (-not [IO.File]::Exists($dnlib)) { throw "dnlib.dll was not found: $dnlib" }
Add-Type -Path $dnlib

$reports = New-Object System.Collections.Generic.List[object]
foreach ($inputPath in $AssemblyPaths) {
    $path = [IO.Path]::GetFullPath($inputPath)
    if (-not [IO.File]::Exists($path)) { throw "Assembly was not found: $path" }
    $module = [dnlib.DotNet.ModuleDefMD]::Load($path)
    try {
        $counters = New-Counters
        $samples = New-Object System.Collections.Generic.List[object]
        foreach ($Type in $module.Types) { Visit-Type $Type $counters $samples }
        $reports.Add([ordered]@{
            assemblyName = [string]$module.Assembly.Name.String
            path = $path
            mvid = [string]$module.Mvid
            counters = $counters
            samples = $samples.ToArray()
        })
    }
    finally { $module.Dispose() }
}

$all = New-Counters
foreach ($report in $reports) {
    $counterKeys = @($all.Keys)
    foreach ($key in $counterKeys) {
        if ($key -eq "opcodeCounts") { continue }
        $all[$key] += [int]$report.counters[$key]
    }
    foreach ($opcode in $report.counters.opcodeCounts.Keys) {
        if (-not $all.opcodeCounts.ContainsKey($opcode)) {
            $all.opcodeCounts[$opcode] = 0
        }
        $all.opcodeCounts[$opcode] += [int]$report.counters.opcodeCounts[$opcode]
    }
}

$outputPath = [IO.Path]::GetFullPath($Output)
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($outputPath)) | Out-Null
$result = [ordered]@{
    schemaVersion = 1
    format = "hybridclr.dhe-capability-report.json"
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    assemblies = $reports.ToArray()
    aggregate = $all
}
[IO.File]::WriteAllText($outputPath, ($result | ConvertTo-Json -Depth 12), (New-Object Text.UTF8Encoding($false)))
Write-Host "DHE capability report: $outputPath"
exit 0
