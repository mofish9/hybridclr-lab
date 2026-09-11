using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class GenericOwnerPolicy
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("generic-owner-policy <Base baseline> <Current> <new output> <preceding tool>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var checks = new Dictionary<string, bool>(); var analyses = new Dictionary<string, object>();
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var files = Directory.GetFiles(args[0], "*.dll").Concat(Directory.GetFiles(args[1], "*.dll")).ToDictionary(path => path, Hash);
        var before = Directory.GetFiles(args[0], "*.dll").Select(MetaVersionSnapshot.Create).ToDictionary(row => row.AssemblyName);
        var after = Directory.GetFiles(args[1], "*.dll").Select(MetaVersionSnapshot.Create).ToDictionary(row => row.AssemblyName);
        const string assembly = "HybridCLR.ValueLayoutOther", ownerName = "HybridCLR.Lab.GenericPhysicalParents.GenericParent`1";
        var real = ResourceUpdateCompatibility.Analyze(before[assembly], after[assembly], baselineAssemblySet: before.Values,
            currentAssemblySet: after.Values, currentStorageTypes: new[] { after[assembly].Types.Single(type => type.Identity == ownerName).StableId });
        checks["actual-generic-owner-admitted"] = real.Compatible;
        analyses["actual"] = new { real.Compatible, real.UnsupportedChanges };

        MetaVersionSnapshot Fixture(string name, bool changed, string variant)
        {
            using var module = new ModuleDefUser("OwnerBoundary.dll") { Kind = ModuleKind.Dll };
            new AssemblyDefUser("OwnerBoundary", new Version(1, 0, 0, 0)).Modules.Add(module);
            var owner = new TypeDefUser("Fixture", "Owner`2", module.CorLibTypes.Object.TypeDefOrRef)
                { Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Abstract };
            owner.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
            owner.GenericParameters.Add(new GenericParamUser(1, GenericParamAttributes.NonVariant, "U"));
            module.Types.Add(owner);
            bool nongeneric = variant == "nongeneric-root", array = variant.StartsWith("array");
            var root = new TypeRefUser(module, "Frozen", nongeneric ? "Root" : "Root`2",
                new AssemblyRefUser(changed && variant == "external-change" ? "OtherRoots" : "FrozenRoots"));
            ITypeDefOrRef Root(uint first, uint second) => nongeneric ? root : new TypeSpecUser(new GenericInstSig(new ClassSig(root),
                array ? new SZArraySig(new GenericVar(first)) : new GenericVar(first), new GenericVar(second)));
            if (!changed) owner.BaseType = Root(0, 1);
            else
            {
                var middle = new TypeDefUser("Fixture", "Middle`2", module.CorLibTypes.Object.TypeDefOrRef)
                    { Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Abstract };
                middle.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "X"));
                middle.GenericParameters.Add(new GenericParamUser(variant == "invalid-middle-number" ? (ushort)7 : (ushort)1, GenericParamAttributes.NonVariant, "Y"));
                module.Types.Add(middle);
                bool swap = variant == "swap" || variant == "swap-cancel" || variant == "array-swap";
                middle.BaseType = Root(swap ? 1u : variant == "middle-var-out-of-range" ? 2u : 0u, swap ? 0u : 1u);
                TypeSig first = variant == "method-variable" ? new GenericMVar(0) : new GenericVar(variant == "owner-var-out-of-range" ? 2u : variant == "swap-cancel" ? 1u : 0u);
                owner.BaseType = new TypeSpecUser(new GenericInstSig(new ClassSig(middle), first, new GenericVar(variant == "swap-cancel" ? 0u : 1u)));
                if (variant == "bare-variable") middle.BaseType = new TypeSpecUser(new GenericVar(0));
                if (variant == "cycle") middle.BaseType = new TypeSpecUser(new GenericInstSig(new ClassSig(owner), new GenericVar(0), new GenericVar(1)));
                if (variant == "constraint") owner.GenericParameters[0].Flags = GenericParamAttributes.ReferenceTypeConstraint;
                if (variant == "arity") owner.GenericParameters.Add(new GenericParamUser(2, GenericParamAttributes.NonVariant, "V"));
                if (variant == "invalid-owner-number") owner.GenericParameters[1].Number = 7;
            }
            string path = Path.Combine(output, name + ".dll"); module.Write(path); files[path] = Hash(path);
            return MetaVersionSnapshot.Create(path);
        }
        foreach (string variant in new[] { "same", "swap-cancel", "array-same", "nongeneric-root", "swap", "array-swap", "external-change",
            "owner-var-out-of-range", "middle-var-out-of-range", "method-variable", "bare-variable", "cycle", "constraint", "arity",
            "invalid-owner-number", "invalid-middle-number" })
        {
            var original = Fixture(variant + "-base", false, variant); var current = Fixture(variant + "-current", true, variant);
            var result = ResourceUpdateCompatibility.Analyze(original, current, baselineAssemblySet: new[] { original },
                currentAssemblySet: new[] { current }, currentStorageTypes: new[] { current.Types.Single(type => type.Identity == "Fixture.Owner`2").StableId });
            bool expected = variant == "same" || variant == "swap-cancel" || variant == "array-same" || variant == "nongeneric-root";
            checks[variant] = result.Compatible == expected;
            analyses[variant] = new { expected, result.Compatible, result.UnsupportedChanges };
        }
        var oldType = Assembly.LoadFrom(Path.GetFullPath(args[3])).GetType("HybridCLR.DheTool.MetaVersionSnapshot", true)!;
        foreach (string path in files.Keys.Where(path => !path.StartsWith(output, StringComparison.OrdinalIgnoreCase)))
        {
            var old = oldType.GetMethod("Create")!.Invoke(null, new object[] { path });
            checks["binary-mv-preserved:" + path] = ((byte[])oldType.GetMethod("ToBinary")!.Invoke(old, null)!).SequenceEqual(MetaVersionSnapshot.Create(path).ToBinary());
        }
        checks["inputs-preserved"] = files.All(row => Hash(row.Key) == row.Value);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, analyses, files,
            hostSha256 = Hash(typeof(GenericOwnerPolicy).Assembly.Location) }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks.Where(row => !row.Value)) Console.WriteLine("FAILED: " + check.Key);
        Console.WriteLine("Generic owner policy: " + passed + "; checks=" + checks.Count); return passed ? 0 : 1;
    }
}
