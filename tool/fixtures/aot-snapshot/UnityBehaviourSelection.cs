using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class UnityBehaviourSelection
{
    internal static string Read(string proof, string resource)
    {
        JsonElement ReadJson(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string model = Path.Combine(resource, "current/HybridCLR.ValueLayoutModel.dll");
        var current = MetaVersionSnapshot.Create(model);
        var baseline = MetaVersionSnapshot.Create(Path.Combine(proof, "base/baseline/HybridCLR.ValueLayoutModel.dll"));
        string baseId = ReadJson(Path.Combine(proof, "base/build-identity.json")).GetProperty("baseId").GetString()!;
        var manifest = ReadJson(Path.Combine(resource, "resource/dhe-resource-update.json"));
        var declared = manifest.GetProperty("assemblies").EnumerateArray().Single(row => row.GetProperty("assemblyName").GetString() == current.AssemblyName);
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))), declared.GetProperty("dllSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Lifecycle expectation does not match the bound Current DLL.");
        var selectedBase = manifest.GetProperty("supportedBases").EnumerateArray().Single(row => row.GetProperty("baseId").GetString() == baseId);
        var mode = selectedBase.GetProperty("assemblyModes").EnumerateArray().Single(row => row.GetProperty("assemblyName").GetString() == current.AssemblyName);
        var tokens = mode.TryGetProperty("executionPlans", out var plans)
            ? plans.EnumerateArray().SelectMany(plan => plan.GetProperty("currentExecutionMethodTokens").EnumerateArray().Select(token => token.GetUInt32())).ToHashSet()
            : new HashSet<uint>();
        return string.Join(",", new[] { "Awake", "ReadUnchanged" }.Select(name => {
            var method = current.Methods.Single(row => row.DeclaringType == "HybridCLR.Lab.UnityCases.EvolvingBehaviour" && row.Name == name);
            var before = baseline.Methods.Single(row => row.StableId == method.StableId);
            return (tokens.Contains(method.Token) || method.Version != before.Version).ToString();
        }));
    }
}
