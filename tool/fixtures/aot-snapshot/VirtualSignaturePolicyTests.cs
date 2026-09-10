using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class VirtualSignaturePolicyTests
{
    private const string Model = "HybridCLR.ValueLayoutModel";
    private const string Native = "HybridCLR.ValueLayoutNative";

    internal static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException(
            "virtual-signature-policy <old DLL root> <grown DLL root> <new-type Base DLL root> <Current DLL root> <archived Current MV root> <new output>");
        string[] roots = args.Take(5).Select(Path.GetFullPath).ToArray();
        string output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output)) throw new IOException("Policy output must be new.");
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>();
        var analyses = new Dictionary<string, object>();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string[] Dlls(string root) => Directory.GetFiles(root, "*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) != Native).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        string[] capabilities = { ResourceUpdateCompatibility.GenericMethodImplOwnerCapability,
            ResourceUpdateCompatibility.VirtualSignatureFrameCapability, ResourceUpdateCompatibility.ScalarInstanceFrameCapability };
        ResourceUpdateCompatibility Analyze(string name, string beforeRoot, string currentRoot)
        {
            string[] beforeFiles = Dlls(beforeRoot), currentFiles = Dlls(currentRoot);
            foreach (string path in beforeFiles.Concat(currentFiles)) files[path] = Hash(path);
            string native = Path.Combine(roots[0], Native + ".dll"); files[native] = Hash(native);
            var compilation = ResourceExecutionPlanner.Compile(beforeFiles, currentFiles, new[] { native });
            var before = beforeFiles.Select(MetaVersionSnapshot.Create).ToDictionary(snapshot => snapshot.AssemblyName);
            var current = currentFiles.Select(MetaVersionSnapshot.Create).ToDictionary(snapshot => snapshot.AssemblyName);
            compilation.Plans.TryGetValue(Model, out var plan);
            uint[] storage = plan?.CurrentStorageTypeTokens ?? Array.Empty<uint>();
            var analysis = ResourceUpdateCompatibility.Analyze(before[Model], current[Model], currentAssemblySet: current.Values,
                currentStorageTypes: current[Model].Types.Where(type => storage.Contains(type.Token)).Select(type => type.StableId),
                currentExecutionMethodTokens: plan?.CurrentExecutionMethodTokens,
                currentGenericContextMethodTokens: plan?.CurrentGenericContextMethodTokens);
            analyses[name] = new { analysis.Compatible, analysis.UnsupportedChanges, analysis.RequiredRuntimeCapabilities,
                plan, plannerUnsupportedChanges = compilation.UnsupportedChanges, genericMethodImplDeclarations = current[Model].GenericMethodImplDeclarations };
            checks[name + ":plan-supported"] = compilation.UnsupportedChanges.Length == 0;
            checks[name + ":admitted"] = analysis.Compatible;
            checks[name + ":matching-runtime"] = ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities, analysis.RequiredRuntimeCapabilities);
            foreach (string capability in capabilities)
            {
                bool required = analysis.RequiredRuntimeCapabilities.Contains(capability);
                checks[name + ":missing-" + capability] = ResourceUpdateCompatibility.CanExecuteUpdate(
                    ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability),
                    analysis.RequiredRuntimeCapabilities) == !required;
            }
            return analysis;
        }
        var old = Analyze("old-to-current", roots[0], roots[3]);
        foreach (string capability in capabilities) checks["old-requires-" + capability] = old.RequiredRuntimeCapabilities.Contains(capability);
        var grown = Analyze("grown-to-current", roots[1], roots[3]);
        checks["grown-requires-owner-fix"] = grown.RequiredRuntimeCapabilities.Contains(capabilities[0]);
        checks["grown-keeps-compatible-frames"] = !grown.RequiredRuntimeCapabilities.Intersect(capabilities.Skip(1)).Any();
        var noOp = Analyze("old-no-op", roots[0], roots[0]);
        checks["no-op-startup-requires-owner-fix"] = noOp.RequiredRuntimeCapabilities.Contains(capabilities[0]);
        checks["no-op-keeps-aot"] = noOp.ChangedMethodCount == 0 && !noOp.RequiredRuntimeCapabilities.Intersect(capabilities.Skip(1)).Any();
        string bodyOnlyRoot = Path.Combine(output, "body-only-current");
        Directory.CreateDirectory(bodyOnlyRoot);
        foreach (string file in Dlls(roots[0])) File.Copy(file, Path.Combine(bodyOnlyRoot, Path.GetFileName(file)));
        string bodyOnlyModel = Path.Combine(bodyOnlyRoot, Model + ".dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(bodyOnlyModel)))
        {
            var method = module.Find("HybridCLR.Lab.VirtualSignatures.Processor", false)!.Methods
                .Single(method => method.Name == "CopyReference");
            var literal = method.Body.Instructions.First(instruction => instruction.OpCode == OpCodes.Ldstr);
            literal.Operand = (string)literal.Operand + "-body-only";
            module.Write(bodyOnlyModel);
        }
        var bodyOnly = Analyze("body-only", roots[0], bodyOnlyRoot);
        checks["body-only-changes-one-implementation"] = bodyOnly.ChangedMethodCount == 1;
        checks["body-only-keeps-compatible-frames"] = !bodyOnly.RequiredRuntimeCapabilities.Intersect(capabilities.Skip(1)).Any();
        var added = Analyze("new-type-control", roots[2], roots[3]);
        checks["new-type-control-keeps-older-runtime"] = !added.RequiredRuntimeCapabilities.Intersect(capabilities).Any();
        foreach (string dll in Dlls(roots[3]))
        {
            string mv = Path.Combine(roots[4], Path.GetFileNameWithoutExtension(dll) + ".mv.bytes");
            files[mv] = Hash(mv);
            checks[Path.GetFileName(dll) + ":unchanged-mv-bytes"] = File.ReadAllBytes(mv).SequenceEqual(MetaVersionSnapshot.Create(dll).ToBinary());
        }
        checks["input-files-preserved"] = files.All(pair => Hash(pair.Key) == pair.Value);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, analyses, files, hostSha256 = Hash(typeof(VirtualSignaturePolicyTests).Assembly.Location),
            scope = "Compiler-produced old/grown/no-op/new-type admission and missing native capability rejection; unchanged binary MV and input identity"
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks.Where(pair => !pair.Value)) Console.WriteLine("FAILED: " + check.Key);
        Console.WriteLine("Virtual signature policy: " + passed + "; checks=" + checks.Count);
        return passed ? 0 : 1;
    }
}
