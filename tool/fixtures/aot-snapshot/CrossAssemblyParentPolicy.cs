using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class CrossAssemblyParentPolicy
{
    private const string Model = "HybridCLR.ValueLayoutModel", Other = "HybridCLR.ValueLayoutOther";
    private const string Owner = "HybridCLR.Lab.VirtualSignatures.Processor", Root = "HybridCLR.Lab.VirtualSignatures.ProcessorRoot";
    private const string Parent = "HybridCLR.Lab.CrossAssemblyParents.CrossParent";
    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("cross-parent-policy <Base101 baseline> <Base100 baseline> <Current root> <new output>");
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>(); var analyses = new Dictionary<string, object>();
        var files = new Dictionary<string, string>();
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Dictionary<string, MetaVersionSnapshot> Read(string root) => Directory.GetFiles(root, "*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) != "HybridCLR.ValueLayoutNative")
            .Select(path => { files[path] = Hash(path); return MetaVersionSnapshot.Create(path); }).ToDictionary(snapshot => snapshot.AssemblyName);
        var before = Read(args[0]); var small = Read(args[1]); var current = Read(args[2]);
        ResourceUpdateCompatibility Analyze(string name, Dictionary<string, MetaVersionSnapshot> oldSet,
            Dictionary<string, MetaVersionSnapshot> newSet, IEnumerable<MetaVersionSnapshot>? oldPeers = null,
            IEnumerable<MetaVersionSnapshot>? newPeers = null, bool select = true)
        {
            string selected = newSet[Model].Types.Single(type => type.Identity == Owner).StableId;
            var result = ResourceUpdateCompatibility.Analyze(oldSet[Model], newSet[Model],
                currentStorageTypes: select ? new[] { selected } : Array.Empty<string>(),
                currentAssemblySet: newPeers ?? newSet.Values, baselineAssemblySet: oldPeers ?? oldSet.Values);
            analyses[name] = new { result.Compatible, result.UnsupportedChanges, result.RequiredRuntimeCapabilities };
            return result;
        }
        checks["existing-parent-to-cross-parent"] = Analyze("existing", before, current).Compatible;
        checks["root-only-to-cross-parent"] = Analyze("small", small, current).Compatible;
        // This helper intentionally selects only Processor. The separate resource
        // workflow supplies all value/reference impact selections for small Base.
        checks["physical-selection-required"] = !Analyze("unselected", before, current, select: false).Compatible;
        checks["cross-parent-removal"] = Analyze("removal", current, before).Compatible;
        checks["missing-original-peer-rejected"] = !Analyze("missing-before", current, before,
            oldPeers: new[] { current[Model] }).Compatible;
        checks["missing-current-peer-rejected"] = !Analyze("missing-current", before, current,
            newPeers: new[] { current[Model] }).Compatible;
        checks["duplicate-original-peer-rejected"] = !Analyze("duplicate-before", before, current,
            oldPeers: before.Values.Append(before[Other])).Compatible;
        checks["duplicate-current-peer-rejected"] = !Analyze("duplicate-current", before, current,
            newPeers: current.Values.Append(current[Other])).Compatible;
        checks["wrong-original-owner-rejected"] = !Analyze("wrong-owner", before, current, oldPeers: current.Values).Compatible;
        var noop = ResourceUpdateCompatibility.Analyze(current[Model], current[Model], currentAssemblySet: current.Values, baselineAssemblySet: current.Values);
        checks["noop-retains-aot"] = noop.Compatible && noop.ChangedMethodCount == 0 &&
            !noop.RequiredRuntimeCapabilities.Contains(ResourceUpdateCompatibility.PhysicalParentEvolutionCapability);
        var accepted = Analyze("capabilities", before, current);
        foreach (string capability in new[] { ResourceUpdateCompatibility.PhysicalParentEvolutionCapability,
            ResourceUpdateCompatibility.ParentMemberHandleCapability, ResourceUpdateCompatibility.NativeReferencePhysicalFrameCapability,
            ResourceUpdateCompatibility.FrozenFieldObjectValidationCapability, ResourceUpdateCompatibility.FrozenBaseInstanceFrameCapability })
        {
            checks[capability + ":required"] = accepted.RequiredRuntimeCapabilities.Contains(capability);
            checks[capability + ":missing-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), accepted.RequiredRuntimeCapabilities);
        }
        foreach (string mutation in new[] { "sealed", "generic", "missing", "cycle", "external", "wrong-original-boundary", "assembly-qualified-duplicate-name" })
        {
            string path = Path.Combine(output, mutation + ".dll");
            using (var other = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(args[2], Other + ".dll"))))
            {
                var parent = other.Find(Parent, false)!;
                switch (mutation)
                {
                    case "sealed": parent.IsSealed = true; break;
                    case "generic": parent.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T")); break;
                    case "missing": other.Types.Remove(parent); break;
                    case "cycle": parent.BaseType = new TypeRefUser(other, "HybridCLR.Lab.VirtualSignatures", "Processor", parent.BaseType.Scope as IResolutionScope); break;
                    case "external": case "wrong-original-boundary": parent.BaseType = new TypeRefUser(other, "System", "Exception", other.CorLibTypes.AssemblyRef); break;
                    case "assembly-qualified-duplicate-name":
                        var shadow = new TypeDefUser("HybridCLR.Lab.VirtualSignatures", "ProcessorRoot", parent.BaseType)
                            { Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Abstract };
                        other.Types.Add(shadow); parent.BaseType = shadow; break;
                }
                other.Write(path);
            }
            files[path] = Hash(path);
            var mutated = new Dictionary<string, MetaVersionSnapshot>(current) { [Other] = MetaVersionSnapshot.Create(path) };
            bool allowed = mutation == "wrong-original-boundary"
                ? Analyze(mutation, mutated, before).Compatible : Analyze(mutation, before, mutated).Compatible;
            checks[mutation] = allowed == (mutation == "assembly-qualified-duplicate-name");
        }
        checks["all-input-bytes-retained"] = files.All(row => Hash(row.Key) == row.Value);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, analyses, files, hostSha256 = Hash(typeof(CrossAssemblyParentPolicy).Assembly.Location),
            scope = "Cross-assembly ancestry admission and negative graph/capability controls; real Player execution is separate"
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var row in checks.Where(row => !row.Value)) Console.WriteLine("FAILED: " + row.Key);
        Console.WriteLine("Cross parent policy: " + passed + "; checks=" + checks.Count); return passed ? 0 : 1;
    }
}
