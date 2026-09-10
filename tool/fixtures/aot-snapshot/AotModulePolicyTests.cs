using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class AotModulePolicyTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("aot-module-policy <real Base module DLL> <new output>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Policy output must be new.");
        Directory.CreateDirectory(output);
        var original = MetaVersionSnapshot.Create(source);
        MetaVersionSnapshot Edit(string name, Action<ModuleDefMD> edit)
        {
            using var module = ModuleDefMD.Load(source); edit(module);
            string path = Path.Combine(output, name + ".dll"); module.Write(path); return MetaVersionSnapshot.Create(path);
        }
        var changed = Edit("changed-body", module => {
            foreach (var instruction in module.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)!.Methods
                .Where(method => method.HasBody).SelectMany(method => method.Body.Instructions))
                if (instruction.IsLdcI4() && instruction.GetLdcI4Value() == 101)
                { instruction.OpCode = OpCodes.Ldc_I4; instruction.Operand = 202; }
        });
        var changedCctor = Edit("changed-cctor", module => module.GlobalType.Methods.Single(method => method.IsStaticConstructor)
            .Body.Instructions.Insert(0, Instruction.Create(OpCodes.Nop)));
        var removed = Edit("removed-cctor", module => module.GlobalType.Methods.Clear());
        var constant = Edit("changed-constant", module => module.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)!
            .Fields.Single(field => field.Name == "ExpectedVersion").Constant = new ConstantUser(202));
        var values = new Dictionary<string, ResourceUpdateCompatibility>
        {
            ["no-op"] = ResourceUpdateCompatibility.Analyze(original, original),
            ["changed-helper"] = ResourceUpdateCompatibility.Analyze(original, changed),
            ["changed-module-cctor"] = ResourceUpdateCompatibility.Analyze(original, changedCctor),
            ["removed-module-cctor"] = ResourceUpdateCompatibility.Analyze(original, removed),
            ["added-module-cctor"] = ResourceUpdateCompatibility.Analyze(removed, original),
            ["changed-constant"] = ResourceUpdateCompatibility.Analyze(original, constant),
        };
        const string capability = "deferred-aot-module-initialization-v1";
        var checks = new Dictionary<string, bool>();
        foreach (var pair in values)
        {
            checks[pair.Key + ":compatible"] = pair.Value.Compatible;
            checks[pair.Key + ":module-capability-required"] = pair.Value.RequiredRuntimeCapabilities.Contains(capability);
            checks[pair.Key + ":module-token-capability-required"] = pair.Value.RequiredRuntimeCapabilities.Contains("aot-module-token-resolution-v1");
            checks[pair.Key + ":old-module-resolver-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v29",
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "aot-module-token-resolution-v1"), pair.Value.RequiredRuntimeCapabilities);
            checks[pair.Key + ":old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v27",
                ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), pair.Value.RequiredRuntimeCapabilities);
            checks[pair.Key + ":candidate-runtime-accepted"] = ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
                ResourceUpdateCompatibility.KnownRuntimeCapabilities, pair.Value.RequiredRuntimeCapabilities);
        }
        const string constantCapability = "current-literal-field-values-v1";
        checks["constant:capability-required"] = values["changed-constant"].RequiredRuntimeCapabilities.Contains(constantCapability);
        checks["constant:old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v28",
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != constantCapability),
            values["changed-constant"].RequiredRuntimeCapabilities);
        foreach (string mutation in new[] { "visibility", "literal-to-storage", "marshal", "offset", "missing-default" })
        {
            var invalid = Edit("constant-" + mutation, module => {
                var field = module.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)!
                    .Fields.Single(field => field.Name == "ExpectedVersion");
                field.Constant = new ConstantUser(202);
                switch (mutation)
                {
                    case "visibility": field.Access = FieldAttributes.Public; break;
                    case "literal-to-storage": field.IsLiteral = false; break;
                    case "marshal": field.MarshalType = new RawMarshalType(new byte[] { 7 }); break;
                    case "offset": field.FieldOffset = 4; break;
                    case "missing-default": field.HasDefault = false; field.Constant = null; break;
                }
            });
            checks["constant:" + mutation + "-rejected"] = !ResourceUpdateCompatibility.Analyze(original, invalid).Compatible;
        }
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks,
            unsupported = values.ToDictionary(pair => pair.Key, pair => pair.Value.UnsupportedChanges),
            scope = "Real module metadata mutations and required runtime capability; separate native/Player gates prove execution"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("AOT module policy: " + passed); return passed ? 0 : 1;
    }
}
