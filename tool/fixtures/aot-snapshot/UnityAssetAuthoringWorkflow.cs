using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;

internal static class UnityAssetAuthoringWorkflow
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static void Write(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    internal static int Build(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("unity-asset-author-bundles <lab> <editor> <template Base> <Current> <new output> <package>");
        string lab = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Authoring output must be new.");
        Directory.CreateDirectory(output);
        string Execute(string exe, params string[] values)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string value in values) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            Console.WriteLine("Asset authoring: " + Path.GetFileName(exe) + " PID " + process.Id);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20 * 60 * 1000)) { process.Kill(true); throw new TimeoutException(exe); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), trace);
            if (process.ExitCode != 0) throw new InvalidOperationException(exe + " failed: " + process.ExitCode);
            return trace.Trim();
        }
        if (Execute("git", "status", "--porcelain").Length != 0) throw new InvalidOperationException("Commit authoring sources first.");
        string labHead = Execute("git", "rev-parse", "HEAD");
        string project = Path.Combine(output, "project"), template = Path.Combine(args[2], "project");
        var inputs = new Dictionary<string, string>();
        foreach (string folder in new[] { "Assets", "Packages", "ProjectSettings" })
            foreach (string path in Directory.GetFiles(Path.Combine(template, folder), "*", SearchOption.AllDirectories))
            {
                inputs[path] = Hash(path); string destination = Path.Combine(project, Path.GetRelativePath(template, path));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination);
            }
        foreach (string path in Directory.GetFiles(args[3], "*.dll"))
        {
            inputs[path] = Hash(path);
            File.Copy(path, Path.Combine(project, "Assets/Plugins/ValueLayout", Path.GetFileName(path)), true);
        }
        // This is a fresh source-only authoring project, not an existing Base or
        // its build cache. Replace its package from the reviewed source identity.
        string packageTarget = Path.Combine(project, "Packages/com.code-philosophy.hybridclr");
        foreach (string path in Directory.GetFiles(args[5], "*", SearchOption.AllDirectories).Where(path => !Path.GetRelativePath(args[5], path).StartsWith(".git", StringComparison.Ordinal)))
        {
            string destination = Path.Combine(packageTarget, Path.GetRelativePath(args[5], path));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(path, destination, true); inputs[path] = Hash(path);
        }
        foreach (string name in new[] { "UnityAssetBuild.cs", "UnityAssetAuthorBuild.cs" })
        {
            string path = Path.Combine(lab, "tool/fixtures/aot-snapshot/Unity", name); inputs[path] = Hash(path);
            File.Copy(path, Path.Combine(project, "Assets/Editor", name), true);
        }
        string[] addedTypes = { "HybridCLR.Lab.UnityAssets.AssetExtension", "HybridCLR.Lab.UnityAssets.AssetAddedNode" };
        using (var original = ModuleDefMD.Load(Path.Combine(template, "Assets/Plugins/ValueLayout/HybridCLR.ValueLayoutModel.dll")))
        using (var current = ModuleDefMD.Load(Path.Combine(args[3], "HybridCLR.ValueLayoutModel.dll")))
            if (addedTypes.Any(name => original.Find(name, false) != null || current.Find(name, false) == null))
                throw new InvalidDataException("Fixture must introduce genuinely new serialized types.");
        string assetOutput = Path.Combine(output, "authored");
        Execute(args[1], "-batchmode", "-nographics", "-quit", "-projectPath", project,
            "-executeMethod", "HybridCLR.Lab.Editor.UnityAssetAuthorBuild.Build", "-dheOutputRoot", Path.Combine(assetOutput, "base"),
            "-dheAssetCurrent", Path.GetFullPath(args[3]), "-dheAssetProvenanceOutput", assetOutput,
            "-logFile", Path.Combine(output, "build.log"));
        string evidence = Path.Combine(assetOutput, "asset-bundles/bundle-evidence.json");
        bool passed = File.Exists(evidence) && inputs.All(row => Hash(row.Key) == row.Value);
        Write(Path.Combine(output, "authoring-result.json"), new { passed, labHead, addedTypes, inputs,
            bundleEvidenceSha256 = File.Exists(evidence) ? Hash(evidence) : null,
            scope = "AssetBundle authoring only; no Base Player or runtime rebuilt" });
        Console.WriteLine("Asset authoring: " + passed); return passed ? 0 : 1;
    }
    internal static int Replay(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("unity-added-asset-replay <lab> <Base> <shared resource> <new output> <bundle proof>");
        int ordinary = UnityAssetWorkflow.Replay(args);
        var lines = File.ReadAllLines(Path.Combine(args[3], "player.log"));
        const string prefix = "DHE added serialized types pass: ";
        int prefab = lines.Count(line => line == prefix + "saved-prefab"), scene = lines.Count(line => line == prefix + "saved-scene");
        bool prefabAfterGc = lines.Contains("DHE added serialized types after-gc pass: saved-prefab");
        bool sceneAfterGc = lines.Contains("DHE added serialized types after-gc pass: saved-scene");
        bool passed = ordinary == 0 && prefab >= 2 && scene >= 2 && prefabAfterGc && sceneAfterGc;
        Write(Path.Combine(args[3], "added-types-result.json"), new { passed, prefabCallbacks = prefab, sceneCallbacks = scene,
            prefabAfterGc, sceneAfterGc,
            require = "42 existing checks plus validated new inline class/array and SerializeReference implementation in both assets, clones and after GC" });
        Console.WriteLine("Added serialized types: " + passed + "; prefab=" + prefab + "; scene=" + scene);
        return passed ? 0 : 1;
    }
}
