using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class UnityBehaviourWorkflow
{
    internal static int SerializationCurrent(string[] args)
    {
        if (args.Length < 5 || args.Length > 6 || args.Length == 6 && args[5] != "read")
            throw new ArgumentException("unity-serialization-current <lab> <Base proof> <Current DLL root> <editor> <new output> [read]");
        bool readOnly = args.Length == 6;
        string output = Path.GetFullPath(args[4]), current = Path.Combine(output, "current");
        if (Directory.Exists(output)) throw new IOException("Serialization fixture output must be new.");
        Directory.CreateDirectory(current);
        string identityPath = Path.Combine(args[1], "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        foreach (string path in Directory.GetFiles(args[2], "*.dll")) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        string model = Path.Combine(current, "HybridCLR.ValueLayoutModel.dll");
        string merged = Path.Combine(output, "merged/HybridCLR.ValueLayoutModel.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(merged)!); File.Copy(model, merged);
        var names = Directory.GetFiles(current, "*.dll").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[3]))!, "Data");
        string unityReferences = Path.Combine(data, "PlaybackEngines/WindowsStandaloneSupport/Variations/il2cpp/Managed");
        string facade = Path.Combine(data, "MonoBleedingEdge/lib/mono/unityaot-win32/Facades/netstandard.dll");
        FrozenStaticWorkflow.CompileAndMerge(Path.GetFullPath(args[0]), args[3], "UnitySerializationCases", merged,
            snapshot.Assemblies.Where(row => !row.Dhe && !names.Contains(row.AssemblyName) && row.AssemblyName != "netstandard").Select(row => {
                string complete = Path.Combine(unityReferences, row.AssemblyName + ".dll");
                return row.AssemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) && File.Exists(complete) ? complete : row.Path;
            }).Append(facade).Concat(Directory.GetFiles(current, "*.dll")), Path.Combine(output, "compiled"), false,
            readOnly ? "SERIALIZATION_READ_ONLY" : null);
        using var module = ModuleDefMD.Load(File.ReadAllBytes(merged));
        var entry = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
        var invoke = module.Find("HybridCLR.Lab.UnitySerialization.SerializationCases", false)!.Methods.Single(method => method.Name == "RunIfRequested");
        // Keep the initializer verification and all 46 existing business cases.
        // The new Unity-only call executes immediately before successful return.
        int insertion = entry.Body.Instructions.Count - 2;
        if (insertion < 0 || entry.Body.Instructions.Last().OpCode != OpCodes.Ret || !entry.Body.Instructions[insertion].IsLdcI4())
            throw new InvalidDataException("Unexpected Current entry shape.");
        entry.Body.Instructions.Insert(insertion, Instruction.Create(OpCodes.Call, invoke));
        module.Write(model);
        File.WriteAllText(Path.Combine(output, "entry-wiring.json"), JsonSerializer.Serialize(new {
            merged, mergedSha256 = MetaVersionSnapshot.FileSha256(merged), current = model,
            currentSha256 = MetaVersionSnapshot.FileSha256(model), readOnly,
            scope = "Append the optional native Unity probe after existing business cases; preserve the compiler's merged output separately"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(current); return 0;
    }

    internal static int PreparationInput(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("public-preparation-input <valid Model DLL> <new malformed DLL>");
        if (File.Exists(args[1])) throw new IOException("Malformed fixture must be new.");
        using var module = ModuleDefMD.Load(args[0]);
        var cycle = new TypeDefUser("HybridCLR.Lab.FaultInjection", "PreparationCycle")
            { Attributes = dnlib.DotNet.TypeAttributes.Public };
        module.Types.Add(cycle); cycle.BaseType = cycle;
        module.Write(args[1]);
        using var verified = ModuleDefMD.Load(args[1]);
        var definition = verified.Find(cycle.FullName, false)!;
        if (definition.BaseType.MDToken != definition.MDToken) throw new InvalidDataException("Fault injection lost its self-parent.");
        Console.WriteLine("Deliberate malformed metadata: self-parent verified; normal artifact admission is bypassed only by the Player fixture.");
        return 0;
    }

    internal static int Attributes(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("unity-behaviour-attributes <merged Model DLL> <new report>");
        if (File.Exists(args[1])) throw new IOException("Attribute report must be new.");
        using var module = ModuleDefMD.Load(args[0]);
        var records = module.GetTypes().SelectMany(type => type.Methods).SelectMany(method => method.CustomAttributes
            .Where(attribute => attribute.TypeFullName == "System.Runtime.CompilerServices.IteratorStateMachineAttribute")
            .Select(attribute => new { method = method.FullName, owner = (attribute.ConstructorArguments[0].Value as TypeSig)?.DefinitionAssembly?.Name.String,
                stateMachine = (attribute.ConstructorArguments[0].Value as TypeSig)?.ReflectionFullName })).ToArray();
        bool passed = records.Length != 0 && records.All(row => row.owner == module.Assembly.Name &&
            module.GetTypes().Any(type => type.ReflectionFullName == row.stateMachine));
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { passed, records }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Iterator attribute ownership: " + passed); return passed ? 0 : 1;
    }

    internal static int Compile(string[] args)
    {
        if (args.Length != 6 || (args[4] != "base" && args[4] != "current"))
            throw new ArgumentException("unity-behaviour-current <lab> <Base proof> <original DLL root> <editor> <base|current> <new output>");
        string output = Path.GetFullPath(args[5]), current = Path.Combine(output, "current");
        if (Directory.Exists(output)) throw new IOException("Unity fixture output must be new.");
        Directory.CreateDirectory(current);
        string identityPath = Path.Combine(args[1], "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        foreach (string path in Directory.GetFiles(args[2], "*.dll")) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        var names = Directory.GetFiles(current, "*.dll").Select(Path.GetFileNameWithoutExtension).ToHashSet();
        FrozenStaticWorkflow.CompileAndMerge(Path.GetFullPath(args[0]), args[3], "UnityBehaviourCases",
            Path.Combine(current, "HybridCLR.ValueLayoutModel.dll"),
            snapshot.Assemblies.Where(row => !row.Dhe && !names.Contains(row.AssemblyName)).Select(row => row.Path)
                .Concat(Directory.GetFiles(current, "*.dll")), Path.Combine(output, "compiled"), false,
            args[4] == "current" ? "UNITY_CASE_CURRENT" : null);
        Console.WriteLine(current); return 0;
    }

    internal static int BaseInputs(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("unity-behaviour-base-inputs <Base proof> <Unity Current DLL root> <new output>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Base inputs must be new.");
        Directory.CreateDirectory(output);
        foreach (string file in Directory.GetFiles(args[1], "*.dll")) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        string identityPath = Path.Combine(args[0], "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        var native = snapshot.Assemblies.Single(row => row.AssemblyName == "HybridCLR.ValueLayoutNative");
        File.Copy(native.Path, Path.Combine(output, native.AssemblyName + ".dll"));
        string model = Path.Combine(output, "HybridCLR.ValueLayoutModel.dll");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(model));
        var entry = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
        entry.Body = new CilBody();
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Call, module.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)!.Methods.Single(method => method.Name == "Verify")));
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 59)); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        module.Write(model); Console.WriteLine(output); return 0;
    }
}
