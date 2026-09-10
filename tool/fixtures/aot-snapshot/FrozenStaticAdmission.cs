using System.Text.Json;
using HybridCLR.DheTool;
using dnlib.DotNet;

internal static class FrozenStaticAdmission
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4 || (args[3] != "reject" && args[3] != "accept"))
            throw new ArgumentException("frozen-static-admission <Base proof> <Current root> <new output> <reject|accept>");
        string proof = Path.GetFullPath(args[0]), current = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string identityPath = Path.Combine(proof, "base/build-identity.json"); var identity = Read(identityPath);
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string[] before = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("baselinePath").GetString()!).ToArray();
        string[] after = Directory.GetFiles(current, "*.dll");
        var execution = ResourceExecutionPlanner.Compile(before, after, snapshot.OrdinaryAssemblyPaths);
        var frozen = FrozenAotAdaptation.Compile(snapshot, before, after, execution.Impact);
        var native = Read(Path.Combine(proof, "base/native/dhe-native-manifest.json"));
        var valid = FrozenAotAdmission.Validate(snapshot, frozen, execution, native);
        const string name = "HybridCLR.ValueLayoutNative";
        var selected = frozen.Assemblies.Single(row => row.AssemblyName == name);
        var checks = new Dictionary<string, bool>
        {
            ["six-ordinary-static-fields-detected"] = execution.Impact.StaticValueFields.Count(field => field.OrdinaryAot && field.AssemblyName == name) == 6,
            ["six-frozen-static-fields-selected"] = selected.StaticValueFieldTokens.Length == 6,
            ["complete-ordinary-guard-coverage"] = valid.MissingGuards.Length == 0 && valid.OrdinaryAssemblyCount == 40,
            ["expected-initial-admission"] = args[3] == "accept" ? valid.UnsupportedChanges.Length == 0 :
                valid.UnsupportedChanges.Count(error => error.StartsWith("current-storage-ordinary-aot-static-field:")) == 6,
        };
        if (args[3] == "accept")
        {
            FrozenAotCompilation Replace(FrozenAotAssemblyPlan plan) => frozen with
            { Assemblies = frozen.Assemblies.Select(row => row.AssemblyName == name ? plan : row).ToArray() };
            bool Rejected(FrozenAotAssemblyPlan plan, string expected) => FrozenAotAdmission.Validate(snapshot, Replace(plan), execution, native)
                .UnsupportedChanges.Any(error => error.StartsWith(expected));
            checks["missing-storage-selection-rejected"] = Rejected(selected with { StaticValueFieldTokens = Array.Empty<uint>() },
                "current-storage-ordinary-aot-static-field:");
            using var module = ModuleDefMD.Load(snapshot.Assemblies.Single(row => row.AssemblyName == name).Path);
            var owner = module.Find("HybridCLR.Lab.ValueLayoutNative.NativeStaticOwner", false)!;
            foreach (string methodName in new[] { ".cctor", "Get", "Set", "Address", "Clear" })
            {
                uint token = owner.Methods.Single(method => method.Name == methodName).MDToken.Raw;
                uint[] tokens = selected.ExecutionPlan.CurrentExecutionMethodTokens.Where(value => value != token).ToArray();
                var plan = selected with { ExecutionPlan = selected.ExecutionPlan with {
                    CurrentExecutionMethodTokens = tokens, CurrentExecutionMethodTokenCount = tokens.Length },
                    Methods = selected.Methods.Where(method => method.Token != token).ToArray() };
                checks["unselected-" + methodName + "-rejected"] = Rejected(plan, "current-storage-ordinary-aot-static-field:");
            }
        }
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, admission = valid, frozen, execution.UnsupportedChanges,
            scope = "Real captured ordinary AOT fields, initializer/read/write selection and complete guard coverage; separate Player gate required" },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Frozen static admission: " + passed);
        return passed ? 0 : 1;
    }
}
