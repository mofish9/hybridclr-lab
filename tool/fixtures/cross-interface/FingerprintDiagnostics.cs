using System.Reflection;
using System.Security.Cryptography;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class FingerprintDiagnostics
{
    // Recompute only in memory. Archived DLLs and MVs must keep their original bytes.
    public static object Analyze(string baselinePath, string currentPath,
        MetaVersionSnapshot baseline, MetaVersionSnapshot current)
    {
        using var previous = ModuleDefMD.Load(baselinePath);
        using var next = ModuleDefMD.Load(currentPath);
        var oldFields = previous.GetTypes().SelectMany(type => type.Fields)
            .ToDictionary(field => field.FullName);
        var relocated = new List<object>();
        foreach (FieldDef field in next.GetTypes().SelectMany(type => type.Fields))
        {
            if (!oldFields.TryGetValue(field.FullName, out var old) || old.RVA == field.RVA)
                continue;
            // dnlib reads RVA-backed data lazily; materialize it before changing the address.
            byte[] data = field.InitialValue;
            bool equal = data.SequenceEqual(old.InitialValue);
            relocated.Add(new { field = field.FullName, baselineRva = (uint)old.RVA,
                currentRva = (uint)field.RVA, sameData = equal, dataSize = data.Length,
                dataSha256 = Convert.ToHexString(SHA256.HashData(data)) });
            if (equal) field.RVA = old.RVA;
        }
        var typeRecords = next.GetTypes().Where(type => type.Name != "<Module>")
            .Select(type => Invoke<MetaVersionType>("CreateType", type)).ToArray();
        var typeIds = typeRecords.ToDictionary(type => type.Identity, type => type.StableId);
        var typeVersions = typeRecords.ToDictionary(type => type.Identity, type => type.Version);
        var fieldVersions = next.GetTypes().Where(type => type.Name != "<Module>")
            .SelectMany(type => type.Fields.Select((field, index) =>
                Invoke<MetaVersionField>("CreateField", field, typeIds[type.FullName], index, false)))
            .ToDictionary(field => field.Identity, field => field.Version);
        var normalized = next.GetTypes().SelectMany(type => type.Methods)
            .Select(method => Invoke<MetaVersionMethod>("CreateMethod", method,
                typeIds[method.DeclaringType.FullName], typeVersions, fieldVersions))
            .ToDictionary(method => method.StableId);
        var currentMethods = current.Methods.ToDictionary(method => method.StableId);
        var cases = baseline.Methods.Where(method =>
            method.ReturnType == "HybridCLR.Lab.ManagedCases.CaseObservation" && method.ParameterTypes.Length == 0)
            .Select(method => new
            {
                method.Identity, method.StableId,
                versionEqual = method.Version == currentMethods[method.StableId].Version,
                bodyEqual = method.BodyVersion == currentMethods[method.StableId].BodyVersion,
                metadataEqual = method.MetadataVersion == currentMethods[method.StableId].MetadataVersion,
                dependencyEqual = method.DependencyVersion == currentMethods[method.StableId].DependencyVersion,
                versionEqualAfterRvaOnlyNormalization = method.Version == normalized[method.StableId].Version,
            }).ToArray();
        return new { caseCount = cases.Length, changedCount = cases.Count(item => !item.versionEqual),
            allBodiesEqual = cases.All(item => item.bodyEqual), allMetadataEqual = cases.All(item => item.metadataEqual),
            allVersionsEqualAfterRvaOnlyNormalization = cases.All(item => item.versionEqualAfterRvaOnlyNormalization),
            relocatedFields = relocated, changedCases = cases.Where(item => !item.versionEqual) };
    }

    private static T Invoke<T>(string name, params object[] arguments) =>
        (T)(typeof(MetaVersionSnapshot).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, arguments) ?? throw new InvalidOperationException(name));
}
