using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR;
using HybridCLR.DheTool;

internal static class ReferenceOwnerPlanTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("reference-owner-plan <Base proof> <Current DLL root> <new output>");
        string proof = Path.GetFullPath(args[0]), current = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Reference owner plan output must be new.");
        Directory.CreateDirectory(output);
        string[] beforeFiles = Directory.GetFiles(Path.Combine(proof, "base/baseline"), "*.dll");
        string[] currentFiles = Directory.GetFiles(current, "*.dll");
        const string model = "HybridCLR.ValueLayoutModel";
        string beforePath = beforeFiles.Single(path => Path.GetFileNameWithoutExtension(path) == model);
        string currentPath = currentFiles.Single(path => Path.GetFileNameWithoutExtension(path) == model);
        var before = MetaVersionSnapshot.Create(beforePath); var after = MetaVersionSnapshot.Create(currentPath);
        var state = after.Types.Single(type => type.Identity.Contains("EvolvingBehaviour/<Steps>d__", StringComparison.Ordinal));
        var oldState = before.Types.Single(type => type.StableId == state.StableId);
        var compilation = ResourceExecutionPlanner.Compile(beforeFiles, currentFiles, Array.Empty<string>());
        var plan = compilation.Plans[model];
        var checks = new Dictionary<string, bool>();
        checks["real-state-layout-version-unchanged"] = oldState.LayoutVersion == state.LayoutVersion;
        checks["real-state-storage-selected"] = plan.CurrentStorageTypeTokens.Contains(state.Token);
        foreach (string name in new[] { ".ctor", "MoveNext" })
        {
            var method = after.Methods.Single(row => row.DeclaringType == state.Identity && row.Name == name);
            bool sameBody = before.Methods.Single(row => row.StableId == method.StableId).Version == method.Version;
            // The captured constructor is unchanged; the captured MoveNext is
            // changed. Both need the same selected receiver representation.
            checks["real-state-" + name + "-captured-body-version"] = sameBody == (name == ".ctor");
            checks["real-state-" + name + "-selected"] = plan.CurrentExecutionMethodTokens.Contains(method.Token);
        }
        uint generic = after.Types.Single(type => type.Identity == "HybridCLR.Lab.ValueLayout.GenericOwner`1").Token;
        checks["real-open-generic-owner-retains-definition"] = !plan.CurrentStorageTypeTokens.Contains(generic);
        void PackageBinding(ResourceExecutionPlan value, MetaVersionSnapshot baseline, MetaVersionSnapshot latest, string name)
        {
            var package = UnityEngine.JsonUtility.FromJson<DheExecutionPlan>(JsonSerializer.Serialize(value,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            package.Validate(value.AssemblyName, baseline.ToBinary(), latest.ToBinary());
            checks[name] = package.CanonicalBinding() == value.CanonicalBinding();
        }
        PackageBinding(plan, before, after, "real-package-binding");
        var fixturePaths = new List<string>();
        foreach (bool changed in new[] { false, true })
        {
            using var module = new ModuleDefUser("ReferenceOwners.dll") { Kind = ModuleKind.Dll };
            var assembly = new AssemblyDefUser("ReferenceOwners", new Version(1, 0, 0, 0)); assembly.Modules.Add(module);
            TypeDef Type(string name)
            {
                var type = new TypeDefUser("Cases", name, module.CorLibTypes.Object.TypeDefOrRef) { Attributes = TypeAttributes.Public };
                module.Types.Add(type);
                var ctor = new MethodDefUser(".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), MethodImplAttributes.IL,
                    MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName) { Body = new CilBody() };
                type.Methods.Add(ctor);
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Call, new MemberRefUser(module, ".ctor",
                    MethodSig.CreateInstance(module.CorLibTypes.Void), module.CorLibTypes.Object.TypeDefOrRef)));
                ctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
                return type;
            }
            void Field(TypeDef owner, string name, TypeSig type) => owner.Fields.Add(new FieldDefUser(name, new FieldSig(type), FieldAttributes.Public));
            var root = Type("Root"); Field(root, "Value", module.CorLibTypes.Int32);
            if (changed) Field(root, "Extra", module.CorLibTypes.Int64);
            var leaf = Type("Leaf"); Field(leaf, "Root", new ClassSig(root));
            var chain = Type("Chain"); Field(chain, "Leaf", new ClassSig(leaf));
            var cycleA = Type("CycleA"); var cycleB = Type("CycleB");
            Field(cycleA, "Next", new ClassSig(cycleB)); Field(cycleB, "Next", new ClassSig(cycleA)); Field(cycleB, "Root", new ClassSig(root));
            var array = Type("ArrayHolder"); Field(array, "Items", new SZArraySig(new ClassSig(root)));
            var open = Type("GenericOwner`1"); open.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
            Field(open, "Value", new GenericVar(0, open));
            var closed = Type("ClosedHolder"); Field(closed, "Owner", new GenericInstSig(new ClassSig(open), new ClassSig(root)));
            var unrelated = Type("Unrelated"); Field(unrelated, "Value", module.CorLibTypes.Int64);
            var scalar = Type("ScalarHolder"); Field(scalar, "Owner", new GenericInstSig(new ClassSig(open), module.CorLibTypes.Int64));
            string path = Path.Combine(output, changed ? "current.dll" : "base.dll"); module.Write(path); fixturePaths.Add(path);
        }
        var fixtureBefore = MetaVersionSnapshot.Create(fixturePaths[0]); var fixtureCurrent = MetaVersionSnapshot.Create(fixturePaths[1]);
        var fixture = ResourceExecutionPlanner.Compile(new[] { fixturePaths[0] }, new[] { fixturePaths[1] }, Array.Empty<string>());
        var fixturePlan = fixture.Plans["ReferenceOwners"];
        checks["fixture-root-changes-exclude-dependent-owners"] = fixture.Impact.ChangedValueTypes.SequenceEqual(new[] { "ReferenceOwners|Cases.Root" });
        checks["cycle-dependencies-retain-original-root"] = fixture.Impact.Layouts.Where(row =>
                row.DefinitionIdentity == "ReferenceOwners|Cases.CycleA" || row.DefinitionIdentity == "ReferenceOwners|Cases.CycleB")
            .Count(row => row.ChangedValueTypes.SequenceEqual(new[] { "ReferenceOwners|Cases.Root" })) == 2;
        foreach (string name in new[] { "Root", "Leaf", "Chain", "CycleA", "CycleB", "ArrayHolder", "ClosedHolder" })
        {
            var type = fixtureCurrent.Types.Single(row => row.Identity == "Cases." + name);
            checks["selected-" + name] = fixturePlan.CurrentStorageTypeTokens.Contains(type.Token);
            checks["constructor-selected-" + name] = fixtureCurrent.Methods.Where(method => method.DeclaringTypeStableId == type.StableId)
                .All(method => fixturePlan.CurrentExecutionMethodTokens.Contains(method.Token));
        }
        foreach (string name in new[] { "GenericOwner`1", "Unrelated", "ScalarHolder" })
        {
            var type = fixtureCurrent.Types.Single(row => row.Identity == "Cases." + name);
            checks["unaffected-storage-" + name] = !fixturePlan.CurrentStorageTypeTokens.Contains(type.Token);
        }
        PackageBinding(fixturePlan, fixtureBefore, fixtureCurrent, "cycle-package-binding");
        checks["fixture-no-unsupported-changes"] = fixture.UnsupportedChanges.Length == 0;
        bool passed = checks.Values.All(value => value);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, plan, fixturePlan,
            beforePath, beforeSha256 = Hash(beforePath), currentPath, currentSha256 = Hash(currentPath),
            hostSha256 = Hash(typeof(ReferenceOwnerPlanTests).Assembly.Location),
            scope = "Existing coroutine owner, typed reference chains/cycles and closed signatures; unchanged generic definitions and unrelated storage retain AOT eligibility"
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks.Where(check => !check.Value)) Console.WriteLine("FAILED: " + check.Key);
        Console.WriteLine("Reference owner plan: " + passed + ", checks=" + checks.Count);
        return passed ? 0 : 1;
    }
}
