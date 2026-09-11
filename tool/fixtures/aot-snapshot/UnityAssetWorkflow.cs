using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class UnityAssetWorkflow
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value,
        new JsonSerializerOptions { WriteIndented = true }));
    internal static int TypeTrees(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("unity-asset-type-trees <Base proof> <new report.json>");
        if (File.Exists(args[1])) throw new IOException("Report must be new.");
        var rows = new List<object>();
        foreach (string name in new[] { "resources.assets", "level1" })
        {
            string path = Path.Combine(args[0], "base/player/Snapshot_Data", name);
            using var stream = File.OpenRead(path); using var reader = new BinaryReader(stream);
            uint Big32() { var bytes = reader.ReadBytes(4); if (BitConverter.IsLittleEndian) Array.Reverse(bytes); return BitConverter.ToUInt32(bytes, 0); }
            ulong Big64() { var bytes = reader.ReadBytes(8); if (BitConverter.IsLittleEndian) Array.Reverse(bytes); return BitConverter.ToUInt64(bytes, 0); }
            stream.Position = 8; uint version = Big32();
            stream.Position = 16; byte endian = reader.ReadByte();
            if (version != 22 || endian != 0) throw new InvalidDataException("This audit expects Unity 2022 little-endian serialized-file version 22.");
            stream.Position = 20; uint metadataSize = Big32(); ulong size = Big64(), dataOffset = Big64();
            if (size != (ulong)stream.Length || dataOffset >= size || metadataSize > size) throw new InvalidDataException("Invalid serialized-file header.");
            stream.Position = 48; var engineBytes = new List<byte>(); byte value;
            while ((value = reader.ReadByte()) != 0) { if (engineBytes.Count >= 256) throw new InvalidDataException("Invalid engine version."); engineBytes.Add(value); }
            string engine = System.Text.Encoding.UTF8.GetString(engineBytes.ToArray());
            int platform = reader.ReadInt32(); bool typeTreeEnabled = reader.ReadBoolean();
            rows.Add(new { path, version, engine, platform, typeTreeEnabled, sha256 = Hash(path) });
        }
        Write(args[1], new { parsed = true, files = rows, hostSha256 = Hash(typeof(UnityAssetWorkflow).Assembly.Location),
            scope = "Read-only serialized-file metadata inspection; absence of a type tree is not a correctness pass" });
        return 0;
    }
    internal static int Inputs(string[] args)
    {
        if (args.Length != 6 || args[5] != "base" && args[5] != "current")
            throw new ArgumentException("unity-asset-inputs <lab> <Base proof> <seed Current> <editor> <new output> <base|current>");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        var inputs = Directory.GetFiles(args[2], "*.dll").ToDictionary(Path.GetFullPath, Hash);
        foreach (string path in inputs.Keys) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        foreach (string path in Directory.GetFiles(current, "*.dll")) FrozenStaticWorkflow.NormalizeSelfReferences(path);
        string identityPath = Path.Combine(args[1], "base/build-identity.json"); var identity = Read(identityPath);
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[3]))!, "Data");
        string managed = Path.Combine(data, "PlaybackEngines/WindowsStandaloneSupport/Variations/il2cpp/Managed");
        var references = snapshot.Assemblies.Where(row => !row.Dhe && row.AssemblyName != "netstandard").Select(row => {
            string full = Path.Combine(managed, row.AssemblyName + ".dll");
            return row.AssemblyName.StartsWith("UnityEngine", StringComparison.Ordinal) && File.Exists(full) ? full : row.Path;
        }).Append(Path.Combine(data, "MonoBleedingEdge/lib/mono/unityaot-win32/Facades/netstandard.dll"))
            .Concat(Directory.GetFiles(current, "*.dll"));
        FrozenStaticWorkflow.CompileAndMerge(args[0], args[3], "UnityAssetDefinitions", Path.Combine(current, "HybridCLR.ValueLayoutModel.dll"),
            references, Path.Combine(output, "compiled"), false, args[5] == "current" ? "UNITY_ASSET_CURRENT" : null);
        foreach (string path in Directory.GetFiles(current, "*.dll")) FrozenStaticWorkflow.NormalizeSelfReferences(path);
        if (inputs.Any(row => Hash(row.Key) != row.Value)) throw new InvalidDataException("Input DLL changed.");
        Write(Path.Combine(output, "input-evidence.json"), new { inputs, schema = args[5],
            current = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash), snapshotSha256 = snapshot.Sha256,
            hostSha256 = Hash(typeof(UnityAssetWorkflow).Assembly.Location) });
        Console.WriteLine(current); return 0;
    }
    internal static int NewBase(string[] args)
    {
        int result = FrozenResourceWorkflow.NewBase(args);
        if (result != 0) return result;
        string proof = Path.GetFullPath(args[5]), playerRoot = Path.Combine(proof, "base/player");
        var assets = Read(Path.Combine(proof, "unity-assets.json"));
        if (assets.GetProperty("assetRevision").GetInt32() is not (1 or 2)) throw new InvalidDataException("Missing authored assets.");
        Write(Path.Combine(proof, "asset-player-files.json"), Directory.GetFiles(playerRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(playerRoot, path), Hash));
        return 0;
    }
    internal static int Replay(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("unity-asset-replay <lab> <Base proof> <shared resource or :noop:> <new output>");
        string lab = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string Execute(string exe, params string[] values)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in values) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Unity asset probe: " + Path.GetFileName(exe) + " PID " + process.Id);
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(exe); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), trace);
            if (process.ExitCode != 0) throw new InvalidOperationException(exe + " failed: " + process.ExitCode);
            return trace.Trim();
        }
        if (Execute("git", "status", "--porcelain").Length != 0) throw new InvalidDataException("Commit sources before replay.");
        string labHead = Execute("git", "rev-parse", "HEAD");
        var identity = Read(Path.Combine(proof, "base/build-identity.json")); var baseResult = Read(Path.Combine(proof, "result.json"));
        var assets = Read(Path.Combine(proof, "unity-assets.json"));
        int assetRevision = assets.GetProperty("assetRevision").GetInt32();
        bool noop = args[2] == ":noop:", current = !noop || assetRevision == 2;
        string stage = Path.Combine(proof, "stage-noop");
        if (!noop)
        {
            var shared = Read(Path.Combine(args[2], "result.json"));
            if (!shared.GetProperty("passed").GetBoolean()) throw new InvalidDataException("Shared resource must pass first.");
            var players = shared.GetProperty("players").EnumerateArray().ToArray();
            int matched = Array.FindIndex(players, row => string.Equals(Path.GetFullPath(row.GetProperty("proof").GetString()!), proof, StringComparison.OrdinalIgnoreCase));
            if (matched < 0) throw new InvalidDataException("Base not in shared resource proof.");
            stage = Path.Combine(args[2], "stage-" + matched);
        }
        string playerRoot = Path.Combine(proof, "base/player"), player = Path.Combine(playerRoot, "Snapshot.exe");
        var frozenFiles = Read(Path.Combine(proof, "asset-player-files.json")).EnumerateObject()
            .ToDictionary(row => Path.Combine(playerRoot, row.Name), row => row.Value.GetString()!);
        foreach (string kind in new[] { "scene", "prefab" })
            frozenFiles[Path.Combine(proof, "project", assets.GetProperty(kind).GetString()!)] = assets.GetProperty(kind + "Sha256").GetString()!;
        foreach (string path in Directory.GetFiles(stage, "*", SearchOption.AllDirectories)) frozenFiles[path] = Hash(path);
        bool Immutable() => frozenFiles.All(row => File.Exists(row.Key) && Hash(row.Key) == row.Value);
        if (!baseResult.GetProperty("passed").GetBoolean() || !Immutable()) throw new InvalidDataException("Immutable Base/assets mismatch.");
        string report = Path.Combine(output, "player.json"), log = Path.Combine(output, "player.log"), error = null;
        try { Execute(player, "-batchmode", "-nographics", "-snapshotResult", report, "-snapshotResourceRoot", stage,
            "-expectedRevision", noop ? "59" : "73", "-expectedAssemblies", identity.GetProperty("assemblies").GetArrayLength().ToString(),
            "-unityAssetProbe", current ? "current" : "base", "-unityAssetRevision", assetRevision.ToString(), "-logFile", log); }
        catch (Exception exception) { error = exception.ToString(); }
        string[] common = { "type", "authored-revision", "inactive", "existing-field", "nested-type", "nested-number", "nested-text", "list",
            "managed-reference", "managed-reference-method", "unity-reference", "deserialize-callback" };
        var expected = new[] { "prefab", "scene" }.SelectMany(name => common.Concat(current
            ? new[] { "added-component-field", "added-nested-field", "added-node-field" } : Array.Empty<string>())
            .Concat(new[] { "clone-fields", "clone-reference", "independent-storage-gc", "awake" }).Select(test => name + ":" + test))
            .Append("scene-unloaded").ToArray();
        var observed = File.Exists(log) ? File.ReadAllLines(log).Where(line => line.StartsWith("DHE Unity asset check: "))
            .Select(line => line["DHE Unity asset check: ".Length..]).ToArray() : Array.Empty<string>();
        bool passed = error == null && Immutable() && File.Exists(report) && Read(report).GetProperty("passed").GetBoolean() &&
            Read(report).GetProperty("baseId").GetString() == identity.GetProperty("baseId").GetString() && observed.SequenceEqual(expected) &&
            Read(report).GetProperty("unityAssetChecks").EnumerateArray().Select(row => row.GetString()).SequenceEqual(expected);
        Write(Path.Combine(output, "result.json"), new { passed, error, noop, assetRevision, current, labHead, expected, observed, frozenFiles,
            hostSha256 = Hash(typeof(UnityAssetWorkflow).Assembly.Location), playerResultSha256 = File.Exists(report) ? Hash(report) : null });
        Console.WriteLine("Unity asset replay: " + passed + "; checks=" + observed.Length); return passed ? 0 : 1;
    }
}
