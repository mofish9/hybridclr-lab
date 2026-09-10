using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class ReferenceInterfacePolicyTests
{
    private const string Owner = "HybridCLR.Lab.UnityCases.EvolvingBehaviour";
    private const string Contract = "UnityEngine.ISerializationCallbackReceiver";
    private const string Capability = "physical-current-interface-additions-v1";
    private const string EvolutionCapability = "physical-current-interface-evolution-v1";

    internal static int Run(string[] args)
    {
        if (args.Length != 2)
            throw new ArgumentException("reference-interface-policy <compiler-produced callback Model DLL> <new output>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Policy output must be new.");
        Directory.CreateDirectory(output);
        var after = MetaVersionSnapshot.Create(source);
        MetaVersionSnapshot Edit(string name, Action<ModuleDefMD, TypeDef> edit)
        {
            using var module = ModuleDefMD.Load(source);
            var type = module.Find(Owner, false) ?? throw new InvalidDataException("Missing reference owner.");
            if (type.Interfaces.Count(row => row.Interface.FullName == Contract) != 1)
                throw new InvalidDataException("Expected the existing-type callback fixture.");
            edit(module, type);
            string path = Path.Combine(output, name + ".dll");
            module.Write(path);
            return MetaVersionSnapshot.Create(path);
        }
        var before = Edit("before-interface", (_, type) =>
            type.Interfaces.Remove(type.Interfaces.Single(row => row.Interface.FullName == Contract)));
        string id = after.Types.Single(type => type.Identity == Owner).StableId;
        ResourceUpdateCompatibility Analyze(MetaVersionSnapshot baseline, MetaVersionSnapshot current,
            params string[] selection) => ResourceUpdateCompatibility.Analyze(baseline, current,
                currentAssemblySet: new[] { current }, currentStorageTypes: selection);
        var checks = new Dictionary<string, bool>();
        var analyses = new Dictionary<string, ResourceUpdateCompatibility>();
        void Accepted(string name, MetaVersionSnapshot baseline, MetaVersionSnapshot current, bool needsCapability)
        {
            var result = Analyze(baseline, current, id); analyses[name] = result;
            checks[name + ":compatible"] = result.Compatible;
            checks[name + ":capability-selection"] = result.RequiredRuntimeCapabilities.Contains(Capability) == needsCapability;
        }
        void Rejected(string name, MetaVersionSnapshot baseline, MetaVersionSnapshot current, params string[] selection)
        {
            var result = Analyze(baseline, current, selection); analyses[name] = result;
            checks[name + ":layout-rejected"] = result.UnsupportedChanges.Contains("existing-type-layout-or-vtable-change:" + Owner);
        }
        void Evolved(string name, MetaVersionSnapshot baseline, MetaVersionSnapshot current)
        {
            var result = Analyze(baseline, current, id); analyses[name] = result;
            checks[name + ":compatible"] = result.Compatible;
            checks[name + ":evolution-capability"] = result.RequiredRuntimeCapabilities.Contains(EvolutionCapability);
            checks[name + ":older-Base-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != EvolutionCapability),
                result.RequiredRuntimeCapabilities);
        }
        Accepted("selected-addition", before, after, true);
        Accepted("no-op", after, after, false);
        Rejected("missing-selection", before, after);
        Rejected("wrong-selection", before, after, new string('0', 64));
        Evolved("interface-removal", after, before);
        var removed = Edit("removed-interface-and-methods", (_, type) =>
        {
            type.Interfaces.Clear();
            foreach (var method in type.Methods.Where(method => method.Name == "OnBeforeSerialize" || method.Name == "OnAfterDeserialize").ToArray())
                type.Methods.Remove(method);
        });
        Evolved("interface-and-method-removal", after, removed);
        Rejected("removal-missing-selection", after, before);
        var replacement = Edit("replacement", (module, type) =>
        {
            type.Interfaces.Clear();
            type.Interfaces.Add(new InterfaceImplUser(new TypeRefUser(module, "System", "IDisposable", module.CorLibTypes.AssemblyRef)));
            var dispose = new MethodDefUser("Dispose", MethodSig.CreateInstance(module.CorLibTypes.Void), MethodImplAttributes.IL,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot) { Body = new CilBody() };
            dispose.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); type.Methods.Add(dispose);
        });
        Evolved("interface-replacement", after, replacement);
        foreach (string mutation in new[] { "duplicate", "parent", "parent-scope", "sealed", "packing", "class-size", "generic-owner", "generic-interface" })
        {
            var invalid = Edit(mutation, (module, type) =>
            {
                switch (mutation)
                {
                    case "duplicate": type.Interfaces.Add(new InterfaceImplUser(type.Interfaces.Single().Interface)); break;
                    case "parent": type.BaseType = module.CorLibTypes.Object.TypeDefOrRef; break;
                    case "parent-scope":
                        type.BaseType = new TypeRefUser(module, type.BaseType.Namespace, type.BaseType.Name,
                            new AssemblyRefUser(new AssemblyNameInfo("Different.Parent.Assembly")));
                        break;
                    case "sealed": type.IsSealed = !type.IsSealed; break;
                    case "packing": type.ClassLayout = new ClassLayoutUser(8, 0); break;
                    case "class-size": type.ClassLayout = new ClassLayoutUser(0, 128); break;
                    case "generic-owner": type.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T")); break;
                    case "generic-interface":
                        type.Interfaces.Add(new InterfaceImplUser(new TypeSpecUser(new GenericInstSig(
                            new ClassSig(new TypeRefUser(module, "System", "IComparable`1", module.CorLibTypes.AssemblyRef)),
                            module.CorLibTypes.Int32))));
                        break;
                }
            });
            Rejected(mutation, before, invalid, id);
        }
        var required = analyses["selected-addition"].RequiredRuntimeCapabilities;
        checks["older-Base-capability-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != Capability), required);
        checks["explicit-capable-candidate-accepted"] = ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Append(Capability), required);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed, checks, source,
            sourceSha256 = Hash(source), hostSha256 = Hash(typeof(ReferenceInterfacePolicyTests).Assembly.Location),
            mutations = Directory.GetFiles(output, "*.dll").ToDictionary(Path.GetFileName, Hash),
            analyses = analyses.ToDictionary(pair => pair.Key, pair => new { pair.Value.Compatible,
                pair.Value.UnsupportedChanges, pair.Value.RequiredRuntimeCapabilities }),
            scope = "Existing reference interface addition/removal/replacement admission and retained shape gates; no native qualification"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Reference interface policy: " + passed);
        foreach (var check in checks.Where(pair => !pair.Value)) Console.WriteLine("FAILED: " + check.Key);
        return passed ? 0 : 1;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
