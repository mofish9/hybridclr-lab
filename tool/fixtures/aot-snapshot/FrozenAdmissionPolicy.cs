using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class FrozenAdmissionPolicy
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("frozen-admission-policy <frozen entry proof> <new output>");
        string proof = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        string build = Path.Combine(proof, "base"), identityPath = Path.Combine(build, "build-identity.json");
        var identity = Read(identityPath);
        string[] names = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!), names)!;
        string[] before = names.Select(name => Path.Combine(build, "baseline", name + ".dll")).ToArray();
        string[] current = names.Select(name => Path.Combine(proof, "frozen-entry-current", name + ".dll")).ToArray();
        var execution = ResourceExecutionPlanner.Compile(before, current, snapshot.OrdinaryAssemblyPaths);
        var frozen = FrozenAotAdaptation.Compile(snapshot, before, current);
        string nativeFile = Path.Combine(build, "native/dhe-native-manifest.json");
        var native = Read(nativeFile);
        var checks = new Dictionary<string, bool>();
        var reports = new Dictionary<string, FrozenAotAdmissionProof>();
        FrozenAotAdmissionProof Check(string name, FrozenAotCompilation plan, ResourceExecutionCompilation exec, JsonElement manifest)
        {
            Console.WriteLine("Checking " + name);
            return reports[name] = FrozenAotAdmission.Validate(snapshot, plan, exec, manifest);
        }
        var valid = Check("complete-coverage", frozen, execution, native);
        checks["complete-ordinary-inventory"] = valid.OrdinaryAssemblyCount == snapshot.OrdinaryAssemblyPaths.Length &&
            valid.ExecutableMethodCount > 49000 && valid.MissingGuards.Length == 0;
        checks["exact-layout-and-method-obligations-discharged"] = execution.UnsupportedChanges.Length == 8 &&
            valid.DischargedChanges.SequenceEqual(execution.UnsupportedChanges.OrderBy(value => value, StringComparer.Ordinal)) && valid.UnsupportedChanges.Length == 0;
        const string nativeName = "HybridCLR.ValueLayoutNative";
        var sentinel = native.GetProperty("methods").EnumerateArray().Single(row =>
            row.GetProperty("assemblyName").GetString() == nativeName && row.GetProperty("methodName").GetString() == "FrozenSentinel");
        uint sentinelToken = sentinel.GetProperty("methodToken").GetUInt32();
        JsonElement WithoutSentinel(string absentReason) => JsonSerializer.SerializeToElement(new {
            methods = native.GetProperty("methods").EnumerateArray().Where(row =>
                row.GetProperty("assemblyName").GetString() != nativeName || row.GetProperty("methodToken").GetUInt32() != sentinelToken).ToArray(),
            interpreterOnlyMethods = native.GetProperty("interpreterOnlyMethods").EnumerateArray().Concat(absentReason == null ? Array.Empty<JsonElement>() :
                new[] { JsonSerializer.SerializeToElement(new { assemblyName = nativeName, methodToken = sentinelToken,
                    managedId = sentinel.GetProperty("managedId").GetString(), stableMethodIdSha256 = sentinel.GetProperty("stableMethodIdSha256").GetString(),
                    reasons = new[] { absentReason } }) }).ToArray() });
        var missing = Check("unaffected-method-missing", frozen, execution, WithoutSentinel(null));
        checks["unaffected-method-still-requires-guard"] = missing.MissingGuards.Length == 1 && missing.DischargedChanges.Length == 0;
        checks["unsupported-native-entry-is-not-proven-absent"] = Check("unsupported-native-entry", frozen, execution,
            WithoutSentinel("unsupported ABI signature")).MissingGuards.Length == 1;
        checks["explicit-absent-entry-accepted"] = Check("proven-absent-entry", frozen, execution,
            WithoutSentinel("no generated native entry")).UnsupportedChanges.Length == 0;
        var omitted = frozen with { Assemblies = frozen.Assemblies.Where(source => source.AssemblyName != nativeName).ToArray() };
        checks["missing-frozen-source-retains-native-obligations"] = Check("missing-source", omitted, execution, native).UnsupportedChanges
            .Any(reason => reason.StartsWith("current-storage-native-abi:" + nativeName));
        var noStorage = frozen with { Assemblies = frozen.Assemblies.Select(source => source.AssemblyName == nativeName ? source with {
            ExecutionPlan = source.ExecutionPlan with { CurrentStorageTypeTokens = Array.Empty<uint>(), CurrentStorageTypeTokenCount = 0 } } : source).ToArray() };
        checks["missing-physical-owner-selection-rejected"] = Check("missing-physical-owner", noStorage, execution, native).UnsupportedChanges
            .Any(reason => reason.StartsWith("current-storage-ordinary-aot-layout:"));
        string[] retained = { "current-storage-ordinary-aot-static-field:fixture", "current-storage-thread-static-value-field:fixture",
            "current-storage-rva-static-value-field:fixture", "current-storage-native-member:fixture" };
        var constrained = execution with { UnsupportedChanges = execution.UnsupportedChanges.Concat(retained).ToArray() };
        checks["unimplemented-storage-and-native-member-gates-retained"] =
            Check("unresolved-storage", frozen, constrained, native).UnsupportedChanges.SequenceEqual(retained.OrderBy(value => value, StringComparer.Ordinal));
        var nativeOnly = frozen with { Obligations = frozen.Obligations.Concat(new[] {
            new FrozenAotObligation("native-only-abi", nativeName, sentinelToken, "fixture-native-only") }).ToArray() };
        checks["native-only-obligation-remains-closed"] = Check("native-only", nativeOnly, execution, native).UnsupportedChanges
            .Contains("frozen-aot-native-only-abi:" + nativeName + ":fixture-native-only");
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, reports,
            baseId = identity.GetProperty("baseId").GetString(), snapshot.Sha256,
            nativeManifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(nativeFile))),
            currentAssemblySetSha256 = FrozenAotSourcePlan.CurrentSetHash(current),
            scope = "Compiler admission policy using immutable real Base artifacts; Player loading is a separate gate" },
            new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        Console.WriteLine("Frozen admission: " + passed + "; checks=" + checks.Count);
        return passed ? 0 : 1;
    }
}
