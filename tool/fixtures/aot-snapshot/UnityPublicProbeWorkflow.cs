using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class UnityPublicProbeWorkflow
{
    internal static int Run(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("unity-public-probes <lab> <comma-separated Base proofs> <passed shared-resource output> <malformed Model DLL> <new output>");
        string lab = Path.GetFullPath(args[0]), resource = Path.GetFullPath(args[2]), malformed = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
        string[] proofs = args[1].Split(',').Select(Path.GetFullPath).ToArray();
        if (Directory.Exists(output)) throw new IOException("Probe output must be new.");
        Directory.CreateDirectory(output);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        var checks = new Dictionary<string, bool>(); var runs = new List<object>();
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        void Track(string path) { files[Path.GetFullPath(path)] = Hash(path); }
        void Require(bool value, string name) { checks.Add(name, value); if (!value) throw new InvalidDataException(name); }
        string Execute(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Unity public probe: " + Path.GetFileName(executable) + " PID " + process.Id);
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(executable); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            string path = Path.Combine(output, "process-" + process.Id + ".log"); File.WriteAllText(path, trace);
            runs.Add(new { executable, arguments, processId = process.Id, exitCode = process.ExitCode, logSha256 = Hash(path) });
            if (process.ExitCode != 0) throw new InvalidOperationException("Probe process failed: " + path);
            return trace.Trim();
        }
        string labHead = "", error = null;
        try
        {
            Require(Execute("git", "-C", lab, "status", "--porcelain").Length == 0, "clean-source");
            labHead = Execute("git", "-C", lab, "rev-parse", "HEAD");
            var shared = Read(Path.Combine(resource, "result.json"));
            Require(shared.GetProperty("passed").GetBoolean(), "shared-resource-already-passed");
            Track(Path.Combine(resource, "result.json")); Track(malformed);
            string[] expected = shared.GetProperty("expected").EnumerateArray().Select(row => row.GetString()!).ToArray();
            for (int index = 0; index < proofs.Length; ++index)
            {
                string proof = proofs[index], build = Path.Combine(proof, "base"), player = Path.Combine(build, "player/Snapshot.exe");
                var original = Read(Path.Combine(proof, "result.json"));
                var baseRun = Read(Path.Combine(proof, "player-result.json"));
                var currentRun = Read(Path.Combine(resource, "player-" + index + ".json"));
                Require(original.GetProperty("passed").GetBoolean() && shared.GetProperty("players")[index].GetProperty("proof").GetString() == proof, "matched-base-" + index);
                Require(Hash(player) == original.GetProperty("playerSha256").GetString() &&
                    Hash(Path.Combine(build, "player/GameAssembly.dll")) == original.GetProperty("gameAssemblySha256").GetString(), "original-player-identity-" + index);
                Track(player); Track(Path.Combine(build, "player/GameAssembly.dll")); Track(Path.Combine(build, "build-identity.json"));
                string stage = Path.Combine(resource, "stage-" + index);
                foreach (string path in Directory.GetFiles(stage, "*", SearchOption.AllDirectories)) Track(path);
                string[] Common(string report, JsonElement source) => new[] { "-batchmode", "-nographics", "-snapshotResult", report,
                    "-expectedRevision", source.GetProperty("revision").GetInt32().ToString(), "-expectedAssemblies", source.GetProperty("loadedAssemblies").GetInt32().ToString(),
                    "-expectedModuleConstant", source.GetProperty("moduleConstantAfterLoad").GetInt32().ToString(), "-logFile", report + ".log" };
                string baseline = Path.Combine(output, "baseline-" + index + ".json");
                Execute(player, Common(baseline, baseRun).Concat(new[] { "-unityBehaviourProbe", "base" }).ToArray());
                var baselineResult = Read(baseline);
                int delta = baselineResult.GetProperty("unityBaseDelta").GetInt32(), baselineCount = delta == 1 ? 14 : 17;
                Require(baselineResult.GetProperty("passed").GetBoolean() && baselineResult.GetProperty("unityChecks").GetArrayLength() == baselineCount &&
                    File.ReadAllLines(baseline + ".log").Count(line => line == "DHE Unity component pass: " + delta + ":" + baselineCount) == 1, "base-unity-semantics-" + index);
                string rejected = Path.Combine(output, "preparation-" + index + ".json");
                Execute(player, Common(rejected, currentRun).Concat(new[] { "-snapshotResourceRoot", stage, "-publicPreparationProbe", malformed }).ToArray());
                var failure = Read(rejected); string[] failedLog = File.ReadAllLines(rejected + ".log");
                string[] expectedFailure = { "native-preparation-failed", "actual-native-preparation-phase", "actual-native-exception-preserved",
                    "uncommitted-restart-required", "no-managed-publication", "no-current-initializer-effect", "old-dispatch-retained",
                    "new-assemblies-unpublished", "original-input-records-restored", "corrected-retry-requires-fresh-process", "reset-cannot-erase-partial-preparation" };
                Require(failure.GetProperty("passed").GetBoolean() && failure.GetProperty("stage").GetString() == "expected-preparation-failure" &&
                    failure.GetProperty("preparationChecks").EnumerateArray().Select(row => row.GetString()).SequenceEqual(expectedFailure) &&
                    failedLog.Where(line => line.StartsWith("DHE public preparation check: ")).Select(line => line.Substring("DHE public preparation check: ".Length)).SequenceEqual(expectedFailure), "native-preparation-full-sequence-" + index);
                Require(failure.GetProperty("revision").GetInt32() == 0 && failure.GetProperty("loadedAssemblies").GetInt32() == 0 &&
                    !failedLog.Any(line => line.StartsWith("DHE case begin: ") || line.StartsWith("DHE selected module: ")), "no-failed-process-business-effects-" + index);
                string restored = Path.Combine(output, "fresh-process-" + index + ".json");
                Execute(player, Common(restored, currentRun).Concat(new[] { "-snapshotResourceRoot", stage, "-unityBehaviourProbe", "current",
                    "-unityBehaviourSelection", UnityBehaviourSelection.Read(proof, resource) }).ToArray());
                var recovered = Read(restored); string[] recoveredLog = File.ReadAllLines(restored + ".log");
                Require(recovered.GetProperty("passed").GetBoolean() && recovered.GetProperty("unityChecks").GetArrayLength() == 17 &&
                    recoveredLog.Count(line => line == "DHE Unity expected selection: " + UnityBehaviourSelection.Read(proof, resource)) == 1 &&
                    recoveredLog.Count(line => line == "DHE Unity component pass: 2:17") == 1 &&
                    recoveredLog.Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).SequenceEqual(expected), "fresh-process-current-recovery-" + index);
                foreach (string report in new[] { baseline, rejected, restored }) { Track(report); Track(report + ".log"); }
            }
            Require(files.All(row => Hash(row.Key) == row.Value), "all-recorded-inputs-remain-identical");
        }
        catch (Exception exception) { error = exception.ToString(); }
        bool passed = error == null;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, error, checks, runs, files, labHead,
            hostSha256 = Hash(typeof(UnityPublicProbeWorkflow).Assembly.Location), scope = "Immutable Base Unity controls, deliberate native preparation fault, and valid Current fresh-process recovery; no performance or device claims" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Unity public probes: " + passed + (error == null ? "" : "\n" + error)); return passed ? 0 : 1;
    }
}
