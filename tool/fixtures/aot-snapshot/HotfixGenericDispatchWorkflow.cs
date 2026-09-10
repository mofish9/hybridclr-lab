using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class HotfixGenericDispatchWorkflow
{
    internal static int Replay(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("hotfix-generic-dispatch-replay <lab> <comma-separated Base proofs> <passed shared-resource output> <new output>");
        string lab = Path.GetFullPath(args[0]), resource = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        string[] proofs = args[1].Split(',').Select(Path.GetFullPath).ToArray();
        if (Directory.Exists(output)) throw new IOException("Generic dispatch replay output must be new.");
        Directory.CreateDirectory(output);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        var files = new Dictionary<string, string>(); var runs = new List<object>();
        var checks = new Dictionary<string, bool>();
        void Track(string path) { files[Path.GetFullPath(path)] = Hash(path); }
        void Require(bool value, string name) { checks[name] = value; if (!value) throw new InvalidDataException(name); }
        string Execute(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Hotfix generic replay: " + Path.GetFileName(executable) + " PID " + process.Id);
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(executable); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            string log = Path.Combine(output, "process-" + process.Id + ".log"); File.WriteAllText(log, trace);
            runs.Add(new { executable, arguments, processId = process.Id, exitCode = process.ExitCode, logSha256 = Hash(log) });
            // Preserve Player failures as structured replay results too.
            if (process.ExitCode != 0 && Path.GetFileName(executable) != "Snapshot.exe")
                throw new InvalidOperationException(trace);
            return trace.Trim();
        }
        string labHead = "", error = null;
        try
        {
            Require(Execute("git", "-C", lab, "status", "--porcelain").Length == 0, "clean-source");
            labHead = Execute("git", "-C", lab, "rev-parse", "HEAD");
            var shared = Read(Path.Combine(resource, "result.json"));
            Require(shared.GetProperty("passed").GetBoolean(), "shared-resource-passed");
            Track(Path.Combine(resource, "result.json"));
            string manifestPath = Path.Combine(resource, "resource/dhe-resource-update.json"); Track(manifestPath);
            var manifest = Read(manifestPath);
            string modelPath = Path.Combine(resource, "current/HybridCLR.ValueLayoutModel.dll"); Track(modelPath);
            var model = MetaVersionSnapshot.Create(modelPath);
            uint payloadToken = model.Types.Single(row => row.Identity == "HybridCLR.Lab.ValueLayout.Payload").Token;
            var identityMethod = model.Methods.Single(row => row.DeclaringType == "HybridCLR.Lab.ResourceCases.FrozenResourceCases" && row.Name == "Identity");
            uint identityToken = identityMethod.Token;
            var declared = manifest.GetProperty("assemblies").EnumerateArray().Single(row => row.GetProperty("assemblyName").GetString() == model.AssemblyName);
            Require(string.Equals(Hash(modelPath), declared.GetProperty("dllSha256").GetString(), StringComparison.OrdinalIgnoreCase), "bound-current-model");
            string[] expected = shared.GetProperty("expected").EnumerateArray().Select(row => row.GetString()!).ToArray();
            for (int index = 0; index < proofs.Length; ++index)
            {
                string proof = proofs[index], identityPath = Path.Combine(proof, "base/build-identity.json");
                var original = Read(Path.Combine(proof, "result.json"));
                int resourceIndex = Array.FindIndex(shared.GetProperty("players").EnumerateArray().ToArray(),
                    row => row.GetProperty("proof").GetString() == proof);
                Require(original.GetProperty("passed").GetBoolean() && resourceIndex >= 0, "matched-base-" + index);
                string baselinePath = Path.Combine(proof, "base/baseline/HybridCLR.ValueLayoutModel.dll"); Track(baselinePath);
                var baseline = MetaVersionSnapshot.Create(baselinePath);
                Require(baseline.Methods.Any(method => method.StableId == identityMethod.StableId &&
                    method.Version == identityMethod.Version), "existing-unchanged-base-generic-" + index);
                string player = Path.Combine(proof, "base/player/Snapshot.exe"), game = Path.Combine(proof, "base/player/GameAssembly.dll");
                Require(Hash(player) == original.GetProperty("playerSha256").GetString() &&
                    Hash(game) == original.GetProperty("gameAssemblySha256").GetString(), "immutable-base-" + index);
                Track(player); Track(game); Track(identityPath);
                string baseId = Read(identityPath).GetProperty("baseId").GetString()!;
                var selection = manifest.GetProperty("supportedBases").EnumerateArray().Single(row => row.GetProperty("baseId").GetString() == baseId);
                var mode = selection.GetProperty("assemblyModes").EnumerateArray().Single(row => row.GetProperty("assemblyName").GetString() == model.AssemblyName);
                var plans = mode.GetProperty("executionPlans").EnumerateArray().ToArray();
                bool affected = plans.Any(plan => plan.GetProperty("currentStorageTypeTokens").EnumerateArray().Any(token => token.GetUInt32() == payloadToken));
                Require(plans.Any(plan => plan.TryGetProperty("currentGenericContextMethodTokens", out var tokens) &&
                    tokens.EnumerateArray().Any(token => token.GetUInt32() == identityToken)), "conditional-identity-plan-" + index);
                string stage = Path.Combine(resource, "stage-" + resourceIndex);
                foreach (string path in Directory.GetFiles(stage, "*", SearchOption.AllDirectories)) Track(path);
                string reportPath = Path.Combine(output, "player-" + index + ".json"), logPath = reportPath + ".log";
                Execute(player, "-batchmode", "-nographics", "-snapshotResult", reportPath, "-snapshotResourceRoot", stage,
                    "-expectedRevision", "73", "-expectedAssemblies", "4", "-expectedModuleConstant", "202",
                    "-hotfixGenericDispatchAffected", affected.ToString(), "-logFile", logPath);
                Track(reportPath); Track(logPath);
                var report = Read(reportPath); string[] lines = File.ReadAllLines(logPath);
                Require(report.GetProperty("passed").GetBoolean(), "player-passed-" + index);
                Require(lines.Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).SequenceEqual(expected), "complete-business-cases-" + index);
                foreach (string name in new[] { "payload", "envelope" })
                {
                    string prefix = "DHE hotfix generic dispatch " + name + ": selected=" + affected + " expected=" + affected +
                        " interpreter=" + (affected ? 1 : 0) + " aot=";
                    Require(lines.Count(line => line.StartsWith(prefix, StringComparison.Ordinal)) == 1, "dispatch-" + name + "-" + index);
                }
                Require(lines.Count(line => line == "DHE hotfix generic dispatch pass: 2") == 1, "complete-dispatch-probe-" + index);
            }
            Require(files.All(row => Hash(row.Key) == row.Value), "inputs-unchanged");
        }
        catch (Exception exception) { error = exception.ToString(); }
        bool passed = error == null;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, error, checks, runs, files, labHead,
            hostSha256 = Hash(typeof(HotfixGenericDispatchWorkflow).Assembly.Location),
            scope = "Conditional hotfix generic dispatch and preserved value/reference copies on immutable Bases; counters include reflection wrappers, no timing claim"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Hotfix generic replay: " + passed + (error == null ? "" : "\n" + error));
        return passed ? 0 : 1;
    }
}
