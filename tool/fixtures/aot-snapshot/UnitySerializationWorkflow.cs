using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class UnitySerializationWorkflow
{
    internal static int Replay(string[] args)
    {
        if (args.Length != 6 || args[5] != "read" && args[5] != "full" && args[5] != "full-unity" && args[5] != "reference" && args[5] != "reference-generic")
            throw new ArgumentException("unity-serialization-replay <lab> <tool.dll> <Base proof> <resource workflow output> <new output> <read|full|full-unity|reference|reference-generic>");
        bool reference = args[5].StartsWith("reference", StringComparison.Ordinal);
        bool generic = args[5] == "reference-generic";
        bool lifecycle = args[5] == "full-unity";
        string lab = Path.GetFullPath(args[0]), tool = Path.GetFullPath(args[1]), proof = Path.GetFullPath(args[2]),
            source = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Replay output must be new.");
        Directory.CreateDirectory(output);
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var records = new List<object>();
        string Execute(string executable, bool failureAllowed, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            if (lifecycle && arguments.Contains("-snapshotResult"))
            {
                start.ArgumentList.Add("-unityBehaviourProbe"); start.ArgumentList.Add("current");
            }
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Unity serialization replay: " + Path.GetFileName(executable) + " PID " + process.Id);
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(executable); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), trace);
            records.Add(new { executable, arguments, processId = process.Id, exitCode = process.ExitCode });
            if (!failureAllowed && process.ExitCode != 0) throw new InvalidOperationException(trace);
            return trace.Trim();
        }
        if (Execute("git", false, "-C", lab, "status", "--porcelain").Length != 0) throw new InvalidDataException("Commit sources first.");
        string labHead = Execute("git", false, "-C", lab, "rev-parse", "HEAD");
        var original = Read(Path.Combine(proof, "result.json"));
        string player = Path.Combine(proof, "base/player/Snapshot.exe"), game = Path.Combine(proof, "base/player/GameAssembly.dll");
        string playerHash = Hash(player), gameHash = Hash(game);
        if (!original.GetProperty("passed").GetBoolean() || playerHash != original.GetProperty("playerSha256").GetString() || gameHash != original.GetProperty("gameAssemblySha256").GetString())
            throw new InvalidDataException("Original Base identity mismatch.");
        string stage = Path.Combine(output, "stage"), embedded = Path.Combine(proof, "base/player/Snapshot_Data/StreamingAssets/SnapshotDHE");
        foreach (string file in Directory.GetFiles(embedded, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(stage, Path.GetRelativePath(embedded, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        Execute("dotnet", false, tool, "stage-resource-update", "-UpdateRoot", Path.Combine(source, "resource"), "-AssetRoot", stage,
            "-BaseBuildIdentity", Path.Combine(proof, "base/build-identity.json"), "-ImmutableFiles", player + "," + game, "-Output", Path.Combine(output, "stage.json"));
        var stageHashes = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        string reportPath = Path.Combine(output, "player.json"), log = reportPath + ".log";
        Execute(player, true, "-batchmode", "-nographics", "-snapshotResult", reportPath, "-snapshotResourceRoot", stage,
            "-expectedRevision", "73", "-expectedAssemblies", "4", "-expectedModuleConstant", "202",
            generic ? "-unityReferenceGenericProbe" : reference ? "-unityReferenceProbe" : "-unitySerializationProbe", "true", "-logFile", log);
        var report = Read(reportPath); string[] lines = File.ReadAllLines(log);
        string[] sequence = { "inactive-source-has-no-callback-effects", "json-reads-existing-field", "json-reads-added-field",
            "json-writes-existing-field", "json-writes-added-field", "old-json-preserves-added-field", "clone-copies-existing-field",
            "clone-copies-added-field", "cloned-storage-is-independent", "cloned-storage-survives-gc", "fixture-preserves-lifecycle-state" };
        if (args[5] == "read") sequence = sequence.Skip(1).Take(2).ToArray();
        if (reference) sequence = new[] {
            "public-type-matches-current-type", "runtime-type-matches-public-type", "runtime-type-equals-public-type",
            "public-type-accepts-instance", "current-type-accepts-instance", "public-type-assignable-from-runtime-type",
            "runtime-type-assignable-from-public-type", "field-declaring-type-matches-public-type",
            "reflected-existing-field-read", "reflected-added-field-read", "reflected-fields-write-current-storage",
            "native-get-component-public-type", "clone-preserves-type-and-fields", "fixture-preserves-lifecycle-state"
        };
        if (generic) sequence = new[] {
            "owner-type-identity", "owner-public-type-accepts", "owner-reflection-field-roundtrip", "owner-reflection-construction",
            "list-type-identity", "list-public-type-accepts", "list-direct-content", "list-reflection-construction", "list-reflection-add",
            "array-type-identity", "array-public-type-accepts", "array-reflection-roundtrip", "nested-list-type-identity",
            "nested-list-public-type-accepts", "invariance-remains-strict", "array-object-covariance", "concurrent-type-identity",
            "fixture-preserves-lifecycle-state"
        };
        string prefix = generic ? "DHE Unity reference generic" : reference ? "DHE Unity reference" : "DHE Unity serialization";
        string[] checks = lines.Where(line => line.StartsWith(prefix + " check: ")).Select(line => line.Substring((prefix + " check: ").Length)).ToArray();
        string[] unityExpected = {
            "awake-current-value", "on-enable-current-value", "awake-diff-selection", "added-instance-field", "added-component-awake",
            "start-current-value", "update-current-value", "late-update-current-value", "coroutine-resumed-across-frames",
            "on-disable-current-value", "layout-dependent-reader-selection", "layout-dependent-reader-execution", "unaffected-method-stays-aot",
            "added-reference-survives-gc", "disable-not-repeated-on-destroy", "on-destroy-current-value", "real-multiple-frames"
        };
        string[] unityChecks = lifecycle ? report.GetProperty("unityChecks").EnumerateArray().Select(row => row.GetString()!).ToArray() : Array.Empty<string>();
        bool lifecyclePassed = !lifecycle || unityChecks.SequenceEqual(unityExpected) &&
            lines.Where(line => line.StartsWith("DHE Unity check: ")).Select(line => line.Substring("DHE Unity check: ".Length)).SequenceEqual(unityExpected) &&
            lines.Count(line => line == "DHE Unity component pass: 2:17") == 1 &&
            report.GetProperty("stage").GetString() == "unity-component-complete";
        bool immutable = Hash(player) == playerHash && Hash(game) == gameHash && stageHashes.All(row => Hash(row.Key) == row.Value);
        bool passed = immutable && lifecyclePassed && report.GetProperty("passed").GetBoolean() && checks.SequenceEqual(sequence) &&
            lines.Count(line => line == prefix + " pass: " + sequence.Length) == 1 &&
            lines.Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).SequenceEqual(
                Read(Path.Combine(source, "reference.json")).GetProperty("records").EnumerateArray().Select(row => row.GetString()));
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, immutable, checks, expected = sequence,
            lifecycle, lifecyclePassed, unityChecks,
            error = report.GetProperty("error").GetString(), proof, source, labHead, playerSha256 = playerHash, gameAssemblySha256 = gameHash,
            hostSha256 = Hash(typeof(UnitySerializationWorkflow).Assembly.Location), toolSha256 = Hash(tool),
            resourceManifestSha256 = Hash(Path.Combine(source, "resource/dhe-resource-update.json")),
            resultSha256 = Hash(reportPath), logSha256 = Hash(log), records,
            scope = reference ? "Public reference type identity and native allocation on an immutable Base" : lifecycle ?
                "Complete native serialization and lifecycle sequences on an immutable Base" :
                args[5] == "read" ? "Native JSON read diagnostic only; complete serialization is still required" : "Native JSON/overwrite/clone assertions on an immutable Base"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Unity serialization replay: " + passed); return passed ? 0 : 1;
    }
}
