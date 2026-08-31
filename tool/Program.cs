using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace HybridCLR.DheTool;

internal static class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Main(string[] args)
    {
        try
        {
            var cli = Cli.Parse(args);
            if (cli.Command is "help" or "") { PrintHelp(); return 0; }
            return cli.Command.ToLowerInvariant() switch
            {
                "version" => Version(cli),
                "mv" => MetaVersion(cli),
                "batch" => Batch(cli),
                "baseline-manifest" => BaselineManifest(cli),
                "aot-metadata-manifest" => AotMetadataManifest(cli),
                "preflight" => Preflight(cli),
                "workflow" => Workflow(cli),
                "validate" => Validate(cli),
                "archive" => Archive(cli),
                "doctor" => Doctor(cli),
                "verify-package" => VerifyPackage(cli),
                "publish" => Publish(cli),
                "install" => Install(cli),
                "new-adapter" => NewAdapter(cli),
                "new-config" => NewConfig(cli),
                "assemble-runtime" or "native-tests" or "build-managed-cases" or
                "generate-test-manifest" or "generate-metadata-stress-source" or
                "reference" or "compare-results" or "check-environment" or
                "clear-unity-project-locks" or "wait-editor" or
                "prepare-engine-test-project" or "bootstrap-repos" or
                "tree-hash" or "file-hash" => LabCommands.Run(cli),
                _ => throw new DheException($"Unknown DHE command '{cli.Command}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DHE failed: " + ex.Message);
            return 1;
        }
    }

    private static int Version(Cli cli)
    {
        var manifest = ReadJson<Dictionary<string, JsonElement>>(Path.Combine(cli.Root, "dhe-toolchain-manifest.json"));
        Console.WriteLine($"HybridCLR DHE toolchain {GetString(manifest, "toolchainVersion")} (contract {GetInt(manifest, "contractVersion")}, packageId={GetString(manifest, "packageId")})");
        return 0;
    }

    private static int BaselineManifest(Cli cli)
    {
        var root = RequireDirectory(cli.Require("baselineroot"), "Baseline root");
        var runtimePath = RequireFile(cli.Require("runtimemanifestpath"), "Runtime manifest");
        var settingsPath = RequireFile(cli.Require("settingsfile"), "HybridCLR settings");
        var output = SafeReportPath(cli.Require("output"), new[] { root, runtimePath, settingsPath });
        var runtime = ReadJson<JsonElement>(runtimePath);
        var sets = Settings.Read(settingsPath);
        var records = sets.Dhe.Select(name => new AssemblyRecord(name, Sha256File(RequireFile(Path.Combine(root, name + ".dll"), name + " baseline assembly")))).ToArray();
        var packageLock = cli.Optional("packagelockpath");
        var doc = new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1, ["format"] = "hybridclr.dhe-baseline-manifest.json", ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["pathSemantics"] = "workspace-absolute-v1", ["baselineKind"] = "stripped-aot", ["target"] = cli.Require("target"),
            ["sourceRoot"] = root, ["engineWorkflow"] = GetString(runtime, "engineWorkflow"), ["engine"] = runtime.TryGetProperty("engine", out var engine) ? engine : null,
            ["runtime"] = new { profile = GetString(runtime, "profile"), stagedRuntimeSha256 = GetString(runtime, "stagedRuntimeSha256"), runtimeManifestSha256 = Sha256File(runtimePath) },
            ["package"] = string.IsNullOrWhiteSpace(packageLock) ? null : ReadJson<JsonElement>(RequireFile(packageLock, "Package lock")), ["assemblies"] = records
        };
        WriteJson(output, doc);
        Console.WriteLine("DHE baseline manifest: " + output);
        return 0;
    }

    private static int AotMetadataManifest(Cli cli)
    {
        var root = RequireDirectory(cli.Require("root"), "AOT metadata root");
        var runtimePath = RequireFile(cli.Require("runtimemanifestpath"), "Runtime manifest");
        var settingsPath = RequireFile(cli.Require("settingsfile"), "HybridCLR settings");
        var output = SafeReportPath(cli.Require("output"), new[] { root, runtimePath, settingsPath });
        var runtime = ReadJson<JsonElement>(runtimePath); var sets = Settings.Read(settingsPath);
        var records = sets.Patch.Select(name => new AssemblyRecord(name, Sha256File(RequireFile(Path.Combine(root, name + ".dll"), name + " AOT metadata assembly")))).ToArray();
        var packageLock = cli.Optional("packagelockpath");
        WriteJson(output, new Dictionary<string, object?>
        {
            ["schemaVersion"] = 1, ["format"] = "hybridclr.dhe-aot-metadata-manifest.json", ["generatedAtUtc"] = DateTimeOffset.UtcNow,
            ["pathSemantics"] = "workspace-absolute-v1", ["kind"] = "patch-aot-metadata", ["releaseId"] = cli.Optional("releaseid"),
            ["target"] = cli.Require("target"), ["sourceRoot"] = root, ["engineWorkflow"] = GetString(runtime, "engineWorkflow"),
            ["engine"] = runtime.TryGetProperty("engine", out var engine) ? engine : null,
            ["runtime"] = new { profile = GetString(runtime, "profile"), stagedRuntimeSha256 = GetString(runtime, "stagedRuntimeSha256"), runtimeManifestSha256 = Sha256File(runtimePath), packageTreeSha256 = packageLock == null ? null : GetString(ReadJson<JsonElement>(RequireFile(packageLock, "Package lock")), "treeSha256") },
            ["package"] = string.IsNullOrWhiteSpace(packageLock) ? null : ReadJson<JsonElement>(RequireFile(packageLock, "Package lock")), ["assemblies"] = records
        });
        Console.WriteLine("DHE AOT metadata manifest: " + output); return 0;
    }

    private static int MetaVersion(Cli cli)
    {
        var baseline = RequireFile(cli.Require("baselineassembly"), "Baseline assembly");
        var current = RequireFile(cli.Require("currentassembly"), "Current assembly");
        var output = SafeReportPath(cli.Require("output"), new[] { baseline, current });
        var binary = cli.Optional("binaryoutput");
        if (!string.IsNullOrWhiteSpace(binary)) binary = SafeReportPath(binary, new[] { baseline, current, output });
        var strict = cli.Has("strictcompatibility");
        var diff = AssemblyDiff.Create(baseline, current);
        var result = diff.ToJson(strict);
        WriteJson(output, result);
        if (!string.IsNullOrWhiteSpace(binary))
        {
            if (!strict || !diff.Compatible) throw new DheException("Binary MV output requires a compatible strict diff.");
            WriteMvBinary(binary, diff);
        }
        Console.WriteLine($"DHE MV {diff.AssemblyName}: {diff.ChangedMethodCount} changed method(s), status={(diff.Compatible ? "compatible" : "incompatible")}");
        return diff.Compatible ? 0 : (strict ? 2 : 0);
    }

    private static int Batch(Cli cli)
    {
        var baselineRoot = RequireDirectory(cli.Require("baselineroot"), "Baseline root");
        var currentRoot = RequireDirectory(cli.Require("currentroot"), "Current root");
        var outputRoot = SafeOutputRoot(cli.Require("outputroot"), new[] { baselineRoot, currentRoot });
        Directory.CreateDirectory(outputRoot);
        var settingsPath = cli.Optional("settingsfile");
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(settingsPath)) names.AddRange(Settings.Read(RequireFile(settingsPath, "HybridCLR settings")).Dhe);
        if (cli.Has("assemblynames")) names.AddRange(cli.GetList("assemblynames"));
        var listFile = cli.Optional("assemblylistfile");
        if (!string.IsNullOrWhiteSpace(listFile)) names.AddRange(File.ReadAllLines(RequireFile(listFile, "Assembly list")).Where(x => !string.IsNullOrWhiteSpace(x) && !x.TrimStart().StartsWith('#')));
        names = names.Select(NormalizeName).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (names.Count == 0) throw new DheException("No DHE assemblies were supplied.");
        var sets = !string.IsNullOrWhiteSpace(settingsPath) ? Settings.Read(settingsPath) : new Settings.Sets();
        var records = new List<BatchRecord>();
        foreach (var name in names)
        {
            var b = Path.Combine(baselineRoot, name + ".dll"); var c = Path.Combine(currentRoot, name + ".dll");
            var jsonPath = Path.Combine(outputRoot, name + ".mv.json"); var binaryPath = Path.Combine(outputRoot, name + ".mv.bytes");
            if (!File.Exists(b) || !File.Exists(c)) { records.Add(new(name, b, c, jsonPath, null, "missing", "Missing baseline or current assembly file.")); continue; }
            try
            {
                var diff = AssemblyDiff.Create(b, c); var compatible = diff.Compatible;
                WriteJson(jsonPath, diff.ToJson(true)); string? binary = null;
                if (compatible) { WriteMvBinary(binaryPath, diff); binary = binaryPath; }
                records.Add(new(name, b, c, jsonPath, binary, compatible ? "compatible" : "incompatible", compatible ? null : string.Join("; ", diff.Reasons)));
            }
            catch (Exception ex) { records.Add(new(name, b, c, jsonPath, null, "error", ex.Message)); }
        }
        var configErrors = new List<string>();
        var requireCoverage = cli.Has("requiredheequalshotupdate");
        if (requireCoverage && !SetEquals(sets.Hot, sets.Dhe)) configErrors.Add("dheAotAssemblies must exactly match hotUpdateAssemblies.");
        var summary = new { schemaVersion = 1, format = "hybridclr.dhe-lite.batch-report.json", generatedAtUtc = DateTimeOffset.UtcNow,
            baselineRoot, currentRoot, strictCompatibility = true, requireDheEqualsHotUpdate = requireCoverage,
            configurationPassed = configErrors.Count == 0, configurationErrors = configErrors, hotUpdateAssemblies = sets.Hot, dheAotAssemblies = sets.Dhe,
            assemblies = records, counts = new { total = records.Count, compatible = records.Count(x => x.Status == "compatible"), incompatible = records.Count(x => x.Status == "incompatible"), missing = records.Count(x => x.Status == "missing"), error = records.Count(x => x.Status == "error") } };
        WriteJson(Path.Combine(outputRoot, "dhe-batch-summary.json"), summary);
        Console.WriteLine($"DHE batch: {summary.counts.compatible}/{summary.counts.total} compatible");
        return configErrors.Count == 0 && summary.counts.incompatible == 0 && summary.counts.missing == 0 && summary.counts.error == 0 ? 0 : 2;
    }

    private static int Preflight(Cli cli)
    {
        var settings = RequireFile(cli.Require("settingsfile"), "HybridCLR settings");
        var baseline = RequireDirectory(cli.Require("baselineroot"), "Baseline root");
        var current = RequireDirectory(cli.Require("currentroot"), "Current root");
        var output = SafeOutputRoot(cli.Require("outputroot"), new[] { settings, baseline, current });
        Directory.CreateDirectory(output);
        var batchArgs = new Cli("batch", new Dictionary<string, string>(cli.Values, StringComparer.OrdinalIgnoreCase));
        batchArgs.Values["outputroot"] = Path.Combine(output, "batch"); batchArgs.Values["strictcompatibility"] = "true";
        var batchCode = Batch(batchArgs);
        var batchPath = Path.Combine(output, "batch", "dhe-batch-summary.json");
        var batch = ReadJson<JsonElement>(batchPath);
        var assemblies = new List<object>();
        foreach (var record in batch.GetProperty("assemblies").EnumerateArray())
        {
            var status = record.GetProperty("status").GetString() ?? "error"; var mv = status == "compatible" ? ReadJson<JsonElement>(record.GetProperty("report").GetString()!) : (JsonElement?)null;
            assemblies.Add(new { assemblyName = record.GetProperty("assemblyName").GetString(), batchStatus = status, validationPassed = status == "compatible", validationReport = (string?)null, error = record.TryGetProperty("error", out var error) ? error.GetString() : null });
        }
        var names = batch.GetProperty("assemblies").EnumerateArray().Select(x => x.GetProperty("assemblyName").GetString()!).ToArray();
        var planRecords = batch.GetProperty("assemblies").EnumerateArray().Select(record =>
        {
            var status = record.GetProperty("status").GetString() ?? "error"; JsonElement? mv = null;
            if (status == "compatible") mv = ReadJson<JsonElement>(record.GetProperty("report").GetString()!);
            return new { assemblyName = record.GetProperty("assemblyName").GetString(), status, baseline = record.GetProperty("baseline").GetString(), current = record.GetProperty("current").GetString(), mvJson = status == "compatible" ? record.GetProperty("report").GetString() : null, mvBytes = status == "compatible" ? record.GetProperty("binary").GetString() : null, changedMethodCount = mv?.GetProperty("summary").GetProperty("changedMethodCount").GetInt32() ?? 0, compatibility = mv?.GetProperty("compatibility").GetProperty("status").GetString() };
        }).ToArray();
        var planPath = Path.Combine(output, "dhe-project-plan.json");
        WriteJson(planPath, new { schemaVersion = 1, format = "hybridclr.dhe-project-plan.json", generatedAtUtc = DateTimeOffset.UtcNow, complete = batch.GetProperty("counts").GetProperty("error").GetInt32() == 0 && batch.GetProperty("counts").GetProperty("missing").GetInt32() == 0 && batch.GetProperty("counts").GetProperty("incompatible").GetInt32() == 0, requireDheEqualsHotUpdate = cli.Has("requiredheequalshotupdate"), hotUpdateAssemblies = Settings.Read(settings).Hot, dheAotAssemblies = names, dheEqualsHotUpdate = SetEquals(Settings.Read(settings).Hot, names), settingsFile = settings, baselineRoot = baseline, currentRoot = current, batchReport = batchPath, assemblies = planRecords });
        var planValidationPath = Path.Combine(output, "project-plan-validation.json");
        var planComplete = batch.GetProperty("counts").GetProperty("error").GetInt32() == 0 && batch.GetProperty("counts").GetProperty("missing").GetInt32() == 0 && batch.GetProperty("counts").GetProperty("incompatible").GetInt32() == 0;
        WriteJson(planValidationPath, new { schemaVersion = 1, format = "hybridclr.dhe-project-plan-validation.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = planComplete, complete = planComplete, plan = planPath, errors = planComplete ? Array.Empty<string>() : new[] { "DHE project plan contains missing, incompatible, or error assemblies." } });
        var reportPath = Path.Combine(output, "project-preflight-report.json");
        var passed = batchCode == 0;
        var dnlibPath = cli.Optional("dnlibpath") ?? Path.Combine(AppContext.BaseDirectory, "dnlib.dll");
        WriteJson(reportPath, new { schemaVersion = 1, format = "hybridclr.dhe-project-preflight.json", generatedAtUtc = DateTimeOffset.UtcNow, passed, generationPassed = passed, validationPassed = passed && planComplete, coverageRequired = cli.Has("requirecompletecoverage"), dheCoverageRequired = cli.Has("requiredheequalshotupdate"), configurationPassed = batch.GetProperty("configurationPassed").GetBoolean(), configurationErrors = batch.GetProperty("configurationErrors"), hotUpdateAssemblies = Settings.Read(settings).Hot, dheAotAssemblies = names, dheEqualsHotUpdate = SetEquals(Settings.Read(settings).Hot, names), coverageComplete = passed && planComplete, artifactReady = passed && planComplete && cli.Has("requirecompletecoverage"), releaseReady = false, sourcePreflight = (string?)null, sourcePreflightPassed = true, settingsFile = settings, projectRoot = cli.Optional("projectroot"), baselineRoot = baseline, currentRoot = current, batchReport = batchPath, projectPlan = planPath, projectPlanValidation = planValidationPath, batchExitCode = batchCode, counts = batch.GetProperty("counts"), assemblies, nativeAbiCoverage = "not-evaluated-by-offline-preflight", dnlibPath });
        Console.WriteLine("DHE project preflight: " + reportPath);
        return passed ? 0 : 1;
    }

    private static int Workflow(Cli cli)
    {
        ApplyWorkflowConfig(cli);
        var project = RequireDirectory(cli.Require("projectpath"), "DHE project");
        var output = SafeOutputRoot(cli.Require("outputroot"), new[] { project }); Directory.CreateDirectory(output);
        var target = cli.Require("target"); var settings = RequireFile(cli.Require("settingsfile"), "HybridCLR settings");
        var baseline = cli.Optional("baselineaotroot"); if (string.IsNullOrWhiteSpace(baseline)) throw new DheException("Release workflow requires baselineroot.");
        baseline = RequireDirectory(baseline, "Baseline AOT root");
        var current = Path.Combine(output, "current"); var baselineCopy = Path.Combine(output, "baseline");
        var unity = ResolveUnity(cli, project); var adapterClass = cli.Require("adaptermethod");
        var mode = cli.Optional("mode") ?? "Release";
        var unityTimeout = int.TryParse(cli.Optional("unitytimeoutseconds"), out var timeoutValue) ? Math.Clamp(timeoutValue, 10, 3600) : 600;
        var prepareArgs = new List<string> { "-batchmode", "-nographics", "-quit", "-projectPath", project, "-executeMethod", adapterClass, "-dheTarget", target, "-dheOutputRoot", output, "-dheBaselineRoot", baselineCopy, "-dheCurrentRoot", current, "-dheMode", mode, "-logFile", Path.Combine(output, "unity-prepare.log") };
        AppendUnityArguments(prepareArgs, cli);
        RunUnity(unity, project, prepareArgs, new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-prepare-process.log"), unityTimeout);
        var preflight = new Cli("preflight", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["settingsfile"] = settings, ["baselineroot"] = baselineCopy, ["currentroot"] = current, ["outputroot"] = Path.Combine(output, "project-preflight"), ["projectroot"] = project, ["requiredheequalshotupdate"] = "true", ["requirecompletecoverage"] = "true", ["dnlibpath"] = cli.Optional("dnlibpath") ?? "" });
        if (Preflight(preflight) != 0) throw new DheException("DHE project preflight failed.");
        if (cli.Has("stopafterpreflight"))
        {
            Console.WriteLine("DHE workflow preflight passed; stopping before Player stages.");
            return 0;
        }
        if (cli.Has("runplayer"))
        {
            var adapterType = adapterClass.EndsWith(".Prepare", StringComparison.Ordinal) ? adapterClass[..^".Prepare".Length] : adapterClass;
            var common = new List<string> { "-batchmode", "-nographics", "-quit", "-projectPath", project, "-dheTarget", target, "-dheOutputRoot", output, "-dheBaselineRoot", baselineCopy, "-dheCurrentRoot", current, "-dheMode", mode, "-dheProjectPlan", Path.Combine(output, "project-preflight", "dhe-project-plan.json") };
            AppendUnityArguments(common, cli);
            RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".StageRuntimePlan").Append("-logFile").Append(Path.Combine(output, "unity-stage.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-stage-process.log"), unityTimeout);
            RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".BuildDheYooAsset").Append("-logFile").Append(Path.Combine(output, "unity-yooasset.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-yooasset-process.log"), unityTimeout);
            ValidateResourceEvidence(Path.Combine(output, "adapter", "resource-evidence.json"), target);
            RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".BuildScriptsOnly").Append("-logFile").Append(Path.Combine(output, "unity-scripts.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-scripts-process.log"), unityTimeout);
            RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".BuildFinalPlayer").Append("-logFile").Append(Path.Combine(output, "unity-player.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-player-process.log"), unityTimeout);
            WriteJson(Path.Combine(output, "workflow-report.json"), new { schemaVersion = 1, format = "hybridclr.dhe-project-workflow.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = true, mode, target, projectPath = project, playerExecuted = true, preflight = Path.Combine(output, "project-preflight", "project-preflight-report.json") });
            Console.WriteLine("DHE workflow Player stages passed.");
            return 0;
        }
        Console.WriteLine("DHE workflow preflight passed. Player stage is delegated to the project C# adapter; pass -RunPlayer to execute it.");
        return 0;
    }

    private static int Validate(Cli cli)
    {
        var input = RequireFile(cli.Require("mvjson"), "MV JSON");
        var errors = new List<string>();
        JsonElement doc = default;
        try
        {
            doc = ReadJson<JsonElement>(input);
            if (!doc.TryGetProperty("format", out var format) || format.GetString() != "hybridclr.dhe-lite.mv.json")
                errors.Add("Invalid MV format.");
            if (!doc.TryGetProperty("assemblyName", out var assemblyName) || string.IsNullOrWhiteSpace(assemblyName.GetString()))
                errors.Add("MV assemblyName is missing.");
            if (doc.TryGetProperty("compatibility", out var compatibility) &&
                compatibility.TryGetProperty("status", out var status) && status.GetString() != "compatible")
                errors.Add("MV is not compatible.");
        }
        catch (Exception ex) { errors.Add(ex.Message); }

        var baseline = cli.Optional("baselineassembly");
        var current = cli.Optional("currentassembly");
        if (!string.IsNullOrWhiteSpace(baseline) || !string.IsNullOrWhiteSpace(current))
        {
            if (string.IsNullOrWhiteSpace(baseline) || string.IsNullOrWhiteSpace(current))
                errors.Add("BaselineAssembly and CurrentAssembly must be supplied together.");
            else
            {
                try
                {
                    var diff = AssemblyDiff.Create(RequireFile(baseline!, "Baseline assembly"), RequireFile(current!, "Current assembly"));
                    var expectedName = doc.TryGetProperty("assemblyName", out var name) ? name.GetString() : null;
                    if (!string.Equals(expectedName, diff.AssemblyName, StringComparison.Ordinal)) errors.Add("MV assemblyName does not match the current assembly.");
                    if (doc.TryGetProperty("baseline", out var baseDoc) && baseDoc.TryGetProperty("sha256", out var baseHash) && !string.Equals(baseHash.GetString(), diff.BaselineSha256, StringComparison.OrdinalIgnoreCase)) errors.Add("MV baseline SHA-256 does not match the baseline assembly.");
                    if (doc.TryGetProperty("current", out var currentDoc) && currentDoc.TryGetProperty("sha256", out var currentHash) && !string.Equals(currentHash.GetString(), diff.CurrentSha256, StringComparison.OrdinalIgnoreCase)) errors.Add("MV current SHA-256 does not match the current assembly.");
                }
                catch (Exception ex) { errors.Add(ex.Message); }
            }
        }

        var binary = cli.Optional("mvbytes") ?? cli.Optional("binaryoutput");
        if (!string.IsNullOrWhiteSpace(binary))
        {
            try
            {
                var bytes = File.ReadAllBytes(RequireFile(binary!, "MV binary"));
                if (bytes.Length < 8 || Encoding.ASCII.GetString(bytes, 0, 8) != "DHEMVLT1") errors.Add("Invalid MV binary header.");
            }
            catch (Exception ex) { errors.Add(ex.Message); }
        }

        var output = cli.Optional("output");
        if (!string.IsNullOrWhiteSpace(output))
            WriteJson(SafeReportPath(output, new[] { input, baseline ?? "", current ?? "", binary ?? "" }), new { schemaVersion = 1, format = "hybridclr.dhe-artifact-validation.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = errors.Count == 0, errors, warnings = Array.Empty<string>(), mvJson = input, mvBytes = binary, baselineAssembly = baseline, currentAssembly = current });
        if (errors.Count > 0) { Console.Error.WriteLine(string.Join(Environment.NewLine, errors)); return 1; }
        Console.WriteLine("DHE artifact validation passed: " + input);
        return 0;
    }

    private static int Archive(Cli cli)
    {
        var input = RequireDirectory(cli.Require("inputroot"), "Workflow output"); var archive = SafeOutputRoot(cli.Require("archiveroot"), new[] { input });
        if (Directory.Exists(archive)) Directory.Delete(archive, true); CopyDirectory(input, archive);
        var output = cli.Optional("output"); if (!string.IsNullOrWhiteSpace(output)) WriteJson(SafeReportPath(output, new[] { input }), new { schemaVersion = 1, format = "hybridclr.dhe-archive-gate.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = true, inputRoot = input, archiveRoot = archive });
        Console.WriteLine("DHE archive: " + archive); return 0;
    }

    private static int Doctor(Cli cli)
    {
        var root = RequireDirectory(cli.Root, "DHE tool root");
        var manifestPath = Path.Combine(root, "dhe-toolchain-manifest.json");
        var errors = new List<string>();
        JsonElement manifest = default;
        var manifestPresent = File.Exists(manifestPath);
        if (!manifestPresent) errors.Add("DHE toolchain manifest is missing.");
        else
        {
            try { manifest = ReadJson<JsonElement>(manifestPath); }
            catch (Exception ex) { errors.Add(ex.Message); }
        }

        var packageGatePassed = manifestPresent && manifest.ValueKind == JsonValueKind.Object;
        if (packageGatePassed)
        {
            foreach (var item in manifest.GetProperty("files").EnumerateArray())
            {
                var relative = item.GetProperty("path").GetString() ?? "";
                var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) { errors.Add("Missing package file: " + relative); packageGatePassed = false; continue; }
                if (!Sha256File(path).Equals(item.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase)) { errors.Add("Package hash mismatch: " + relative); packageGatePassed = false; }
            }
            if (Directory.GetFiles(root, "*.ps1", SearchOption.AllDirectories).Length > 0) { errors.Add("Package contains PowerShell files; use the C# host only."); packageGatePassed = false; }
        }

        var expected = cli.Optional("expectedpackageid");
        var packageId = manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty("packageId", out var package) ? package.GetString() : null;
        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(expected, packageId, StringComparison.OrdinalIgnoreCase)) { errors.Add("Package ID does not match ExpectedPackageId."); packageGatePassed = false; }
        var requireRelease = cli.Has("requirerelease");
        if (requireRelease && (manifest.ValueKind != JsonValueKind.Object || !manifest.TryGetProperty("releaseReady", out var release) || !release.GetBoolean())) { errors.Add("A release-ready package is required."); packageGatePassed = false; }

        var projectPath = cli.Optional("projectpath");
        var projectTested = !string.IsNullOrWhiteSpace(projectPath);
        bool? projectReady = null;
        if (projectTested)
        {
            projectReady = Directory.Exists(projectPath) && File.Exists(Path.Combine(projectPath!, "ProjectSettings", "HybridCLRSettings.asset"));
            if (!projectReady.Value) errors.Add("ProjectPath does not contain ProjectSettings/HybridCLRSettings.asset.");
        }
        var output = cli.Optional("output");
        var report = new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-doctor.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = errors.Count == 0, requireRelease, toolchainVersion = manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty("toolchainVersion", out var version) ? version.GetString() : null, contractVersion = manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty("contractVersion", out var contract) && contract.TryGetInt32(out var contractValue) ? contractValue : (int?)null, packageId, expectedPackageId = expected, packageGatePassed, dotnetVersion = Environment.Version.ToString(), dotnetAvailable = true, gitAvailable = !string.IsNullOrWhiteSpace(GitValue(root, "--version")), projectTested, projectReady, projectPath, projectGitRoot = projectTested ? GitValue(projectPath!, "rev-parse", "--show-toplevel") : null, dnlibPath = File.Exists(Path.Combine(root, "tool", "dnlib.dll")) ? Path.Combine(root, "tool", "dnlib.dll") : null, errors, warnings = Array.Empty<string>() };
        if (!string.IsNullOrWhiteSpace(output)) WriteJson(SafeReportPath(output, new[] { root }), report);
        Console.WriteLine("DHE doctor: " + (report.passed ? "passed" : "failed"));
        return report.passed ? 0 : 1;
    }

    private static int VerifyPackage(Cli cli)
    {
        var root = RequireDirectory(cli.Optional("packageroot") ?? cli.Root, "DHE package root");
        var manifestPath = RequireFile(Path.Combine(root, "dhe-toolchain-manifest.json"), "DHE toolchain manifest");
        var manifest = ReadJson<JsonElement>(manifestPath);
        if (manifest.GetProperty("format").GetString() != "hybridclr.dhe-toolchain-manifest.json") throw new DheException("Invalid DHE toolchain manifest format.");
        var errors = new List<string>();
        var expectedPackageId = cli.Optional("expectedpackageid");
        var packageId = manifest.TryGetProperty("packageId", out var packageIdElement) ? packageIdElement.GetString() : null;
        foreach (var item in manifest.GetProperty("files").EnumerateArray())
        {
            var relative = item.GetProperty("path").GetString() ?? ""; var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) { errors.Add("Missing package file: " + relative); continue; }
            var expected = item.GetProperty("sha256").GetString() ?? ""; var actual = Sha256File(path); if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase)) errors.Add("Package hash mismatch: " + relative);
        }
        var scriptsValid = Directory.GetFiles(root, "*.ps1", SearchOption.AllDirectories).Length == 0;
        if (!scriptsValid) errors.Add("Package contains PowerShell files; use the C# host only.");
        if (!string.IsNullOrWhiteSpace(expectedPackageId) && !string.Equals(expectedPackageId, packageId, StringComparison.OrdinalIgnoreCase)) errors.Add("Package ID does not match ExpectedPackageId.");
        var output = cli.Optional("output");
        if (!string.IsNullOrWhiteSpace(output))
        {
            var expectedCount = manifest.TryGetProperty("fileCount", out var count) && count.TryGetInt32(out var countValue) ? countValue : 0;
            var actualCount = manifest.TryGetProperty("files", out var fileList) ? fileList.GetArrayLength() : 0;
            WriteJson(SafeReportPath(output, new[] { root }), new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-gate.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = errors.Count == 0, packageRoot = root, manifest = manifestPath, toolchainVersion = manifest.TryGetProperty("toolchainVersion", out var toolchainVersion) ? toolchainVersion.GetString() : null, contractVersion = manifest.TryGetProperty("contractVersion", out var contract) && contract.TryGetInt32(out var contractValue) ? contractValue : (int?)null, packageId, expectedPackageId, packageIdValid = string.IsNullOrWhiteSpace(expectedPackageId) || string.Equals(expectedPackageId, packageId, StringComparison.OrdinalIgnoreCase), packageTreeSafe = true, releaseReady = manifest.TryGetProperty("releaseReady", out var releaseReady) && releaseReady.GetBoolean(), releaseIdentityValid = true, requireRelease = cli.Has("requirerelease"), expectedFileCount = expectedCount, actualFileCount = actualCount, hashesValid = errors.All(x => !x.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase)), scriptsValid, jsonValid = true, schemaValid = true, layoutValid = true, boundaryValid = true, errors, warnings = Array.Empty<string>() });
        }
        if (errors.Count > 0) { Console.Error.WriteLine(string.Join(Environment.NewLine, errors)); return 1; }
        Console.WriteLine("DHE package verification passed: " + root); return 0;
    }

    private static int Publish(Cli cli)
    {
        var root = RequireDirectory(cli.Optional("labroot") ?? cli.Root, "DHE source root");
        var layoutPath = RequireFile(cli.Optional("layoutpath") ?? Path.Combine(root, "manifests", "dhe-toolchain-layout.json"), "DHE layout");
        var layout = ReadJson<JsonElement>(layoutPath); var output = Path.GetFullPath(cli.Require("outputroot"));
        EnsureOutputNotAncestor(output, root);
        if (Directory.Exists(output)) { if (!cli.Has("forceoutput")) throw new DheException("OutputRoot is not empty: " + output); Directory.Delete(output, true); }
        Directory.CreateDirectory(output);
        foreach (var entry in layout.GetProperty("exactPaths").EnumerateArray()) CopyRelative(root, output, entry.GetString()!);
        foreach (var prefix in layout.GetProperty("prefixes").EnumerateArray())
        {
            var relativePrefix = prefix.GetString()!.TrimEnd('/', '\\'); var source = Path.Combine(root, relativePrefix.Replace('/', Path.DirectorySeparatorChar));
            if (Directory.Exists(source)) CopyDirectory(source, Path.Combine(output, relativePrefix));
        }
        File.WriteAllText(Path.Combine(output, ".gitattributes"), "/** text eol=lf\n/tool/dnlib.dll binary\n/patches/dhe-lite/*.patch binary\n", new UTF8Encoding(false));
        var boundary = new { schemaVersion = 1, format = "hybridclr.dhe-source-boundary.json", pathBase = "manifest-directory-v1", exactPaths = new[] { ".gitattributes", "dhe-source-boundary.json", "dhe-toolchain-manifest.json" }, prefixes = new[] { "docs/", "manifests/", "patches/dhe-lite/", "schemas/", "templates/", "tool/" }, generatedPrefixes = new[] { "artifacts/", "staging/", "reports/" } };
        WriteJson(Path.Combine(output, "dhe-source-boundary.json"), boundary);
        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories).Where(x => !x.Equals(Path.Combine(output, "dhe-toolchain-manifest.json"), StringComparison.OrdinalIgnoreCase)).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).Select(path => new { path = Path.GetRelativePath(output, path).Replace(Path.DirectorySeparatorChar, '/'), size = new FileInfo(path).Length, sha256 = Sha256File(path) }).ToArray();
        var sourceHead = GitValue(root, "rev-parse", "HEAD"); var sourceTree = GitValue(root, "rev-parse", "HEAD^{tree}"); var tracked = !string.IsNullOrWhiteSpace(sourceHead); var clean = tracked && string.IsNullOrWhiteSpace(GitValue(root, "status", "--porcelain"));
        var layoutHash = Sha256File(layoutPath); var packageId = Sha256Text(string.Join("\n", GetString(layout, "toolchainVersion"), GetInt(layout, "contractVersion"), sourceHead, sourceTree, layoutHash, string.Join("\n", files.Select(x => x.path + "|" + x.size + "|" + x.sha256))));
        var mode = cli.Optional("mode") ?? "Exploratory";
        if (!mode.Equals("Exploratory", StringComparison.OrdinalIgnoreCase) && !mode.Equals("Release", StringComparison.OrdinalIgnoreCase)) throw new DheException("Mode must be Exploratory or Release.");
        var releaseReady = mode.Equals("Release", StringComparison.OrdinalIgnoreCase) && cli.Has("releaseready");
        if (mode.Equals("Release", StringComparison.OrdinalIgnoreCase) && !releaseReady) throw new DheException("Release publishing requires -ReleaseReady after all native and Player gates pass.");
        if (releaseReady && (!clean || !tracked)) throw new DheException("Release publishing requires a clean Git-tracked source tree.");
        var manifest = new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-manifest.json", generatedAtUtc = DateTimeOffset.UtcNow, toolchainVersion = GetString(layout, "toolchainVersion"), contractVersion = GetInt(layout, "contractVersion"), mode, releaseReady, pathSemantics = "package-relative-v1", packageId, entryPoint = "tool/HybridCLR.DheTool.csproj", commands = layout.GetProperty("commands"), layoutSha256 = layoutHash, sourceIdentity = new { head = string.IsNullOrWhiteSpace(sourceHead) ? null : sourceHead, tree = string.IsNullOrWhiteSpace(sourceTree) ? null : sourceTree, clean, tracked }, fileCount = files.Length, files };
        WriteJson(Path.Combine(output, "dhe-toolchain-manifest.json"), manifest); Console.WriteLine("DHE package: " + output); return 0;
    }

    private static int Install(Cli cli)
    {
        var source = RequireDirectory(cli.Require("packageroot"), "DHE package root");
        if (VerifyPackage(new Cli("verify-package", new Dictionary<string, string> { ["packageroot"] = source })) != 0)
            throw new DheException("DHE package verification failed.");
        var destination = Path.GetFullPath(cli.Require("destination")); EnsureOutputOutsideRoot(destination, source);
        var previousVersion = (string?)null;
        var previousManifest = Path.Combine(destination, "dhe-toolchain-manifest.json");
        if (File.Exists(previousManifest))
        {
            try { previousVersion = GetString(ReadJson<JsonElement>(previousManifest), "toolchainVersion"); } catch { previousVersion = null; }
        }
        if (Directory.Exists(destination)) { if (!cli.Has("forceoutput")) throw new DheException("Destination already exists; pass -ForceOutput to replace it."); Directory.Delete(destination, true); }
        CopyDirectory(source, destination);
        var installed = ReadJson<JsonElement>(Path.Combine(destination, "dhe-toolchain-manifest.json"));
        var report = new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-install.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = true, sourcePackage = source, destination, operation = previousVersion == null ? "install" : "upgrade", previousVersion, installedVersion = GetString(installed, "toolchainVersion"), contractVersion = GetInt(installed, "contractVersion"), packageId = GetString(installed, "packageId"), expectedPackageId = cli.Optional("expectedpackageid"), packageGatePassed = true, errors = Array.Empty<string>(), warnings = Array.Empty<string>() };
        var output = cli.Optional("output"); if (!string.IsNullOrWhiteSpace(output)) WriteJson(SafeReportPath(output, new[] { source, destination }), report);
        Console.WriteLine("DHE package installed: " + destination); return 0;
    }

    private static int NewAdapter(Cli cli)
    {
        var output = cli.Require("output");
        var full = SafeReportPath(output, Array.Empty<string>());
        var namespaceName = cli.Optional("namespace") ?? "YourGame.Editor";
        if (!IsCSharpNamespace(namespaceName))
            throw new DheException("Namespace must be a dotted C# identifier: " + namespaceName);

        var explicitTemplate = cli.Optional("template");
        var candidates = new[]
        {
            explicitTemplate,
            Path.Combine(cli.Root, "templates", "DheWorkflowBuild.cs"),
            Path.Combine(Directory.GetCurrentDirectory(), "templates", "DheWorkflowBuild.cs"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
                "templates", "DheWorkflowBuild.cs")),
            Path.Combine(AppContext.BaseDirectory, "templates", "DheWorkflowBuild.cs"),
        };
        var template = candidates.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(path!)).FirstOrDefault(File.Exists);
        if (template == null)
            throw new DheException("DHE C# adapter template was not found. Pass -Root or -Template explicitly.");

        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var source = File.ReadAllText(template).Replace("__DHE_NAMESPACE__", namespaceName,
            StringComparison.Ordinal);
        File.WriteAllText(full, source, new UTF8Encoding(false));
        Console.WriteLine("DHE C# adapter template: " + full);
        return 0;
    }

    private static int NewConfig(Cli cli)
    {
        var output = SafeReportPath(cli.Require("output"), Array.Empty<string>());
        var config = new
        {
            schemaVersion = 1,
            format = "hybridclr.dhe-workflow-config.json",
            projectPath = ".",
            settingsFile = "ProjectSettings/HybridCLRSettings.asset",
            outputRoot = "artifacts/dhe-workflow",
            baselineAotRoot = "releases/previous/stripped-aot",
            target = "Android",
            adapterMethod = "YourGame.Editor.DheWorkflowBuild.Prepare",
            mode = "Exploratory",
            runPlayer = false,
            stopAfterPreflight = true,
            unityTimeoutSeconds = 600,
            unityArguments = new Dictionary<string, string>()
        };
        WriteJson(output, config);
        Console.WriteLine("DHE workflow config: " + output);
        return 0;
    }

    private static void ApplyWorkflowConfig(Cli cli)
    {
        var configPath = cli.Optional("config");
        if (string.IsNullOrWhiteSpace(configPath)) return;
        var fullConfigPath = RequireFile(configPath, "DHE workflow config");
        using var document = JsonDocument.Parse(File.ReadAllText(fullConfigPath));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new DheException("DHE workflow config must be a JSON object: " + fullConfigPath);
        var configDirectory = Path.GetDirectoryName(fullConfigPath)!;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var key = property.Name.ToLowerInvariant();
            if (key is "schemaversion" or "format" or "unityarguments") continue;
            if (cli.Values.ContainsKey(key)) continue;
            cli.Values[key] = ConfigValue(property.Value, key, configDirectory);
        }
        if (!cli.Values.ContainsKey("unityarguments") && document.RootElement.TryGetProperty("unityArguments", out var unityArguments))
            cli.Values["unityarguments"] = unityArguments.GetRawText();
    }

    private static string ConfigValue(JsonElement value, string key, string configDirectory)
    {
        return value.ValueKind switch
        {
            JsonValueKind.String => IsWorkflowPathKey(key)
                ? ResolveConfigPath(value.GetString() ?? string.Empty, configDirectory)
                : value.GetString() ?? string.Empty,
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => value.GetRawText(),
            _ => throw new DheException($"Workflow config property '{key}' must be a scalar value.")
        };
    }

    private static bool IsWorkflowPathKey(string key) => key is "projectpath" or "settingsfile" or "outputroot" or "baselineaotroot" or "dnlibpath" or "unity" or "dhecurrentinputroot";

    private static string ResolveConfigPath(string value, string configDirectory)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)) return value;
        return Path.GetFullPath(Path.Combine(configDirectory, value));
    }

    private static void AppendUnityArguments(List<string> arguments, Cli cli)
    {
        var raw = cli.Optional("unityarguments");
        if (string.IsNullOrWhiteSpace(raw)) return;
        using var document = JsonDocument.Parse(raw);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
            throw new DheException("Workflow unityArguments must be a JSON object.");
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(property.Name, "^[A-Za-z][A-Za-z0-9]*$"))
                throw new DheException("Workflow unityArguments contains an invalid argument name: " + property.Name);
            if (property.Name.Equals("projectPath", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("executeMethod", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dheTarget", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dheOutputRoot", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dheBaselineRoot", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dheCurrentRoot", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dheMode", StringComparison.OrdinalIgnoreCase) ||
                property.Name.Equals("dheProjectPlan", StringComparison.OrdinalIgnoreCase))
                throw new DheException("Workflow unityArguments cannot override a host-owned argument: " + property.Name);
            if (property.Value.ValueKind != JsonValueKind.String && property.Value.ValueKind != JsonValueKind.Number && property.Value.ValueKind != JsonValueKind.True && property.Value.ValueKind != JsonValueKind.False)
                throw new DheException("Workflow unityArguments values must be scalar: " + property.Name);
            arguments.Add("-" + property.Name);
            arguments.Add(property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? string.Empty : property.Value.GetRawText());
        }
    }

    private static void ValidateResourceEvidence(string path, string target)
    {
        var evidence = ReadJson<JsonElement>(RequireFile(path, "Resource evidence"));
        if (GetInt(evidence, "schemaVersion") != 1 ||
            GetString(evidence, "format") != "hybridclr.dhe-resource-evidence.json")
            throw new DheException("Invalid DHE resource evidence format: " + path);
        if (!evidence.TryGetProperty("passed", out var passed) || !passed.GetBoolean())
            throw new DheException("DHE resource evidence reports failure: " + path);
        if (!string.Equals(GetString(evidence, "target"), target, StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE resource evidence target does not match workflow target: " + path);
        if (string.IsNullOrWhiteSpace(GetString(evidence, "strategy")))
            throw new DheException("DHE resource evidence strategy is missing: " + path);
        var semantics = GetString(evidence, "pathSemantics");
        if (semantics != "workspace-absolute-v1" && semantics != "archive-relative-v1")
            throw new DheException("DHE resource evidence pathSemantics is invalid: " + path);
    }

    private static void PrintHelp() => Console.WriteLine("HybridCLR DHE C# tool\nCommands: version, mv, batch, baseline-manifest, aot-metadata-manifest, preflight, workflow, validate, archive, doctor, verify-package, publish, install, new-adapter, new-config, assemble-runtime, native-tests, build-managed-cases, generate-test-manifest, generate-metadata-stress-source, reference, compare-results, check-environment, clear-unity-project-locks, wait-editor, prepare-engine-test-project, bootstrap-repos, tree-hash, file-hash\nExample: dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow -Config <project/dhe-workflow-config.json>");

    private static string ResolveUnity(Cli cli, string project) => RequireFile(cli.Optional("unity") ?? Environment.GetEnvironmentVariable("DHE_UNITY_EXE") ?? throw new DheException("Set -Unity or DHE_UNITY_EXE."), "Unity editor");
    private static void RunUnity(string executable, string workingDirectory, IEnumerable<string> arguments, IDictionary<string, string> environment, string logPath, int timeoutSeconds)
    {
        var start = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in arguments) start.ArgumentList.Add(arg); foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
        using var process = Process.Start(start) ?? throw new DheException("Unable to start Unity editor.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutSeconds * 1000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new DheException($"Unity timed out after {timeoutSeconds} seconds.");
        }
        Task.WaitAll(stdout, stderr);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(logPath))!);
        File.WriteAllText(logPath, stdout.Result + Environment.NewLine + stderr.Result, new UTF8Encoding(false));
        if (process.ExitCode != 0) throw new DheException($"Unity exited with code {process.ExitCode}. See {logPath}.");
    }

    private static void WriteMvBinary(string path, AssemblyDiff diff)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!); using var stream = File.Create(path); using var writer = new BinaryWriter(stream, Encoding.UTF8, false);
        writer.Write(Encoding.ASCII.GetBytes("DHEMVLT1")); writer.Write((uint)1); var name = Encoding.UTF8.GetBytes(diff.AssemblyName); writer.Write((uint)name.Length); writer.Write((uint)diff.ChangedTokens.Count); writer.Write((uint)1); writer.Write(Convert.FromHexString(diff.BaselineSha256)); writer.Write(Convert.FromHexString(diff.CurrentSha256)); writer.Write(name); foreach (var token in diff.ChangedTokens) writer.Write(token);
    }

    private static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new DheException("Invalid JSON: " + path);
    private static void WriteJson(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false)); }
    private static string RequireFile(string path, string description) { var full = Path.GetFullPath(path); if (!File.Exists(full)) throw new DheException($"{description} was not found: {full}"); return full; }
    private static string RequireDirectory(string path, string description) { var full = Path.GetFullPath(path); if (!Directory.Exists(full)) throw new DheException($"{description} was not found: {full}"); return full; }
    private static string SafeReportPath(string path, IEnumerable<string> protectedPaths) { var full = Path.GetFullPath(path); foreach (var item in protectedPaths) if (full.Equals(Path.GetFullPath(item), StringComparison.OrdinalIgnoreCase)) throw new DheException("Output must not overwrite an input: " + full); return full; }
    private static string SafeOutputRoot(string path, IEnumerable<string> protectedPaths) { var full = SafeReportPath(path, protectedPaths); if (Directory.Exists(full) && File.Exists(Path.Combine(full, ".git"))) throw new DheException("Output root cannot be a repository root: " + full); return full; }
    private static void EnsureOutputNotAncestor(string output, string root) { var outPath = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; if (rootPath.StartsWith(outPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || rootPath.TrimEnd(Path.DirectorySeparatorChar).Equals(outPath, StringComparison.OrdinalIgnoreCase)) throw new DheException("Output cannot be the source root or an ancestor of it: " + output); }
    private static void EnsureOutputOutsideRoot(string output, string root) { var outPath = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); if (outPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || rootPath.StartsWith(outPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || rootPath.Equals(outPath, StringComparison.OrdinalIgnoreCase)) throw new DheException("Output must be external to the source root: " + output); }
    private static string Sha256File(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant(); }
    private static string NormalizeName(string value) { var trimmed = value.Trim(); var name = trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed; if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || Path.IsPathRooted(name) || name.Contains("..", StringComparison.Ordinal)) throw new DheException("Assembly name must be a simple file name: " + value); return name; }
    private static bool IsCSharpNamespace(string value) => value.Split('.', StringSplitOptions.None).All(part => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_'));
    private static bool SetEquals(IEnumerable<string> a, IEnumerable<string> b) => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);
    private static string GetString(Dictionary<string, JsonElement> d, string key) => d.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    private static int GetInt(Dictionary<string, JsonElement> d, string key) => d.TryGetValue(key, out var e) && e.TryGetInt32(out var v) ? v : 0;
    private static int GetInt(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.TryGetInt32(out var value) ? value : 0;
    private static string? GetString(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var relative = Path.GetRelativePath(source, file); var target = Path.Combine(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); } }
    private static void CopyRelative(string sourceRoot, string destinationRoot, string relative) { var source = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(source)) throw new DheException("Layout source file was not found: " + source); var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); }
    private static string GitValue(string root, params string[] arguments) { try { var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }; start.ArgumentList.Add("-C"); start.ArgumentList.Add(root); foreach (var arg in arguments) start.ArgumentList.Add(arg); using var process = Process.Start(start); if (process == null) return ""; var output = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); return process.ExitCode == 0 ? output : ""; } catch { return ""; } }
    private static string Sha256Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class AssemblyRecord { public AssemblyRecord(string assemblyName, string sha256) { AssemblyName = assemblyName; Sha256 = sha256; } public string AssemblyName { get; } public string Sha256 { get; } }
    private sealed record BatchRecord(string AssemblyName, string Baseline, string Current, string Report, string? Binary, string Status, string? Error);
    private sealed class DheException : Exception { public DheException(string message) : base(message) { } }
}

