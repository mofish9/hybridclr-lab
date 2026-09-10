using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class FrozenStaticWorkflow
{
    internal const string ProbeName = "HybridCLR.Lab.ResourceCases.FrozenStaticCases";
    internal const int CaseCount = 15;
    private const string NativeName = "HybridCLR.ValueLayoutNative";
    private const string ModelName = "HybridCLR.ValueLayoutModel";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static AotAnalysisSnapshot Snapshot(string proof)
    {
        string identityPath = Path.Combine(proof, "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        return AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
    }
    internal static void CompileAndMerge(string lab, string editor, string workload, string targetPath,
        IEnumerable<string> references, string output, bool setEntry, string defines = null, bool mergeModule = false)
    {
        Directory.CreateDirectory(output);
        string source = Path.Combine(lab, "tool/fixtures/aot-snapshot/Workloads", workload + ".cs");
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(editor))!, "Data");
        string host = Path.Combine(data, "NetCoreRuntime/dotnet.exe"), compiler = Path.Combine(data, "DotNetSdkRoslyn/csc.dll");
        string library = Path.Combine(output, workload + ".dll");
        var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in new[] { compiler, "-nologo", "-noconfig", "-nostdlib+", "-target:library", "-optimize+", "-debug-",
            "-utf8output", "-deterministic+", "-langversion:9.0", "-out:" + library }
            .Concat(string.IsNullOrEmpty(defines) ? Array.Empty<string>() : new[] { "-define:" + defines })
            .Concat(references.Select(path => "-r:" + (Path.GetFileNameWithoutExtension(path) == ModelName ? "model=" : "") + path)).Append(source))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException(workload); }
        string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        File.WriteAllText(Path.Combine(output, "compile.log"), log);
        if (process.ExitCode != 0) throw new InvalidOperationException(log);
        using (var target = ModuleDefMD.Load(File.ReadAllBytes(targetPath)))
        using (var compiled = ModuleDefMD.Load(File.ReadAllBytes(library)))
        {
            foreach (TypeRef reference in compiled.GetTypeRefs())
                if (reference.ResolutionScope == compiled || reference.DefinitionAssembly?.Name == compiled.Assembly.Name)
                    reference.ResolutionScope = target;
            foreach (var type in compiled.Types.Where(type => type.Name != "<Module>").ToArray())
            {
                if (target.GetTypes().Any(existing => existing.FullName == type.FullName)) throw new InvalidDataException("Duplicate fixture type: " + type.FullName);
                compiled.Types.Remove(type); target.Types.Add(type);
            }
            if (mergeModule)
            {
                if (target.GlobalType.HasMethods || target.GlobalType.HasFields || compiled.GlobalType.HasFields)
                    throw new InvalidDataException("Module fixture requires an empty target global type and no global fields.");
                foreach (var method in compiled.GlobalType.Methods.ToArray())
                { compiled.GlobalType.Methods.Remove(method); target.GlobalType.Methods.Add(method); }
                var entry = target.Find("HybridCLR.Lab.ValueLayout.Factory", false)?.Methods.Single(method => method.Name == "GetRevision");
                if (entry != null)
                    entry.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call,
                        target.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)!.Methods.Single(method => method.Name == "Verify")));
            }
            if (setEntry)
            {
                var entry = target.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
                entry.Body = new CilBody();
                foreach (string typeName in new[] { FrozenResourceCasesCompiler.ProbeName, FrozenAddedAssemblyCompiler.CallerName, ProbeName })
                {
                    entry.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target.Find(typeName, false)!.Methods.Single(method => method.Name == "Run")));
                    entry.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
                }
                entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 73)); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            }
            target.Write(targetPath);
        }
        File.WriteAllText(Path.Combine(output, "compiler-evidence.json"), JsonSerializer.Serialize(new {
            source, sourceSha256 = Hash(source), defines, mergeModule, compilerSha256 = Hash(compiler), hostSha256 = Hash(host),
            librarySha256 = Hash(library), targetPath, targetSha256 = Hash(targetPath),
            scope = "Real Unity compiler fixture; Base ordinary types are merged only before Base construction"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    internal static int NewBase(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("frozen-static-new-base <lab> <package> <editor> <runtime manifest> <old proof> <new output>");
        string lab = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[4]), inputs = Path.GetFullPath(args[5]) + ".inputs";
        if (Directory.Exists(inputs)) throw new IOException("Base inputs must be new.");
        Directory.CreateDirectory(inputs);
        var snapshot = Snapshot(proof);
        foreach (var source in snapshot.Assemblies.Where(row => row.Dhe || row.AssemblyName == NativeName))
            File.Copy(source.Path, Path.Combine(inputs, source.AssemblyName + ".dll"));
        CompileAndMerge(lab, args[2], "FrozenStaticNative", Path.Combine(inputs, NativeName + ".dll"),
            snapshot.Assemblies.Select(row => row.Path), Path.Combine(inputs, "compiled"), false);
        return UnityWorkflow.Run(args.Take(4).Concat(new[] { inputs, args[5], "41", ":all-ordinary-guards:" }).ToArray());
    }

    internal static int Current(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("compile-frozen-static-current <lab> <Base proof> <editor> <31-case Current root> <new output>");
        string lab = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[1]), original = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Current output must be new.");
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        var snapshot = Snapshot(proof);
        var hashes = Directory.GetFiles(original, "*.dll").ToDictionary(Path.GetFileName, Hash);
        foreach (string file in Directory.GetFiles(original, "*.dll")) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
        CompileAndMerge(lab, args[2], "FrozenStaticCases", Path.Combine(current, ModelName + ".dll"),
            snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path).Concat(Directory.GetFiles(current, "*.dll")),
            Path.Combine(output, "compiled"), true);
        if (hashes.Any(row => Hash(Path.Combine(original, row.Key!)) != row.Value)) throw new InvalidDataException("Original Current changed.");
        Console.WriteLine(current);
        return 0;
    }
}
