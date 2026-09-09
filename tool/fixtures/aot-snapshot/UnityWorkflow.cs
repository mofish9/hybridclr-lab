using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class UnityWorkflow
{
    public static int Run(string[] args)
    {
        if (args.Length != 6 && args.Length != 7) throw new ArgumentException("unity-workflow <lab> <package> <editor> <runtime manifest> <fixture DLL root> <new output> [expected revision]");
        string expectedRevision = args.Length == 7 ? int.Parse(args[6]).ToString() : "41";
        string lab = Path.GetFullPath(args[0]), package = Path.GetFullPath(args[1]), editor = Path.GetFullPath(args[2]),
            runtimeManifest = Path.GetFullPath(args[3]), fixtures = Path.GetFullPath(args[4]), output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string project = Path.Combine(output, "project"), build = Path.Combine(output, "base");
        string tool = Path.Combine(lab, "tool/bin/Release/net6.0/HybridCLR.DheTool.dll");
        string probe = Path.Combine(lab, "tool/fixtures/value-layout/bin/Release/net6.0/ValueLayoutTests.dll");
        string Execute(string exe, params string[] arguments)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false,
                RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException(exe);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Started " + Path.GetFileName(exe) + " PID " + process.Id);
            if (!process.WaitForExit(20 * 60 * 1000)) { process.Kill(true); throw new TimeoutException(exe); }
            string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), text);
            if (process.ExitCode != 0) throw new InvalidOperationException(exe + " failed: " + text);
            return text.Trim();
        }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        if (Execute("git", "-C", lab, "status", "--porcelain").Length != 0 ||
            Execute("git", "-C", package, "status", "--porcelain").Length != 0)
            throw new InvalidDataException("Commit candidate sources before the workflow build.");
        string labHead = Execute("git", "-C", lab, "rev-parse", "HEAD"), packageHead = Execute("git", "-C", package, "rev-parse", "HEAD");
        Execute("dotnet", probe, "probe-project", lab, package, "Unity2022Fgs", fixtures, project);
        // This dedicated fixture has its own Player, retaining only the shared
        // project/bootstrap configuration from the earlier storage probe.
        File.Delete(Path.Combine(project, "Assets/CurrentStorageRuntime.cs"));
        foreach (var pair in new[] { ("SnapshotPlayer.cs", "Assets"), ("SnapshotWorkflowBuild.cs", "Assets/Editor") })
            File.Copy(Path.Combine(lab, "tool/fixtures/aot-snapshot/Unity", pair.Item1), Path.Combine(project, pair.Item2, pair.Item1));
        File.WriteAllText(Path.Combine(project, "Assets/SnapshotIdentity.cs"),
            File.ReadAllText(Path.Combine(lab, "templates/DheBuildIdentity.cs")).Replace("__DHE_IDENTITY_NAMESPACE__", "HybridCLR.Lab.Snapshot"));
        using var runtime = JsonDocument.Parse(File.ReadAllBytes(runtimeManifest));
        Execute(editor, "-batchmode", "-nographics", "-quit", "-projectPath", project,
            "-executeMethod", "HybridCLR.Lab.Editor.CurrentStorageProbeBuild.Prepare", "-probeRuntime",
            runtime.RootElement.GetProperty("stagedLibil2cpp").GetString(), "-probeOutput", Path.Combine(output, "install"),
            "-logFile", Path.Combine(output, "install.log"));
        Directory.CreateDirectory(build);
        string plan = Path.Combine(build, "project-preflight/dhe-project-plan.json");
        void Phase(string phase)
        {
            Execute(editor, "-batchmode", "-nographics", "-quit", "-projectPath", project,
                "-executeMethod", "HybridCLR.Lab.Editor.SnapshotWorkflowBuild." + phase,
                "-dheTarget", "StandaloneWindows64", "-dheOutputRoot", build, "-dheBaselineRoot", Path.Combine(build, "baseline"),
                "-dheCurrentRoot", Path.Combine(build, "current"), "-dheMode", "Exploratory", "-dheBootstrap", "true",
                "-dheProjectPlan", plan, "-dheEngineWorkflow", "Unity2022Fgs", "-dheIl2CppCodeGeneration", "OptimizeSize",
                "-logFile", Path.Combine(output, phase + ".log"));
        }
        Phase("Prepare");
        Execute("dotnet", tool, "preflight", "-SettingsFile", Path.Combine(project, "ProjectSettings/HybridCLRSettings.asset"),
            "-BaselineRoot", Path.Combine(build, "baseline"), "-CurrentRoot", Path.Combine(build, "current"),
            "-OutputRoot", Path.Combine(build, "project-preflight"), "-ProjectRoot", project, "-RequireDheEqualsHotUpdate", "-RequireCompleteCoverage");
        Phase("StageRuntimePlan"); Phase("BuildScriptsOnly"); Phase("BuildFinalPlayer");
        string identityPath = Path.Combine(build, "build-identity.json");
        using var identity = JsonDocument.Parse(File.ReadAllBytes(identityPath));
        string[] Names(string property, string child = null) => identity.RootElement.GetProperty(property).EnumerateArray()
            .Select(row => child == null ? row.GetString() : row.GetProperty(child).GetString()).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity.RootElement, Names("aotAssemblyNames"), Names("assemblies", "assemblyName"));
        string nativeManifest = Path.Combine(build, "native/dhe-native-manifest.json");
        var toolAssembly = System.Reflection.Assembly.LoadFrom(tool);
        toolAssembly.GetType("HybridCLR.DheTool.Program", true)
            .GetMethod("ValidateNativeFinalizeEvidence", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Invoke(null, new object[] { Path.Combine(build, "adapter/native-finalize.json"),
                Path.Combine(build, "adapter/build-final-player.json"), "StandaloneWindows64", project, nativeManifest });
        Execute("dotnet", tool, "schema-validate", "-Schema", Path.Combine(lab, "schemas/dhe-build-identity.schema.json"),
            "-Document", identityPath, "-Output", Path.Combine(output, "identity-schema.json"));
        string player = Path.Combine(build, "player/Snapshot.exe"), playerReport = Path.Combine(output, "player-result.json");
        Execute(player, "-batchmode", "-nographics", "-snapshotResult", playerReport, "-expectedRevision", expectedRevision, "-logFile", Path.Combine(output, "player.log"));
        using var result = JsonDocument.Parse(File.ReadAllBytes(playerReport));
        bool passed = result.RootElement.GetProperty("passed").GetBoolean() &&
            result.RootElement.GetProperty("baseId").GetString() == identity.RootElement.GetProperty("baseId").GetString() &&
            result.RootElement.GetProperty("aotAnalysisSnapshotSha256").GetString() == snapshot.Sha256;
        string resource = Path.Combine(output, "resource-noop"), staging = Path.Combine(output, "stage-noop");
        Execute("dotnet", tool, "resource-update", "-CurrentRoot", Path.Combine(build, "current"), "-SettingsFile",
            Path.Combine(project, "ProjectSettings/HybridCLRSettings.asset"), "-BaselineRoot", Path.Combine(build, "baseline"),
            "-BaseNativeManifest", nativeManifest, "-BaseBuildIdentity", identityPath,
            "-AotMetadataRoot", Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath), "assemblies"),
            "-Mode", "Exploratory", "-OutputRoot", resource);
        string embeddedAssets = Path.Combine(build, "player/Snapshot_Data/StreamingAssets/SnapshotDHE");
        foreach (string source in Directory.GetFiles(embeddedAssets, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(staging, Path.GetRelativePath(embeddedAssets, source));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(source, destination);
        }
        Execute("dotnet", tool, "stage-resource-update", "-UpdateRoot", resource, "-AssetRoot", staging,
            "-BaseBuildIdentity", identityPath, "-ImmutableFiles", player + "," + Path.Combine(build, "player/GameAssembly.dll"),
            "-Output", Path.Combine(output, "stage-noop.json"));
        string resourceResult = Path.Combine(output, "resource-player-result.json");
        Execute(player, "-batchmode", "-nographics", "-snapshotResult", resourceResult, "-snapshotResourceRoot", staging,
            "-expectedRevision", expectedRevision, "-logFile", Path.Combine(output, "resource-player.log"));
        using var loadedResource = JsonDocument.Parse(File.ReadAllBytes(resourceResult));
        passed &= loadedResource.RootElement.GetProperty("passed").GetBoolean() &&
            loadedResource.RootElement.GetProperty("resourceUpdate").GetBoolean() &&
            loadedResource.RootElement.GetProperty("baseId").GetString() == identity.RootElement.GetProperty("baseId").GetString();
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
        {
            passed, scope = "Unity2022 package Base phases, final snapshot/native validation and generated no-op resource loaded by the same immutable Player; not layout resource release qualification",
            labHead, packageHead, toolSha256 = Hash(tool), hostSha256 = Hash(typeof(UnityWorkflow).Assembly.Location),
            runtimeManifest, runtimeManifestSha256 = Hash(runtimeManifest), identitySha256 = Hash(identityPath),
            snapshotManifestSha256 = snapshot.Sha256, ordinaryAotCount = snapshot.OrdinaryAssemblyPaths.Length,
            resourceManifestSha256 = Hash(Path.Combine(resource, "dhe-resource-update.json")),
            resourcePlayerResultSha256 = Hash(resourceResult), stageSha256 = Hash(Path.Combine(output, "stage-noop.json")),
            playerSha256 = Hash(player), gameAssemblySha256 = Hash(Path.Combine(build, "player/GameAssembly.dll")),
        }, new JsonSerializerOptions { WriteIndented = true }));
        return passed ? 0 : 1;
    }
}
