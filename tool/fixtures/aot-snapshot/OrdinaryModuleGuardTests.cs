using System.Text.Json;
using HybridCLR.DheTool;
using HybridCLR.Editor.Commands;

internal static class OrdinaryModuleGuardTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("ordinary-module-guards <Base proof> <new output>");
        string output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Guard output must be new.");
        Directory.CreateDirectory(output);
        string build = Path.Combine(args[0], "base");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(build, "build-identity.json")));
        string sourceRoot = Path.Combine(Path.GetDirectoryName(Path.Combine(build,
            identity.GetProperty("aotAnalysisSnapshot").GetString()!))!, "assemblies");
        string[] mutable = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        var inventory = DheOrdinaryGuardInventory.Generate(sourceRoot, mutable, "HybridCLR.Lab.Snapshot.DheBuildIdentity",
            Path.Combine(output, "requests"), value => JsonSerializer.Serialize(value, value.GetType(), options));
        const string nativeName = "HybridCLR.ValueLayoutNative";
        var expected = MetaVersionSnapshot.Create(Path.Combine(sourceRoot, nativeName + ".dll"));
        var request = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(output, "requests", nativeName + ".mv.json")));
        string Key(uint token, string id) => token + ":" + id.ToUpperInvariant();
        var actual = request.GetProperty("methods").EnumerateArray().Select(row =>
            Key(row.GetProperty("token").GetUInt32(), row.GetProperty("stableId").GetString()!)).ToHashSet();
        var checks = new Dictionary<string, bool>
        {
            ["ordinary-module-fixture-present"] = expected.Methods.Any(method => method.DeclaringType == "<Module>" && method.Name == ".cctor"),
            ["all-ordinary-module-methods-requested"] = expected.Methods.Where(method => method.DeclaringType == "<Module>")
                .All(method => actual.Contains(Key(method.Token, method.StableId))),
            ["exact-final-native-method-set"] = actual.SetEquals(expected.Methods.Select(method => Key(method.Token, method.StableId))),
            ["mutable-requests-excluded"] = !mutable.Any(name => File.Exists(Path.Combine(output, "requests", name + ".mv.json"))),
        };
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks,
            sourceRoot, inventory.ManifestPath, scope = "Real ordinary module in archived Base compared against independent MV metadata; no Player mutation" }, options));
        Console.WriteLine("Ordinary module guards: " + passed); return passed ? 0 : 1;
    }
}
