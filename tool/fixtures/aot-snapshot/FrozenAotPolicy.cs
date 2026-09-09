using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using HybridCLR.DheTool;
using HybridCLR.Editor.Commands;

internal static class FrozenAotPolicy
{
    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("frozen-aot-policy <Base build identity> <actual Current DLL root> <new output>");
        string identityPath = Path.GetFullPath(args[0]), currentRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(identityPath));
        string[] names = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()).ToArray();
        string[] allNames = identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity, allNames, names) ?? throw new InvalidDataException("Snapshot is required.");
        string[] before = names.Select(name => Path.Combine(Path.GetDirectoryName(identityPath), "baseline", name + ".dll")).ToArray();
        string[] current = names.Select(name => Path.Combine(currentRoot, name + ".dll")).ToArray();
        var checks = new Dictionary<string, bool>();
        var errors = new Dictionary<string, string>();
        void Reject(string name, Action action)
        {
            try { action(); checks[name] = false; }
            catch (InvalidDataException error) { checks[name] = true; errors[name] = error.Message; }
        }
        void Write(string path, object value) => File.WriteAllText(Path.Combine(output, path), JsonSerializer.Serialize(value, Json));
        void Mutate(string original, string destination, Action<ModuleDefMD> edit)
        {
            using var module = ModuleDefMD.Load(File.ReadAllBytes(original));
            edit(module); module.Write(destination, new ModuleWriterOptions(module) { MetadataOptions = { Flags = MetadataFlags.PreserveAll } });
        }
        Console.WriteLine("Check real snapshot and static-storage checkpoint.");
        var stable = FrozenAotAdaptation.Compile(snapshot, before, current);
        checks["full-real-snapshot-read"] = snapshot.Assemblies.Length == allNames.Length && snapshot.OrdinaryAssemblyPaths.Length == allNames.Length - names.Length;
        checks["static-checkpoint-needs-no-frozen-selection"] = stable.Assemblies.Length == 0;
        checks["snapshot-identity-is-excluded"] = snapshot.Assemblies.Count(source => source.ExcludedTypeTokens.Length != 0) == 1 &&
            snapshot.Assemblies.Where(source => source.ExcludedTypeTokens.Length != 0).All(source => !source.Dhe);
        checks["existing-hotfix-mv-bytes-unchanged"] = identity.GetProperty("assemblies").EnumerateArray().All(row =>
            Hash(MetaVersionSnapshot.Create(Path.Combine(Path.GetDirectoryName(identityPath), "baseline", row.GetProperty("assemblyName").GetString() + ".dll")).ToBinary())
                .Equals(row.GetProperty("baseMetaVersionSha256").GetString(), StringComparison.OrdinalIgnoreCase));
        // Exercise duplicate references in the actual retained System inventory.
        var system = snapshot.Assemblies.Single(source => source.AssemblyName == "System");
        var systemMv = MetaVersionSnapshot.Create(system.Path);
        checks["real-ordinary-duplicate-references-supported"] = systemMv.AssemblySha256.Length == 64 &&
            systemMv.AssemblyReferences.Count != 0;
        using (var module = ModuleDefMD.Load(system.Path))
            checks["all-framework-reference-identities-preserved"] = module.GetAssemblyRefs().GroupBy(reference => reference.Name.String).All(group =>
                systemMv.AssemblyReferences[group.Key].Split('\n').ToHashSet().SetEquals(group.Select(reference => reference.FullName)));
        string model = names.Single(name => name == "HybridCLR.ValueLayoutModel");
        string grown = Path.Combine(output, model + ".dll");
        Mutate(Path.Combine(currentRoot, model + ".dll"), grown, module =>
            module.GetTypes().Single(type => type.Name == "Payload").Fields.Add(new FieldDefUser("FrozenAotAddedReference",
                new FieldSig(module.CorLibTypes.Object), FieldAttributes.Public)));
        string[] evolved = current.Select(path => Path.GetFileNameWithoutExtension(path) == model ? grown : path).ToArray();
        Console.WriteLine("Compile original ordinary AOT methods against a grown hotfix value.");
        var plan = FrozenAotAdaptation.Compile(snapshot, before, evolved);
        var ordinary = plan.Assemblies.Single(row => row.AssemblyName == "HybridCLR.ValueLayoutNative");
        checks["ordinary-original-echo-selected"] = ordinary.Methods.Any(method => method.Identity.Contains("NativeBoundary::Echo"));
        checks["ordinary-inline-owner-selected"] = ordinary.ExecutionPlan.CurrentStorageTypeTokens.Length != 0;
        checks["ordinary-unchanged-native-neighbor-preserved"] = ordinary.Methods.All(method => !method.Identity.Contains("StaticNeighbor"));
        checks["source-and-mv-bound-to-base"] = plan.Assemblies.All(row => row.SnapshotSha256 == snapshot.Sha256 &&
            row.SourceDllSha256 == snapshot.Assemblies.Single(source => source.AssemblyName == row.AssemblyName).Sha256 &&
            row.SourceMetaVersionSha256 == row.ExecutionPlan.BaseMetaVersionSha256 && row.SourceMetaVersionSha256 == row.ExecutionPlan.CurrentMetaVersionSha256);
        checks["all-selected-methods-require-native-guards"] = plan.Assemblies.All(row => row.Methods.All(method =>
            plan.Obligations.Any(item => item.Kind == "base-native-guard-required" && item.AssemblyName == row.AssemblyName && item.Token == method.Token)));
        checks["generic-context-work-is-explicit"] = plan.Obligations.Any(item => item.Kind == "generic-context-analysis");
        var admission = ResourceExecutionPlanner.Compile(before, evolved, snapshot.OrdinaryAssemblyPaths);
        checks["resource-native-abi-gate-not-bypassed"] = admission.UnsupportedChanges.Any(reason => reason.StartsWith("current-storage-native-abi:"));
        Reject("incomplete-hotfix-set-rejected", () => FrozenAotAdaptation.Compile(snapshot, before.Skip(1), evolved));
        Reject("wrong-hotfix-base-source-rejected", () => FrozenAotAdaptation.Compile(snapshot, evolved, evolved));
        Write("plan-real-base-grown-current.json", plan);

        // Additional policy fixtures: keep real stripped assemblies, but capture
        // an explicitly synthetic Base with static/native-only edge declarations.
        string synthetic = Path.Combine(output, "synthetic-base"); Directory.CreateDirectory(synthetic);
        foreach (var source in snapshot.Assemblies) File.Copy(source.Path, Path.Combine(synthetic, source.AssemblyName + ".dll"));
        string native = Path.Combine(synthetic, "HybridCLR.ValueLayoutNative.dll");
        Mutate(native, native, module =>
        {
            var owner = module.GetTypes().Single(type => type.Name == "NativeBoundary");
            var payload = module.GetTypes().Single(type => type.Name == "NativeInlineOwner").Fields.Single(field => field.Name == "Value").FieldType;
            var field = new FieldDefUser("FrozenStaticValue", new FieldSig(payload), FieldAttributes.Public | FieldAttributes.Static);
            owner.Fields.Add(field);
            var getter = new MethodDefUser("FrozenStaticCopy", MethodSig.CreateStatic(payload), MethodImplAttributes.IL | MethodImplAttributes.Managed,
                MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
            getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, field)); getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            owner.Methods.Add(getter);
            owner.Methods.Add(new MethodDefUser("FrozenInternalEcho", MethodSig.CreateStatic(payload, payload),
                MethodImplAttributes.InternalCall | MethodImplAttributes.Runtime, MethodAttributes.Public | MethodAttributes.Static));
        });
        var nativeSource = snapshot.Assemblies.Single(source => source.AssemblyName == "HybridCLR.ValueLayoutNative");
        var syntheticNativePath = Path.Combine(output, "synthetic-native.dll");
        File.Copy(native, syntheticNativePath);
        string syntheticManifestPath = Path.Combine(output, "synthetic-manifest.json");
        File.Copy(snapshot.ManifestPath, syntheticManifestPath);
        var syntheticSnapshot = snapshot with
        {
            ManifestPath = syntheticManifestPath,
            Assemblies = snapshot.Assemblies.Select(source => source.AssemblyName == nativeSource.AssemblyName
                ? source with { Path = syntheticNativePath, Sha256 = Hash(File.ReadAllBytes(syntheticNativePath)) }
                : source).ToArray()
        };
        Console.WriteLine("Check synthetic ordinary static storage and internal-call boundaries.");
        var edges = FrozenAotAdaptation.Compile(syntheticSnapshot, before, evolved);
        var edgeNative = edges.Assemblies.Single(row => row.AssemblyName == "HybridCLR.ValueLayoutNative");
        checks["ordinary-static-field-selected"] = edgeNative.StaticValueFieldTokens.Length == 1 && edgeNative.Methods.Any(method => method.Identity.Contains("FrozenStaticCopy"));
        checks["internal-native-only-abi-remains-obligation"] = edges.Obligations.Any(item => item.Kind == "native-only-abi" && item.Identity.Contains("FrozenInternalEcho")) &&
            edgeNative.Methods.All(method => !method.Identity.Contains("FrozenInternalEcho"));
        checks["frozen-source-is-base-specific"] = edgeNative.SourceDllSha256 != ordinary.SourceDllSha256 && edgeNative.SourceMetaVersionSha256 != ordinary.SourceMetaVersionSha256;
        Write("plan-synthetic-native-edges.json", edges);
        // Later modification cannot silently change an already authenticated source.
        var capturedNative = syntheticSnapshot.Assemblies.Single(source => source.AssemblyName == "HybridCLR.ValueLayoutNative");
        File.WriteAllBytes(capturedNative.Path, File.ReadAllBytes(native).Concat(new byte[] { 0 }).ToArray());
        Reject("frozen-source-replacement-rejected", () => FrozenAotAdaptation.Compile(syntheticSnapshot, before, evolved));
        File.AppendAllText(syntheticSnapshot.ManifestPath, " ");
        Reject("frozen-manifest-replacement-rejected", () => FrozenAotAdaptation.Compile(syntheticSnapshot, before, evolved));
        string ambiguous = Path.Combine(output, "ambiguous.dll");
        Mutate(system.Path, ambiguous, module =>
        {
            var duplicate = module.GetAssemblyRefs().GroupBy(reference => reference.Name.String).First(group => group.Count() > 1).Last();
            duplicate.Version = new Version(99, 0, 0, 0);
        });
        var changedSystemMv = MetaVersionSnapshot.Create(ambiguous);
        checks["reference-retargeting-remains-visible"] = systemMv.AssemblyReferences.Any(entry =>
            changedSystemMv.AssemblyReferences[entry.Key] != entry.Value);
        checks["reference-retargeting-remains-rejected"] = ResourceUpdateCompatibility.Analyze(systemMv, changedSystemMv)
            .UnsupportedChanges.Any(reason => reason.StartsWith("existing-assembly-reference-identity-change:"));
        bool passed = checks.Values.All(value => value);
        string Git(string root)
        {
            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (string item in new[] { "-C", root, "rev-parse", "HEAD" }) start.ArgumentList.Add(item);
            using var process = Process.Start(start); string result = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Cannot bind source identity.");
            return result.Trim();
        }
        string lab = Path.GetFullPath("../../../../../..", AppContext.BaseDirectory);
        Write("result.json", new { passed, checks, errors, labHead = Git(lab),
            hostSha256 = Hash(File.ReadAllBytes(typeof(FrozenAotPolicy).Assembly.Location)), snapshot.Sha256,
            inputs = current.Select(path => new { path, sha256 = Hash(File.ReadAllBytes(path)) }),
            scope = "Source-bound compiler policy with real Unity snapshot and labelled synthetic layout changes; no frozen-source Player claim" });
        foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
        return passed ? 0 : 1;
    }
}
