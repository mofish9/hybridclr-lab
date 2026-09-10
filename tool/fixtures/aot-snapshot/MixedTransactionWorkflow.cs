using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class MixedTransactionWorkflow
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
    private static AotAnalysisSnapshot Snapshot(string proof)
    {
        string path = Path.Combine(proof, "base/build-identity.json"); var identity = Read(path);
        return AotAnalysisSnapshot.Read(path, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
    }
    private static string Execute(string output, string exe, params string[] arguments)
    {
        var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; Console.WriteLine("Mixed transaction: " + Path.GetFileName(exe) + " PID " + process.Id);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(exe); }
        string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
        string log = Path.Combine(output, "process-" + process.Id + ".log"); File.WriteAllText(log, text);
        if (process.ExitCode != 0) throw new InvalidOperationException(exe + " failed; " + log + "\n" + text);
        return text.Trim();
    }
    internal static int NewBase(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("mixed-transaction-new-base <lab> <package> <editor> <runtime manifest> <Base-27 proof> <new output>");
        string proof = Path.GetFullPath(args[4]), inputs = Path.GetFullPath(args[5]) + ".inputs";
        if (Directory.Exists(inputs)) throw new IOException("Base inputs must be new.");
        Directory.CreateDirectory(inputs);
        var snapshot = Snapshot(proof);
        foreach (var source in snapshot.Assemblies.Where(row => row.Dhe || row.AssemblyName == FrozenEntryWorkflow.NativeName))
            File.Copy(source.Path, Path.Combine(inputs, source.AssemblyName + ".dll"));
        return UnityWorkflow.Run(args.Take(4).Concat(new[] { inputs, args[5], "41", ":mixed-transaction:" }).ToArray());
    }
    internal static int Prepare(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("mixed-transaction-prepare <lab> <Base proof> <46-case Current root> <Unity editor> <new output>");
        string lab = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[1]), original = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Prepare output must be new.");
        Directory.CreateDirectory(output);
        if (Execute(output, "git", "-C", lab, "status", "--porcelain").Length != 0) throw new InvalidDataException("Commit sources before verification.");
        string labHead = Execute(output, "git", "-C", lab, "rev-parse", "HEAD");
        string build = Path.Combine(proof, "base"), identityPath = Path.Combine(build, "build-identity.json");
        var snapshot = Snapshot(proof); var identity = Read(identityPath); var baseResult = Read(Path.Combine(proof, "result.json"));
        if (!baseResult.GetProperty("passed").GetBoolean() || Hash(identityPath) != baseResult.GetProperty("identitySha256").GetString())
            throw new InvalidDataException("Base identity differs from the completed build.");
        string current = Path.Combine(output, "current"), payload = Path.Combine(output, "payload");
        Directory.CreateDirectory(current); Directory.CreateDirectory(payload);
        var originalHashes = Directory.GetFiles(original, "*.dll").ToDictionary(Path.GetFileName, Hash);
        foreach (string file in Directory.GetFiles(original, "*.dll")) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
        string source = Path.Combine(lab, "tool/fixtures/aot-snapshot/Workloads/MixedModuleInitializer.cs");
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[3]))!, "Data");
        string compiler = Path.Combine(data, "DotNetSdkRoslyn/csc.dll"), compilerHost = Path.Combine(data, "NetCoreRuntime/dotnet.exe");
        foreach (string name in new[] { "HybridCLR.TransactionInitializer", "HybridCLR.TransactionPeer" })
            Execute(output, compilerHost, new[] { compiler, "-nologo", "-noconfig", "-nostdlib+", "-target:library", "-optimize+", "-debug-",
                "-utf8output", "-deterministic+", "-langversion:9.0", "-out:" + Path.Combine(current, name + ".dll") }
                .Concat(snapshot.Assemblies.Where(row => !row.Dhe).Select(row => "-r:" + row.Path)).Append(source).ToArray());
        string host = typeof(MixedTransactionWorkflow).Assembly.Location;
        Execute(output, "dotnet", host, "frozen-resource-reference", current,
            snapshot.Assemblies.Single(row => row.AssemblyName == FrozenEntryWorkflow.NativeName).Path, Path.Combine(output, "reference.json"));
        string[] baselines = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("baselinePath").GetString()!).ToArray();
        string[] afterDlls = Directory.GetFiles(current, "*.dll");
        var execution = ResourceExecutionPlanner.Compile(baselines, afterDlls, snapshot.OrdinaryAssemblyPaths);
        var frozen = FrozenAotAdaptation.Compile(snapshot, baselines, afterDlls, execution.Impact);
        var admission = FrozenAotAdmission.Validate(snapshot, frozen, execution, Read(Path.Combine(build, "native/dhe-native-manifest.json")));
        File.WriteAllText(Path.Combine(output, "admission.json"), JsonSerializer.Serialize(admission, Json));
        if (admission.UnsupportedChanges.Length != 0 || admission.MissingGuards.Length != 0)
            throw new InvalidDataException("Mixed transaction requires complete frozen admission: " + string.Join(",", admission.UnsupportedChanges));
        var records = new List<object>();
        void Add(string name, string path, string baseDll, int kind, uint[] types, uint[] methods, uint[] excluded, uint[] conditional)
        {
            string dll = Path.Combine(payload, name + ".dll"), before = Path.Combine(payload, name + ".base.mv"), after = Path.Combine(payload, name + ".current.mv");
            File.Copy(path, dll); MetaVersionSnapshot.Create(baseDll).WriteBinary(before); MetaVersionSnapshot.Create(path).WriteBinary(after);
            string? invalidBefore = null, invalidBeforeSha256 = null;
            if (kind == 0 && name == "HybridCLR.ValueLayoutModel")
            {
                var invalid = MetaVersionSnapshot.Create(baseDll); int index = Array.FindIndex(invalid.Methods, method => method.Name == "UnchangedRevision");
                if (index < 0 || methods.Contains(invalid.Methods[index].Token)) throw new InvalidDataException("Expected unselected Base sentinel.");
                invalid.Methods[index] = invalid.Methods[index] with { StableId = new string('F', 64), Token = 0x0600ffffu };
                invalidBefore = Path.Combine(payload, name + ".invalid-base.mv"); invalid.WriteBinary(invalidBefore); invalidBeforeSha256 = Hash(invalidBefore);
            }
            records.Add(new { name, dll, before, after, sourceKind = kind, types, methods, excluded, conditional,
                dllSha256 = Hash(dll), beforeSha256 = Hash(before), afterSha256 = Hash(after), invalidBefore, invalidBeforeSha256 });
        }
        foreach (var plan in frozen.Assemblies)
        {
            var captured = snapshot.Assemblies.Single(row => row.AssemblyName == plan.AssemblyName);
            Add(plan.AssemblyName, captured.Path, captured.Path, 1, plan.ExecutionPlan.CurrentStorageTypeTokens,
                plan.ExecutionPlan.CurrentExecutionMethodTokens, plan.ExcludedBaseTypeTokens, plan.GenericContextMethodTokens);
        }
        foreach (string baseDll in baselines)
        {
            string name = Path.GetFileNameWithoutExtension(baseDll); var plan = execution.Plans[name];
            Add(name, Path.Combine(current, name + ".dll"), baseDll, 0, plan.CurrentStorageTypeTokens, plan.CurrentExecutionMethodTokens,
                Array.Empty<uint>(), Array.Empty<uint>());
        }
        var baseNames = baselines.Select(Path.GetFileNameWithoutExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var peers = afterDlls.Where(path => !baseNames.Contains(Path.GetFileNameWithoutExtension(path))).OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new { name = Path.GetFileNameWithoutExtension(path), dll = path, sha256 = Hash(path) }).ToArray();
        File.WriteAllText(Path.Combine(output, "plan.json"), JsonSerializer.Serialize(new { format = "hybridclr.mixed-transaction-probe",
            baseId = identity.GetProperty("baseId").GetString(), records, peers }, Json));
        if (originalHashes.Any(row => Hash(Path.Combine(original, row.Key!)) != row.Value || Hash(Path.Combine(current, row.Key!)) != row.Value))
            throw new InvalidDataException("Original Current bytes changed.");
        File.WriteAllText(Path.Combine(output, "prepare.json"), JsonSerializer.Serialize(new { passed = true, proof, labHead,
            hostSha256 = Hash(host), compilerSha256 = Hash(compiler), compilerHostSha256 = Hash(compilerHost), sourceSha256 = Hash(source),
            original, originalHashes, identitySha256 = Hash(identityPath), snapshotSha256 = snapshot.Sha256,
            planSha256 = Hash(Path.Combine(output, "plan.json")), currentHashes = afterDlls.ToDictionary(Path.GetFileName, Hash) }, Json));
        Console.WriteLine("Mixed transaction inputs prepared."); return 0;
    }
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("mixed-transaction-run <prepared input root> <retry|retry-reversed|initializer-failure> <new output>");
        string inputs = Path.GetFullPath(args[0]), mode = args[1], output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Run output must be new.");
        Directory.CreateDirectory(output);
        var prepared = Read(Path.Combine(inputs, "prepare.json"));
        string proof = prepared.GetProperty("proof").GetString()!, build = Path.Combine(proof, "base"), plan = Path.Combine(inputs, "plan.json");
        if (Hash(plan) != prepared.GetProperty("planSha256").GetString()) throw new InvalidDataException("Prepared plan changed.");
        var original = Read(Path.Combine(proof, "result.json"));
        string player = Path.Combine(build, "player/Snapshot.exe"), game = Path.Combine(build, "player/GameAssembly.dll");
        string playerHash = Hash(player), gameHash = Hash(game);
        if (playerHash != original.GetProperty("playerSha256").GetString() || gameHash != original.GetProperty("gameAssemblySha256").GetString())
            throw new InvalidDataException("Original Player changed.");
        string resultPath = Path.Combine(output, "player.json");
        var start = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-batchmode", "-nographics", "-mixedTransactionPlan", plan, "-mixedTransactionResult", resultPath,
            "-mixedTransactionMode", mode, "-logFile", resultPath + ".log" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!; Console.WriteLine("Mixed transaction Player PID " + process.Id);
        if (!process.WaitForExit(120000)) { process.Kill(true); process.WaitForExit(); }
        var result = File.Exists(resultPath) ? Read(resultPath) : default;
        string[] expected = Read(Path.Combine(inputs, "reference.json")).GetProperty("records").EnumerateArray().Select(row => row.GetString()!).ToArray();
        string[] actual = File.Exists(resultPath + ".log") ? File.ReadLines(resultPath + ".log").Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).ToArray() : Array.Empty<string>();
        bool passed = process.ExitCode == 0 && result.ValueKind == JsonValueKind.Object && result.GetProperty("passed").GetBoolean() &&
            result.GetProperty("pid").GetInt32() == process.Id && result.GetProperty("mode").GetString() == mode &&
            result.GetProperty("checks").EnumerateArray().All(row => row.GetProperty("passed").GetBoolean()) &&
            (mode == "initializer-failure" ? actual.Length == 0 : actual.SequenceEqual(expected)) &&
            Hash(player) == playerHash && Hash(game) == gameHash;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, mode, pid = process.Id, exitCode = process.ExitCode,
            caseCount = actual.Length, expectedCaseCount = mode == "initializer-failure" ? 0 : expected.Length,
            planSha256 = Hash(plan), playerSha256 = playerHash, gameAssemblySha256 = gameHash,
            playerResultSha256 = File.Exists(resultPath) ? Hash(resultPath) : null, hostSha256 = Hash(typeof(MixedTransactionWorkflow).Assembly.Location),
            scope = "Native mixed-batch rollback/retry and real module initializers in an immutable Player; public resource API is a separate gate" }, Json));
        Console.WriteLine("Mixed transaction passed: " + passed); return passed ? 0 : 1;
    }
}
