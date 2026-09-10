using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR;
using HybridCLR.DheTool;

internal static class HotfixGenericPlanTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("hotfix-generic <Base proof> <Current DLL root> <new output>");
        string baseline = Path.Combine(Path.GetFullPath(args[0]), "base/baseline");
        string current = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Generic plan output must be new.");
        Directory.CreateDirectory(output);
        string[] beforeFiles = Directory.GetFiles(baseline, "*.dll"), currentFiles = Directory.GetFiles(current, "*.dll");
        const string model = "HybridCLR.ValueLayoutModel";
        string beforePath = beforeFiles.Single(path => Path.GetFileNameWithoutExtension(path) == model);
        string currentPath = currentFiles.Single(path => Path.GetFileNameWithoutExtension(path) == model);
        var before = MetaVersionSnapshot.Create(beforePath);
        var after = MetaVersionSnapshot.Create(currentPath);
        var identity = after.Methods.Single(method => method.DeclaringType == "HybridCLR.Lab.ResourceCases.FrozenResourceCases" && method.Name == "Identity");
        var previous = before.Methods.Single(method => method.StableId == identity.StableId);
        var compilation = ResourceExecutionPlanner.Compile(beforeFiles, currentFiles, Array.Empty<string>());
        var plan = compilation.Plans[model];
        var checks = new Dictionary<string, bool>
        {
            ["real-Base-generic-method-unchanged"] = previous.Version == identity.Version,
            ["real-Base-generic-selected"] = plan.CurrentExecutionMethodTokens.Contains(identity.Token),
            ["real-Base-generic-is-conditional"] = plan.CurrentGenericContextMethodTokens.Contains(identity.Token),
            ["conditional-count-matches"] = plan.CurrentGenericContextMethodTokenCount == plan.CurrentGenericContextMethodTokens.Length,
        };
        DheExecutionPlan Package(ResourceExecutionPlan value) => UnityEngine.JsonUtility.FromJson<DheExecutionPlan>(
            JsonSerializer.Serialize(value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
        var package = Package(plan);
        package.Validate(model, before.ToBinary(), after.ToBinary());
        checks["compiler-package-binding"] = package.CanonicalBinding() == plan.CanonicalBinding();
        var compatibility = ResourceUpdateCompatibility.Analyze(before, after, currentAssemblySet: currentFiles.Select(MetaVersionSnapshot.Create),
            currentStorageTypes: after.Types.Where(type => plan.CurrentStorageTypeTokens.Contains(type.Token)).Select(type => type.StableId),
            currentExecutionMethodTokens: plan.CurrentExecutionMethodTokens,
            currentGenericContextMethodTokens: plan.CurrentGenericContextMethodTokens);
        checks["real-resource-compatible"] = compatibility.Compatible;
        checks["generic-capability-required"] = compatibility.RequiredRuntimeCapabilities.Contains(ResourceExecutionPlan.GenericContextCapability);
        checks["old-runtime-capability-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != ResourceExecutionPlan.GenericContextCapability),
            compatibility.RequiredRuntimeCapabilities);
        void RejectPackage(string name, Action<DheExecutionPlan> mutate)
        {
            var candidate = Package(plan); mutate(candidate);
            bool rejected = false;
            try { candidate.Validate(model, before.ToBinary(), after.ToBinary()); }
            catch (InvalidDataException) { rejected = true; }
            checks[name] = rejected;
        }
        RejectPackage("conditional-count-tamper", value => value.currentGenericContextMethodTokenCount++);
        RejectPackage("conditional-duplicate-tamper", value => {
            value.currentGenericContextMethodTokens = new[] { identity.Token, identity.Token };
            value.currentGenericContextMethodTokenCount = 2;
        });
        RejectPackage("conditional-outside-execution", value => {
            value.currentExecutionMethodTokens = value.currentExecutionMethodTokens.Where(token => token != identity.Token).ToArray();
            value.currentExecutionMethodTokenCount = value.currentExecutionMethodTokens.Length;
        });
        RejectPackage("conditional-selected-owner-rejected", value => {
            uint owner = after.Types.Single(type => type.StableId == identity.DeclaringTypeStableId).Token;
            value.currentStorageTypeTokens = value.currentStorageTypeTokens.Append(owner).Distinct().OrderBy(token => token).ToArray();
            value.currentStorageTypeTokenCount = value.currentStorageTypeTokens.Length;
        });

        string changedPath = Path.Combine(output, "changed-body.dll");
        using (var module = ModuleDefMD.Load(currentPath))
        {
            module.Find(identity.DeclaringType, false)!.Methods.Single(method => method.Name == identity.Name)
                .Body.Instructions.Insert(0, Instruction.Create(OpCodes.Nop));
            module.Write(changedPath);
        }
        var changed = MetaVersionSnapshot.Create(changedPath);
        var changedMethod = changed.Methods.Single(method => method.StableId == identity.StableId);
        var changedPlan = ResourceExecutionPlanner.Compile(beforeFiles, currentFiles.Select(path => path == currentPath ? changedPath : path),
            Array.Empty<string>()).Plans[model];
        checks["changed-body-remains-unconditional"] = changedPlan.CurrentExecutionMethodTokens.Contains(changedMethod.Token) &&
            !changedPlan.CurrentGenericContextMethodTokens.Contains(changedMethod.Token);
        var forged = Package(changedPlan);
        forged.currentGenericContextMethodTokens = new[] { changedMethod.Token };
        forged.currentGenericContextMethodTokenCount = 1;
        bool changedRejected = false;
        try { forged.Validate(model, before.ToBinary(), changed.ToBinary()); }
        catch (InvalidDataException) { changedRejected = true; }
        checks["changed-body-cannot-be-forged-conditional"] = changedRejected;

        // The same generic body references a changed type in another assembly.
        // Its local MV fingerprint stays equal, so that equality must never
        // replace the compiler's concrete dependency analysis.
        var crossFiles = new List<string[]>();
        foreach (bool evolved in new[] { false, true })
        {
            string directory = Path.Combine(output, evolved ? "cross-current" : "cross-base");
            Directory.CreateDirectory(directory);
            using var dependency = new ModuleDefUser("GenericDependency.dll") { Kind = ModuleKind.Dll };
            var dependencyAssembly = new AssemblyDefUser("GenericDependency", new Version(1, 0));
            dependencyAssembly.Modules.Add(dependency);
            var reference = new TypeDefUser("Cases", "Affected", dependency.CorLibTypes.Object.TypeDefOrRef)
                { Attributes = TypeAttributes.Public };
            dependency.Types.Add(reference);
            reference.Fields.Add(new FieldDefUser("Value", new FieldSig(dependency.CorLibTypes.Int32), FieldAttributes.Public));
            if (evolved) reference.Fields.Add(new FieldDefUser("Extra", new FieldSig(dependency.CorLibTypes.Int64), FieldAttributes.Public));
            string dependencyPath = Path.Combine(directory, "GenericDependency.dll"); dependency.Write(dependencyPath);
            using var caller = new ModuleDefUser("GenericCaller.dll") { Kind = ModuleKind.Dll };
            new AssemblyDefUser("GenericCaller", new Version(1, 0)).Modules.Add(caller);
            var owner = new TypeDefUser("Cases", "Caller", caller.CorLibTypes.Object.TypeDefOrRef)
                { Attributes = TypeAttributes.Public };
            caller.Types.Add(owner);
            var signature = MethodSig.CreateStatic(new GenericMVar(0), new GenericMVar(0));
            signature.Generic = true; signature.GenParamCount = 1;
            var method = new MethodDefUser("Identity", signature, MethodImplAttributes.IL,
                MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
            method.GenericParameters.Add(new GenericParamUser(0, GenericParamAttributes.NonVariant, "T"));
            owner.Methods.Add(method);
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldtoken,
                new TypeRefUser(caller, "Cases", "Affected", new AssemblyRefUser(dependencyAssembly))));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            string callerPath = Path.Combine(directory, "GenericCaller.dll"); caller.Write(callerPath);
            crossFiles.Add(new[] { dependencyPath, callerPath });
        }
        var crossBefore = MetaVersionSnapshot.Create(crossFiles[0][1]);
        var crossAfter = MetaVersionSnapshot.Create(crossFiles[1][1]);
        var crossMethod = crossAfter.Methods.Single(method => method.Name == "Identity");
        var crossPlan = ResourceExecutionPlanner.Compile(crossFiles[0], crossFiles[1], Array.Empty<string>());
        checks["cross-assembly-generic-fingerprint-unchanged"] = crossBefore.Methods.Single().Version == crossMethod.Version;
        checks["cross-assembly-concrete-dependency-detected"] = crossPlan.Impact.Methods.Any(method =>
            method.AssemblyName == "GenericCaller" && method.CurrentMethodToken == crossMethod.Token &&
            method.Decision == "interpret" && method.ChangedValueTypes.Length != 0);
        checks["cross-assembly-dependency-remains-unconditional"] =
            crossPlan.Plans["GenericCaller"].CurrentExecutionMethodTokens.Contains(crossMethod.Token) &&
            !crossPlan.Plans["GenericCaller"].CurrentGenericContextMethodTokens.Contains(crossMethod.Token);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed, checks, plan, changedPlan, crossPlan,
            beforePath, beforeSha256 = Hash(beforePath), currentPath, currentSha256 = Hash(currentPath),
            hostSha256 = Hash(typeof(HotfixGenericPlanTests).Assembly.Location),
            scope = "Real Base-66 planning, package binding, changed bodies and cross-assembly dependency admission; no native execution claim"
        }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks.Where(check => !check.Value)) Console.WriteLine("FAILED: " + check.Key);
        Console.WriteLine("Hotfix generic plan: " + passed + ", checks=" + checks.Count);
        return passed ? 0 : 1;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
