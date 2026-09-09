using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class ResourceEvolutionWorkflow
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static readonly string[] Names = { "HybridCLR.ValueLayoutModel", "HybridCLR.ValueLayoutOther", "HybridCLR.ValueLayoutConsumer" };
    private const string Native = "HybridCLR.ValueLayoutNative";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
    private static void Fresh(string path)
    {
        if (Directory.Exists(path) || File.Exists(path)) throw new IOException("Output must be new: " + path);
    }
    public static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("evolution-reference <Current root> <ordinary Native DLL> <new report>");
        string current = Path.GetFullPath(args[0]), nativePath = Path.GetFullPath(args[1]), report = Path.GetFullPath(args[2]); Fresh(report);
        AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(current, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(current, name.Name + ".dll")) : null;
        var model = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(current, Names[0] + ".dll"));
        var records = (string[])model.GetType("HybridCLR.Lab.ValueLayout.ResourceEvolutionProbe", true).GetMethod("Run").Invoke(null, null);
        var native = AssemblyLoadContext.Default.LoadFromAssemblyPath(nativePath);
        long ordinary = (long)native.GetType("HybridCLR.Lab.ValueLayoutNative.NativeBoundary", true).GetMethod("ResourceResult").Invoke(null, null);
        int neighbor = (int)native.GetType("HybridCLR.Lab.ValueLayoutNative.NativeBoundary", true).GetMethod("StaticNeighbor").Invoke(null, null);
        bool passed = records.Contains("static-initializations=1") && records.Contains("static-reflection=1");
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed, records, ordinaryAotReferenceResult = ordinary, ordinaryAotStaticNeighbor = neighbor,
            scope = "CLR reference of actual Unity stripped input", current, nativePath,
            inputs = Names.Select(name => new { name, sha256 = Hash(Path.Combine(current, name + ".dll")) }), nativeSha256 = Hash(nativePath) }, Json));
        return passed ? 0 : 1;
    }
    public static int Run(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("evolution-workflow <lab> <package> <Unity.exe> <runtime manifest> <new output>");
        string lab = Path.GetFullPath(args[0]), package = Path.GetFullPath(args[1]), editor = Path.GetFullPath(args[2]),
            runtimeManifest = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]); Fresh(output); Directory.CreateDirectory(output);
        string host = Assembly.GetExecutingAssembly().Location, tool = Path.Combine(lab, "tool/bin/Release/net6.0/HybridCLR.DheTool.dll");
        string Execute(string exe, params string[] arguments)
        {
            var start = new ProcessStartInfo(exe) { WorkingDirectory = lab, UseShellExecute = false, RedirectStandardOutput = true,
                RedirectStandardError = true, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException(exe);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Evolution started " + Path.GetFileName(exe) + " PID " + process.Id);
            if (!process.WaitForExit(40 * 60 * 1000)) { process.Kill(true); throw new TimeoutException(exe); }
            string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            string log = Path.Combine(output, "process-" + process.Id + ".log"); File.WriteAllText(log, text);
            if (process.ExitCode != 0) throw new InvalidOperationException(exe + " failed; see " + log + "\n" + text);
            return text.Trim();
        }
        string labHead = Execute("git", "-C", lab, "rev-parse", "HEAD");
        if (Execute("git", "-C", lab, "status", "--porcelain").Length != 0) throw new InvalidDataException("Commit sources before evolution verification.");
        foreach (string version in new[] { "old", "new", "current" })
            Execute("dotnet", "build", "managed-cases/HybridCLR.ValueLayoutModel/HybridCLR.ValueLayoutModel.csproj", "-c", "Release", "--nologo", "--no-incremental",
                "-o", Path.Combine(output, "sdk", version), "-p:DefineConstants=" + (version == "current" ? "DHE_RESOURCE_CURRENT" : version == "new" ? "DHE_RESOURCE_BASE_NEW" : "DHE_RESOURCE_BASE_OLD"));
        Execute("dotnet", "build", "managed-cases/HybridCLR.ValueLayoutConsumer/HybridCLR.ValueLayoutConsumer.csproj", "-c", "Release", "--nologo", "--no-incremental",
            "-o", Path.Combine(output, "sdk/consumer"), "-p:DheValueLayoutBaseRoot=" + Path.Combine(output, "sdk/old"));
        foreach (string version in new[] { "old", "new", "current" })
            foreach (string name in Names.Skip(1).Append(Native))
                File.Copy(Path.Combine(output, "sdk/consumer", name + ".dll"), Path.Combine(output, "sdk", version, name + ".dll"));
        foreach (string version in new[] { "old", "new" })
            Execute("dotnet", host, "unity-workflow", lab, package, editor, runtimeManifest, Path.Combine(output, "sdk", version),
                Path.Combine(output, "base-" + version), version == "old" ? "41" : "51");

        // Compile Current in a separate project. Neither Base's captured input,
        // generated C++ evidence, embedded MV nor Player is changed by this step.
        string currentProject = Path.Combine(output, "current-project"), currentBuild = Path.Combine(output, "current-build");
        Execute("dotnet", Path.Combine(lab, "tool/fixtures/value-layout/bin/Release/net6.0/ValueLayoutTests.dll"), "probe-project",
            lab, package, "Unity2022Fgs", Path.Combine(output, "sdk/current"), currentProject);
        Execute(editor, "-batchmode", "-nographics", "-quit", "-projectPath", currentProject,
            "-executeMethod", "HybridCLR.Lab.Editor.CurrentStorageProbeBuild.Prepare", "-probeRuntime",
            Read(runtimeManifest).GetProperty("stagedLibil2cpp").GetString(), "-probeOutput", currentBuild,
            "-logFile", Path.Combine(output, "current-prepare.log"));
        string current = Path.Combine(currentBuild, "generated-current");
        var snapshots = new Dictionary<string, AotAnalysisSnapshot>();
        foreach (string version in new[] { "old", "new" })
        {
            string baseRoot = Path.Combine(output, "base-" + version, "base"), identityPath = Path.Combine(baseRoot, "build-identity.json");
            var identity = Read(identityPath);
            snapshots.Add(version, AotAnalysisSnapshot.Read(identityPath, identity,
                identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(item => item.GetString()), Names));
            Execute("dotnet", host, "evolution-reference", Path.Combine(baseRoot, "current"),
                snapshots[version].OrdinaryAssemblyPaths.Single(path => Path.GetFileNameWithoutExtension(path) == Native),
                Path.Combine(output, "reference-" + version + ".json"));
        }
        Execute("dotnet", host, "evolution-reference", current,
            snapshots["old"].OrdinaryAssemblyPaths.Single(path => Path.GetFileNameWithoutExtension(path) == Native), Path.Combine(output, "reference-current.json"));
        string Pair(string suffix) => string.Join(",", new[] { "old", "new" }.Select(version => Path.Combine(output, "base-" + version, "base", suffix)));
        string resource = Path.Combine(output, "resource-current");
        Execute("dotnet", tool, "resource-update", "-CurrentRoot", current, "-SettingsFile", Path.Combine(currentProject, "ProjectSettings/HybridCLRSettings.asset"),
            "-BaseRoots", Pair("baseline"), "-BaseNativeManifests", Pair("native/dhe-native-manifest.json"), "-BaseBuildIdentities", Pair("build-identity.json"),
            "-AotMetadataRoots", string.Join(",", new[] { "old", "new" }.Select(version => Path.Combine(Path.GetDirectoryName(snapshots[version].ManifestPath), "assemblies"))),
            "-Mode", "Exploratory", "-OutputRoot", resource);
        var checks = new Dictionary<string, bool>();
        var reference = Read(Path.Combine(output, "reference-current.json"));
        bool Matches(JsonElement player, JsonElement expected) => player.GetProperty("passed").GetBoolean() &&
            player.GetProperty("records").EnumerateArray().Select(item => item.GetString()).SequenceEqual(expected.GetProperty("records").EnumerateArray().Select(item => item.GetString())) &&
            player.GetProperty("ordinaryAotReferenceResult").GetInt64() == expected.GetProperty("ordinaryAotReferenceResult").GetInt64() &&
            player.GetProperty("ordinaryAotStaticNeighbor").GetInt32() == expected.GetProperty("ordinaryAotStaticNeighbor").GetInt32();
        foreach (string version in new[] { "old", "new" })
        {
            string run = Path.Combine(output, "base-" + version), build = Path.Combine(run, "base"), stage = Path.Combine(output, "stage-" + version);
            checks["base-reference-" + version] = Matches(Read(Path.Combine(run, "player-result.json")), Read(Path.Combine(output, "reference-" + version + ".json")));
            checks["base-noop-reference-" + version] = Matches(Read(Path.Combine(run, "resource-player-result.json")), Read(Path.Combine(output, "reference-" + version + ".json")));
            string embedded = Path.Combine(build, "player/Snapshot_Data/StreamingAssets/SnapshotDHE");
            foreach (string file in Directory.GetFiles(embedded, "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(stage, Path.GetRelativePath(embedded, file)); Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(file, target);
            }
            string player = Path.Combine(build, "player/Snapshot.exe"), game = Path.Combine(build, "player/GameAssembly.dll");
            Execute("dotnet", tool, "stage-resource-update", "-UpdateRoot", resource, "-AssetRoot", stage,
                "-BaseBuildIdentity", Path.Combine(build, "build-identity.json"), "-ImmutableFiles", player + "," + game, "-Output", Path.Combine(output, "stage-" + version + ".json"));
            string report = Path.Combine(output, "player-current-" + version + ".json");
            Execute(player, "-batchmode", "-nographics", "-snapshotResult", report, "-snapshotResourceRoot", stage, "-expectedRevision", "73", "-logFile", report + ".log");
            checks["current-reference-" + version] = Matches(Read(report), reference);
        }
        var manifest = Read(Path.Combine(resource, "dhe-resource-update.json"));
        checks["two-immutable-base-identities"] = manifest.GetProperty("supportedBases").GetArrayLength() == 2 &&
            manifest.GetProperty("supportedBases").EnumerateArray().Select(item => item.GetProperty("baseId").GetString()).Distinct().Count() == 2;
        checks["one-current-payload"] = manifest.GetProperty("payloadModel").GetString() == "single-current-payload" &&
            manifest.GetProperty("supportedBases").EnumerateArray().Select(item => item.GetProperty("currentAssemblySetSha256").GetString()).Distinct().Count() == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = checks.Values.All(value => value), checks,
            labHead, packageHead = Execute("git", "-C", package, "rev-parse", "HEAD"), hostSha256 = Hash(host), toolSha256 = Hash(tool),
            runtimeManifestSha256 = Hash(runtimeManifest), resourceManifestSha256 = Hash(Path.Combine(resource, "dhe-resource-update.json")),
            expectedRecords = reference.GetProperty("records"), scope = "Unity2022 resource evolution across two immutable Bases; not native value ABI qualification" }, Json));
        foreach (var item in checks) Console.WriteLine(item.Key + ": " + item.Value);
        return checks.Values.All(value => value) ? 0 : 1;
    }
}