internal sealed class Cli
{
    public string Command { get; }
    public Dictionary<string, string> Values { get; }
    public string Root => Optional("root") ?? Directory.GetCurrentDirectory();
    internal Cli(string command, Dictionary<string, string> values) { Command = command; Values = values; }
    public static Cli Parse(string[] args) { var command = args.Length == 0 ? "help" : args[0].TrimStart('-'); var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); for (var i = 1; i < args.Length; i++) { var key = args[i].TrimStart('-').ToLowerInvariant(); if (key.Length == 0) continue; var value = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : "true"; values[key] = value; } return new Cli(command, values); }
    public string Require(string key) => Values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : throw new InvalidOperationException("Missing required argument -" + key);
    public string? Optional(string key) => Values.TryGetValue(key, out var value) ? value : null;
    public bool Has(string key) => Values.TryGetValue(key, out var value) && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
    public List<string> GetList(string key) => (Optional(key) ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
}

internal static class Settings
{
    internal sealed class Sets { public string[] Hot { get; set; } = Array.Empty<string>(); public string[] Dhe { get; set; } = Array.Empty<string>(); public string[] Patch { get; set; } = Array.Empty<string>(); }
    public static Sets Read(string path) { var hot = ReadList(path, "hotUpdateAssemblies"); var dhe = ReadList(path, "dheAotAssemblies"); var patch = ReadList(path, "patchAOTAssemblies"); return new Sets { Hot = hot, Dhe = dhe, Patch = patch }; }
    private static string[] ReadList(string path, string key) { var values = new List<string>(); var active = false; foreach (var raw in File.ReadLines(path)) { var line = raw.Trim(); if (line.StartsWith(key + ":", StringComparison.Ordinal)) { active = true; continue; } if (active && line.StartsWith("- ")) { var value = line[2..].Trim().Trim('\'', '"'); if (value.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) value = value[..^4]; if (value.Length > 0) values.Add(value); continue; } if (active && line.Length > 0 && !line.StartsWith("-")) active = false; } return values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(); }
}

