using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class FrozenAddedAssemblyCompiler
{
    public const string AssemblyName = "HybridCLR.ValueLayoutAdded";
    public const string CallerName = "HybridCLR.Lab.ResourceCases.FrozenAddedCaller";
    public const int CaseCount = 8;
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    public static int Run(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("compile-added-resource-current <lab> <proof> <Unity editor> <23-case Current> <new output>");
        string lab = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[1]), original = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Added Current output must be new.");
        string identityPath = Path.Combine(proof, "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string current = Path.Combine(output, "current"), compiled = Path.Combine(output, "compiled");
        Directory.CreateDirectory(current); Directory.CreateDirectory(compiled);
        var inputHashes = Directory.GetFiles(original, "*.dll").ToDictionary(Path.GetFileName, Hash);
        foreach (string file in Directory.GetFiles(original, "*.dll")) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
        if (File.Exists(Path.Combine(current, AssemblyName + ".dll"))) throw new InvalidDataException("Current already contains the new assembly.");
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[2]))!, "Data"), compiler = Path.Combine(data, "DotNetSdkRoslyn/csc.dll"),
            dotnet = Path.Combine(data, "NetCoreRuntime/dotnet.exe");
        var sources = new[] { "FrozenAddedAssembly.cs", "FrozenAddedCaller.cs" }.Select(name => Path.Combine(lab, "tool/fixtures/aot-snapshot/Workloads", name)).ToArray();
        void Compile(string source, string destination)
        {
            var start = new ProcessStartInfo(dotnet) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            string[] references = snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path).Concat(Directory.GetFiles(current, "*.dll")).ToArray();
            foreach (string argument in new[] { compiler, "-nologo", "-noconfig", "-nostdlib+", "-target:library", "-optimize+", "-debug-", "-utf8output",
                "-deterministic+", "-langversion:9.0", "-out:" + destination }.Concat(references.Select(path => "-r:" +
                    (Path.GetFileNameWithoutExtension(path) == "HybridCLR.ValueLayoutModel" ? "model=" : "") + path)).Append(source))
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException(source); }
            string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(compiled, Path.GetFileName(source) + ".log"), log);
            if (process.ExitCode != 0) throw new InvalidOperationException(log);
        }
        Compile(sources[0], Path.Combine(current, AssemblyName + ".dll"));
        string callerDll = Path.Combine(compiled, "FrozenAddedCaller.dll"); Compile(sources[1], callerDll);
        string model = Path.Combine(current, "HybridCLR.ValueLayoutModel.dll");
        using (var target = ModuleDefMD.Load(File.ReadAllBytes(model)))
        using (var caller = ModuleDefMD.Load(File.ReadAllBytes(callerDll)))
        {
            if (target.Find(CallerName, false) != null || target.Find(FrozenResourceCasesCompiler.ProbeName, false) == null)
                throw new InvalidDataException("Expected the original 23-case Model without added callers.");
            foreach (TypeRef reference in caller.GetTypeRefs())
                if (reference.ResolutionScope == caller || reference.DefinitionAssembly?.Name == caller.Assembly.Name) reference.ResolutionScope = target;
            foreach (var type in caller.Types.Where(type => type.Name != "<Module>").ToArray())
            {
                if (target.GetTypes().Any(existing => existing.FullName == type.FullName)) throw new InvalidDataException("Duplicate added type.");
                caller.Types.Remove(type); target.Types.Add(type);
            }
            var entry = target.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            entry.Body = new CilBody();
            foreach (string typeName in new[] { FrozenResourceCasesCompiler.ProbeName, CallerName })
            {
                entry.Body.Instructions.Add(Instruction.Create(OpCodes.Call, target.Find(typeName, false)!.Methods.Single(method => method.Name == "Run")));
                entry.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            }
            entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 73)); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            target.Write(model);
        }
        string settings = File.ReadAllText(Path.Combine(proof, "project/ProjectSettings/HybridCLRSettings.asset")).Replace("\r\n", "\n");
        const string lastAssembly = "  - HybridCLR.ValueLayoutConsumer\n";
        if (settings.Split(lastAssembly).Length != 3) throw new InvalidDataException("Expected fixture hotfix and DHE assembly lists.");
        settings = settings.Replace(lastAssembly, lastAssembly + "  - " + AssemblyName + "\n");
        File.WriteAllText(Path.Combine(output, "HybridCLRSettings.asset"), settings);
        bool immutable = inputHashes.All(row => Hash(Path.Combine(original, row.Key!)) == row.Value);
        if (!immutable) throw new InvalidDataException("Original Current changed while compiling new resources.");
        File.WriteAllText(Path.Combine(output, "compiler-evidence.json"), JsonSerializer.Serialize(new { passed = true, original, inputHashes,
            sources = sources.Select(path => new { path, sha256 = Hash(path) }), compilerSha256 = Hash(compiler), hostSha256 = Hash(dotnet),
            snapshotSha256 = snapshot.Sha256, caseCount = FrozenResourceCasesCompiler.CaseCount + CaseCount,
            currentHashes = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash),
            scope = "New interpreter assembly plus new parent/field declarations merged into original hotfix Model; real Unity compiler" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(current); return 0;
    }
}
