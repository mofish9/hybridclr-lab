using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

// Diagnose one case per fresh immutable Player, so a corrupt call frame or
// failed initializer cannot hide the independent outcomes of the other cases.
// This complements, and never replaces, the complete resource workflow.
internal static class FrozenResourceDiagnostics
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));

    public static int Run(string[] args)
    {
        if (args.Length != 4)
            throw new ArgumentException("frozen-resource-cases <immutable proof root> <staged resource root> <reference.json> <new output>");
        string proof = Path.GetFullPath(args[0]), stage = Path.GetFullPath(args[1]), referencePath = Path.GetFullPath(args[2]),
            output = Path.GetFullPath(args[3]), player = Path.Combine(proof, "base/player/Snapshot.exe");
        if (Directory.Exists(output)) throw new IOException("Diagnostic output must be new.");
        var reference = Read(referencePath);
        string[] cases = reference.GetProperty("records").EnumerateArray().Select(row => row.GetString()!).ToArray();
        if (!reference.GetProperty("passed").GetBoolean() || cases.Length != FrozenResourceCasesCompiler.CaseCount ||
            cases.Distinct(StringComparer.Ordinal).Count() != cases.Length || reference.GetProperty("revision").GetInt32() != 73)
            throw new InvalidDataException("A passing complete CLR reference is required.");
        string game = Path.Combine(proof, "base/player/GameAssembly.dll"), playerHash = Hash(player), gameHash = Hash(game);
        var resources = Directory.GetFiles(stage, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(stage, path), Hash, StringComparer.Ordinal);
        if (resources.Count == 0) throw new InvalidDataException("Empty staged resource.");
        Directory.CreateDirectory(output);
        var results = new List<object>(); bool allPassed = true;
        for (int index = 0; index < cases.Length; ++index)
        {
            string name = cases[index], report = Path.Combine(output, "case-" + index + ".json"), log = report + ".log";
            var start = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { "-batchmode", "-nographics", "-snapshotResult", report,
                "-snapshotResourceRoot", stage, "-expectedRevision", "73", "-dheResourceCase", name, "-logFile", log })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            Console.WriteLine("DHE isolated case " + name + ": PID " + process.Id);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            bool timedOut = !process.WaitForExit(45000);
            if (timedOut) { process.Kill(true); process.WaitForExit(); }
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
            string[] started = lines.Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).ToArray();
            string[] completed = lines.Where(line => line.StartsWith("DHE case pass: ")).Select(line => line.Substring(15)).ToArray();
            bool passed = false; string error = "Player result missing";
            if (File.Exists(report))
            {
                var result = Read(report);
                error = result.GetProperty("error").GetString()!;
                passed = !timedOut && process.ExitCode == 0 && result.GetProperty("passed").GetBoolean() &&
                    result.GetProperty("resourceUpdate").GetBoolean() && result.GetProperty("revision").GetInt32() == 73 &&
                    started.SequenceEqual(new[] { name }) && completed.SequenceEqual(new[] { name });
            }
            allPassed &= passed;
            results.Add(new { name, passed, process.Id, process.ExitCode, timedOut, started, completed, error,
                resultSha256 = File.Exists(report) ? Hash(report) : null, logSha256 = File.Exists(log) ? Hash(log) : null });
            Console.WriteLine("DHE isolated result " + name + ": " + passed);
            File.WriteAllText(Path.Combine(output, "progress.json"), JsonSerializer.Serialize(results, Json));
        }
        bool immutable = Hash(player) == playerHash && Hash(game) == gameHash &&
            Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Length == resources.Count &&
            resources.All(row => File.Exists(Path.Combine(stage, row.Key)) && Hash(Path.Combine(stage, row.Key)) == row.Value);
        bool passedAll = allPassed && immutable;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = passedAll, immutable,
            proof, stage, playerSha256 = playerHash, gameAssemblySha256 = gameHash, resources,
            referenceSha256 = Hash(referencePath), hostSha256 = Hash(typeof(FrozenResourceDiagnostics).Assembly.Location), results,
            scope = "One fresh Player per case for failure isolation; full workflow and multi-Base replay remain required" }, Json));
        return passedAll ? 0 : 1;
    }
}
