using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;

internal static class UnityAssetControlWorkflow
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string TreeHash(string root)
    {
        var records = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => (name: Path.GetRelativePath(root, path).Replace('\\', '/'), hash: Hash(path)))
            .OrderBy(row => row.name, StringComparer.Ordinal).Select(row => row.name + "\t" + row.hash);
        return Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", records))));
    }
    private static void Write(string path, object value) => File.WriteAllText(path,
        JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    internal static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("unity-asset-stock-control <lab> <editor> <original Current> <latest bundle proof> <old bundle proof> <new output>");
        string lab = Path.GetFullPath(args[0]), editor = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output)) throw new IOException("Control output must be new.");
        Directory.CreateDirectory(output);
        int Execute(string exe, string logName, params string[] values)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false,
                CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            // A control must not inherit another build's custom IL2CPP override.
            start.Environment.Remove("UNITY_IL2CPP_PATH");
            foreach (string value in values) start.ArgumentList.Add(value);
            using var process = Process.Start(start)!;
            Console.WriteLine("Stock control: " + Path.GetFileName(exe) + " PID " + process.Id);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(20 * 60 * 1000)) { process.Kill(true); throw new TimeoutException(exe); }
            File.WriteAllText(Path.Combine(output, logName), stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
            return process.ExitCode;
        }
        if (Execute("git", "git-status.log", "status", "--porcelain") != 0 ||
            File.ReadAllText(Path.Combine(output, "git-status.log")).Trim().Length != 0) throw new InvalidOperationException("Commit source before control.");
        Execute("git", "git-head.log", "rev-parse", "HEAD");
        string project = Path.Combine(output, "project"), plugins = Path.Combine(project, "Assets/Plugins/ValueLayout");
        Directory.CreateDirectory(plugins); Directory.CreateDirectory(Path.Combine(project, "Assets/Editor"));
        Directory.CreateDirectory(Path.Combine(project, "Packages")); Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        string original = Path.Combine(args[2], "HybridCLR.ValueLayoutModel.dll"), originalHash = Hash(original);
        string model = Path.Combine(plugins, "HybridCLR.ValueLayoutModel.dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(original)))
        {
            foreach (var type in module.Types.Where(type => type.Name != "<Module>" && type.Namespace != "HybridCLR.Lab.UnityAssets").ToArray())
                module.Types.Remove(type);
            module.GlobalType.Methods.Clear(); module.GlobalType.Fields.Clear();
            module.Types.Add(new TypeDefUser("HybridCLR.Lab.ValueLayout", "Factory", module.CorLibTypes.Object.TypeDefOrRef) {
                Attributes = TypeAttributes.Public | TypeAttributes.Class });
            module.Write(model);
        }
        FrozenStaticWorkflow.NormalizeSelfReferences(model);
        File.Copy(Path.Combine(args[3], "project/Assets/Plugins/ValueLayout/HybridCLR.ValueLayoutModel.dll.meta"), model + ".meta");
        var sources = new Dictionary<string, string>();
        foreach (var item in new[] { ("UnityAssetPlayer.cs", "Assets"), ("UnityAssetControlEntry.cs", "Assets"), ("UnityAssetControlBuild.cs", "Assets/Editor") })
        {
            string source = Path.Combine(lab, "tool/fixtures/aot-snapshot/Unity", item.Item1);
            sources[source] = Hash(source); File.Copy(source, Path.Combine(project, item.Item2, item.Item1));
        }
        File.WriteAllText(Path.Combine(project, "Assets/link.xml"), "<linker><assembly fullname=\"HybridCLR.ValueLayoutModel\" preserve=\"all\" /></linker>");
        Write(Path.Combine(project, "Packages/manifest.json"), new { dependencies = new Dictionary<string, string> {
            ["com.unity.modules.assetbundle"] = "1.0.0", ["com.unity.modules.jsonserialize"] = "1.0.0" } });
        File.Copy(Path.Combine(args[3], "project/ProjectSettings/ProjectVersion.txt"), Path.Combine(project, "ProjectSettings/ProjectVersion.txt"));
        string runtime = Path.Combine(Path.GetDirectoryName(editor)!, "Data/il2cpp/libil2cpp");
        string runtimeProbe = Path.Combine(runtime, "vm/Object.cpp");
        if (File.ReadAllText(runtimeProbe).Contains("hybridclr", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stock control runtime contains HybridCLR modifications.");
        string runtimeHash = TreeHash(runtime);
        string player = Path.Combine(output, "player/Control.exe");
        int build = Execute(editor, "build-process.log", "-batchmode", "-nographics", "-quit", "-projectPath", project,
            "-executeMethod", "HybridCLR.Lab.Editor.UnityAssetControlBuild.Build", "-controlPlayer", player, "-logFile", Path.Combine(output, "build.log"));
        if (build != 0 || !File.Exists(player)) throw new InvalidOperationException("Stock control Player build failed.");
        var playerFiles = Directory.GetFiles(Path.GetDirectoryName(player)!, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        var runs = new List<object>(); bool latestPassed = false, oldPassed = false;
        for (int index = 0; index < 2; ++index)
        {
            string name = index == 0 ? "latest" : "old", proof = args[index == 0 ? 3 : 4];
            string bundles = Path.Combine(proof, "asset-bundles");
            var evidence = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(bundles, "bundle-evidence.json")));
            var hashes = Directory.GetFiles(bundles).ToDictionary(path => path, Hash);
            string report = Path.Combine(output, name + ".json");
            int exit = Execute(player, name + "-process.log", "-batchmode", "-nographics", "-controlResult", report,
                "-unityAssetBundleRoot", bundles, "-unityAssetRevision", evidence.GetProperty("assetRevision").GetInt32().ToString(),
                "-logFile", Path.Combine(output, name + ".log"));
            var result = File.Exists(report) ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(report)) : default;
            bool passed = exit == 0 && result.ValueKind == JsonValueKind.Object && result.GetProperty("passed").GetBoolean();
            runs.Add(new { name, exit, passed, report, result, bundles = hashes });
            if (index == 0) latestPassed = passed; else oldPassed = passed;
            if (hashes.Any(row => Hash(row.Key) != row.Value)) throw new InvalidDataException("Control modified a bundle.");
        }
        if (Hash(original) != originalHash || playerFiles.Any(row => Hash(row.Key) != row.Value) || TreeHash(runtime) != runtimeHash)
            throw new InvalidDataException("Control input/Player/runtime changed.");
        Write(Path.Combine(output, "result.json"), new { controlValid = latestPassed, oldBundlePassed = oldPassed,
            labHead = File.ReadAllText(Path.Combine(output, "git-head.log")).Trim(), sources, original, originalHash,
            modelHash = Hash(model), editorHash = Hash(editor), runtime, runtimeHash, runtimeProbeHash = Hash(runtimeProbe), playerFiles, runs,
            scope = "Stock Unity 2022 IL2CPP, no DHE package or initialization; old-bundle failure is not a migration pass" });
        Console.WriteLine("Stock control: latest=" + latestPassed + "; old=" + oldPassed);
        return latestPassed ? 0 : 1;
    }
}
