using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class GenericOwnerWorkflow
{
    private const string Model = "HybridCLR.ValueLayoutModel", Other = "HybridCLR.ValueLayoutOther";
    private const string Owner = "HybridCLR.Lab.GenericPhysicalParents.GenericParent`1";
    private const string Middle = "HybridCLR.Lab.GenericPhysicalParents.GenericMiddle`1";
    private const string Probe = "HybridCLR.Lab.GenericPhysicalParents.OwnerCases";
    private const string ParentProbe = "HybridCLR.Lab.GenericPhysicalParents.Cases";
    private const string Root = "HybridCLR.Lab.VirtualSignatures.ProcessorRoot";
    private const string Prefix = "DHE generic owner check: ";
    internal static readonly string[] Expected = new[] { "open-owner-parent" }.Concat(
        new[] { "value", "long", "reference" }.SelectMany(name => new[] { "parent", "constructor", "three-level-fields",
            "generic-methods", "field-reflection", "property-reflection", "method-reflection", "root-dispatch", "exception" }
            .Select(test => name + ":" + test))).Concat(new[] { "static-isolation", "physical-descendant-gc", "physical-descendant-parent-chain" }).ToArray();
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true }));

    internal static int Current(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("generic-owner-current <lab> <Base proof> <original generic Current> <Unity editor> <new output>");
        string lab = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        var inputs = Directory.GetFiles(args[2], "*.dll").ToDictionary(Path.GetFullPath, Hash);
        foreach (string path in inputs.Keys) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        string identityPath = Path.Combine(args[1], "base/build-identity.json"); var identity = Read(identityPath);
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string modelPath = Path.Combine(current, Model + ".dll"), otherPath = Path.Combine(current, Other + ".dll");
        string[] References() => snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path)
            .Concat(Directory.GetFiles(current, "*.dll")).ToArray();
        using (var model = ModuleDefMD.Load(File.ReadAllBytes(modelPath)))
        {
            var entry = model.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            var call = entry.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.DeclaringType.FullName == ParentProbe);
            call.Operand = model.Find("HybridCLR.Lab.CrossAssemblyParentRemoval.Cases", false)!
                .Methods.Single(method => method.Name == "RunIfRequested");
            model.Types.Remove(model.Find(ParentProbe, false)!); model.Write(modelPath);
        }
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "GenericOwnerDefinitions", otherPath,
            References(), Path.Combine(output, "compiled-definitions"), false);
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "GenericPhysicalParentCases", modelPath,
            References(), Path.Combine(output, "compiled-parent-cases"), false, "DHE_GENERIC_OWNER_PARENT");
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "GenericOwnerCases", modelPath,
            References(), Path.Combine(output, "compiled-owner-cases"), false);
        using (var other = ModuleDefMD.Load(File.ReadAllBytes(otherPath)))
        {
            var owner = other.Find(Owner, false)!; var middle = other.Find(Middle, false)!;
            if (owner.BaseType?.FullName != Root || owner.GenericParameters.Count != 1 || middle.BaseType?.FullName != Root)
                throw new InvalidDataException("Unexpected original generic owner.");
            var ctor = owner.Methods.Single(method => method.IsInstanceConstructor);
            var call = ctor.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.Name == ".ctor" && method.DeclaringType.FullName == Root);
            var parent = new TypeSpecUser(new GenericInstSig(new ClassSig(middle), new GenericVar(0)));
            owner.BaseType = parent;
            call.Operand = new MemberRefUser(other, ".ctor", MethodSig.CreateInstance(other.CorLibTypes.Void), parent);
            other.Write(otherPath);
        }
        using (var model = ModuleDefMD.Load(File.ReadAllBytes(modelPath)))
        {
            var entry = model.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            var call = entry.Body.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.DeclaringType.FullName == "HybridCLR.Lab.CrossAssemblyParentRemoval.Cases");
            call.Operand = model.Find(ParentProbe, false)!.Methods.Single(method => method.Name == "RunIfRequested");
            entry.Body.Instructions.Insert(entry.Body.Instructions.IndexOf(call) + 1,
                Instruction.Create(OpCodes.Call, model.Find(Probe, false)!.Methods.Single(method => method.Name == "RunIfRequested")));
            model.Write(modelPath);
        }
        if (inputs.Any(row => Hash(row.Key) != row.Value)) throw new InvalidDataException("Input Current changed.");
        Write(Path.Combine(output, "current-evidence.json"), new { inputs,
            current = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash),
            snapshotSha256 = snapshot.Sha256, hostSha256 = Hash(typeof(GenericOwnerWorkflow).Assembly.Location),
            scope = "Unity-compiled generic owner inserts GenericMiddle<T> under existing GenericParent<T>; original inputs unchanged" });
        Console.WriteLine(current); return 0;
    }

    internal static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("generic-owner-reference <Current> <Native DLL> <new output>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        int prior = GenericPhysicalParentWorkflow.Reference(args.Take(2).Append(Path.Combine(output, "parent")).ToArray());
        var model = AssemblyLoadContext.Default.Assemblies.Single(assembly => assembly.GetName().Name == Model);
        string error = null; string[] checks = Array.Empty<string>();
        using var trace = new StringWriter(); var original = Console.Out;
        try { Console.SetOut(trace); checks = (string[])model.GetType(Probe, true)!.GetMethod("Run")!.Invoke(null, null)!; }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        File.WriteAllText(Path.Combine(output, "owner.log"), trace.ToString());
        var observed = trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith(Prefix))
            .Select(line => line[Prefix.Length..]).ToArray();
        bool passed = prior == 0 && error == null && checks.SequenceEqual(Expected) && observed.SequenceEqual(Expected);
        Write(Path.Combine(output, "result.json"), new { passed, checks, observed, expected = Expected, error,
            hostSha256 = Hash(typeof(GenericOwnerWorkflow).Assembly.Location) });
        Console.WriteLine("Generic owner reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error); return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("generic-owner-replay <lab> <tool> <Base proof> <shared resource> <new output>");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output); string parent = Path.Combine(output, "parent");
        int prior = -1; string error = null;
        try { prior = GenericPhysicalParentWorkflow.Replay(args.Take(4).Append(parent).ToArray()); }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(parent, "framework/virtual-business/player.json.log");
        var lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        var observed = lines.Where(line => line.StartsWith(Prefix)).Select(line => line[Prefix.Length..]).ToArray();
        bool passed = prior == 0 && error == null && observed.SequenceEqual(Expected) &&
            lines.Count(line => line == "DHE generic owner pass: " + Expected.Length) == 1;
        Write(Path.Combine(output, "result.json"), new { passed, observed, expected = Expected, error,
            logSha256 = File.Exists(log) ? Hash(log) : null, hostSha256 = Hash(typeof(GenericOwnerWorkflow).Assembly.Location) });
        Console.WriteLine("Generic owner replay: " + passed + "; checks=" + observed.Length); return passed ? 0 : 1;
    }
}
