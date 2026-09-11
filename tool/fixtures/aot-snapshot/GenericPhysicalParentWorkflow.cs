using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class GenericPhysicalParentWorkflow
{
    private const string Model = "HybridCLR.ValueLayoutModel", Other = "HybridCLR.ValueLayoutOther";
    private const string Owner = "HybridCLR.Lab.VirtualSignatures.Processor", Root = "HybridCLR.Lab.VirtualSignatures.ProcessorRoot";
    private const string Parent = "HybridCLR.Lab.GenericPhysicalParents.GenericParent`1", Probe = "HybridCLR.Lab.GenericPhysicalParents.Cases";
    private const string Prefix = "DHE generic physical parent check: ";
    internal static readonly string[] Expected = {
        "closed-parent-identity", "cross-assembly-parent", "generic-definition", "logical-parent-casts", "immutable-root",
        "constructor-once", "inherited-default-layout", "child-layout", "generic-value-field", "generic-field-owner",
        "generic-field-read", "generic-field-write", "inherited-generic-method", "generic-method-owner", "generic-method-reflection",
        "inherited-generic-virtual", "generic-property-owner", "generic-property-roundtrip", "generic-parent-event", "root-virtual",
        "interface-virtual", "root-method-definition", "closed-static-isolation", "generic-root-value", "reflection-construction",
        "inherited-value-reference-gc", "independent-objects", "constructor-exception", "child-data-preserved"
    };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
    private static void Write(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));

    internal static int Current(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("generic-physical-parent-current <lab> <Base proof> <root-parent Current> <Unity editor> <new output>");
        string lab = Path.GetFullPath(args[0]), source = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        var inputs = Directory.GetFiles(source, "*.dll").ToDictionary(path => path, Hash);
        foreach (string path in inputs.Keys) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        string identityPath = Path.Combine(args[1], "base/build-identity.json"); var identity = Read(identityPath);
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string modelPath = Path.Combine(current, Model + ".dll"), otherPath = Path.Combine(current, Other + ".dll");
        string[] References() => snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path)
            .Concat(Directory.GetFiles(current, "*.dll")).ToArray();
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "GenericPhysicalParentDefinitions", otherPath,
            References(), Path.Combine(output, "compiled-parent"), false);
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "GenericPhysicalParentCases", modelPath,
            References(), Path.Combine(output, "compiled-cases"), false);
        using (var model = ModuleDefMD.Load(File.ReadAllBytes(modelPath)))
        using (var other = ModuleDefMD.Load(File.ReadAllBytes(otherPath)))
        {
            var owner = model.Find(Owner, false)!; var parent = other.Find(Parent, false)!;
            if (owner.BaseType?.FullName != Root || parent.BaseType?.FullName != Root)
                throw new InvalidDataException("Expected a common original root.");
            var ctor = owner.Methods.Single(method => method.IsInstanceConstructor);
            var call = ctor.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.Name == ".ctor" && method.DeclaringType.FullName == Root);
            var importer = new Importer(model);
            var closed = new TypeSpecUser(new GenericInstSig(new ClassSig(importer.Import(parent)),
                new ValueTypeSig(model.Find("HybridCLR.Lab.VirtualSignatures.Packet", false)!)));
            owner.BaseType = closed;
            call.Operand = new MemberRefUser(model, ".ctor", MethodSig.CreateInstance(model.CorLibTypes.Void), closed);
            var entry = model.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            var formerProbe = entry.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.DeclaringType.FullName == "HybridCLR.Lab.CrossAssemblyParentRemoval.Cases");
            formerProbe.Operand = model.Find(Probe, false)!.Methods.Single(method => method.Name == "RunIfRequested");
            model.Write(modelPath);
        }
        if (inputs.Any(row => Hash(row.Key) != row.Value)) throw new InvalidDataException("Input Current changed.");
        Write(Path.Combine(output, "current-evidence.json"), new { inputs,
            current = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash),
            snapshotSha256 = snapshot.Sha256, hostSha256 = Hash(typeof(GenericPhysicalParentWorkflow).Assembly.Location),
            scope = "Unity-compiled generic physical parent and probes; existing owner changed to GenericParent<Packet> across assemblies" });
        Console.WriteLine(current); return 0;
    }

    internal static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("generic-physical-parent-reference <Current root> <Base Native DLL> <new output>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        int prior = FrameworkCallbackWorkflow.Reference(new[] { args[0], args[1], Path.Combine(output, "framework") });
        var model = AssemblyLoadContext.Default.Assemblies.Single(assembly => assembly.GetName().Name == Model);
        string[] checks = Array.Empty<string>(); string error = null;
        using var trace = new StringWriter(); var original = Console.Out;
        try { Console.SetOut(trace); checks = (string[])model.GetType(Probe, true)!.GetMethod("Run")!.Invoke(null, null)!; }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        File.WriteAllText(Path.Combine(output, "parent.log"), trace.ToString());
        var observed = trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(Prefix)).Select(line => line[Prefix.Length..]).ToArray();
        bool passed = prior == 0 && error == null && checks.SequenceEqual(Expected) && observed.SequenceEqual(Expected);
        Write(Path.Combine(output, "result.json"), new { passed, checks, observed, expected = Expected, error,
            hostSha256 = Hash(typeof(GenericPhysicalParentWorkflow).Assembly.Location) });
        Console.WriteLine("Generic physical parent reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error); return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("generic-physical-parent-replay <lab> <tool> <Base proof> <shared resource> <new output>");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output); string framework = Path.Combine(output, "framework");
        int prior = -1; string error = null;
        try { prior = FrameworkCallbackWorkflow.Replay(args.Take(4).Append(framework).ToArray()); }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(framework, "virtual-business/player.json.log");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        var observed = lines.Where(line => line.StartsWith(Prefix)).Select(line => line[Prefix.Length..]).ToArray();
        bool passed = prior == 0 && error == null && observed.SequenceEqual(Expected) &&
            lines.Count(line => line == "DHE generic physical parent pass: " + Expected.Length) == 1;
        Write(Path.Combine(output, "result.json"), new { passed, observed, expected = Expected, error,
            logSha256 = File.Exists(log) ? Hash(log) : null,
            hostSha256 = Hash(typeof(GenericPhysicalParentWorkflow).Assembly.Location) });
        Console.WriteLine("Generic physical parent replay: " + passed + "; checks=" + observed.Length); return passed ? 0 : 1;
    }
}
