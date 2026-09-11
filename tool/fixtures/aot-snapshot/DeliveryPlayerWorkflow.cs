using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class DeliveryPlayerWorkflow
{
    internal static int Run(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("delivery-player-workflow <lab> <shared resource workflow> <bundle proof> <delivery builder host> <new output>");
        string lab = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("New delivery workflow output required.");
        Directory.CreateDirectory(output);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string Execute(string exe, bool requireSuccess, params string[] values)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in values) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!; Console.WriteLine("Delivery Player: " + Path.GetFileName(exe) + " PID " + process.Id);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(exe); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), trace);
            if (requireSuccess && process.ExitCode != 0) throw new InvalidOperationException(trace);
            return trace.Trim();
        }
        if (Execute("git", true, "status", "--porcelain").Length != 0) throw new InvalidOperationException("Commit sources before Player proof.");
        string labHead = Execute("git", true, "rev-parse", "HEAD");
        var shared = Read(Path.Combine(args[1], "result.json"));
        if (!shared.GetProperty("passed").GetBoolean()) throw new InvalidDataException("Shared Current must pass first.");
        string delivery = Path.Combine(output, "delivery");
        Execute("dotnet", true, args[3], "delivery-build", Path.Combine(args[1], "resource"), args[2], delivery);
        string manifestHash = Hash(Path.Combine(delivery, "dhe-delivery.json"));
        var inventory = Directory.GetFiles(delivery, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        var runs = new List<object>(); var checks = new Dictionary<string, bool>();
        void Check(string name, bool value) { checks[name] = value; if (!value) throw new InvalidDataException(name); }
        int index = 0;
        foreach (var baseRecord in shared.GetProperty("players").EnumerateArray())
        {
            string proof = baseRecord.GetProperty("proof").GetString()!, root = Path.Combine(proof, "base/player");
            var identity = Read(Path.Combine(proof, "base/build-identity.json"));
            var files = Read(Path.Combine(proof, "asset-player-files.json")).EnumerateObject().ToDictionary(
                property => Path.Combine(root, property.Name), property => property.Value.GetString()!);
            Check("original-player-" + index, files.All(row => Hash(row.Key) == row.Value));
            foreach (string mode in new[] { "missing-asset", "wrong-manifest-hash", "valid" })
            {
                string selected = delivery, expectedHash = manifestHash;
                if (mode == "missing-asset")
                {
                    selected = Path.Combine(output, "missing-" + index);
                    foreach (var row in inventory)
                    {
                        string relative = Path.GetRelativePath(delivery, row.Key);
                        if (relative.Replace('\\', '/') == "assets/prefab") continue;
                        string destination = Path.Combine(selected, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                        File.Copy(row.Key, destination);
                    }
                }
                if (mode == "wrong-manifest-hash") expectedHash = new string('0', 64);
                string name = "base-" + index + "-" + mode, report = Path.Combine(output, name + ".json"), log = Path.Combine(output, name + ".log");
                Execute(Path.Combine(root, "Snapshot.exe"), mode == "valid", "-batchmode", "-nographics", "-snapshotResult", report,
                    "-dheDeliveryRoot", selected, "-dheDeliveryHash", expectedHash, "-logFile", log);
                var result = Read(report); string[] lines = File.ReadAllLines(log);
                Check(name + "-base", result.GetProperty("baseId").GetString() == identity.GetProperty("baseId").GetString());
                if (mode == "valid")
                {
                    Check(name, result.GetProperty("passed").GetBoolean() && result.GetProperty("metadataCommitted").GetBoolean() &&
                        result.GetProperty("checks").GetArrayLength() == 42 && result.GetProperty("revision").GetInt32() == 73 &&
                        lines.Count(line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)) == 46 &&
                        lines.Contains("DHE added serialized types after-gc pass: saved-prefab") &&
                        lines.Contains("DHE added serialized types after-gc pass: saved-scene"));
                }
                else Check(name, !result.GetProperty("passed").GetBoolean() && result.GetProperty("stage").GetString() == "prepare" &&
                    !result.GetProperty("metadataCommitted").GetBoolean() && !result.GetProperty("restartRequired").GetBoolean() &&
                    !lines.Any(line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)));
                Check(name + "-immutable", files.All(row => Hash(row.Key) == row.Value) && inventory.All(row => Hash(row.Key) == row.Value));
                runs.Add(new { proof, mode, reportSha256 = Hash(report), logSha256 = Hash(log) });
            }
            index++;
        }
        Check("multiple-bases", index >= 2);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, labHead, manifestHash, checks, runs, inventory,
            hostSha256 = Hash(typeof(DeliveryPlayerWorkflow).Assembly.Location), builderHostSha256 = Hash(args[3]) }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Delivery Player workflow passed: " + index + " Bases"); return 0;
    }
}
