using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class PublicFailureWorkflow
{
    internal static int Run(string[] args)
    {
        if (args.Length != 8 || (args[7] != "legacy" && args[7] != "restart"))
            throw new ArgumentException("public-failure-workflow <lab> <proofs CSV> <tool> <new output> <Current root> <settings> <expected module version> <legacy|restart>");
        string lab = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[3]), current = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Failure output must be new.");
        Directory.CreateDirectory(output);
        string[] proofs = args[1].Split(',').Select(Path.GetFullPath).ToArray();
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string Execute(bool allowFailure, string executable, params string[] parameters)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = lab, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string parameter in parameters) start.ArgumentList.Add(parameter);
            using var process = Process.Start(start)!; Console.WriteLine("Public failure: " + Path.GetFileName(executable) + " PID " + process.Id);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20 * 60 * 1000)) { process.Kill(true); throw new TimeoutException(executable); }
            string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), text);
            if (!allowFailure && process.ExitCode != 0) throw new InvalidOperationException(text);
            return text.Trim();
        }
        if (Execute(false, "git", "status", "--porcelain").Length != 0) throw new InvalidOperationException("Commit lab before verification.");
        string labHead = Execute(false, "git", "rev-parse", "HEAD");
        string Join(string suffix) => string.Join(",", proofs.Select(proof => Path.Combine(proof, "base", suffix)));
        var snapshots = proofs.Select(proof => {
            string path = Path.Combine(proof, "base/build-identity.json"); var identity = Read(path);
            return AotAnalysisSnapshot.Read(path, identity, identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
                identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        }).ToArray();
        string resource = Path.Combine(output, "resource");
        Execute(false, "dotnet", args[2], "resource-update", "-CurrentRoot", current, "-SettingsFile", args[5],
            "-BaseRoots", Join("baseline"), "-BaseNativeManifests", Join("native/dhe-native-manifest.json"), "-BaseBuildIdentities", Join("build-identity.json"),
            "-AotMetadataRoots", string.Join(",", snapshots.Select(snapshot => Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "assemblies"))),
            "-Mode", "Exploratory", "-OutputRoot", resource);
        var checks = new Dictionary<string, bool>(); var players = new List<object>();
        for (int index = 0; index < proofs.Length; ++index)
        {
            string proof = proofs[index], build = Path.Combine(proof, "base"), stage = Path.Combine(output, "stage-" + index);
            var original = Read(Path.Combine(proof, "result.json"));
            string player = Path.Combine(build, "player/Snapshot.exe"), game = Path.Combine(build, "player/GameAssembly.dll");
            string playerHash = Hash(player), gameHash = Hash(game);
            if (!original.GetProperty("passed").GetBoolean() || playerHash != original.GetProperty("playerSha256").GetString() ||
                gameHash != original.GetProperty("gameAssemblySha256").GetString()) throw new InvalidDataException("Original Base changed.");
            string embedded = Path.Combine(build, "player/Snapshot_Data/StreamingAssets/SnapshotDHE");
            foreach (string file in Directory.GetFiles(embedded, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stage, Path.GetRelativePath(embedded, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
            Execute(false, "dotnet", args[2], "stage-resource-update", "-UpdateRoot", resource, "-AssetRoot", stage,
                "-BaseBuildIdentity", Path.Combine(build, "build-identity.json"), "-ImmutableFiles", player + "," + game, "-Output", Path.Combine(output, "stage-" + index + ".json"));
            string resultFile = Path.Combine(output, "player-" + index + ".json");
            Execute(args[7] == "legacy", player, "-batchmode", "-nographics", "-snapshotResult", resultFile,
                "-snapshotResourceRoot", stage, "-expectedRevision", "73", "-expectedAssemblies", Directory.GetFiles(current, "*.dll").Length.ToString(),
                "-expectedModuleConstant", args[6], "-publicFailureProbe", "true", "-logFile", resultFile + ".log");
            var result = Read(resultFile); string[] log = File.ReadAllLines(resultFile + ".log");
            checks["module-entered-once-" + index] = log.Count(line => line == "DHE selected module: " + args[6] + ":1") == 1;
            checks["no-business-entry-" + index] = !log.Any(line => line.StartsWith("DHE case begin: ")) && result.GetProperty("revision").GetInt32() == 0;
            checks["expected-outcome-" + index] = args[7] == "legacy"
                ? !result.GetProperty("passed").GetBoolean() && result.GetProperty("error").GetString()!.Contains("DHE_MV_REGISTRATION_FAILED")
                : result.GetProperty("passed").GetBoolean() && result.GetProperty("recoveryChecks").GetArrayLength() >= 8;
            checks["immutable-Player-" + index] = playerHash == Hash(player) && gameHash == Hash(game);
            players.Add(new { proof, playerSha256 = playerHash, gameAssemblySha256 = gameHash, resultSha256 = Hash(resultFile) });
        }
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, mode = args[7], checks, players, labHead,
            hostSha256 = Hash(typeof(PublicFailureWorkflow).Assembly.Location), toolSha256 = Hash(args[2]),
            currentAssemblySetSha256 = FrozenAotSourcePlan.CurrentSetHash(Directory.GetFiles(current, "*.dll")),
            resourceManifestSha256 = Hash(Path.Combine(resource, "dhe-resource-update.json")),
            scope = args[7] == "legacy" ? "Reproduction of old public exception misclassification; not recovery qualification" : "Public committed-failure state and rejected in-process recovery attempts" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Public failure workflow: " + passed); return passed ? 0 : 1;
    }
}
