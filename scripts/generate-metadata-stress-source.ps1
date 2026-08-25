param(
    [string]$LabRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$Policy = "manifests/metadata-benchmark-policy.json"
)

$ErrorActionPreference = "Stop"
$LabRoot = [IO.Path]::GetFullPath($LabRoot)
$policyPath = if ([IO.Path]::IsPathRooted($Policy)) { $Policy } else { Join-Path $LabRoot $Policy }
$policyData = Get-Content -Raw $policyPath | ConvertFrom-Json
$stress = $policyData.stressAssembly
$output = Join-Path $LabRoot "managed-cases/HybridCLR.MetadataStress/Generated/MetadataStress.Generated.cs"

foreach ($name in @("typeCount", "methodsPerType", "fieldsPerType", "propertiesPerType")) {
    if ([int]$stress.$name -lt 1) { throw "$name must be at least 1." }
}

$builder = [Text.StringBuilder]::new(4MB)
[void]$builder.AppendLine("using System;")
[void]$builder.AppendLine("namespace HybridCLR.Lab.MetadataStress")
[void]$builder.AppendLine("{")
[void]$builder.AppendLine("    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]")
[void]$builder.AppendLine("    public sealed class StressTagAttribute : Attribute")
[void]$builder.AppendLine("    {")
[void]$builder.AppendLine("        public StressTagAttribute(int id, string name) { Id = id; Name = name; }")
[void]$builder.AppendLine("        public int Id { get; }")
[void]$builder.AppendLine("        public string Name { get; }")
[void]$builder.AppendLine("    }")
[void]$builder.AppendLine("    public interface IStressContract<T> { T Transform(T value); }")
[void]$builder.AppendLine("    public static class MetadataStressEntry")
[void]$builder.AppendLine("    {")
[void]$builder.AppendLine("        public static long Touch()")
[void]$builder.AppendLine("        {")
[void]$builder.AppendLine("            long checksum = 0;")
for ($typeIndex = 0; $typeIndex -lt [int]$stress.typeCount; $typeIndex += 16) {
    $typeName = "StressType{0:D4}" -f $typeIndex
    [void]$builder.AppendLine("            checksum += new $typeName().Method00($typeIndex + 1);")
    [void]$builder.AppendLine("            checksum += new $typeName.Nested<int>($typeIndex).Value;")
}
[void]$builder.AppendLine("            return checksum;")
[void]$builder.AppendLine("        }")
[void]$builder.AppendLine("    }")

for ($typeIndex = 0; $typeIndex -lt [int]$stress.typeCount; $typeIndex++) {
    $typeName = "StressType{0:D4}" -f $typeIndex
    [void]$builder.AppendLine("    [StressTag($typeIndex, `"$typeName`")]")
    [void]$builder.AppendLine("    public sealed class $typeName : IStressContract<int>")
    [void]$builder.AppendLine("    {")
    for ($fieldIndex = 0; $fieldIndex -lt [int]$stress.fieldsPerType; $fieldIndex++) {
        [void]$builder.AppendLine(("        public long Field{0:D2};" -f $fieldIndex))
    }
    for ($propertyIndex = 0; $propertyIndex -lt [int]$stress.propertiesPerType; $propertyIndex++) {
        [void]$builder.AppendLine(("        public int Property{0:D2} {{ get; set; }}" -f $propertyIndex))
    }
    [void]$builder.AppendLine("        public int Transform(int value) { return value + $typeIndex; }")
    [void]$builder.AppendLine("        public T Echo<T>(T value) { return value; }")
    for ($methodIndex = 0; $methodIndex -lt [int]$stress.methodsPerType; $methodIndex++) {
        $tagId = $typeIndex * [int]$stress.methodsPerType + $methodIndex
        [void]$builder.AppendLine(("        [StressTag({0}, `"M{1:D2}`")]" -f $tagId, $methodIndex))
        [void]$builder.AppendLine(("        public int Method{0:D2}(int value) {{ return value + {1} + {0}; }}" -f $methodIndex, $typeIndex))
    }
    [void]$builder.AppendLine("        public sealed class Nested<T>")
    [void]$builder.AppendLine("        {")
    [void]$builder.AppendLine("            public Nested(T value) { Value = value; }")
    [void]$builder.AppendLine("            public T Value { get; }")
    [void]$builder.AppendLine("        }")
    [void]$builder.AppendLine("    }")
}
[void]$builder.AppendLine("}")

New-Item -ItemType Directory -Force -Path (Split-Path $output) | Out-Null
[IO.File]::WriteAllText($output, $builder.ToString(), [Text.UTF8Encoding]::new($false))
Write-Host "Metadata stress source: $output"
Write-Host "Types: $($stress.typeCount), methods/type: $($stress.methodsPerType), fields/type: $($stress.fieldsPerType), properties/type: $($stress.propertiesPerType)"
