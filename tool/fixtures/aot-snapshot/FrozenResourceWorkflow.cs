using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class FrozenResourceWorkflow
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private const string ModelName = "HybridCLR.ValueLayoutModel";
    private const string NativeName = "HybridCLR.ValueLayoutNative";
    private const string ProbeName = "HybridCLR.Lab.ValueLayout.ResourceEvolutionProbe";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));

    public static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("frozen-resource-reference <Current root> <Base ordinary Native DLL> <new result>");
        string current = Path.GetFullPath(args[0]), nativePath = Path.GetFullPath(args[1]);
        if (File.Exists(args[2])) throw new IOException("Reference output must be new.");
        AssemblyLoadContext.Default.Resolving += (_, name) => name.Name == NativeName
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(nativePath)
            : File.Exists(Path.Combine(current, name.Name + ".dll"))
                ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(current, name.Name + ".dll")) : null;
        var model = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(current, ModelName + ".dll"));
        var records = (string[])model.GetType(ProbeName, true)!.GetMethod("Run")!.Invoke(null, null)!;
        File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed = records.Length == 4, records }, Json));
        return records.Length == 4 ? 0 : 1;
    }

    // Add actual Current-only business code. The Base Player already invokes a
    // probe by reflection, so this strengthens verification without rebuilding it.
    private static void AddProbe(string current)
    {
        string path = Path.Combine(current, ModelName + ".dll");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(path));
        if (module.Find(ProbeName, false) != null) throw new InvalidDataException("Probe already exists.");
        var payload = module.Find("HybridCLR.Lab.ValueLayout.Payload", false)!;
        var type = new TypeDefUser("HybridCLR.Lab.ValueLayout", "ResourceEvolutionProbe", module.CorLibTypes.Object.TypeDefOrRef)
            { Attributes = dnlib.DotNet.TypeAttributes.Public | dnlib.DotNet.TypeAttributes.Abstract | dnlib.DotNet.TypeAttributes.Sealed };
        module.Types.Add(type);
        var run = new MethodDefUser("Run", MethodSig.CreateStatic(new SZArraySig(module.CorLibTypes.String)),
            dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static) { Body = new CilBody { InitLocals = true } };
        type.Methods.Add(run);
        var value = new Local(new ValueTypeSig(payload)); var marker = new Local(module.CorLibTypes.Object);
        run.Body.Variables.Add(value); run.Body.Variables.Add(marker);
        var il = run.Body.Instructions;
        var objCtor = new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), module.CorLibTypes.Object.TypeDefOrRef);
        var exceptionType = new TypeRefUser(module, "System", "InvalidOperationException", module.CorLibTypes.AssemblyRef);
        var exceptionCtor = new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String), exceptionType);
        var native = new TypeRefUser(module, "HybridCLR.Lab.ValueLayoutNative", "NativeBoundary",
            new AssemblyRefUser(new AssemblyNameInfo(NativeName + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null")));
        var count = payload.Fields.Single(field => field.Name == "Count");
        var extra = payload.Fields.Single(field => field.Name == "Extra");
        var reference = payload.Fields.Single(field => field.Name == "Reference");
        void Require(string message)
        {
            var ok = Instruction.Create(OpCodes.Nop);
            il.Add(Instruction.Create(OpCodes.Brtrue, ok)); il.Add(Instruction.Create(OpCodes.Ldstr, message));
            il.Add(Instruction.Create(OpCodes.Newobj, exceptionCtor)); il.Add(Instruction.Create(OpCodes.Throw)); il.Add(ok);
        }
        il.Add(Instruction.Create(OpCodes.Newobj, objCtor)); il.Add(Instruction.Create(OpCodes.Stloc, marker));
        foreach (string method in new[] { "Echo", "FrozenCopyBox", "FrozenInlineBox" })
        {
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Initobj, payload));
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Ldc_I4, 17)); il.Add(Instruction.Create(OpCodes.Stfld, count));
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Ldc_I8, 90000000001L)); il.Add(Instruction.Create(OpCodes.Stfld, extra));
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Ldloc, marker)); il.Add(Instruction.Create(OpCodes.Stfld, reference));
            il.Add(Instruction.Create(OpCodes.Ldloc, value));
            TypeSig signature = method == "Echo" ? new ValueTypeSig(payload) : module.CorLibTypes.Object;
            if (method != "Echo") il.Add(Instruction.Create(OpCodes.Box, payload));
            il.Add(Instruction.Create(OpCodes.Call, new MemberRefUser(module, method, MethodSig.CreateStatic(signature, signature), native)));
            if (method != "Echo") il.Add(Instruction.Create(OpCodes.Unbox_Any, payload));
            il.Add(Instruction.Create(OpCodes.Stloc, value));
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Ldfld, count));
            il.Add(Instruction.Create(OpCodes.Ldc_I4, 17)); il.Add(Instruction.Create(OpCodes.Ceq)); Require(method + ":Count");
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Ldfld, extra));
            il.Add(Instruction.Create(OpCodes.Ldc_I8, 90000000001L)); il.Add(Instruction.Create(OpCodes.Ceq)); Require(method + ":Extra");
            il.Add(Instruction.Create(OpCodes.Ldloca, value)); il.Add(Instruction.Create(OpCodes.Ldfld, reference));
            il.Add(Instruction.Create(OpCodes.Ldloc, marker)); il.Add(Instruction.Create(OpCodes.Ceq)); Require(method + ":Reference");
        }
        il.Add(Instruction.Create(OpCodes.Call, new MemberRefUser(module, "FrozenSentinel", MethodSig.CreateStatic(module.CorLibTypes.Int32), native)));
        il.Add(Instruction.Create(OpCodes.Ldc_I4, 137)); il.Add(Instruction.Create(OpCodes.Ceq)); Require("FrozenSentinel");
        string[] records = { "Echo:Count+Extra+Reference", "FrozenCopyBox:Count+Extra+Reference", "FrozenInlineBox:Count+Extra+Reference", "FrozenSentinel=137" };
        il.Add(Instruction.Create(OpCodes.Ldc_I4, records.Length)); il.Add(Instruction.Create(OpCodes.Newarr, module.CorLibTypes.String.TypeDefOrRef));
        for (int index = 0; index < records.Length; index++)
        {
            il.Add(Instruction.Create(OpCodes.Dup)); il.Add(Instruction.Create(OpCodes.Ldc_I4, index));
            il.Add(Instruction.Create(OpCodes.Ldstr, records[index])); il.Add(Instruction.Create(OpCodes.Stelem_Ref));
        }
        il.Add(Instruction.Create(OpCodes.Ret)); module.Write(path);
    }

    public static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("frozen-resource-workflow <lab> <comma-separated immutable proof roots> <tool.dll> <new output>");
        string lab = Path.GetFullPath(args[0]), tool = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        string[] proofs = args[1].Split(',').Select(Path.GetFullPath).ToArray();
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string Execute(string exe, params string[] arguments)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in arguments) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            Console.WriteLine("Frozen resource: " + Path.GetFileName(exe) + " PID " + process.Id);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20 * 60 * 1000)) { process.Kill(true); throw new TimeoutException(exe); }
            string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            string log = Path.Combine(output, "process-" + process.Id + ".log"); File.WriteAllText(log, text);
            if (process.ExitCode != 0) throw new InvalidOperationException(exe + " failed; " + log + "\n" + text);
            return text.Trim();
        }
        if (Execute("git", "-C", lab, "status", "--porcelain").Length != 0) throw new InvalidDataException("Commit sources before verification.");
        string labHead = Execute("git", "-C", lab, "rev-parse", "HEAD");
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        foreach (string source in Directory.GetFiles(Path.Combine(proofs[0], "frozen-entry-current"), "*.dll")) File.Copy(source, Path.Combine(current, Path.GetFileName(source)));
        AddProbe(current);
        string Join(string suffix) => string.Join(",", proofs.Select(proof => Path.Combine(proof, "base", suffix)));
        var snapshots = proofs.Select(proof => {
            string path = Path.Combine(proof, "base/build-identity.json"); var identity = Read(path);
            return AotAnalysisSnapshot.Read(path, identity, identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
                identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        }).ToArray();
        string referenceFile = Path.Combine(output, "reference.json"), host = typeof(FrozenResourceWorkflow).Assembly.Location;
        Execute("dotnet", host, "frozen-resource-reference", current, snapshots[0].Assemblies.Single(row => row.AssemblyName == NativeName).Path, referenceFile);
        string resource = Path.Combine(output, "resource");
        Execute("dotnet", tool, "resource-update", "-CurrentRoot", current,
            "-SettingsFile", Path.Combine(proofs[0], "project/ProjectSettings/HybridCLRSettings.asset"),
            "-BaseRoots", Join("baseline"), "-BaseNativeManifests", Join("native/dhe-native-manifest.json"), "-BaseBuildIdentities", Join("build-identity.json"),
            "-AotMetadataRoots", string.Join(",", snapshots.Select(snapshot => Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "assemblies"))),
            "-Mode", "Exploratory", "-OutputRoot", resource);
        string[] expected = Read(referenceFile).GetProperty("records").EnumerateArray().Select(row => row.GetString()!).ToArray();
        var checks = new Dictionary<string, bool>(); var players = new List<object>();
        for (int index = 0; index < proofs.Length; index++)
        {
            string build = Path.Combine(proofs[index], "base"), stage = Path.Combine(output, "stage-" + index);
            string embedded = Path.Combine(build, "player/Snapshot_Data/StreamingAssets/SnapshotDHE");
            foreach (string source in Directory.GetFiles(embedded, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stage, Path.GetRelativePath(embedded, source)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(source, target);
            }
            string player = Path.Combine(build, "player/Snapshot.exe"), game = Path.Combine(build, "player/GameAssembly.dll");
            string playerHash = Hash(player), gameHash = Hash(game), report = Path.Combine(output, "player-" + index + ".json");
            Execute("dotnet", tool, "stage-resource-update", "-UpdateRoot", resource, "-AssetRoot", stage,
                "-BaseBuildIdentity", Path.Combine(build, "build-identity.json"), "-ImmutableFiles", player + "," + game, "-Output", Path.Combine(output, "stage-" + index + ".json"));
            Execute(player, "-batchmode", "-nographics", "-snapshotResult", report, "-snapshotResourceRoot", stage,
                "-expectedRevision", "41", "-logFile", report + ".log");
            var result = Read(report);
            checks["standard-resource-player-" + index] = result.GetProperty("passed").GetBoolean() && result.GetProperty("resourceUpdate").GetBoolean() &&
                result.GetProperty("records").EnumerateArray().Select(row => row.GetString()!).SequenceEqual(expected);
            checks["immutable-player-" + index] = Hash(player) == playerHash && Hash(game) == gameHash;
            players.Add(new { proof = proofs[index], playerSha256 = playerHash, gameAssemblySha256 = gameHash, resultSha256 = Hash(report) });
        }
        var manifest = Read(Path.Combine(resource, "dhe-resource-update.json"));
        checks["one-current-payload"] = manifest.GetProperty("payloadModel").GetString() == "single-current-payload" &&
            manifest.GetProperty("supportedBases").EnumerateArray().Select(row => row.GetProperty("currentAssemblySetSha256").GetString()).Distinct().Count() == 1;
        checks["distinct-bases"] = manifest.GetProperty("supportedBases").EnumerateArray().Select(row => row.GetProperty("baseId").GetString()).Distinct().Count() == proofs.Length;
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, players, labHead,
            hostSha256 = Hash(host), toolSha256 = Hash(tool), currentAssemblySetSha256 = FrozenAotSourcePlan.CurrentSetHash(Directory.GetFiles(current, "*.dll")),
            resourceManifestSha256 = Hash(Path.Combine(resource, "dhe-resource-update.json")), expected,
            scope = "Standard resource generation/staging/public loading with original ordinary Base IL and evolved Current value fields" }, Json));
        Console.WriteLine("Frozen resource workflow: " + passed);
        return passed ? 0 : 1;
    }
}
