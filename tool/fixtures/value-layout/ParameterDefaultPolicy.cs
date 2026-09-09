using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class ParameterDefaultPolicy
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("parameter-default-policy <Base Model DLL> <new output>");
        string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var before = MetaVersionSnapshot.Create(input);
        var checks = new Dictionary<string, bool>();
        ResourceUpdateCompatibility Mutation(string name, Action<MethodDef> edit)
        {
            using var module = ModuleDefMD.Load(input);
            var method = module.GetTypes().Single(type => type.Name == "ResourceEntity")
                .Methods.Single(method => method.Name == "ExistingDefault");
            edit(method);
            string path = Path.Combine(output, name + ".dll"); module.Write(path);
            return ResourceUpdateCompatibility.Analyze(before, MetaVersionSnapshot.Create(path));
        }
        var changed = Mutation("default", method => method.ParamDefs.Single(p => p.Sequence == 1).Constant = new ConstantUser(17));
        const string capability = "current-parameter-default-metadata-v1";
        checks["constant-change-accepted"] = changed.Compatible && changed.RequiredRuntimeCapabilities.Contains(capability);
        var removed = Mutation("remove-default", method =>
        {
            var parameter = method.ParamDefs.Single(p => p.Sequence == 1);
            parameter.Attributes &= ~(ParamAttributes.Optional | ParamAttributes.HasDefault);
            parameter.Constant = null;
        });
        checks["default-removal-accepted"] = removed.Compatible && removed.RequiredRuntimeCapabilities.Contains(capability);
        bool Rejected(ResourceUpdateCompatibility value) => value.UnsupportedChanges.Any(reason => reason.StartsWith("existing-method-metadata-change:"));
        checks["access-change-rejected"] = Rejected(Mutation("access", method => method.Access = MethodAttributes.Private));
        checks["implementation-change-rejected"] = Rejected(Mutation("impl", method => method.ImplAttributes |= MethodImplAttributes.Synchronized));
        checks["parameter-in-out-change-rejected"] = Rejected(Mutation("parameter-flags", method => method.ParamDefs.Single(p => p.Sequence == 1).Attributes |= ParamAttributes.Out));
        checks["parameter-name-change-still-rejected"] = Rejected(Mutation("parameter-name", method => method.ParamDefs.Single(p => p.Sequence == 1).Name = "renamed"));
        checks["old-capabilities-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), changed.RequiredRuntimeCapabilities);
        checks["current-capabilities-accepted"] = ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities, changed.RequiredRuntimeCapabilities);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, input }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var item in checks) Console.WriteLine(item.Key + ": " + item.Value);
        return passed ? 0 : 1;
    }
}
