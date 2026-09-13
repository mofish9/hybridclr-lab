using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ResourceRollbackWorkflow
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("resource-rollback <lab> <passed resource A> <passed resource B> <new output>");
        string lab = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Rollback output must be new.");
        Directory.CreateDirectory(output);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var runs = new List<object>(); var checks = new Dictionary<string, bool>();
        void Check(string name, bool pass) { checks.Add(name, pass); if (!pass) throw new InvalidDataException(name); }
        void Track(string path) { files[Path.GetFullPath(path)] = Hash(path); }
        string Execute(string exe, params string[] values)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in values) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Resource rollback: " + Path.GetFileName(exe) + " PID " + process.Id);
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(exe); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            string log = Path.Combine(output, "process-" + process.Id + ".log"); File.WriteAllText(log, trace);
            runs.Add(new { exe, arguments = values, processId = process.Id, exitCode = process.ExitCode, logSha256 = Hash(log) });
            if (process.ExitCode != 0) throw new InvalidOperationException(trace);
            return trace.Trim();
        }
        Check("clean-source", Execute("git", "status", "--porcelain").Length == 0);
        string labHead = Execute("git", "rev-parse", "HEAD");
        string[] roots = args.Skip(1).Take(2).Select(Path.GetFullPath).ToArray();
        var resources = roots.Select(root => Read(Path.Combine(root, "result.json"))).ToArray();
        Check("both-resources-passed", resources.All(resource => resource.GetProperty("passed").GetBoolean() &&
            resource.GetProperty("checks").EnumerateObject().All(check => check.Value.GetBoolean())));
        Check("distinct-current-versions", resources[0].GetProperty("currentAssemblySetSha256").GetString() !=
            resources[1].GetProperty("currentAssemblySetSha256").GetString());
        string[][] bases = resources.Select(resource => resource.GetProperty("players").EnumerateArray()
            .Select(row => row.GetProperty("proof").GetString()!).ToArray()).ToArray();
        Check("same-multiple-bases", bases[0].Length >= 2 && bases[0].SequenceEqual(bases[1]));
        foreach (string root in roots)
            foreach (string path in Directory.GetFiles(root, "*", SearchOption.AllDirectories)) Track(path);
        for (int index = 0; index < bases[0].Length; ++index)
        {
            string proof = bases[0][index], player = Path.Combine(proof, "base/player/Snapshot.exe");
            var original = Read(Path.Combine(proof, "result.json"));
            Check("original-base-" + index, original.GetProperty("passed").GetBoolean() &&
                Hash(player) == original.GetProperty("playerSha256").GetString() &&
                Hash(Path.Combine(proof, "base/player/GameAssembly.dll")) == original.GetProperty("gameAssemblySha256").GetString());
            foreach (string path in Directory.GetFiles(Path.Combine(proof, "base/player"), "*", SearchOption.AllDirectories)) Track(path);
            string identityPath = Path.Combine(proof, "base/build-identity.json"); Track(identityPath);
            Check("original-identity-" + index, Hash(identityPath) == original.GetProperty("identitySha256").GetString());
            string baseId = Read(identityPath).GetProperty("baseId").GetString()!;
            int phase = 0;
            foreach (int selected in new[] { 0, 1, 0 })
            {
                string root = roots[selected], report = Path.Combine(output, "base-" + index + "-phase-" + phase + ".json");
                var expectedRun = Read(Path.Combine(root, "player-" + index + ".json"));
                var reference = Read(Path.Combine(root, "reference.json"));
                string[] expected = reference.GetProperty("records").EnumerateArray().Select(row => row.GetString()!).ToArray();
                Check("reference-" + index + "-" + phase, reference.GetProperty("passed").GetBoolean() &&
                    expected.SequenceEqual(resources[selected].GetProperty("expected").EnumerateArray().Select(row => row.GetString())));
                Execute(player, "-batchmode", "-nographics", "-snapshotResult", report,
                    "-snapshotResourceRoot", Path.Combine(root, "stage-" + index),
                    "-expectedRevision", expectedRun.GetProperty("revision").GetInt32().ToString(),
                    "-expectedAssemblies", expectedRun.GetProperty("loadedAssemblies").GetInt32().ToString(),
                    "-expectedModuleConstant", expectedRun.GetProperty("moduleConstantAfterLoad").GetInt32().ToString(), "-logFile", report + ".log");
                var actual = Read(report);
                Check("selected-resource-" + index + "-" + phase, actual.GetProperty("passed").GetBoolean() &&
                    actual.GetProperty("baseId").GetString() == baseId &&
                    actual.GetProperty("revision").GetInt32() == expectedRun.GetProperty("revision").GetInt32() &&
                    File.ReadAllLines(report + ".log").Where(line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal))
                        .Select(line => line.Substring(16)).SequenceEqual(expected));
                Track(report); Track(report + ".log"); ++phase;
            }
        }
        Check("all-inputs-and-players-immutable", files.All(row => Hash(row.Key) == row.Value));
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, labHead,
            hostSha256 = Hash(typeof(ResourceRollbackWorkflow).Assembly.Location), checks, runs, files,
            scope = "Two distinct archived Current resources selected A/B/A in fresh processes on each fixed Base; no native rebuild, live-state migration or CDN/channel automation claim" },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Resource rollback passed: " + bases[0].Length + " Bases, A/B/A in fresh processes.");
        return 0;
    }
}
