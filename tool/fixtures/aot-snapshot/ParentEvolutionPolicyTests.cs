using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class ParentEvolutionPolicyTests
{
    private const string Model = "HybridCLR.ValueLayoutModel";
    private const string Owner = "HybridCLR.Lab.VirtualSignatures.Processor";
    private const string Middle = "HybridCLR.Lab.ParentEvolution.ProcessorMiddle";

    internal static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException(
            "parent-evolution-policy <old DLL root> <grown DLL root> <Current DLL root> <new-type Base DLL root> <archived Current MV root> <new output>");
        string[] roots = args.Take(5).Select(Path.GetFullPath).ToArray();
        string output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output)) throw new IOException("Policy output must be new.");
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>(); var analyses = new Dictionary<string, object>();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string[] Dlls(string root) => Directory.GetFiles(root, "*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) != "HybridCLR.ValueLayoutNative").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string native = Path.Combine(roots[0], "HybridCLR.ValueLayoutNative.dll"); files[native] = Hash(native);
        var currentFiles = Dlls(roots[2]);
        var current = currentFiles.Select(MetaVersionSnapshot.Create).ToDictionary(snapshot => snapshot.AssemblyName);
        ResourceUpdateCompatibility Analyze(string name, string beforeRoot, string afterRoot, bool select = true)
        {
            string[] beforeFiles = Dlls(beforeRoot), afterFiles = Dlls(afterRoot);
            foreach (string path in beforeFiles.Concat(afterFiles)) files[path] = Hash(path);
            var compilation = ResourceExecutionPlanner.Compile(beforeFiles, afterFiles, new[] { native });
            var before = beforeFiles.Select(MetaVersionSnapshot.Create).ToDictionary(snapshot => snapshot.AssemblyName);
            var after = afterFiles.Select(MetaVersionSnapshot.Create).ToDictionary(snapshot => snapshot.AssemblyName);
            compilation.Plans.TryGetValue(Model, out var plan);
            var types = after[Model].Types.Where(type => select && plan != null && plan.CurrentStorageTypeTokens.Contains(type.Token))
                .Select(type => type.StableId).ToArray();
            var result = ResourceUpdateCompatibility.Analyze(before[Model], after[Model], currentAssemblySet: after.Values,
                currentStorageTypes: types, currentExecutionMethodTokens: select ? plan?.CurrentExecutionMethodTokens : null,
                currentGenericContextMethodTokens: select ? plan?.CurrentGenericContextMethodTokens : null);
            analyses[name] = new { result.Compatible, result.UnsupportedChanges, result.RequiredRuntimeCapabilities, plan,
                plannerErrors = compilation.UnsupportedChanges };
            checks[name + ":planner"] = compilation.UnsupportedChanges.Length == 0;
            return result;
        }
        var inserted = Analyze("old-insertion", roots[0], roots[2]);
        checks["old-insertion-admitted"] = inserted.Compatible;
        checks["grown-insertion-admitted"] = Analyze("grown-insertion", roots[1], roots[2]).Compatible;
        checks["removal-planning-admitted"] = Analyze("removal", roots[2], roots[1]).Compatible;
        checks["new-type-control-admitted"] = Analyze("new-type", roots[3], roots[2]).Compatible;
        var noop = Analyze("no-op", roots[2], roots[2]);
        checks["no-op-keeps-methods"] = noop.Compatible && noop.ChangedMethodCount == 0;
        checks["missing-physical-selection-rejected"] = !Analyze("missing-selection", roots[1], roots[2], false).Compatible;
        foreach (string capability in new[] { "current-storage-execution-plan-array-v1",
            ResourceUpdateCompatibility.PhysicalInterfaceMapCapability, ResourceUpdateCompatibility.ReferenceVirtualInvocationCapability,
            ResourceUpdateCompatibility.VirtualSignatureFrameCapability })
        {
            checks[capability + ":required"] = inserted.RequiredRuntimeCapabilities.Contains(capability);
            checks[capability + ":missing-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), inserted.RequiredRuntimeCapabilities);
        }
        var grown = MetaVersionSnapshot.Create(Path.Combine(roots[1], Model + ".dll"));
        string ownerId = current[Model].Types.Single(type => type.Identity == Owner).StableId;
        foreach (string mutation in new[] { "external-root", "sealed-parent", "generic-parent", "generic-owner", "owner-flags", "packing", "interfaces", "parent-cycle", "missing-parent" })
        {
            string path = Path.Combine(output, mutation + ".dll");
            using (var module = ModuleDefMD.Load(Path.Combine(roots[2], Model + ".dll")))
            {
                var owner = module.Find(Owner, false)!; var middle = module.Find(Middle, false)!;
                switch (mutation)
                {
                    case "external-root": middle.BaseType = new TypeRefUser(module, "System", "Exception", module.CorLibTypes.AssemblyRef); break;
                    case "sealed-parent": middle.IsSealed = true; break;
                    case "generic-parent": middle.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T")); break;
                    case "generic-owner": owner.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T")); break;
                    case "owner-flags": owner.IsSealed = false; break;
                    case "packing": owner.ClassLayout = new ClassLayoutUser(4, 64); break;
                    case "interfaces": owner.Interfaces.Clear(); break;
                    case "parent-cycle": middle.BaseType = owner; break;
                    case "missing-parent": owner.BaseType = new TypeRefUser(module, "HybridCLR.Lab.ParentEvolution", "Missing", module); break;
                }
                module.Write(path);
            }
            var snapshot = MetaVersionSnapshot.Create(path);
            var result = ResourceUpdateCompatibility.Analyze(grown, snapshot,
                currentAssemblySet: current.Values.Where(value => value.AssemblyName != Model).Append(snapshot),
                currentStorageTypes: new[] { ownerId });
            checks[mutation + ":rejected"] = !result.Compatible;
            analyses[mutation] = new { result.Compatible, result.UnsupportedChanges };
            files[path] = Hash(path);
        }
        foreach (string path in currentFiles)
        {
            string mv = Path.Combine(roots[4], Path.GetFileNameWithoutExtension(path) + ".mv.bytes"); files[mv] = Hash(mv);
            checks[Path.GetFileName(path) + ":mv-unchanged"] = File.ReadAllBytes(mv).SequenceEqual(MetaVersionSnapshot.Create(path).ToBinary());
        }
        checks["input-bytes-preserved"] = files.All(pair => Hash(pair.Key) == pair.Value);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, analyses, files, hostSha256 = Hash(typeof(ParentEvolutionPolicyTests).Assembly.Location),
            scope = "Parent insertion/removal planning, immutable external boundary and metadata rejection; no native runtime qualification inferred"
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks.Where(pair => !pair.Value)) Console.WriteLine("FAILED: " + check.Key);
        Console.WriteLine("Parent evolution policy: " + passed + "; checks=" + checks.Count);
        return passed ? 0 : 1;
    }
}
