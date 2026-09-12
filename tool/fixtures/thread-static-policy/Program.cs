using System.Text.Json;
using System.Security.Cryptography;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;
using System.Runtime.Loader;

internal static class ThreadStaticPolicyTests
{
    internal static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "make-current") return MakeCurrent(args[1], args[2]);
        if (args.Length == 4 && args[0] == "reference") return Reference(args[1], args[2], args[3]);
        if (args.Length == 3 && args[0] == "noop-policy") return NoopPolicy(args[1], args[2]);
        if (args.Length != 3) throw new ArgumentException("<Base proof> <evolved Current with original TLS owner> <new output>");
        string proof = Path.GetFullPath(args[0]), current = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string identityPath = Path.Combine(proof, "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(identityPath));
        var names = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!), names)!;
        string[] beforeFiles = names.Select(name => Path.Combine(proof, "base/baseline", name + ".dll")).ToArray();
        string[] afterFiles = names.Select(name => Path.Combine(current, name + ".dll")).ToArray();
        var before = beforeFiles.Select(MetaVersionSnapshot.Create).ToArray();
        var after = afterFiles.Select(MetaVersionSnapshot.Create).ToArray();
        var plan = ResourceExecutionPlanner.Compile(beforeFiles, afterFiles, snapshot.OrdinaryAssemblyPaths);
        var checks = new Dictionary<string, bool>();
        void Require(string name, bool ok) { checks[name] = ok; if (!ok) throw new InvalidDataException(name); }
        const string modelName = "HybridCLR.ValueLayoutModel";
        var baseModel = before.Single(row => row.AssemblyName == modelName);
        var nextModel = after.Single(row => row.AssemblyName == modelName);
        var field = nextModel.Fields.Single(row => row.IsThreadStatic && row.FieldType == "HybridCLR.Lab.ValueLayout.Payload");
        Require("same-existing-tls-owner-and-field", baseModel.Fields.Any(row => row.StableId == field.StableId && row.IsThreadStatic));
        Require("actual-layout-change-detected", plan.Impact.ChangedValueTypes.Length > 0);
        Require("affected-existing-tls-field-detected", plan.Impact.StaticValueFields.Any(row => !row.OrdinaryAot && row.ThreadStatic));
        Require("hotfix-tls-not-rejected-as-native-slot", !plan.UnsupportedChanges.Any(reason => reason.StartsWith("current-storage-thread-static-value-field:")));
        string changed = Path.Combine(output, modelName + ".dll");
        using (var module = ModuleDefMD.Load(afterFiles.Single(path => Path.GetFileNameWithoutExtension(path) == modelName)))
        {
            var definition = module.GetTypes().SelectMany(type => type.Fields).Single(row => row.MDToken.Raw == field.Token);
            definition.CustomAttributes.Remove(definition.CustomAttributes.Single(attribute => attribute.TypeFullName == "System.ThreadStaticAttribute"));
            module.Write(changed);
        }
        var withoutTls = MetaVersionSnapshot.Create(changed);
        var compatibility = ResourceUpdateCompatibility.Analyze(baseModel, withoutTls);
        Require("tls-to-shared-static-transition-still-rejected", compatibility.UnsupportedChanges.Any(reason => reason == "existing-field-metadata-change:" + field.Identity));
        compatibility = ResourceUpdateCompatibility.Analyze(withoutTls, nextModel);
        Require("shared-to-tls-transition-still-rejected", compatibility.UnsupportedChanges.Any(reason => reason == "existing-field-metadata-change:" + field.Identity));
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, checks,
            snapshotSha256 = snapshot.Sha256, identitySha256 = Hash(identityPath), current = afterFiles.ToDictionary(path => Path.GetFileName(path)!, Hash),
            hostSha256 = Hash(typeof(ThreadStaticPolicyTests).Assembly.Location), scope = "Static policy only; actual TLS allocation and GC require the immutable Player suite." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("TLS policy checks passed: " + checks.Count);
        return 0;
    }

    private static int MakeCurrent(string source, string output)
    {
        if (!Directory.Exists(source) || Directory.Exists(output)) throw new IOException("Require original Current and a new output directory.");
        Directory.CreateDirectory(output);
        foreach (string path in Directory.GetFiles(source, "*.dll")) File.Copy(path, Path.Combine(output, Path.GetFileName(path)));
        string file = Path.Combine(output, "HybridCLR.ValueLayoutModel.dll");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(file));
        var suite = module.Find("HybridCLR.Lab.ResourceCases.FrozenResourceCases", false)!;
        var callback = suite.NestedTypes.SelectMany(type => type.Methods).Single(method => method.Name == "<Run>b__0");
        if (!callback.HasThis || callback.MethodSig.Params.Count != 2 || callback.MethodSig.Params[0].ElementType != ElementType.String)
            throw new InvalidDataException("Unknown archived callback shape.");
        var first = callback.Body.Instructions[0];
        var equality = new MemberRefUser(module, "op_Equality", MethodSig.CreateStatic(module.CorLibTypes.Boolean,
            module.CorLibTypes.String, module.CorLibTypes.String), module.CorLibTypes.String.TypeDefOrRef);
        var prefix = new[] { Instruction.Create(OpCodes.Ldarg_1), Instruction.Create(OpCodes.Ldstr, "thread-static-current-value-isolation"),
            Instruction.Create(OpCodes.Call, equality), Instruction.Create(OpCodes.Brtrue, first), Instruction.Create(OpCodes.Ret) };
        for (int i = prefix.Length - 1; i >= 0; i--) callback.Body.Instructions.Insert(0, prefix[i]);
        var entry = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
        entry.Body = new CilBody();
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Call, suite.Methods.Single(method => method.Name == "Run")));
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 73));
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        module.Write(file);
        Console.WriteLine("Isolated TLS Current: " + output);
        return 0;
    }

    private static int NoopPolicy(string proof, string output)
    {
        string identityPath = Path.Combine(proof, "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        using var core = ModuleDefMD.Load(snapshot.Assemblies.Single(row => row.AssemblyName == "mscorlib").ReadVerifiedBytes());
        var owner = core.Find("System.Collections.Generic.List`1/Enumerator", false)!;
        var dispose = owner.Methods.Single(method => method.Name == "Dispose");
        var checks = new Dictionary<string, bool>();
        void Require(string name, bool ok) { checks[name] = ok; if (!ok) throw new InvalidDataException(name); }
        bool Empty(MethodDef method) => FrozenAotAdaptation.IsStorageIndependentEmptyValueMethod(method);
        Require("actual-base-dispose-is-storage-independent", Empty(dispose));
        Require("actual-move-next-still-needs-adaptation", !Empty(owner.Methods.Single(method => method.Name == "MoveNext")));
        Require("constructors-never-exempt", !Empty(owner.Methods.Single(method => method.IsInstanceConstructor)));
        var implementation = dispose.ImplAttributes;
        dispose.ImplAttributes |= MethodImplAttributes.Synchronized;
        Require("synchronized-methods-never-exempt", !Empty(dispose)); dispose.ImplAttributes = implementation;
        var convention = dispose.MethodSig.CallingConvention;
        dispose.MethodSig.CallingConvention = (convention & ~CallingConvention.Mask) | CallingConvention.VarArg;
        Require("vararg-methods-never-exempt", !Empty(dispose)); dispose.MethodSig.CallingConvention = convention;
        dispose.MethodSig.Params.Add(core.CorLibTypes.Int32);
        Require("parameters-never-exempt", !Empty(dispose)); dispose.MethodSig.Params.Clear();
        dispose.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Nop));
        Require("debug-nop-does-not-change-proof", Empty(dispose));
        dispose.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldarg_0));
        dispose.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Pop));
        Require("receiver-reading-methods-never-exempt", !Empty(dispose));
        dispose.Body.Instructions.RemoveAt(0); dispose.Body.Instructions.RemoveAt(0);
        var initializer = new MethodDefUser(".cctor", MethodSig.CreateStatic(core.CorLibTypes.Void),
            MethodImplAttributes.IL, MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName) { Body = new CilBody() };
        initializer.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); owner.Methods.Add(initializer);
        Require("owners-with-type-initializers-never-exempt", !Empty(dispose));
        if (File.Exists(output)) throw new IOException("Output must be new.");
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed = true, checks, snapshotSha256 = snapshot.Sha256,
            scope = "Native eligibility proof for frozen empty value methods only; not a Player result." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Empty frozen method checks: " + checks.Count); return 0;
    }

    private static int Reference(string current, string native, string output)
    {
        current = Path.GetFullPath(current); native = Path.GetFullPath(native);
        if (File.Exists(output)) throw new IOException("Reference output must be new.");
        AssemblyLoadContext.Default.Resolving += (_, name) =>
            AssemblyLoadContext.Default.LoadFromAssemblyPath(name.Name == "HybridCLR.ValueLayoutNative" ? native : Path.Combine(current, name.Name + ".dll"));
        var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(current, "HybridCLR.ValueLayoutModel.dll"));
        using var trace = new StringWriter(); var original = Console.Out;
        int revision;
        try
        {
            Console.SetOut(trace);
            revision = (int)assembly.GetType("HybridCLR.Lab.ValueLayout.Factory", true)!.GetMethod("GetRevision")!.Invoke(null, null)!;
        }
        finally { Console.SetOut(original); }
        string[] records = trace.ToString().Split('\n').Select(line => line.TrimEnd('\r'))
            .Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).ToArray();
        bool passed = revision == 73 && records.SequenceEqual(new[] { "thread-static-current-value-isolation" });
        File.WriteAllText(output, JsonSerializer.Serialize(new { passed, revision, records }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("TLS reference: " + passed);
        return passed ? 0 : 1;
    }
}
