using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class StaticStoragePolicy
{
    public static int Run(string[] args)
    {
        if (args.Length != 3 && args.Length != 4) throw new ArgumentException("static-storage-policy <Base DLL root> <Current DLL root> <new output> [full AOT snapshot assemblies]");
        string baseline = Path.GetFullPath(args[0]), current = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string[] names = { "HybridCLR.ValueLayoutModel", "HybridCLR.ValueLayoutOther", "HybridCLR.ValueLayoutConsumer" };
        string[] Paths(string root) => names.Select(name => Path.Combine(root, name + ".dll")).ToArray();
        string native = Path.Combine(args.Length == 4 ? Path.GetFullPath(args[3]) : baseline, "HybridCLR.ValueLayoutNative.dll");
        var accepted = ResourceExecutionPlanner.Compile(Paths(baseline), Paths(current), new[] { native });
        var checks = new Dictionary<string, bool>
        {
            ["hotfix-static-storage-accepted"] = accepted.UnsupportedChanges.Length == 0,
            ["three-inline-static-fields-detected"] = accepted.Impact.StaticValueFields.Count(field => !field.OrdinaryAot) == 3,
            ["ordinary-unchanged-neighbor-remains-aot"] = !accepted.Impact.Methods.Any(method => method.AssemblyName == "HybridCLR.ValueLayoutNative" && method.MethodIdentity.Contains("StaticNeighbor")),
            ["unchanged-copy-body-selected"] = accepted.Impact.Methods.Any(method => method.MethodIdentity.Contains("StaticValueState::Copy") && method.Decision == "interpret"),
            ["generic-access-context-selected"] = accepted.Impact.Methods.Any(method => method.MethodIdentity.Contains("StaticGenericArgument`1::Get") && method.Decision == "inspect-generic-context"),
        };
        string threadModel = Path.Combine(output, "thread", names[0] + ".dll"); Directory.CreateDirectory(Path.GetDirectoryName(threadModel)!);
        using (var module = ModuleDefMD.Load(Path.Combine(current, names[0] + ".dll")))
        {
            var field = module.GetTypes().Single(type => type.Name == "StaticValueState").Fields.Single(field => field.Name == "Value");
            var attributeType = new TypeRefUser(module, "System", "ThreadStaticAttribute", module.CorLibTypes.AssemblyRef);
            field.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), attributeType)));
            module.Write(threadModel);
        }
        var thread = ResourceExecutionPlanner.Compile(Paths(baseline), new[] { threadModel }.Concat(Paths(current).Skip(1)), new[] { native });
        checks["thread-static-value-still-rejected"] = thread.UnsupportedChanges.Any(reason => reason.StartsWith("current-storage-thread-static-value-field:"));
        string nativeWithStatic = Path.Combine(output, "HybridCLR.ValueLayoutNative.dll");
        using (var module = ModuleDefMD.Load(native))
        {
            var owner = module.GetTypes().Single(type => type.Name == "NativeInlineOwner");
            var modelRef = module.GetAssemblyRefs().Single(assembly => assembly.Name == names[0]);
            owner.Fields.Add(new FieldDefUser("StaticValue", new FieldSig(new ValueTypeSig(
                new TypeRefUser(module, "HybridCLR.Lab.ValueLayout", "StaticPayload", modelRef))), FieldAttributes.Public | FieldAttributes.Static));
            module.Write(nativeWithStatic);
        }
        var ordinary = ResourceExecutionPlanner.Compile(Paths(baseline), Paths(current), new[] { nativeWithStatic });
        checks["ordinary-static-storage-still-rejected"] = ordinary.UnsupportedChanges.Any(reason => reason.StartsWith("current-storage-ordinary-aot-static-field:"));
        if (args.Length == 4)
        {
            string[] fullAot = Directory.GetFiles(Path.GetFullPath(args[3]), "*.dll")
                .Where(path => !names.Contains(Path.GetFileNameWithoutExtension(path))).ToArray();
            var full = ResourceExecutionPlanner.Compile(Paths(baseline), Paths(current), fullAot);
            checks["full-stripped-aot-snapshot-accepted"] = full.UnsupportedChanges.Length == 0;
            checks["full-snapshot-does-not-invent-primitive-layout-changes"] =
                full.Impact.Layouts.All(layout => !layout.TypeIdentity.StartsWith("mscorlib|"));
        }
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, impact = accepted.Impact,
            accepted.UnsupportedChanges, threadErrors = thread.UnsupportedChanges, ordinaryErrors = ordinary.UnsupportedChanges }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
        return passed ? 0 : 1;
    }
}
