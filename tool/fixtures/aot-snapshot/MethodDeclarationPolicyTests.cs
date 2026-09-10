using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class MethodDeclarationPolicyTests
{
    private const string Owner = "HybridCLR.Lab.UnityCases.EvolvingBehaviour";
    private const string Capability = "current-implicit-interface-method-declarations-v1";
    private const MethodAttributes SlotFlags = MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;

    internal static int Run(string[] args)
    {
        if (args.Length != 3)
            throw new ArgumentException("method-declaration-policy <callback Current Model DLL> <compiler removal Model DLL> <new output>");
        string beforePath = Path.GetFullPath(args[0]), afterPath = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Policy output must be new.");
        Directory.CreateDirectory(output);
        var before = MetaVersionSnapshot.Create(beforePath);
        var after = MetaVersionSnapshot.Create(afterPath);
        string ownerId = before.Types.Single(type => type.Identity == Owner).StableId;
        var checks = new Dictionary<string, bool>();
        var analyses = new Dictionary<string, ResourceUpdateCompatibility>();
        ResourceUpdateCompatibility Analyze(string name, MetaVersionSnapshot baseline, MetaVersionSnapshot current, bool selected = true)
        {
            var result = ResourceUpdateCompatibility.Analyze(baseline, current, currentAssemblySet: new[] { current },
                currentStorageTypes: selected ? new[] { ownerId } : Array.Empty<string>());
            analyses[name] = result; return result;
        }
        MetaVersionSnapshot Edit(string source, string name, Action<ModuleDefMD, TypeDef, MethodDef> edit)
        {
            using var module = ModuleDefMD.Load(source);
            var type = module.Find(Owner, false)!;
            edit(module, type, type.Methods.Single(method => method.Name == "OnBeforeSerialize"));
            string path = Path.Combine(output, name + ".dll"); module.Write(path);
            return MetaVersionSnapshot.Create(path);
        }
        foreach (string name in new[] { "OnBeforeSerialize", "OnAfterDeserialize" })
        {
            using var original = ModuleDefMD.Load(beforePath);
            using var compiled = ModuleDefMD.Load(afterPath);
            checks[name + ":compiler-flags"] =
                (original.Find(Owner, false)!.Methods.Single(method => method.Name == name).Attributes & SlotFlags) == SlotFlags &&
                (compiled.Find(Owner, false)!.Methods.Single(method => method.Name == name).Attributes & SlotFlags) == 0;
        }
        void Accepted(string name, MetaVersionSnapshot baseline, MetaVersionSnapshot current)
        {
            var result = Analyze(name, baseline, current);
            checks[name + ":accepted"] = result.Compatible;
            checks[name + ":capability"] = result.RequiredRuntimeCapabilities.Contains(Capability);
            checks[name + ":old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != Capability), result.RequiredRuntimeCapabilities);
        }
        Accepted("compiler-removal", before, after);
        Accepted("compiler-addition", after, before);
        var noOp = Analyze("no-op", before, before);
        checks["no-op-unchanged"] = noOp.Compatible && noOp.ChangedMethodCount == 0 && !noOp.RequiredRuntimeCapabilities.Contains(Capability);
        checks["missing-physical-selection"] = !Analyze("missing-selection", before, after, false).Compatible;
        foreach (string mutation in new[] { "access", "static", "abstract", "pinvoke", "hidebysig", "virtual-only", "final-only", "newslot-only", "impl-flags", "parent", "unchanged-interface", "generic-method" })
        {
            var invalid = Edit(afterPath, mutation, (module, type, method) =>
            {
                switch (mutation)
                {
                    case "access": method.Access = MethodAttributes.Assembly; break;
                    case "static": method.IsStatic = true; break;
                    case "abstract": method.IsAbstract = true; method.Body = null; break;
                    case "pinvoke": method.IsPinvokeImpl = true; break;
                    case "hidebysig": method.IsHideBySig = !method.IsHideBySig; break;
                    case "virtual-only": method.IsVirtual = true; break;
                    case "final-only": method.IsFinal = true; break;
                    case "newslot-only": method.IsNewSlot = true; break;
                    case "impl-flags": method.IsNoInlining = !method.IsNoInlining; break;
                    case "parent": type.BaseType = module.CorLibTypes.Object.TypeDefOrRef; break;
                    case "unchanged-interface":
                        type.Interfaces.Add(new InterfaceImplUser(new TypeRefUser(module, "UnityEngine", "ISerializationCallbackReceiver",
                            module.GetAssemblyRefs().Single(reference => reference.Name == "UnityEngine.CoreModule"))));
                        break;
                    case "generic-method": method.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T")); break;
                }
            });
            checks[mutation + ":rejected"] = !Analyze(mutation, before, invalid).Compatible;
        }
        void Optional(ModuleDefMD module, TypeDef type, MethodDef method, int value)
        {
            method.MethodSig.Params.Add(module.CorLibTypes.Int32);
            method.ParamDefs.Add(new ParamDefUser("value", 1, ParamAttributes.Optional | ParamAttributes.HasDefault)
                { Constant = new ConstantUser(value) });
        }
        var optionalBefore = Edit(beforePath, "optional-before", (module, type, method) => Optional(module, type, method, 3));
        var optionalAfter = Edit(afterPath, "optional-after", (module, type, method) => Optional(module, type, method, 7));
        Accepted("declaration-and-default", optionalBefore, optionalAfter);
        checks["default-capability-retained"] = analyses["declaration-and-default"].RequiredRuntimeCapabilities.Contains("current-parameter-default-metadata-v1");
        foreach (string capability in new[] { ResourceUpdateCompatibility.PhysicalInterfaceMapCapability,
            ResourceUpdateCompatibility.ParameterCacheSelectionCapability, ResourceUpdateCompatibility.ReferenceVirtualInvocationCapability })
        {
            var required = analyses["declaration-and-default"].RequiredRuntimeCapabilities;
            checks[capability + ":required"] = required.Contains(capability);
            checks[capability + ":old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), required);
        }
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed, checks, beforePath, afterPath, beforeSha256 = Hash(beforePath), afterSha256 = Hash(afterPath),
            beforeMvSha256 = Convert.ToHexString(SHA256.HashData(before.ToBinary())),
            afterMvSha256 = Convert.ToHexString(SHA256.HashData(after.ToBinary())),
            hostSha256 = Hash(typeof(MethodDeclarationPolicyTests).Assembly.Location),
            mutations = Directory.GetFiles(output, "*.dll").ToDictionary(Path.GetFileName, Hash),
            analyses = analyses.ToDictionary(pair => pair.Key, pair => new { pair.Value.Compatible, pair.Value.UnsupportedChanges,
                pair.Value.RequiredRuntimeCapabilities }),
            scope = "Compiler method declaration admission and unrelated metadata rejection; native/Player qualification remains separate"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Method declaration policy: " + passed + "; checks=" + checks.Count);
        foreach (var check in checks.Where(pair => !pair.Value)) Console.WriteLine("FAILED: " + check.Key);
        return passed ? 0 : 1;
    }

    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
