using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace HybridCLR.DheTool;

/// <summary>
/// Cross-platform replacements for the old lab PowerShell helpers. These
/// commands deliberately use only .NET, git, dotnet, cmake and the selected
/// Unity-family editor. A consuming project only needs the <c>workflow</c>
/// command; the other commands keep the repository's reproducibility checks
/// usable without a shell-specific implementation.
/// </summary>
internal static class LabCommands
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Run(Cli cli)
    {
        return cli.Command.ToLowerInvariant() switch
        {
            "assemble-runtime" => AssembleRuntime(cli),
            "native-tests" => NativeTests(cli),
            "build-managed-cases" => BuildManagedCases(cli),
            "generate-test-manifest" => GenerateTestManifest(cli),
            "generate-metadata-stress-source" => GenerateMetadataStressSource(cli),
            "reference" => Reference(cli),
            "compare-results" => CompareResults(cli),
            "check-environment" => CheckEnvironment(cli),
            "clear-unity-project-locks" => ClearUnityProjectLocks(cli),
            "wait-editor" => WaitEditor(cli),
            "prepare-engine-test-project" => PrepareEngineTestProject(cli),
            "bootstrap-repos" => BootstrapRepos(cli),
            "tree-hash" => TreeHashCommand(cli),
            "file-hash" => FileHashCommand(cli),
            _ => throw new InvalidOperationException("Unsupported C# lab command: " + cli.Command)
        };
    }

    private static int TreeHashCommand(Cli cli)
    {
        var root = RequireDirectory(cli.Require("path"));
        var hash = TreeHash(root, cli.Has("excludegit"), cli.GetList("ignore"));
        Console.WriteLine(hash);
        return 0;
    }

    private static int FileHashCommand(Cli cli)
    {
        var path = Path.GetFullPath(cli.Require("path"));
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        Console.WriteLine(Sha256File(path).ToUpperInvariant());
        return 0;
    }

    private static int GenerateTestManifest(Cli cli)
    {
        var lab = LabRoot(cli);
        var output = ResolvePath(lab, cli.Optional("output") ?? "manifests/test-manifest.json");
        var project = Path.Combine(lab, "runners", "manifest-generator", "HybridCLR.ManifestGenerator.csproj");
        RunProcess("dotnet", new[] { "run", "--project", project, "--configuration", "Release", "--", output }, lab);
        var document = ReadJson(output);
        var suite = StringProperty(document, "suiteId");
        var ids = new List<string> { "suiteId=" + suite };
        var contracts = new List<string> { "suiteId=" + suite };
        foreach (var item in document.GetProperty("cases").EnumerateArray())
        {
            var id = StringProperty(item, "id");
            ids.Add(id);
            contracts.Add(string.Join("\t", id, StringProperty(item, "category"), StringProperty(item, "layer"),
                string.Join(',', item.GetProperty("features").EnumerateArray().Select(x => x.GetString() ?? string.Empty))));
        }
        WriteText(Path.ChangeExtension(output, ".ids"), string.Join('\n', ids) + "\n");
        WriteText(Path.ChangeExtension(output, ".contracts"), string.Join('\n', contracts) + "\n");
        Console.WriteLine("Generated manifest: " + output);
        return 0;
    }

    private static int GenerateMetadataStressSource(Cli cli)
    {
        var lab = LabRoot(cli);
        var policy = ResolvePath(lab, cli.Optional("policy") ?? "manifests/metadata-benchmark-policy.json");
        var stress = ReadJson(policy).GetProperty("stressAssembly");
        var typeCount = PositiveInt(stress, "typeCount");
        var methodsPerType = PositiveInt(stress, "methodsPerType");
        var fieldsPerType = PositiveInt(stress, "fieldsPerType");
        var propertiesPerType = PositiveInt(stress, "propertiesPerType");
        var output = Path.Combine(lab, "managed-cases", "HybridCLR.MetadataStress", "Generated", "MetadataStress.Generated.cs");
        var builder = new StringBuilder(4 * 1024 * 1024);
        builder.AppendLine("using System;");
        builder.AppendLine("namespace HybridCLR.Lab.MetadataStress");
        builder.AppendLine("{");
        builder.AppendLine("    [AttributeUsage(AttributeTargets.All, AllowMultiple = true)]");
        builder.AppendLine("    public sealed class StressTagAttribute : Attribute");
        builder.AppendLine("    {");
        builder.AppendLine("        public StressTagAttribute(int id, string name) { Id = id; Name = name; }");
        builder.AppendLine("        public int Id { get; }");
        builder.AppendLine("        public string Name { get; }");
        builder.AppendLine("    }");
        builder.AppendLine("    public interface IStressContract<T> { T Transform(T value); }");
        builder.AppendLine("    public static class MetadataStressEntry");
        builder.AppendLine("    {");
        builder.AppendLine("        public static long Touch()");
        builder.AppendLine("        {");
        builder.AppendLine("            long checksum = 0;");
        for (var i = 0; i < typeCount; i += 16)
        {
            builder.AppendLine($"            checksum += new StressType{i:D4}().Method00({i + 1});");
            builder.AppendLine($"            checksum += new StressType{i:D4}.Nested<int>({i}).Value;");
        }
        builder.AppendLine("            return checksum;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        for (var typeIndex = 0; typeIndex < typeCount; typeIndex++)
        {
            var typeName = $"StressType{typeIndex:D4}";
            builder.AppendLine($"    [StressTag({typeIndex}, \"{typeName}\")]");
            builder.AppendLine($"    public sealed class {typeName} : IStressContract<int>");
            builder.AppendLine("    {");
            for (var field = 0; field < fieldsPerType; field++) builder.AppendLine($"        public long Field{field:D2};");
            for (var property = 0; property < propertiesPerType; property++) builder.AppendLine($"        public int Property{property:D2} {{ get; set; }}");
            builder.AppendLine($"        public int Transform(int value) {{ return value + {typeIndex}; }}");
            builder.AppendLine("        public T Echo<T>(T value) { return value; }");
            for (var method = 0; method < methodsPerType; method++)
            {
                var tag = typeIndex * methodsPerType + method;
                builder.AppendLine($"        [StressTag({tag}, \"M{method:D2}\")]");
                builder.AppendLine($"        public int Method{method:D2}(int value) {{ return value + {typeIndex} + {method}; }}");
            }
            builder.AppendLine("        public sealed class Nested<T>");
            builder.AppendLine("        {");
            builder.AppendLine("            public Nested(T value) { Value = value; }");
            builder.AppendLine("            public T Value { get; }");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
        }
        builder.AppendLine("}");
        WriteText(output, builder.ToString());
        Console.WriteLine("Metadata stress source: " + output);
        return 0;
    }

    private static int BuildManagedCases(Cli cli)
    {
        var lab = LabRoot(cli);
        var target = cli.Optional("target") ?? "StandaloneWindows64";
        var configuration = cli.Optional("configuration") ?? "Release";
        var variant = cli.Optional("variant") ?? "default";
        if (!variant.Equals("default", StringComparison.OrdinalIgnoreCase) &&
            !variant.Equals("base2", StringComparison.OrdinalIgnoreCase) &&
            !variant.Equals("current", StringComparison.OrdinalIgnoreCase) &&
            !variant.Equals("structural", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "build-managed-cases -Variant must be default, base2, current, or structural.");
        var isCurrent = variant.Equals("current", StringComparison.OrdinalIgnoreCase) ||
            variant.Equals("structural", StringComparison.OrdinalIgnoreCase);
        var isStructural = variant.Equals("structural", StringComparison.OrdinalIgnoreCase);
        GenerateTestManifest(new Cli("generate-test-manifest", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["labroot"] = lab }));
        GenerateMetadataStressSource(new Cli("generate-metadata-stress-source", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["labroot"] = lab }));
        var output = ResolvePath(lab, cli.Optional("outputroot") ??
            (isCurrent
                ? $"artifacts/managed-cases-{variant}/{target}"
                : $"artifacts/managed-cases/{target}"));
        var aotOutput = ResolvePath(lab, isCurrent
            ? $"artifacts/managed-cases-{variant}-aot/{target}"
            : variant.Equals("base2", StringComparison.OrdinalIgnoreCase)
                ? $"artifacts/managed-cases-base2-aot/{target}"
                : $"artifacts/managed-cases-aot/{target}");
        SafeDelete(output, lab); SafeDelete(aotOutput, lab);
        Directory.CreateDirectory(output); Directory.CreateDirectory(aotOutput);
        var targetDefine = target.Equals("StandaloneWindows64", StringComparison.OrdinalIgnoreCase) ? "HYBRIDCLR_TARGET_WINDOWS" : target.Equals("Android", StringComparison.OrdinalIgnoreCase) ? "HYBRIDCLR_TARGET_ANDROID" : "";
        // Keep the fixture variants comparable to the historical DHE gate:
        // the baseline dependency build uses the project defaults and the
        // current dependency build adds only DHE_CURRENT. Target-specific
        // symbols would change compiler-generated metadata/token ordering and
        // turn a body-only diff into a false layout/token incompatibility.
        var dependencyDefine = isCurrent
            ? isStructural ? "DHE_CURRENT;DHE_STRUCTURE_CURRENT" : "DHE_CURRENT"
            : targetDefine;
        var projects = new[]
        {
            "managed-cases/HybridCLR.ManagedCases/HybridCLR.ManagedCases.csproj",
            "managed-cases/HybridCLR.CrossAssemblyDerived/HybridCLR.CrossAssemblyDerived.csproj",
            "managed-cases/HybridCLR.MetadataStress/HybridCLR.MetadataStress.csproj"
        };
        foreach (var relative in projects)
        {
			var args = new List<string> { "build", Path.Combine(lab, relative), "--configuration", configuration,
				"--output", output, "--nologo", "--no-incremental", "-v:minimal" };
            if (!string.IsNullOrWhiteSpace(dependencyDefine))
                args.Add("-p:DefineConstants=" + dependencyDefine.Replace(";", "%3B", StringComparison.Ordinal));
            RunProcess("dotnet", args, lab);
        }
        var aotProject = Path.Combine(lab, "managed-cases/HybridCLR.ManagedCasesAot/HybridCLR.ManagedCasesAot.csproj");
		var aotArgs = new List<string> { "build", aotProject, "--configuration", configuration,
			"--output", aotOutput, "--nologo", "--no-incremental", "-v:minimal" };
        if (isCurrent)
            aotArgs.Add("-p:DefineConstants=HYBRIDCLR_AOT_BENCHMARK%3BDHE_CURRENT" +
                (isStructural ? "%3BDHE_STRUCTURE_CURRENT" : string.Empty));
        else if (variant.Equals("base2", StringComparison.OrdinalIgnoreCase))
            aotArgs.Add("-p:DefineConstants=HYBRIDCLR_AOT_BENCHMARK%3BDHE_BASE2");
		else
			aotArgs.Add("-p:DefineConstants=HYBRIDCLR_AOT_BENCHMARK");
        RunProcess("dotnet", aotArgs, lab);
        var aotDll = Path.Combine(aotOutput, "HybridCLR.ManagedCasesAot.dll");
        CopyRequired(aotDll, Path.Combine(output, "HybridCLR.ManagedCasesAot.dll"));
		if (cli.Optional("unityprojectroot") is { Length: > 0 } unityProjectRoot)
		{
			var plugin = ResolvePath(lab, Path.Combine(unityProjectRoot,
				"Assets/Plugins/HybridCLRLab"));
			Directory.CreateDirectory(plugin);
			foreach (var name in new[] { "HybridCLR.BoundaryContracts.dll", "HybridCLR.ManagedCases.dll",
				"HybridCLR.MetadataStress.dll", "HybridCLR.CrossAssemblyDerived.dll" })
				CopyRequired(Path.Combine(output, name), Path.Combine(plugin, name));
			CopyRequired(aotDll, Path.Combine(plugin, "HybridCLR.ManagedCasesAot.dll"));
		}
        Console.WriteLine("Managed cases (" + variant + "): " + output);
        return 0;
    }

    private static int Reference(Cli cli)
    {
        var lab = LabRoot(cli);
        var output = ResolvePath(lab, cli.Optional("output") ?? "reports/reference-result.json");
        var project = Path.Combine(lab, "runners/dotnet-reference/HybridCLR.ReferenceRunner.csproj");
        RunProcess("dotnet", new[] { "run", "--project", project, "--configuration", "Release", "--", "--manifest", Path.Combine(lab, "manifests/test-manifest.json"), "--golden", Path.Combine(lab, "manifests/test-golden.json"), "--output", output }, lab);
        return 0;
    }

    private static int CompareResults(Cli cli)
    {
        var lab = LabRoot(cli);
        var reference = ReadJson(ResolvePath(lab, cli.Optional("reference") ?? "reports/reference-result.json"));
        var actual = ReadJson(ResolvePath(lab, cli.Require("actual")));
        var expected = reference.GetProperty("cases").EnumerateArray().ToDictionary(x => StringProperty(x, "id"), StringComparer.Ordinal);
        var observed = actual.GetProperty("cases").EnumerateArray().ToDictionary(x => StringProperty(x, "id"), StringComparer.Ordinal);
        var differences = new List<object>();
        var fields = new[] { "category", "layer", "status", "returnValue", "sideEffect", "exceptionType" };
        foreach (var id in expected.Keys.Union(observed.Keys).OrderBy(x => x, StringComparer.Ordinal))
        {
            if (!expected.TryGetValue(id, out var e)) { differences.Add(new { @case = id, field = "case", expected = "missing", actual = "present" }); continue; }
            if (!observed.TryGetValue(id, out var a)) { differences.Add(new { @case = id, field = "case", expected = "present", actual = "missing" }); continue; }
            foreach (var field in fields)
            {
                var ev = e.TryGetProperty(field, out var ep) ? ep.GetRawText() : "null";
                var av = a.TryGetProperty(field, out var ap) ? ap.GetRawText() : "null";
                if (!string.Equals(ev, av, StringComparison.Ordinal)) differences.Add(new { @case = id, field, expected = ev, actual = av });
            }
            var ef = e.GetProperty("features").GetRawText(); var af = a.GetProperty("features").GetRawText();
            if (!string.Equals(ef, af, StringComparison.Ordinal)) differences.Add(new { @case = id, field = "features", expected = ef, actual = af });
        }
        var output = ResolvePath(lab, cli.Optional("output") ?? "reports/differential-result.json");
        WriteJson(output, new { schemaVersion = 2, comparedAtUtc = DateTimeOffset.UtcNow, reference = ResolvePath(lab, cli.Optional("reference") ?? "reports/reference-result.json"), actual = ResolvePath(lab, cli.Require("actual")), summary = new { referenceCases = expected.Count, actualCases = observed.Count, differences = differences.Count, passed = differences.Count == 0 }, differences });
        Console.WriteLine("Differential result: " + differences.Count + " differences");
        return differences.Count == 0 ? 0 : 1;
    }

    private static int CheckEnvironment(Cli cli)
    {
        var lab = LabRoot(cli);
        var workflowId = cli.Optional("engineworkflow") ?? "Tuanjie2022Fgs";
        var target = cli.Optional("target") ?? "StandaloneWindows64";
        var workflows = ReadJson(Path.Combine(lab, "manifests/runtime-workflows.json"));
        var workflow = workflows.GetProperty("workflows").EnumerateArray().SingleOrDefault(x => StringProperty(x, "id") == workflowId);
        if (workflow.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Engine workflow was not found: " + workflowId);
        var engine = workflow.GetProperty("engine");
        var editor = StringProperty(engine, "executablePath");
        var editorExists = File.Exists(editor);
        var requirements = new Dictionary<string, bool>(StringComparer.Ordinal)
        {
            ["editor"] = editorExists,
            ["dotnet"] = FindOnPath("dotnet"),
            ["git"] = FindOnPath("git"),
            ["cmake"] = CanResolveExecutable("cmake")
        };
        var ready = requirements["editor"] && requirements["dotnet"] && (target.Equals("Android", StringComparison.OrdinalIgnoreCase) ? true : requirements["cmake"]);
        var report = ResolvePath(lab, cli.Optional("output") ?? "reports/build-environment.json");
        WriteJson(report, new { schemaVersion = 1, checkedAtUtc = DateTimeOffset.UtcNow, target, engineWorkflow = workflowId, editorPath = editor, editorExists, requirements, ready, platform = Environment.OSVersion.Platform.ToString() });
        Console.WriteLine("Build environment: " + (ready ? "ready" : "incomplete"));
        return ready ? 0 : 1;
    }

    private static int ClearUnityProjectLocks(Cli cli)
    {
        var project = RequireDirectory(cli.Require("projectroot"));
        foreach (var relative in new[] { "Library/ArtifactDB-lock", "Library/SourceAssetDB-lock", "Temp/UnityLockfile" })
        {
            var file = Path.Combine(project, relative);
            if (File.Exists(file)) File.Delete(file);
        }
        return 0;
    }

    private static int WaitEditor(Cli cli)
    {
        var project = Path.GetFullPath(cli.Require("projectroot"));
        var processName = Path.GetFileNameWithoutExtension(cli.Optional("editorprocessname") ?? "Tuanjie");
        var timeout = int.TryParse(cli.Optional("timeoutseconds"), out var value) ? Math.Clamp(value, 1, 7200) : 900;
        var stable = int.TryParse(cli.Optional("stableabsenceseconds"), out var stableValue) ? Math.Clamp(stableValue, 1, 60) : 5;
        var requireObserved = cli.Has("requireobserved");
        var observed = false;
        var deadline = DateTime.UtcNow.AddSeconds(timeout); var absentSince = (DateTime?)null;
        while (DateTime.UtcNow < deadline)
        {
            var projectLock = Path.Combine(project, "Temp", "UnityLockfile");
            var active = File.Exists(projectLock)
                ? Process.GetProcessesByName(processName)
                : Process.GetProcessesByName(processName)
                    .Where(p => ProcessMentionsProject(p, project)).ToArray();
            if (active.Length != 0) { observed = true; absentSince = null; }
            else if (requireObserved && !observed) absentSince = null;
            else if (absentSince == null) absentSince = DateTime.UtcNow;
            else if ((DateTime.UtcNow - absentSince.Value).TotalSeconds >= stable) return 0;
            Thread.Sleep(500);
        }
        throw new TimeoutException("Timed out waiting for editor processes for " + project +
            (requireObserved && !observed ? " (the target Editor was never observed)" : string.Empty));
    }

    private static int PrepareEngineTestProject(Cli cli)
    {
        var lab = LabRoot(cli); var workflowId = cli.Require("engineworkflow");
        var destinationRoot = ResolvePath(lab, cli.Optional("outputroot") ?? "artifacts/engine-projects");
        var destination = Path.Combine(destinationRoot, workflowId);
        SafeDelete(destination, destinationRoot);
        var source = Path.Combine(lab, "unity-test-project"); Directory.CreateDirectory(Path.Combine(destination, "Assets"));
        var workflows = ReadJson(Path.Combine(lab, "manifests/runtime-workflows.json"));
        var workflow = workflows.GetProperty("workflows").EnumerateArray().Single(x => StringProperty(x, "id") == workflowId);
        foreach (var folder in new[] { "Packages", "ProjectSettings" }) CopyDirectory(Path.Combine(source, folder), Path.Combine(destination, folder));
        foreach (var asset in new[] { "Editor", "Editor.meta", "Runtime", "Runtime.meta", "Scenes", "Scenes.meta" }) CopyDirectoryOrFile(Path.Combine(source, "Assets", asset), Path.Combine(destination, "Assets", asset));
        foreach (var meta in Directory.GetFiles(Path.Combine(destination, "Assets"), "*.meta", SearchOption.AllDirectories)) File.Delete(meta);
        var repoLock = ReadJson(Path.Combine(lab, "manifests/repo-lock.json"));
        var reposRoot = ResolveReposRoot(lab, repoLock, cli.Optional("reposroot"));
        var repoRoot = ResolvePath(lab, cli.Optional("hybridclrUnitySource") ??
            Path.Combine(reposRoot, "hybridclr_unity"));
        ValidateRepoIdentity("hybridclr_unity", repoRoot,
            StringProperty(repoLock.GetProperty("repositories").GetProperty("hybridclr_unity"),
                "commit"), false);
        CopyDirectory(repoRoot, Path.Combine(destination, "Packages/com.code-philosophy.hybridclr"), new[] { ".git" });
        var runtimeLock = ReadJson(Path.Combine(lab, "manifests/dhe-runtime-lock.json"));
        ApplyLockedOverlays(lab, runtimeLock, "hybridclr_unity",
            Path.Combine(destination, "Packages/com.code-philosophy.hybridclr"), workflowId,
            Git(repoRoot, "rev-parse", "HEAD"));
        var packageManifestPath = Path.Combine(destination, "Packages", "manifest.json");
        if (File.Exists(packageManifestPath))
        {
            var packageManifest = JsonNode.Parse(File.ReadAllText(packageManifestPath))?.AsObject() ?? throw new InvalidOperationException("Invalid Unity package manifest: " + packageManifestPath);
            if (packageManifest["dependencies"] is JsonObject dependencies)
            {
                dependencies.Remove("com.code-philosophy.hybridclr");
                if (StringProperty(workflow.GetProperty("engine"), "family") == "Unity")
                {
                    dependencies.Remove("com.unity.modules.infinity");
                    if (workflowId == "Unity2021Standard") dependencies.Remove("com.unity.ai.navigation");
                }
            }
            WriteText(packageManifestPath, packageManifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        var packageLock = Path.Combine(destination, "Packages", "packages-lock.json");
        if (File.Exists(packageLock)) File.Delete(packageLock);
        var version = StringProperty(workflow.GetProperty("engine"), "unityVersion");
        WriteText(Path.Combine(destination, "ProjectSettings/ProjectVersion.txt"), "m_EditorVersion: " + version + "\n");
        Console.WriteLine(destination);
        return 0;
    }

    private static int BootstrapRepos(Cli cli)
    {
        var lab = LabRoot(cli); var lockDoc = ReadJson(Path.Combine(lab, "manifests/repo-lock.json"));
        var repos = ResolveReposRoot(lab, lockDoc, cli.Optional("reposroot")); Directory.CreateDirectory(repos);
        foreach (var name in new[] { "hybridclr_unity", "hybridclr", "il2cpp_plus" })
        {
            var spec = lockDoc.GetProperty("repositories").GetProperty(name); var path = Path.Combine(repos, name);
            if (!Directory.Exists(Path.Combine(path, ".git"))) RunProcess("git", new[] { "clone", StringProperty(spec, "fork"), path }, lab);
            var dirty = Git(path, "status", "--porcelain");
            if (!string.IsNullOrWhiteSpace(dirty) && !cli.Has("allowdirty")) throw new InvalidOperationException($"Repository '{name}' has local changes.");
            if (!cli.Has("nocheckout"))
            {
                RunProcess("git", new[] { "-C", path, "fetch", "origin", "--tags", "--force" }, lab);
                RunProcess("git", new[] { "-C", path, "checkout", "--detach", StringProperty(spec, "commit") }, lab);
            }
        }
        return 0;
    }

    private static int NativeTests(Cli cli)
    {
        var lab = LabRoot(cli); var profile = cli.Optional("profile") ?? "Baseline-Clean"; var configuration = cli.Optional("configuration") ?? "Release";
        var runtimeBase = ResolvePath(lab, cli.Optional("runtimeroot") ?? "staging/runtime");
        var runtimeRoot = Path.Combine(runtimeBase, profile); var runtimeManifest = ReadJson(Path.Combine(runtimeRoot, "runtime-manifest.json"));
        var source = Path.Combine(lab, "native-unit-tests");
        var outputRoot = ResolvePath(lab, cli.Optional("outputroot") ?? "artifacts/native-tests");
        var build = Path.Combine(outputRoot, profile);
        if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source); SafeDelete(build, outputRoot); Directory.CreateDirectory(build);
        var runtime = Path.Combine(runtimeRoot, "libil2cpp"); var external = Path.Combine(runtimeRoot, "external");
        if (runtimeManifest.TryGetProperty("externalHeaders", out var externalHeaders) && externalHeaders.TryGetProperty("surrogate", out var surrogate) && surrogate.ValueKind == JsonValueKind.True && !cli.Has("allowsurrogateexternalheaders"))
            throw new InvalidOperationException("Native tests refuse surrogate external headers; pass -AllowSurrogateExternalHeaders for exploratory validation.");
        var engine = runtimeManifest.TryGetProperty("engine", out var engineValue) ? engineValue : default;
        var unityVersion = engine.TryGetProperty("unityVersionNumber", out var unityValue) && unityValue.TryGetInt32(out var unityNumber) ? unityNumber : 20220362;
        var tuanjieVersion = engine.TryGetProperty("tuanjieVersionNumber", out var tuanjieValue) && tuanjieValue.TryGetInt32(out var tuanjieNumber) ? tuanjieNumber : 0;
        var fgs = runtimeManifest.TryGetProperty("fullGenericSharingDiagnostics", out var fgsValue) && fgsValue.ValueKind == JsonValueKind.True;
        var expectDhe = runtimeManifest.TryGetProperty("dheEnabled", out var dheValue) && dheValue.ValueKind == JsonValueKind.True;
        var platform = OperatingSystem.IsWindows() ? "Windows" : "OSX";
        var args = new List<string> { "-S", source, "-B", build, "-DHYBRIDCLR_RUNTIME_ROOT=" + runtime, "-DIL2CPP_EXTERNAL=" + external,
            "-DIL2CPP_BASELIB_INCLUDE=" + Path.Combine(external, "baselib", "Include"),
            "-DIL2CPP_BASELIB_PLATFORM_INCLUDE=" + Path.Combine(external, "baselib", "Platforms", platform, "Include"),
            "-DHYBRIDCLR_TEST_UNITY_VERSION=" + unityVersion, "-DHYBRIDCLR_TEST_TUANJIE_VERSION=" + tuanjieVersion,
            "-DHYBRIDCLR_TEST_FULL_GENERIC_SHARING=" + (fgs ? "1" : "0"), "-DHYBRIDCLR_TEST_EXPECT_DHE=" + (expectDhe ? "1" : "0") };
        if (cli.Optional("generator") is { Length: > 0 } generator) { args.Add("-G"); args.Add(generator); }
        var cmake = ResolveExecutable("cmake");
        var ctest = ResolveExecutable("ctest", Path.Combine(Path.GetDirectoryName(cmake) ?? string.Empty, "ctest" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)));
        RunProcess(cmake, args, lab);
        RunProcess(cmake, new[] { "--build", build, "--config", configuration, "--parallel" }, lab);
        RunProcess(ctest, new[] { "--test-dir", build, "-C", configuration, "--output-on-failure" }, lab);
        var surrogateHeadersAllowed = cli.Has("allowsurrogateexternalheaders");
        var logPath = Path.Combine(build, "native-test.log");
        WriteText(logPath, "CMake build and CTest completed successfully.\n");
        var runtimeManifestPath = Path.Combine(runtimeRoot, "runtime-manifest.json");
        WriteJson(Path.Combine(build, "native-gate.json"), new { schemaVersion = 1, format = "hybridclr.dhe-native-gate.json", passed = true, mergeReady = !(runtimeManifest.TryGetProperty("externalHeaders", out var headers) && headers.TryGetProperty("surrogate", out var surrogateValue) && surrogateValue.ValueKind == JsonValueKind.True), profile, configuration, runtimeRoot = runtime, runtimeManifest = runtimeManifestPath, runtimeManifestSha256 = Sha256File(runtimeManifestPath), runtimeTreeSha256 = TreeHash(runtime), externalTreeSha256 = TreeHash(external), nativeExitCode = 0, surrogateHeadersAllowed, log = logPath, errors = Array.Empty<string>(), generatedAtUtc = DateTimeOffset.UtcNow });
        return 0;
    }

    private static int AssembleRuntime(Cli cli)
    {
        var lab = LabRoot(cli); var profile = cli.Optional("profile") ?? "Baseline-Clean"; var workflowId = cli.Optional("engineworkflow") ?? "Tuanjie2022Fgs";
        var lockDoc = ReadJson(Path.Combine(lab, "manifests/repo-lock.json")); var workflowDoc = ReadJson(Path.Combine(lab, "manifests/runtime-workflows.json"));
        var runtimeLockPath = Path.Combine(lab, "manifests/dhe-runtime-lock.json");
        var runtimeLock = ReadJson(runtimeLockPath);
        var workflow = workflowDoc.GetProperty("workflows").EnumerateArray().Single(x => StringProperty(x, "id") == workflowId); var engine = workflow.GetProperty("engine");
        var repos = ResolveReposRoot(lab, lockDoc, cli.Optional("reposroot"));
        var hybridclr = ResolvePath(lab, cli.Optional("hybridclrsource") ?? Path.Combine(repos, "hybridclr"));
        var il2cpp = ResolvePath(lab, cli.Optional("il2cppplussource") ?? Path.Combine(repos, "il2cpp_plus"));
        var hybridclrUnity = ResolvePath(lab, cli.Optional("hybridclrUnitySource") ??
			Path.Combine(repos, "hybridclr_unity"));
        RequireDirectory(Path.Combine(hybridclr, "hybridclr")); RequireDirectory(Path.Combine(il2cpp, "libil2cpp"));
        var dhe = profile is "DHE-Tuanjie2022" or "DHE-Unity2022" or "DHE-Unity2021";
        if (dhe && cli.Has("allowdirty")) throw new InvalidOperationException("Publishable DHE runtime cannot use -AllowDirty.");
        var expected = StringProperty(workflow.GetProperty("il2cppPlus"), "commit");
        ValidateRepoIdentity("hybridclr", hybridclr, StringProperty(lockDoc.GetProperty("repositories").GetProperty("hybridclr"), "commit"), cli.Has("allowdirty"));
        ValidateRepoIdentity("il2cpp_plus", il2cpp, expected, cli.Has("allowdirty"));
        ValidateRepoIdentity("hybridclr_unity", hybridclrUnity,
            StringProperty(lockDoc.GetProperty("repositories").GetProperty("hybridclr_unity"), "commit"),
            cli.Has("allowdirty"));
        var output = ResolvePath(lab, cli.Optional("outputroot") ?? "staging/runtime"); var stage = Path.Combine(output, profile); SafeDelete(stage, output); Directory.CreateDirectory(stage);
        var stagedRuntime = Path.Combine(stage, "libil2cpp"); var stagedExternal = Path.Combine(stage, "external");
        CopyDirectory(Path.Combine(il2cpp, "libil2cpp"), stagedRuntime);
        var stagedHybridclr = Path.Combine(stagedRuntime, "hybridclr");
        if (Directory.Exists(stagedHybridclr)) Directory.Delete(stagedHybridclr, true);
        CopyDirectory(Path.Combine(hybridclr, "hybridclr"), stagedHybridclr);
        ApplyLockedOverlays(lab, runtimeLock, "il2cpp_plus", stagedRuntime, workflowId,
            Git(il2cpp, "rev-parse", "HEAD"));
        ApplyLockedOverlays(lab, runtimeLock, "hybridclr", stagedRuntime, workflowId,
            Git(hybridclr, "rev-parse", "HEAD"));
        var editor = cli.Optional("editorexecutable") ?? StringProperty(engine, "executablePath"); var editorAvailable = File.Exists(editor); var external = ResolveExternalHeaders(editor, cli.Optional("externalheadersroot"), engine);
        if (!Directory.Exists(external)) throw new DirectoryNotFoundException("IL2CPP external headers: " + external);
        if (!editorAvailable && !cli.Has("allowsurrogateexternalheaders")) throw new InvalidOperationException("Engine editor is unavailable; pass -AllowSurrogateExternalHeaders only for exploratory native validation.");
        CopyDirectory(external, stagedExternal);
        if (dhe && (!File.Exists(Path.Combine(stagedRuntime, "hybridclr/DheRuntime.cpp")) || !File.Exists(Path.Combine(stagedRuntime, "hybridclr/DheRuntime.h")))) throw new InvalidOperationException("Staged DHE runtime sources are missing.");
        var instrumented = profile is "Baseline-Instrumented" or "Metadata-Instrumented"; var fgs = profile.Contains("Fgs", StringComparison.OrdinalIgnoreCase) || profile.Contains("Compatibility", StringComparison.OrdinalIgnoreCase);
        if (instrumented || fgs) WriteText(Path.Combine(stagedRuntime, "hybridclr/lab/InstrumentationConfig.h"), "#pragma once\n" + (instrumented ? "#define HYBRIDCLR_LAB_INSTRUMENTED 1\n" : "") + (fgs ? "#define HYBRIDCLR_LAB_FGS_TESTS 1\n" : ""));
        var appliedRuntimePatches = SelectLockedOverlays(runtimeLock, "il2cpp_plus", workflowId)
            .Concat(SelectLockedOverlays(runtimeLock, "hybridclr", workflowId)).ToArray();
        var manifest = new { schemaVersion = 1, format = "hybridclr.dhe-runtime-manifest.json", profile, dheEnabled = dhe, pathSemantics = "workspace-absolute-v1", createdAtUtc = DateTimeOffset.UtcNow, engineWorkflow = workflowId, engine, fullGenericSharingDiagnostics = fgs, externalHeaders = new { sourcePath = external, stagedPath = stagedExternal, stagedTreeSha256 = TreeHash(stagedExternal), surrogate = !editorAvailable, editorAvailable, explicitlyAllowed = !editorAvailable && cli.Has("allowsurrogateexternalheaders") }, source = new { hybridclr = SourceRecord(lockDoc, "hybridclr", hybridclr, Path.Combine(hybridclr, "hybridclr")), il2cpp_plus = SourceRecord(lockDoc, "il2cpp_plus", il2cpp, Path.Combine(il2cpp, "libil2cpp")), hybridclr_unity = SourceRecord(lockDoc, "hybridclr_unity", hybridclrUnity, hybridclrUnity) }, stagedLibil2cpp = stagedRuntime, stagedRuntimeSha256 = TreeHash(stagedRuntime), dheRuntimeLock = runtimeLockPath, dheRuntimeLockSha256 = Sha256File(runtimeLockPath), dheRuntimeSourceMode = StringProperty(runtimeLock, "sourceMode"), dhePatches = appliedRuntimePatches };
        WriteJson(Path.Combine(stage, "runtime-manifest.json"), manifest); Console.WriteLine("Assembled " + profile + " runtime: " + stagedRuntime); return 0;
    }

    private static void ApplyLockedOverlays(string lab, JsonElement runtimeLock,
        string repository, string destination, string engineWorkflow, string sourceCommit)
    {
        if (!string.Equals(StringProperty(runtimeLock, "sourceMode"), "overlay",
                StringComparison.Ordinal)) return;
        JsonElement[] patches = SelectLockedOverlays(runtimeLock, repository, engineWorkflow);
        if (patches.Length == 0)
            throw new InvalidOperationException("DHE overlay lock has no patch for " + repository + ".");
        foreach (JsonElement patch in patches)
        {
            if (!string.Equals(StringProperty(patch, "baseCommit"), sourceCommit,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DHE overlay base commit does not match " +
                    repository + ": " + StringProperty(patch, "baseCommit") + " != " +
                    sourceCommit + ".");
            if (!string.Equals(StringProperty(patch, "sourceMode"), "overlay",
                    StringComparison.Ordinal))
                throw new InvalidOperationException("DHE overlay patch sourceMode is invalid for " +
                    repository + ".");
            string patchPath = ResolvePath(lab, StringProperty(patch, "path"));
            if (!File.Exists(patchPath) || !string.Equals(Sha256File(patchPath),
                    StringProperty(patch, "sha256"), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("DHE overlay patch hash mismatch: " + patchPath);
            string applyRoot = StringProperty(patch, "applyRoot");
            string expectedRoot = repository == "hybridclr_unity" ? "package" : "libil2cpp";
            if (!string.Equals(applyRoot, expectedRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("DHE overlay applyRoot is invalid for " + repository + ".");
            int stripComponents = patch.GetProperty("stripComponents").GetInt32();
            string strip = "-p" + stripComponents.ToString(System.Globalization.CultureInfo.InvariantCulture);
            string directory = "--directory=" + Path.GetFullPath(destination)
                .Replace(Path.DirectorySeparatorChar, '/');
            RunProcess("git", new[] { "apply", "--check", "--unsafe-paths", strip, directory,
                patchPath }, lab);
            RunProcess("git", new[] { "apply", "--unsafe-paths", strip, directory, patchPath },
                lab);
            Console.WriteLine("Applied DHE overlay " + StringProperty(patch, "id") + " to " +
                destination);
        }
    }

    private static JsonElement[] SelectLockedOverlays(JsonElement runtimeLock, string repository,
        string engineWorkflow)
    {
        return runtimeLock.GetProperty("patches").EnumerateArray().Where(item =>
        {
            if (!string.Equals(StringProperty(item, "repository"), repository,
                    StringComparison.Ordinal)) return false;
            if (!item.TryGetProperty("engineWorkflows", out JsonElement workflows) ||
                workflows.ValueKind != JsonValueKind.Array) return true;
            return workflows.EnumerateArray().Any(value => value.ValueKind == JsonValueKind.String &&
                string.Equals(value.GetString(), engineWorkflow, StringComparison.Ordinal));
        }).ToArray();
    }

    private static object SourceRecord(JsonElement lockDoc, string name, string path, string tree)
    {
        var spec = lockDoc.GetProperty("repositories").GetProperty(name);
        return new { url = StringProperty(spec, "fork"), path, commit = Git(path, "rev-parse", "HEAD"), dirty = !string.IsNullOrWhiteSpace(Git(path, "status", "--porcelain")), treeSha256 = TreeHash(tree, name == "hybridclr_unity") };
    }

    private static string ResolveExternalHeaders(string editor, string? explicitRoot, JsonElement engine)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot);
        if (!string.IsNullOrWhiteSpace(editor))
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(editor))!;
            foreach (var candidate in new[] { Path.Combine(dir, "Data/il2cpp/external"), Path.GetFullPath(Path.Combine(dir, "../il2cpp/external")), Path.GetFullPath(Path.Combine(dir, "../Data/il2cpp/external")) }) if (Directory.Exists(candidate)) return candidate;
        }
        return engine.TryGetProperty("nativeTestExternalPath", out var fallback) ? Path.GetFullPath(fallback.GetString() ?? "") : "";
    }

    private static void ValidateRepoIdentity(string name, string path, string expected, bool allowDirty)
    {
        var actual = Git(path, "rev-parse", "HEAD"); if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"{name} is at {actual}, expected {expected}.");
        if (!allowDirty && !string.IsNullOrWhiteSpace(Git(path, "status", "--porcelain"))) throw new InvalidOperationException(name + " is dirty; pass -AllowDirty for exploratory work.");
    }

    private static string ResolvePath(string root, string path) => Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
    private static string LabRoot(Cli cli) => Path.GetFullPath(cli.Optional("labroot") ?? Directory.GetCurrentDirectory());

    private static string ResolveReposRoot(string lab, JsonElement lockDoc, string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return Path.GetFullPath(Path.IsPathRooted(requested) ? requested : Path.Combine(lab, requested));
        var candidates = new[] { Path.GetFullPath(Path.Combine(lab, "../repos")), Path.GetFullPath(Path.Combine(lab, "../../repos")) }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var matches = candidates.Where(candidate => Directory.Exists(candidate) && new[] { "hybridclr_unity", "hybridclr", "il2cpp_plus" }.All(name =>
        {
            var path = Path.Combine(candidate, name);
            if (!Directory.Exists(path)) return false;
            var expected = lockDoc.GetProperty("repositories").GetProperty(name).GetProperty("commit").GetString();
            return string.Equals(Git(path, "rev-parse", "HEAD"), expected, StringComparison.OrdinalIgnoreCase);
        })).ToArray();
        if (matches.Length == 1) return matches[0];
        if (matches.Length > 1) throw new InvalidOperationException("Multiple repository roots match repo-lock.json; pass -ReposRoot explicitly.");
        return candidates[0];
    }
    private static JsonElement ReadJson(string path) => JsonDocument.Parse(File.ReadAllText(path)).RootElement.Clone();
    private static string StringProperty(JsonElement element, string name) => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static int PositiveInt(JsonElement element, string name) { if (!element.TryGetProperty(name, out var value) || !value.TryGetInt32(out var result) || result < 1) throw new InvalidOperationException(name + " must be at least 1."); return result; }
    private static string RequireDirectory(string path) { var full = Path.GetFullPath(path); if (!Directory.Exists(full)) throw new DirectoryNotFoundException(full); return full; }
    private static void CopyRequired(string source, string destination) { if (!File.Exists(source)) throw new FileNotFoundException(source); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); }
    private static void CopyDirectoryOrFile(string source, string destination) { if (File.Exists(source)) CopyRequired(source, destination); else CopyDirectory(source, destination); }
    private static void CopyDirectory(string source, string destination, IEnumerable<string>? ignored = null) { var skip = new HashSet<string>(ignored ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase); if (!Directory.Exists(source)) throw new DirectoryNotFoundException(source); Directory.CreateDirectory(destination); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var rel = Path.GetRelativePath(source, file); if (skip.Contains(rel.Split(Path.DirectorySeparatorChar)[0])) continue; var target = Path.Combine(destination, rel); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); } }
    private static void SafeDelete(string path, string parent) { var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var basePath = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); if (!full.StartsWith(basePath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing to delete outside output root: " + full); if (Directory.Exists(full)) Directory.Delete(full, true); }
    private static void WriteJson(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false)); }
    private static void WriteText(string path, string value) { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!); File.WriteAllText(path, value, new UTF8Encoding(false)); }
    private static string Sha256File(string path) { using var sha = SHA256.Create(); using var stream = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant(); }
    private static string TreeHash(string root, bool excludeGit = false,
        IEnumerable<string>? ignoredPaths = null)
    {
        root = RequireDirectory(root);
        var ignored = new HashSet<string>((ignoredPaths ?? Array.Empty<string>())
            .Select(path => path.Replace('\\', '/').TrimStart('/')), StringComparer.OrdinalIgnoreCase);
        using var sha = SHA256.Create();
        var buffer = new byte[1024 * 1024];
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                     .Where(path =>
                     {
                         var relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                         return !ignored.Contains(relative) && (!excludeGit ||
                             !relative.Split('/')[0].Equals(".git", StringComparison.OrdinalIgnoreCase));
                     })
                     .OrderBy(x => Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal))
        {
            var rel = Path.GetRelativePath(root, file).Replace(Path.DirectorySeparatorChar, '/');
            var name = Encoding.UTF8.GetBytes(rel + "\n");
            sha.TransformBlock(name, 0, name.Length, name, 0);
            using var input = File.OpenRead(file);
            int read;
            while ((read = input.Read(buffer, 0, buffer.Length)) > 0) sha.TransformBlock(buffer, 0, read, buffer, 0);
            var sep = Encoding.UTF8.GetBytes("\n");
            sha.TransformBlock(sep, 0, sep.Length, sep, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToUpperInvariant();
    }
    private static string Git(string cwd, params string[] args) { var result = RunProcess("git", new[] { "-C", cwd }.Concat(args).ToArray(), cwd, false, false); return result.stdout.Trim(); }
    private static (int exitCode, string stdout, string stderr) RunProcess(string file, IEnumerable<string> args, string cwd, bool throwOnError = true, bool writeOutput = true)
    {
        var start = new ProcessStartInfo(file) { WorkingDirectory = cwd, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Unable to start " + file);
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync(); process.WaitForExit(); Task.WaitAll(stdout, stderr);
        var result = (process.ExitCode, stdout.Result, stderr.Result); if (throwOnError && result.Item1 != 0) throw new InvalidOperationException($"{file} exited with {result.Item1}: {result.Item3.Trim()}");
        if (writeOutput && !string.IsNullOrWhiteSpace(result.Item2)) Console.Write(result.Item2); if (writeOutput && !string.IsNullOrWhiteSpace(result.Item3)) Console.Error.Write(result.Item3); return result;
    }
    private static bool FindOnPath(string command) { try { return RunProcess(command, new[] { "--version" }, Directory.GetCurrentDirectory(), false, false).Item1 == 0; } catch { return false; } }
    private static bool CanResolveExecutable(string command) { try { _ = ResolveExecutable(command); return true; } catch { return false; } }
    private static string ResolveExecutable(string command, params string[] extraCandidates)
    {
        if (Path.IsPathRooted(command) && File.Exists(command)) return Path.GetFullPath(command);
        var candidates = new List<string>();
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            candidates.Add(Path.Combine(directory, command + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)));
        candidates.AddRange(extraCandidates);
        if (OperatingSystem.IsWindows())
        {
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            candidates.Add(Path.Combine(programFilesX86, "Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin", command + ".exe"));
            candidates.Add(Path.Combine(programFiles, "Microsoft Visual Studio/2022/BuildTools/Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin", command + ".exe"));
        }
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("Executable was not found on PATH or in known tool locations: " + command);
    }
    private static bool ProcessMentionsProject(Process process, string project)
    {
        try
        {
            if (File.Exists(Path.Combine(project, "Temp", "UnityLockfile"))) return !process.HasExited;
            return process.StartInfo.Arguments.Contains(project, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
