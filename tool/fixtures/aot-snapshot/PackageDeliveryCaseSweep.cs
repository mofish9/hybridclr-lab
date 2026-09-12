using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class PackageDeliveryCaseSweep
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("delivery-case-sweep <Base proof> <staged resources> <reference.json> <new output>");
        string proof = Path.GetFullPath(args[0]), stage = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Sweep output must be new.");
        string player = Path.Combine(proof, "base/player/Snapshot.exe"), game = Path.Combine(proof, "base/player/GameAssembly.dll");
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string playerHash = Hash(player), gameHash = Hash(game);
        var reference = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(args[2]));
        string[] allCases = reference.GetProperty("records").EnumerateArray().Select(row => row.GetString()!).ToArray();
        if (!reference.GetProperty("passed").GetBoolean() || allCases.Length != 46) throw new InvalidDataException("Expected the complete 46-case reference.");
        Directory.CreateDirectory(output);
        var rows = new List<object>();
        int passed = 0;
        foreach (string name in allCases.Take(FrozenResourceCasesCompiler.CaseCount))
        {
            string report = Path.Combine(output, name + ".json"), log = report + ".log";
            var start = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in new[] { "-batchmode", "-nographics", "-snapshotResult", report, "-snapshotResourceRoot", stage,
                "-expectedRevision", "73", "-expectedAssemblies", "4", "-expectedModuleConstant", "202", "-dheResourceCase", name, "-logFile", log })
                start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException(name); }
            Task.WaitAll(stdout, stderr);
            var result = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(report));
            string[] expected = new[] { name }.Concat(allCases.Skip(FrozenResourceCasesCompiler.CaseCount)).ToArray();
            string[] actual = File.ReadAllLines(log).Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).ToArray();
            bool ok = process.ExitCode == 0 && result.GetProperty("passed").GetBoolean() && actual.SequenceEqual(expected);
            if (ok) passed++;
            rows.Add(new { name, passed = ok, processId = process.Id, actual, error = result.GetProperty("error").GetString(), reportSha256 = Hash(report) });
        }
        bool immutable = Hash(player) == playerHash && Hash(game) == gameHash;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = passed == 23 && immutable,
            passedScenarios = passed, scenarioCount = 23, immutable, playerSha256 = playerHash, gameAssemblySha256 = gameHash,
            referenceSha256 = Hash(args[2]), toolHostSha256 = Hash(typeof(PackageDeliveryCaseSweep).Assembly.Location), rows,
            scope = "Each resource case in a fresh immutable Player, followed by the original 23 added/static cases; diagnostics, not a replacement for the full-sequence gate." }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Delivery Player sweep: {passed}/23; immutable={immutable}");
        return passed == 23 && immutable ? 0 : 1;
    }
}