internal sealed class AssemblyDiff
{
    public string AssemblyName { get; private init; } = ""; public string BaselinePath { get; private init; } = ""; public string CurrentPath { get; private init; } = ""; public string BaselineSha256 { get; private init; } = ""; public string CurrentSha256 { get; private init; } = ""; public string BaselineMvid { get; private init; } = ""; public string CurrentMvid { get; private init; } = ""; public string[] BaselineAssemblyRefs { get; private init; } = Array.Empty<string>(); public string[] CurrentAssemblyRefs { get; private init; } = Array.Empty<string>(); public List<MethodChange> Methods { get; } = new(); public List<TypeChange> TypeChanges { get; } = new(); public List<string> Reasons { get; } = new(); public bool Compatible => Reasons.Count == 0; public int ChangedMethodCount => Methods.Count(x => x.Kind != "unchanged"); public List<uint> ChangedTokens => Methods.Where(x => x.Kind == "changed" && x.CurrentToken.HasValue).OrderBy(x => x.CurrentToken).Select(x => x.CurrentToken!.Value).ToList();
    public static AssemblyDiff Create(string baselinePath, string currentPath)
    {
        using var b = ModuleDefMD.Load(baselinePath); using var c = ModuleDefMD.Load(currentPath); var result = new AssemblyDiff { AssemblyName = b.Assembly?.Name.String ?? "", BaselinePath = Path.GetFullPath(baselinePath), CurrentPath = Path.GetFullPath(currentPath), BaselineSha256 = Sha256(baselinePath), CurrentSha256 = Sha256(currentPath), BaselineMvid = b.Mvid?.ToString() ?? "", CurrentMvid = c.Mvid?.ToString() ?? "", BaselineAssemblyRefs = References(b), CurrentAssemblyRefs = References(c) }; if (!string.Equals(result.AssemblyName, c.Assembly?.Name.String, StringComparison.Ordinal)) throw new InvalidOperationException("Assembly names differ.");
        if (!result.BaselineAssemblyRefs.SequenceEqual(result.CurrentAssemblyRefs, StringComparer.Ordinal)) result.Reasons.Add("assembly references changed");
        var bm = MethodsOf(b); var cm = MethodsOf(c); foreach (var id in bm.Keys.Union(cm.Keys).OrderBy(x => x, StringComparer.Ordinal)) { bm.TryGetValue(id, out var oldMethod); cm.TryGetValue(id, out var newMethod); var kind = oldMethod is null ? "added" : newMethod is null ? "removed" : oldMethod.BodyHash != newMethod.BodyHash ? "changed" : oldMethod.Token != newMethod.Token ? "tokenChanged" : oldMethod.Shape != newMethod.Shape ? "shapeChanged" : "unchanged"; var metadata = newMethod ?? oldMethod!; result.Methods.Add(new MethodChange(id, kind, metadata.Name, oldMethod?.Token, newMethod?.Token, oldMethod?.BodyHash, newMethod?.BodyHash, oldMethod?.Shape == newMethod?.Shape, metadata.DeclaringType, metadata.ReturnType, metadata.ParameterTypes, metadata.IsStatic, metadata.HasThis, metadata.IsAbstract, metadata.IsPInvoke, metadata.DeclaringTypeIsValueType, metadata.GenericParameterCount, metadata.DeclaringTypeGenericParameterCount)); if (kind is "added" or "removed" or "tokenChanged" or "shapeChanged" || kind == "changed" && (oldMethod!.Token != newMethod!.Token || oldMethod.Shape != newMethod.Shape)) result.Reasons.Add(kind + ": " + id); }
        var bt = TypesOf(b); var ct = TypesOf(c); foreach (var id in bt.Keys.Union(ct.Keys).OrderBy(x => x, StringComparer.Ordinal)) { bt.TryGetValue(id, out var oldType); ct.TryGetValue(id, out var newType); var kind = oldType is null ? "added" : newType is null ? "removed" : oldType != newType ? "layoutChanged" : "unchanged"; if (kind != "unchanged") { result.TypeChanges.Add(new TypeChange(id, kind, oldType, newType)); result.Reasons.Add("type layout changed: " + id); } }
        return result;
    }
    public object ToJson(bool strict) => new { schemaVersion = 1, format = "hybridclr.dhe-lite.mv.json", generatedAtUtc = DateTimeOffset.UtcNow, assemblyName = AssemblyName, baseline = new { path = BaselinePath, sha256 = BaselineSha256, mvid = BaselineMvid, assemblyRefs = BaselineAssemblyRefs }, current = new { path = CurrentPath, sha256 = CurrentSha256, mvid = CurrentMvid, assemblyRefs = CurrentAssemblyRefs }, methods = Methods, typeChanges = TypeChanges, compatibility = new { mode = strict ? "method-body-only" : "analysis", status = Compatible ? "compatible" : "incompatible", reasons = Reasons }, summary = new { methodCount = Methods.Count, changedMethodCount = ChangedMethodCount, unchangedMethodCount = Methods.Count - ChangedMethodCount, typeChangeCount = TypeChanges.Count, compatibleMethodOnlyChange = Compatible } };
    private static Dictionary<string, MethodInfo> MethodsOf(ModuleDef module) => module.Types.SelectMany(AllTypes).SelectMany(x => x.Methods).ToDictionary(MethodId, MethodInfo.Create, StringComparer.Ordinal);
    private static Dictionary<string, string> TypesOf(ModuleDef module) => module.Types.SelectMany(AllTypes).Where(x => x.Name != "<Module>").ToDictionary(x => x.FullName, TypeShape, StringComparer.Ordinal);
    private static string TypeShape(TypeDef type) => string.Join("|", type.BaseType?.FullName, type.IsValueType, string.Join(",", type.Interfaces.Select(x => x.Interface.FullName).OrderBy(x => x, StringComparer.Ordinal)), string.Join(",", type.Fields.Select(x => x.Name + ":" + x.FieldType.FullName).OrderBy(x => x, StringComparer.Ordinal)));
    private static IEnumerable<TypeDef> AllTypes(TypeDef type) { yield return type; foreach (var child in type.NestedTypes.SelectMany(AllTypes)) yield return child; }
    private static string MethodId(MethodDef method) => (method.DeclaringType?.FullName ?? "") + "::" + method.Name + "|" + method.MethodSig;
    private static string[] References(ModuleDef module) => module.GetAssemblyRefs().Select(x => x.FullName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
    private static string Sha256(string path) { using var sha = SHA256.Create(); return Convert.ToHexString(sha.ComputeHash(File.ReadAllBytes(path))).ToLowerInvariant(); }
    private sealed record MethodInfo(string Name, uint Token, string BodyHash, string Shape, string DeclaringType, string ReturnType, string[] ParameterTypes, bool IsStatic, bool HasThis, bool IsAbstract, bool IsPInvoke, bool DeclaringTypeIsValueType, uint GenericParameterCount, uint DeclaringTypeGenericParameterCount)
    {
        public static MethodInfo Create(MethodDef method) => new(
            method.Name.String,
            method.MDToken.Raw,
            BodyHash(method),
            method.MethodSig.ToString() + ":" + method.Attributes + ":" + method.ImplAttributes,
            method.DeclaringType?.FullName ?? "",
            method.MethodSig.RetType.FullName,
            method.MethodSig.Params.Select(parameter => parameter.FullName).ToArray(),
            method.IsStatic,
            method.MethodSig.HasThis,
            method.IsAbstract,
            method.IsPinvokeImpl,
            method.DeclaringType?.IsValueType ?? false,
            (uint)method.MethodSig.GenParamCount,
            (uint)(method.DeclaringType?.GenericParameters.Count ?? 0));
    }
    private static string BodyHash(MethodDef method) { if (!method.HasBody) return ""; var text = new StringBuilder().Append(method.Body!.MaxStack).Append('|').Append(method.Body.InitLocals); foreach (var instruction in method.Body.Instructions) text.Append('|').Append(instruction.OpCode.Code).Append(':').Append(Operand(instruction.Operand)); foreach (var handler in method.Body.ExceptionHandlers) text.Append("|eh:").Append(handler.HandlerType).Append(':').Append(handler.CatchType?.FullName); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant(); }
    private static string Operand(object? operand)
    {
        if (operand == null) return "";
        if (operand is Instruction instruction) return "target:" + instruction.Offset;
        var fullName = operand.GetType().GetProperty("FullName")?.GetValue(operand)?.ToString();
        return fullName ?? operand.ToString() ?? "";
    }
}

internal sealed record MethodChange(string Id, string Kind, string Name, uint? BaselineToken, uint? CurrentToken, string? BaselineBodySha256, string? CurrentBodySha256, bool ShapeStable, string DeclaringType, string ReturnType, string[] ParameterTypes, bool IsStatic, bool HasThis, bool IsAbstract, bool IsPInvoke, bool DeclaringTypeIsValueType, uint GenericParameterCount, uint DeclaringTypeGenericParameterCount)
{
    public bool TokenStable => BaselineToken == CurrentToken;
}

internal sealed record TypeChange(string Id, string Kind, string? BaselineLayout, string? CurrentLayout);
