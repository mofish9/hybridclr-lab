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
    private const string ProbeName = "HybridCLR.Lab.ValueLayout.FrozenResourceProbe";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));

    public static int NewBase(string[] args)
    {
        if (args.Length < 6 || args.Length > 7) throw new ArgumentException("frozen-resource-new-base <lab> <package> <editor> <runtime manifest> <old proof> <new output> [Base Current DLL root]");
        string proof = Path.GetFullPath(args[4]), inputs = Path.GetFullPath(args[5]) + ".inputs";
        if (Directory.Exists(inputs)) throw new IOException("New Base inputs must be new.");
        Directory.CreateDirectory(inputs);
        string sourceCurrent = args.Length == 7 ? Path.GetFullPath(args[6]) : Path.Combine(proof, "frozen-entry-current");
        foreach (string source in Directory.GetFiles(sourceCurrent, "*.dll"))
            File.Copy(source, Path.Combine(inputs, Path.GetFileName(source)));
        string identityPath = Path.Combine(proof, "base/build-identity.json"); var identity = Read(identityPath);
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        File.Copy(snapshot.Assemblies.Single(row => row.AssemblyName == NativeName).Path, Path.Combine(inputs, NativeName + ".dll"));
        string modelPath = Path.Combine(inputs, ModelName + ".dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(modelPath)))
        {
            var revision = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            revision.Body = new CilBody(); revision.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 59));
            revision.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); module.Write(modelPath);
        }
        return UnityWorkflow.Run(args.Take(4).Concat(new[] { inputs, args[5], "59", ":all-ordinary-guards:" }).ToArray());
    }

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
        var probe = model.GetType(FrozenResourceCasesCompiler.ProbeName) ?? model.GetType(ProbeName, true)!;
        int expectedCount = probe.FullName == FrozenResourceCasesCompiler.ProbeName ? FrozenResourceCasesCompiler.CaseCount : 4;
        var addedCaller = model.GetType(FrozenAddedAssemblyCompiler.CallerName);
        if (addedCaller != null) expectedCount += FrozenAddedAssemblyCompiler.CaseCount;
        if (model.GetType(FrozenStaticWorkflow.ProbeName) != null) expectedCount += FrozenStaticWorkflow.CaseCount;
        string[] records;
        int revision;
        if (probe.FullName == FrozenResourceCasesCompiler.ProbeName)
        {
            // Initializer-sensitive suites must run the business entry exactly
            // once, as the Player does. Collect its complete case sequence.
            using var trace = new StringWriter(); var originalOutput = Console.Out;
            try
            {
                Console.SetOut(trace);
                revision = (int)model.GetType("HybridCLR.Lab.ValueLayout.Factory", true)!.GetMethod("GetRevision")!.Invoke(null, null)!;
            }
            finally { Console.SetOut(originalOutput); Console.Write(trace.ToString()); }
            records = trace.ToString().Split('\n').Select(line => line.TrimEnd('\r'))
                .Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).ToArray();
        }
        else
        {
            records = (string[])probe.GetMethod("Run")!.Invoke(null, null)!;
            revision = (int)model.GetType("HybridCLR.Lab.ValueLayout.Factory", true)!.GetMethod("GetRevision")!.Invoke(null, null)!;
        }
        File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed = records.Length == expectedCount && revision == 73, records, revision }, Json));
        return records.Length == expectedCount && revision == 73 ? 0 : 1;
    }

    // The real business entry returns a new revision only after all value-copy
    // assertions succeed. This works with immutable Bases without fixture-specific
    // optional reflection hooks or any change to ordinary AOT source bytes.
    private static void AddProbe(string current)
    {
        string path = Path.Combine(current, ModelName + ".dll");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(path));
        if (module.Find(ProbeName, false) != null) throw new InvalidDataException("Probe already exists.");
        var payload = module.Find("HybridCLR.Lab.ValueLayout.Payload", false)!;
        var type = new TypeDefUser("HybridCLR.Lab.ValueLayout", "FrozenResourceProbe", module.CorLibTypes.Object.TypeDefOrRef)
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
        il.Add(Instruction.Create(OpCodes.Ret));
        var entry = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
        entry.Body = new CilBody(); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Call, run));
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Pop)); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 73));
        entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); module.Write(path);
    }

    public static int Run(string[] args)
    {
        if (args.Length < 4 || args.Length > 7) throw new ArgumentException("frozen-resource-workflow <lab> <comma-separated immutable proof roots> <tool.dll> <new output> [Unity editor executable for expanded suite OR existing Current directory] [Current settings file] [precommit|unity|precommit-unity]");
        string validationMode = args.Length == 7 ? args[6] : "";
        if (validationMode != "" && validationMode != "precommit" && validationMode != "unity" && validationMode != "precommit-unity")
            throw new ArgumentException("Unknown Player validation mode.");
        string lab = Path.GetFullPath(args[0]), tool = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        string[] proofs = args[1].Split(',').Select(Path.GetFullPath).ToArray();
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        int? expectedModuleConstant = null;
        string Execute(string exe, params string[] arguments)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in arguments) start.ArgumentList.Add(value);
            if (expectedModuleConstant.HasValue && arguments.Contains("-snapshotResult"))
            {
                start.ArgumentList.Add("-expectedModuleConstant");
                start.ArgumentList.Add(expectedModuleConstant.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
            if (arguments.Contains("-snapshotResult"))
            {
                if (validationMode.Contains("precommit")) { start.ArgumentList.Add("-publicPrecommitProbe"); start.ArgumentList.Add("true"); }
                if (validationMode.Contains("unity")) { start.ArgumentList.Add("-unityBehaviourProbe"); start.ArgumentList.Add("current"); }
            }
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
        bool reuseCurrent = args.Length >= 5 && Directory.Exists(args[4]);
        string sourceCurrent = reuseCurrent ? Path.GetFullPath(args[4]) : Path.Combine(proofs[0], "frozen-entry-current");
        foreach (string source in Directory.GetFiles(sourceCurrent, "*.dll")) File.Copy(source, Path.Combine(current, Path.GetFileName(source)));
        using (var module = ModuleDefMD.Load(Path.Combine(current, ModelName + ".dll")))
            expectedModuleConstant = module.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)?
                .Fields.Single(field => field.Name == "ExpectedVersion").Constant?.Value as int?;
        string Join(string suffix) => string.Join(",", proofs.Select(proof => Path.Combine(proof, "base", suffix)));
        var snapshots = proofs.Select(proof => {
            string path = Path.Combine(proof, "base/build-identity.json"); var identity = Read(path);
            return AotAnalysisSnapshot.Read(path, identity, identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
                identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        }).ToArray();
        if (!reuseCurrent)
        {
            if (args.Length >= 5) FrozenResourceCasesCompiler.Compile(lab, current, snapshots[0], args[4], output, Execute);
            else AddProbe(current);
        }
        string referenceFile = Path.Combine(output, "reference.json"), host = typeof(FrozenResourceWorkflow).Assembly.Location;
        Execute("dotnet", host, "frozen-resource-reference", current, snapshots[0].Assemblies.Single(row => row.AssemblyName == NativeName).Path, referenceFile);
        string resource = Path.Combine(output, "resource");
        Execute("dotnet", tool, "resource-update", "-CurrentRoot", current,
            "-SettingsFile", args.Length >= 6 ? Path.GetFullPath(args[5]) : Path.Combine(proofs[0], "project/ProjectSettings/HybridCLRSettings.asset"),
            "-BaseRoots", Join("baseline"), "-BaseNativeManifests", Join("native/dhe-native-manifest.json"), "-BaseBuildIdentities", Join("build-identity.json"),
            "-AotMetadataRoots", string.Join(",", snapshots.Select(snapshot => Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "assemblies"))),
            "-Mode", "Exploratory", "-OutputRoot", resource);
        string[] expected = Read(referenceFile).GetProperty("records").EnumerateArray().Select(row => row.GetString()!).ToArray();
        string[] currentNames = Directory.GetFiles(current, "*.dll").Select(Path.GetFileNameWithoutExtension).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray()!;
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
                "-expectedRevision", "73", "-expectedAssemblies", currentNames.Length.ToString(), "-logFile", report + ".log");
            var result = Read(report);
            checks["standard-resource-player-" + index] = result.GetProperty("passed").GetBoolean() && result.GetProperty("resourceUpdate").GetBoolean() &&
                result.GetProperty("revision").GetInt32() == Read(referenceFile).GetProperty("revision").GetInt32() &&
                result.GetProperty("sentinel").GetInt32() == 5 && result.GetProperty("loadedAssemblies").GetInt32() == currentNames.Length &&
                (expected.Length == FrozenResourceCasesCompiler.CaseCount || expected.Length == FrozenResourceCasesCompiler.CaseCount + FrozenAddedAssemblyCompiler.CaseCount ||
                 expected.Length == FrozenResourceCasesCompiler.CaseCount + FrozenAddedAssemblyCompiler.CaseCount + FrozenStaticWorkflow.CaseCount || expected.Length == 4);
            if (expected.Length != 4)
                checks["complete-reference-case-sequence-" + index] = File.ReadAllLines(report + ".log")
                    .Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).SequenceEqual(expected);
            string[] baseNames = snapshots[index].Assemblies.Where(row => row.Dhe).Select(row => row.AssemblyName).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
            string[] newNames = currentNames.Except(baseNames, StringComparer.OrdinalIgnoreCase).ToArray();
            if (newNames.Length != 0 || result.TryGetProperty("plannedAssemblies", out _))
            {
                string[] PlayerNames(string key) => result.GetProperty(key).EnumerateArray().Select(row => row.GetString()!).OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
                checks["complete-assembly-modes-" + index] = PlayerNames("plannedAssemblies").SequenceEqual(currentNames) &&
                    PlayerNames("differentialAssemblies").SequenceEqual(baseNames) && PlayerNames("interpreterOnlyAssemblies").SequenceEqual(newNames) &&
                    currentNames.All(name => PlayerNames("loadedAssemblyNames").Contains(name));
            }
            foreach (string name in newNames)
            {
                const string assetPrefix = "Assets/StreamingAssets/SnapshotDHE/";
                string assetPath = Read(Path.Combine(stage, "dhe-runtime-plan.json")).GetProperty("assemblies")
                    .EnumerateArray().Single(row => row.GetProperty("assemblyName").GetString() == name)
                    .GetProperty("current").GetString()!;
                if (!assetPath.StartsWith(assetPrefix, StringComparison.Ordinal)) throw new InvalidDataException("Unexpected Current asset root.");
                string dll = Path.GetFullPath(Path.Combine(stage, assetPath.Substring(assetPrefix.Length)));
                if (!dll.StartsWith(Path.GetFullPath(stage) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Current DLL escaped disposable resource staging.");
                byte[] original = File.ReadAllBytes(dll);
                foreach (string fault in new[] { "missing", "corrupt" })
                {
                    string rejectedReport = Path.Combine(output, "player-new-" + fault + "-" + index + "-" + name + ".json");
                    bool failed = false;
                    try
                    {
                        if (fault == "missing") File.Delete(dll);
                        else File.WriteAllBytes(dll, original.Concat(new byte[] { 0 }).ToArray());
                        try { Execute(player, "-batchmode", "-nographics", "-snapshotResult", rejectedReport, "-snapshotResourceRoot", stage,
                            "-expectedRevision", "73", "-expectedAssemblies", currentNames.Length.ToString(), "-logFile", rejectedReport + ".log"); }
                        catch (InvalidOperationException) { failed = true; }
                    }
                    finally { File.WriteAllBytes(dll, original); }
                    var rejected = Read(rejectedReport);
                    string error = rejected.GetProperty("error").GetString()!;
                    checks["new-dll-" + fault + "-rejected-before-entry-" + index + "-" + name] = failed &&
                        !rejected.GetProperty("passed").GetBoolean() && rejected.GetProperty("loadedAssemblies").GetInt32() == 0 &&
                        rejected.GetProperty("revision").GetInt32() == 0 && error.Contains(name) &&
                        (fault == "missing" ? error.Contains("FileNotFoundException") : error.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase));
                }
                string restoredReport = Path.Combine(output, "player-new-restored-" + index + "-" + name + ".json");
                Execute(player, "-batchmode", "-nographics", "-snapshotResult", restoredReport, "-snapshotResourceRoot", stage,
                    "-expectedRevision", "73", "-expectedAssemblies", currentNames.Length.ToString(), "-logFile", restoredReport + ".log");
                checks["new-dll-restored-complete-cases-" + index + "-" + name] = Read(restoredReport).GetProperty("passed").GetBoolean() &&
                    File.ReadAllLines(restoredReport + ".log").Where(line => line.StartsWith("DHE case begin: "))
                        .Select(line => line.Substring(16)).SequenceEqual(expected) && Hash(dll) == Hash(Path.Combine(current, name + ".dll"));
            }
            string snapshotAsset = Path.Combine(stage, "payload/frozen-aot", result.GetProperty("baseId").GetString()!, "snapshot.json");
            if (File.Exists(snapshotAsset))
            {
                byte[] original = File.ReadAllBytes(snapshotAsset);
                string rejectedReport = Path.Combine(output, "player-snapshot-rejected-" + index + ".json");
                bool failed = false;
                try
                {
                    File.WriteAllBytes(snapshotAsset, original.Concat(new byte[] { 32 }).ToArray());
                    try { Execute(player, "-batchmode", "-nographics", "-snapshotResult", rejectedReport, "-snapshotResourceRoot", stage,
                        "-expectedRevision", "73", "-expectedAssemblies", currentNames.Length.ToString(), "-logFile", rejectedReport + ".log"); }
                    catch (InvalidOperationException) { failed = true; }
                }
                finally { File.WriteAllBytes(snapshotAsset, original); }
                var rejected = Read(rejectedReport);
                checks["snapshot-rejected-before-business-entry-" + index] = failed && !rejected.GetProperty("passed").GetBoolean() &&
                    rejected.GetProperty("loadedAssemblies").GetInt32() == 0 && rejected.GetProperty("revision").GetInt32() == 0 &&
                    rejected.GetProperty("error").GetString()!.Contains("not the manifest embedded in this Base identity");
                string restoredReport = Path.Combine(output, "player-snapshot-restored-" + index + ".json");
                Execute(player, "-batchmode", "-nographics", "-snapshotResult", restoredReport, "-snapshotResourceRoot", stage,
                    "-expectedRevision", "73", "-expectedAssemblies", currentNames.Length.ToString(), "-logFile", restoredReport + ".log");
                checks["original-resource-restored-" + index] = Read(restoredReport).GetProperty("passed").GetBoolean();
            }
            checks["immutable-player-" + index] = Hash(player) == playerHash && Hash(game) == gameHash;
            players.Add(new { proof = proofs[index], playerSha256 = playerHash, gameAssemblySha256 = gameHash, resultSha256 = Hash(report) });
        }
        var manifest = Read(Path.Combine(resource, "dhe-resource-update.json"));
        checks["one-current-payload"] = manifest.GetProperty("payloadModel").GetString() == "single-current-payload" &&
            manifest.GetProperty("supportedBases").EnumerateArray().Select(row => row.GetProperty("currentAssemblySetSha256").GetString()).Distinct().Count() == 1;
        checks["distinct-bases"] = manifest.GetProperty("supportedBases").EnumerateArray().Select(row => row.GetProperty("baseId").GetString()).Distinct().Count() == proofs.Length;
        if (reuseCurrent) checks["exact-existing-current-bytes-preserved"] = FrozenAotSourcePlan.CurrentSetHash(Directory.GetFiles(sourceCurrent, "*.dll")) ==
            FrozenAotSourcePlan.CurrentSetHash(Directory.GetFiles(current, "*.dll"));
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, players, labHead, validationMode,
            hostSha256 = Hash(host), toolSha256 = Hash(tool), currentAssemblySetSha256 = FrozenAotSourcePlan.CurrentSetHash(Directory.GetFiles(current, "*.dll")),
            resourceManifestSha256 = Hash(Path.Combine(resource, "dhe-resource-update.json")), expected,
            scope = "Standard resource generation/staging/public loading with original ordinary Base IL and evolved Current value fields" }, Json));
        Console.WriteLine("Frozen resource workflow: " + passed);
        return passed ? 0 : 1;
    }
}
