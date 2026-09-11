using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class CrossAssemblyParentWorkflow
{
    private const string Model = "HybridCLR.ValueLayoutModel", Other = "HybridCLR.ValueLayoutOther";
    private const string Parent = "HybridCLR.Lab.CrossAssemblyParents.CrossParent";
    private const string Owner = "HybridCLR.Lab.VirtualSignatures.Processor";
    private const string Root = "HybridCLR.Lab.VirtualSignatures.ProcessorRoot";
    private const string Prefix = "DHE cross parent check: ";
    private static readonly string[] Expected = {
        "current-parent", "parent-assembly", "root-assembly", "parent-qualified-lookup", "owner-assembly-has-no-parent-definition",
        "logical-parent-cast", "root-contract", "parent-constructor-once", "parent-field-defaults", "child-field-layout",
        "field-owner", "field-owner-assembly", "field-read", "field-write", "method-owner", "method-invoke", "property-owner",
        "property-roundtrip", "event-owner", "event-roundtrip", "declared-only", "private-parent-filtered", "inherited-static-property",
        "root-virtual", "interface-virtual", "generic-root-parent", "generic-root-value", "root-method-definition",
        "reflection-construction", "parent-reference-gc", "independent-instances", "constructor-exception", "child-data-preserved"
    };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
    private static string[] Observed(IEnumerable<string> lines) => lines.Where(line => line.StartsWith(Prefix))
        .Select(line => line[Prefix.Length..]).ToArray();

    internal static int Current(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("cross-parent-current <lab> <Base proof> <original deletion Current> <Unity editor> <new output>");
        string lab = Path.GetFullPath(args[0]), original = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        TypeDeletionWorkflow.ValidateCurrent(Path.Combine(original, Model + ".dll"));
        string identityPath = Path.Combine(args[1], "base/build-identity.json");
        var identity = Read(identityPath);
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        var inputs = Directory.GetFiles(original, "*.dll").ToDictionary(path => path, Hash);
        foreach (string file in inputs.Keys) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
        string modelPath = Path.Combine(current, Model + ".dll"), otherPath = Path.Combine(current, Other + ".dll");
        string[] References() => snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path)
            .Concat(Directory.GetFiles(current, "*.dll")).ToArray();
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "CrossAssemblyParentDefinitions", otherPath,
            References(), Path.Combine(output, "compiled-parent"), false);
        FrozenStaticWorkflow.CompileAndMerge(lab, args[3], "CrossAssemblyParentCases", modelPath,
            References(), Path.Combine(output, "compiled-cases"), false);
        string merged = Path.Combine(output, "merged"); Directory.CreateDirectory(merged);
        foreach (string file in Directory.GetFiles(current, "*.dll")) File.Copy(file, Path.Combine(merged, Path.GetFileName(file)));
        using (var model = ModuleDefMD.Load(modelPath))
        using (var other = ModuleDefMD.Load(otherPath))
        {
            var receiver = model.Find(Owner, false)!; var parent = other.Find(Parent, false)!;
            if (receiver.BaseType?.FullName != Root || parent.BaseType?.FullName != Root || parent.BaseType.DefinitionAssembly?.Name != Model)
                throw new InvalidDataException("Expected the original Model root on both declarations.");
            var ctor = receiver.Methods.Single(method => method.IsInstanceConstructor);
            var calls = ctor.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.Name == ".ctor" && method.DeclaringType.FullName == Root).ToArray();
            var entry = model.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            var previous = entry.Body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call &&
                instruction.Operand is IMethod method && method.DeclaringType.FullName == "HybridCLR.Lab.TypeDeletion.Cases" && method.Name == "RunIfRequested").ToArray();
            if (calls.Length != 1 || previous.Length != 1) throw new InvalidDataException("Expected the original root constructor and deletion probe.");
            var importer = new Importer(model);
            receiver.BaseType = importer.Import(parent); calls[0].Operand = importer.Import(parent.Methods.Single(method => method.IsInstanceConstructor));
            entry.Body.Instructions.Remove(previous[0]);
            int insertion = entry.Body.Instructions.Count - 2;
            if (insertion < 0 || !entry.Body.Instructions[insertion].IsLdcI4() || entry.Body.Instructions.Last().OpCode != OpCodes.Ret)
                throw new InvalidDataException("Unexpected Current entry shape.");
            entry.Body.Instructions.Insert(insertion, Instruction.Create(OpCodes.Call,
                model.Find("HybridCLR.Lab.CrossAssemblyParents.Cases", false)!.Methods.Single(method => method.Name == "RunIfRequested")));
            model.Write(modelPath);
        }
        var childSnapshot = MetaVersionSnapshot.Create(modelPath); var parentSnapshot = MetaVersionSnapshot.Create(otherPath);
        if (childSnapshot.TypeParents[Owner].AssemblyName != Other || childSnapshot.TypeParents[Owner].TypeName != Parent ||
            parentSnapshot.TypeParents[Parent].AssemblyName != Model || parentSnapshot.TypeParents[Parent].TypeName != Root)
            throw new InvalidDataException("Final cross-assembly ancestry is incorrect.");
        if (inputs.Any(row => Hash(row.Key) != row.Value)) throw new InvalidDataException("Original Current changed.");
        File.WriteAllText(Path.Combine(output, "current-evidence.json"), JsonSerializer.Serialize(new {
            inputs, current = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash),
            merged = Directory.GetFiles(merged, "*.dll").ToDictionary(Path.GetFileName, Hash),
            baseIdentitySha256 = Hash(identityPath), snapshotSha256 = snapshot.Sha256,
            hostSha256 = Hash(typeof(CrossAssemblyParentWorkflow).Assembly.Location),
            scope = "Unity-compiled cross-assembly parent and probes, explicit parent/constructor/entry wiring; original Current and ordinary AOT unchanged"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(current); return 0;
    }

    internal static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("cross-parent-reference <Current DLL root> <Base Native DLL> <new output>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output); string previous = Path.Combine(output, "framework");
        int prior = FrameworkCallbackWorkflow.Reference(new[] { args[0], args[1], previous });
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(value => value.GetName().Name == Model);
        string error = null; string[] checks = Array.Empty<string>();
        using var trace = new StringWriter(); var original = Console.Out;
        try
        {
            Console.SetOut(trace);
            checks = (string[])assembly.GetType("HybridCLR.Lab.CrossAssemblyParents.Cases", true)!.GetMethod("Run")!.Invoke(null, null)!;
        }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "parent.log"); File.WriteAllText(log, trace.ToString());
        string[] observed = Observed(trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')));
        bool passed = prior == 0 && error == null && checks.SequenceEqual(Expected) && observed.SequenceEqual(Expected);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, observed, expected = Expected, error, priorResultSha256 = Hash(Path.Combine(previous, "result.json")),
            logSha256 = Hash(log), hostSha256 = Hash(typeof(CrossAssemblyParentWorkflow).Assembly.Location),
            scope = "CLR cross-assembly parent plus full 18 framework,25 virtual-signature and46 business sequence"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Cross parent reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error); return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        bool cached = args.Length == 6 && args[5] == "cached";
        if (args.Length != 5 && !cached) throw new ArgumentException("cross-parent-replay <lab> <tool> <Base proof> <shared resource> <new output> [cached]");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output); string previous = Path.Combine(output, "framework");
        int prior = -1; string error = null;
        try
        {
            var previousArgs = args.Take(4).Append(previous);
            prior = FrameworkCallbackWorkflow.Replay((cached ? previousArgs.Append("virtual-signatures-cached") : previousArgs).ToArray());
        }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(previous, "virtual-business/player.json.log"), result = Path.Combine(previous, "result.json");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        string[] observed = Observed(lines);
        bool passed = prior == 0 && error == null && observed.SequenceEqual(Expected) && lines.Count(line => line == "DHE cross parent pass: " + Expected.Length) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, cached, checks = observed, expected = Expected, error,
            priorResultSha256 = File.Exists(result) ? Hash(result) : null, logSha256 = File.Exists(log) ? Hash(log) : null,
            hostSha256 = Hash(typeof(CrossAssemblyParentWorkflow).Assembly.Location),
            scope = "Immutable Player cross-assembly parent and full framework/virtual/business sequence; optional original root-only receiver cache"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Cross parent replay: " + passed + "; checks=" + observed.Length); return passed ? 0 : 1;
    }
}
