using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class GenericPhysicalParentPolicy
{
    private const string Model = "HybridCLR.ValueLayoutModel", Other = "HybridCLR.ValueLayoutOther";
    private const string Owner = "HybridCLR.Lab.VirtualSignatures.Processor";
    private const string Parent = "HybridCLR.Lab.GenericPhysicalParents.GenericParent`1";
    internal static int Run(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("generic-physical-parent-policy <Base baseline> <generic Current> <root Current> <new output> <preceding tool.dll>");
        string output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>(); var analyses = new Dictionary<string, object>();
        var files = new Dictionary<string, string>();
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        Dictionary<string, MetaVersionSnapshot> Read(string root) => Directory.GetFiles(root, "*.dll")
            .Where(path => Path.GetFileNameWithoutExtension(path) != "HybridCLR.ValueLayoutNative")
            .Select(path => { files[path] = Hash(path); return MetaVersionSnapshot.Create(path); }).ToDictionary(value => value.AssemblyName);
        var before = Read(args[0]); var current = Read(args[1]); var root = Read(args[2]);
        ResourceUpdateCompatibility Analyze(string name, Dictionary<string, MetaVersionSnapshot> oldSet,
            Dictionary<string, MetaVersionSnapshot> newSet, bool select = true,
            IEnumerable<MetaVersionSnapshot>? oldPeers = null, IEnumerable<MetaVersionSnapshot>? newPeers = null)
        {
            var result = ResourceUpdateCompatibility.Analyze(oldSet[Model], newSet[Model],
                currentStorageTypes: select ? new[] { newSet[Model].Types.Single(type => type.Identity == Owner).StableId } : Array.Empty<string>(),
                baselineAssemblySet: oldPeers ?? oldSet.Values, currentAssemblySet: newPeers ?? newSet.Values);
            analyses[name] = new { result.Compatible, result.UnsupportedChanges, result.RequiredRuntimeCapabilities };
            return result;
        }
        var insertion = Analyze("insertion", before, current);
        checks["generic-parent-insertion"] = insertion.Compatible;
        checks["generic-parent-removal"] = Analyze("removal", current, root).Compatible;
        checks["physical-selection-required"] = !Analyze("unselected", before, current, false).Compatible;
        checks["missing-base-peer-rejected"] = !Analyze("missing-base-peer", before, current, oldPeers: new[] { before[Model] }).Compatible;
        checks["missing-current-peer-rejected"] = !Analyze("missing-current-peer", before, current, newPeers: new[] { current[Model] }).Compatible;
        checks["duplicate-peer-rejected"] = !Analyze("duplicate-peer", before, current, newPeers: current.Values.Append(current[Other])).Compatible;
        var noop = Analyze("noop", current, current, false);
        checks["noop-retains-aot"] = noop.Compatible && noop.ChangedMethodCount == 0 &&
            !noop.RequiredRuntimeCapabilities.Contains(ResourceUpdateCompatibility.PhysicalParentEvolutionCapability);
        checks["parent-capability-required"] = insertion.RequiredRuntimeCapabilities.Contains(ResourceUpdateCompatibility.PhysicalParentEvolutionCapability);

        foreach (string mutation in new[] { "arity", "open-variable", "generic-owner", "sealed-parent", "cycle", "external-root" })
        {
            string name = mutation is "arity" or "open-variable" or "generic-owner" ? Model : Other;
            string path = Path.Combine(output, mutation + ".dll");
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(args[1], name + ".dll"))))
            {
                if (name == Model)
                {
                    var owner = module.Find(Owner, false)!;
                    var signature = (GenericInstSig)((TypeSpec)owner.BaseType).TypeSig;
                    if (mutation == "arity") signature.GenericArguments.Clear();
                    else if (mutation == "open-variable") signature.GenericArguments[0] = new GenericVar(0);
                    else owner.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
                }
                else
                {
                    var parent = module.Find(Parent, false)!;
                    if (mutation == "sealed-parent") parent.IsSealed = true;
                    else if (mutation == "cycle") parent.BaseType = new TypeSpecUser(new GenericInstSig(new ClassSig(parent), new GenericVar(0)));
                    else parent.BaseType = new TypeRefUser(module, "System", "Exception", module.CorLibTypes.AssemblyRef);
                }
                module.Write(path);
            }
            files[path] = Hash(path);
            var changed = new Dictionary<string, MetaVersionSnapshot>(current) { [name] = MetaVersionSnapshot.Create(path) };
            checks[mutation + "-rejected"] = !Analyze(mutation, before, changed).Compatible;
        }

        // Separate assemblies isolate substitution at the immutable boundary.
        // They are admission fixtures, not executable Player workloads.
        MetaVersionSnapshot Boundary(string name, bool inserted, string argumentAssembly, string shape, bool swap)
        {
            string path = Path.Combine(output, name + ".dll");
            using var module = new ModuleDefUser("GenericBoundary.dll") { Kind = ModuleKind.Dll };
            new AssemblyDefUser("GenericBoundary", new Version(1, 0, 0, 0)).Modules.Add(module);
            var external = new TypeRefUser(module, "Frozen", "Root`2", new AssemblyRefUser("FrozenRoots"));
            var argument = new ClassSig(new TypeRefUser(module, "Same", "Argument", new AssemblyRefUser(argumentAssembly)));
            var owner = new TypeDefUser("Fixture", "Owner", module.CorLibTypes.Object.TypeDefOrRef)
                { Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Abstract };
            module.Types.Add(owner);
            TypeSig First(TypeSig value) => shape == "array" ? new SZArraySig(value) : value;
            if (inserted)
            {
                var middle = new TypeDefUser("Fixture", "Middle`2", module.CorLibTypes.Object.TypeDefOrRef)
                    { Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Abstract };
                middle.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
                middle.GenericParameters.Add(new GenericParamUser(1, GenericParamAttributes.NonVariant, "U"));
                middle.BaseType = new TypeSpecUser(new GenericInstSig(new ClassSig(external),
                    First(new GenericVar(swap ? 1u : 0u)), new GenericVar(swap ? 0u : 1u)));
                module.Types.Add(middle);
                owner.BaseType = new TypeSpecUser(new GenericInstSig(new ClassSig(middle), argument, module.CorLibTypes.Int64));
            }
            else owner.BaseType = new TypeSpecUser(new GenericInstSig(new ClassSig(external), First(argument), module.CorLibTypes.Int64));
            module.Write(path); files[path] = Hash(path); return MetaVersionSnapshot.Create(path);
        }
        foreach (string shape in new[] { "named", "array" })
        {
            var original = Boundary(shape + "-base", false, "ArgumentsA", shape, false);
            foreach (string change in new[] { "same", "different-scope", "swapped" })
            {
                var updated = Boundary(shape + "-" + change, true, change == "different-scope" ? "ArgumentsB" : "ArgumentsA", shape, change == "swapped");
                var result = ResourceUpdateCompatibility.Analyze(original, updated,
                    baselineAssemblySet: new[] { original }, currentAssemblySet: new[] { updated },
                    currentStorageTypes: new[] { updated.Types.Single(type => type.Identity == "Fixture.Owner").StableId });
                string test = "external-substitution-" + shape + "-" + change;
                checks[test] = change == "same" ? result.Compatible : !result.Compatible &&
                    result.UnsupportedChanges.Contains("existing-type-layout-or-vtable-change:Fixture.Owner");
                analyses[test] = new { result.Compatible, result.UnsupportedChanges };
            }
        }
        var previous = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[4]))
            .GetType("HybridCLR.DheTool.MetaVersionSnapshot", true)!;
        foreach (string path in Directory.GetFiles(args[0], "*.dll").Concat(Directory.GetFiles(args[1], "*.dll")))
        {
            var oldSnapshot = previous.GetMethod("Create")!.Invoke(null, new object[] { path });
            byte[] oldBytes = (byte[])previous.GetMethod("ToBinary")!.Invoke(oldSnapshot, null)!;
            checks["binary-mv-preserved:" + path] = oldBytes.SequenceEqual(MetaVersionSnapshot.Create(path).ToBinary());
        }
        files[Path.GetFullPath(args[4])] = Hash(args[4]);
        checks["inputs-preserved"] = files.All(row => Hash(row.Key) == row.Value);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, analyses, files,
            hostSha256 = Hash(typeof(GenericPhysicalParentPolicy).Assembly.Location),
            scope = "Generic parent admission, structural substitution and negative metadata controls; not a Player gate"
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks.Where(row => !row.Value)) Console.WriteLine("FAILED: " + check.Key);
        Console.WriteLine("Generic physical parent policy: " + passed + "; checks=" + checks.Count); return passed ? 0 : 1;
    }
}
