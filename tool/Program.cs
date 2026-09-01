using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace HybridCLR.DheTool;

internal static partial class Program
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private const string PackageIdAlgorithm = "sha256-canonical-manifest-v2";
    private static readonly string[] RequiredRegressionChecks =
    {
        "mv-field-order", "mv-switch-target", "mv-assembly-metadata", "mv-flags-tamper",
        "mv-token-tamper", "verify-require-release", "verify-expected-id", "verify-package-id-recompute",
        "verify-extra-source", "verify-release-bit-tamper", "evidence-role-format",
        "evidence-native-runtime-binding", "archive-safe-replace",
        "native-guard-unrelated-source-stable", "native-guard-block-tamper",
        "native-guard-duplicate-marker", "native-guard-missing-end-marker",
        "layout-release-role-schemas",
        "schema-valid-document", "schema-maximum-rejected", "schema-additional-type-rejected",
        "schema-unsupported-keyword-rejected", "schema-gate-contract"
    };

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
                "release-gate" => ReleaseGate(cli),
                "regression" => Regression(cli),
                "schema-validate" => SchemaValidate(cli),
                "schema-gate" => SchemaGate(cli),
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
        WriteJson(planValidationPath, new { schemaVersion = 1, format = "hybridclr.dhe-project-plan-validation.json", generatedAtUtc = DateTimeOffset.UtcNow, pathSemantics = "workspace-absolute-v1", passed = planComplete, complete = planComplete, coverageRequired = cli.Has("requirecompletecoverage"), coverageComplete = planComplete && (!cli.Has("requirecompletecoverage") || batchCode == 0), plan = planPath, assemblies = planRecords, errors = planComplete ? Array.Empty<string>() : new[] { "DHE project plan contains missing, incompatible, or error assemblies." }, warnings = Array.Empty<string>() });
        var reportPath = Path.Combine(output, "project-preflight-report.json");
        var passed = batchCode == 0;
        var dnlibPath = cli.Optional("dnlibpath") ?? Path.Combine(AppContext.BaseDirectory, "dnlib.dll");
        WriteJson(reportPath, new { schemaVersion = 1, format = "hybridclr.dhe-project-preflight.json", generatedAtUtc = DateTimeOffset.UtcNow, passed, generationPassed = passed, validationPassed = passed && planComplete, coverageRequired = cli.Has("requirecompletecoverage"), dheCoverageRequired = cli.Has("requiredheequalshotupdate"), configurationPassed = batch.GetProperty("configurationPassed").GetBoolean(), configurationErrors = batch.GetProperty("configurationErrors"), hotUpdateAssemblies = Settings.Read(settings).Hot, dheAotAssemblies = names, dheEqualsHotUpdate = SetEquals(Settings.Read(settings).Hot, names), coverageComplete = passed && planComplete, artifactReady = passed && planComplete && cli.Has("requirecompletecoverage"), releaseReady = false, sourcePreflight = (string?)null, sourcePreflightPassed = true, settingsFile = settings, projectRoot = cli.Optional("projectroot"), baselineRoot = baseline, currentRoot = current, batchReport = batchPath, projectPlan = planPath, projectPlanValidation = planValidationPath, batchExitCode = batchCode, counts = batch.GetProperty("counts"), assemblies, nativeAbiCoverage = "not-evaluated-by-offline-preflight", dnlibPath });
        Console.WriteLine("DHE project preflight: " + reportPath);
        return passed ? 0 : 1;
    }

    private static int Workflow(Cli cli) => ProductionWorkflow(cli);

    private static int ProductionWorkflow(Cli cli)
    {
        ApplyWorkflowConfig(cli);
        var outputValue = cli.Optional("outputroot");
        try { return ProductionWorkflowCore(cli); }
        catch (Exception ex)
        {
            if (!string.IsNullOrWhiteSpace(outputValue))
            {
                try
                {
                    var output = Path.GetFullPath(outputValue);
                    var project = Path.GetFullPath(cli.Optional("projectpath") ?? ".");
                    if (!output.Equals(project, StringComparison.OrdinalIgnoreCase) && Directory.Exists(output))
                    {
                        var stage = (string name, string? path) => new { passed = path != null && File.Exists(path), report = path };
                        WriteJson(Path.Combine(output, "project-workflow-failure.json"), new
                        {
                            schemaVersion = 1, format = "hybridclr.dhe-project-workflow-failure.json", generatedAtUtc = DateTimeOffset.UtcNow,
                            passed = false, toolchainContractVersion = 1, adapterScript = cli.Optional("adaptermethod") ?? "unknown",
                            projectPath = project, settingsFile = cli.Optional("settingsfile") ?? "unknown",
                            expectedToolchainPackageId = cli.Optional("expectedtoolchainpackageid"), error = ex.Message,
                            errors = new[] { ex.Message }, stages = new Dictionary<string, object>
                            {
                                ["toolchain"] = stage("toolchain", Path.Combine(output, "toolchain-gate.json")),
                                ["prepare"] = stage("prepare", Path.Combine(output, "adapter", "prepare.json")),
                                ["cleanCheckout"] = stage("cleanCheckout", Path.Combine(output, "clean-checkout", "clean-checkout-gate-report.json")),
                                ["sourcePreflight"] = stage("sourcePreflight", Path.Combine(output, "source-preflight", "source-preflight-report.json")),
                                ["projectPreflight"] = stage("projectPreflight", Path.Combine(output, "project-preflight", "project-preflight-report.json")),
                                ["player"] = stage("player", Path.Combine(output, "dhe-player-result.json")),
                                ["archive"] = stage("archive", Path.Combine(output, "archive-gate.json")),
                                ["release"] = stage("release", Path.Combine(output, "release-gate.json"))
                            }
                        });
                    }
                }
                catch { }
            }
            throw;
        }
    }

    private static int ProductionWorkflowCore(Cli cli)
    {
        var project = RequireDirectory(cli.Require("projectpath"), "DHE project");
        var output = SafeOutputRoot(cli.Require("outputroot"), new[] { project });
        var target = cli.Require("target");
        var settings = RequireFile(cli.Require("settingsfile"), "HybridCLR settings");
        var baseline = RequireDirectory(cli.Require("baselineaotroot"), "Baseline AOT root");
        var unity = ResolveUnity(cli, project);
        var adapterClass = cli.Require("adaptermethod");
        var mode = cli.Optional("mode") ?? "Release";
        if (mode is not ("Release" or "Exploratory")) throw new DheException("Mode must be Release or Exploratory.");
        if (mode == "Release" && !cli.Has("runplayer")) throw new DheException("Release workflow requires -RunPlayer.");
        if (mode == "Release" && cli.Has("stopafterpreflight")) throw new DheException("Release workflow cannot stop after preflight.");
        var timeout = int.TryParse(cli.Optional("unitytimeoutseconds"), out var seconds) ? Math.Clamp(seconds, 10, 3600) : 600;
        var current = Path.Combine(output, "current");
        var baselineCopy = Path.Combine(output, "baseline");
        var preflightRoot = Path.Combine(output, "project-preflight");
        Directory.CreateDirectory(output);
        var productionEvidence = PrepareProductionEvidence(cli, mode, project, settings, baseline, target, output);

        var prepareArguments = new List<string> { "-batchmode", "-nographics", "-quit", "-projectPath", project,
            "-executeMethod", adapterClass, "-dheTarget", target, "-dheOutputRoot", output,
            "-dheBaselineRoot", baselineCopy, "-dheCurrentRoot", current, "-dheMode", mode,
            "-logFile", Path.Combine(output, "unity-prepare.log") };
        AppendUnityArguments(prepareArguments, cli);
        RunUnity(unity, project, prepareArguments,
            new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline },
            Path.Combine(output, "unity-prepare-process.log"), timeout);
        var preparePath = Path.Combine(output, "adapter", "prepare.json");
        RequireFile(preparePath, "DHE adapter prepare report");

        var preflight = new Cli("preflight", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["settingsfile"] = settings, ["baselineroot"] = baselineCopy, ["currentroot"] = current,
            ["outputroot"] = preflightRoot, ["projectroot"] = project,
            ["requiredheequalshotupdate"] = "true", ["requirecompletecoverage"] = "true",
            ["dnlibpath"] = cli.Optional("dnlibpath") ?? ""
        });
        if (Preflight(preflight) != 0) throw new DheException("DHE project preflight failed.");
        var planPath = Path.Combine(preflightRoot, "dhe-project-plan.json");
        if (cli.Has("stopafterpreflight"))
        {
            var reportPath = Path.Combine(output, "project-workflow-report.json");
            WriteJson(reportPath, ProjectWorkflowReport(output, mode, target, project, settings, adapterClass,
                productionEvidence, preparePath, null, null, null, null, false, null, null));
            Console.WriteLine("DHE workflow preflight passed: " + reportPath);
            return 0;
        }
        var adapterType = adapterClass.EndsWith(".Prepare", StringComparison.Ordinal) ? adapterClass[..^".Prepare".Length] : adapterClass;
        var common = new List<string> { "-batchmode", "-nographics", "-quit", "-projectPath", project,
            "-dheTarget", target, "-dheOutputRoot", output, "-dheBaselineRoot", baselineCopy,
            "-dheCurrentRoot", current, "-dheMode", mode, "-dheProjectPlan", planPath };
        AppendUnityArguments(common, cli);
        RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".StageRuntimePlan").Append("-logFile").Append(Path.Combine(output, "unity-stage.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-stage-process.log"), timeout);
        RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".BuildDheYooAsset").Append("-logFile").Append(Path.Combine(output, "unity-yooasset.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-yooasset-process.log"), timeout);
        var resourcePath = Path.Combine(output, "adapter", "resource-evidence.json");
        ValidateResourceEvidence(resourcePath, target);
        RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".BuildScriptsOnly").Append("-logFile").Append(Path.Combine(output, "unity-scripts.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-scripts-process.log"), timeout);
        RunUnity(unity, project, common.Append("-executeMethod").Append(adapterType + ".BuildFinalPlayer").Append("-logFile").Append(Path.Combine(output, "unity-player.log")), new Dictionary<string, string> { ["DHE_BASELINE_ROOT"] = baseline }, Path.Combine(output, "unity-player-process.log"), timeout);
        var playerPath = Path.Combine(output, "dhe-player-result.json");
        RequireFile(playerPath, "DHE Player result");
        var nativePath = Path.Combine(output, "native", "dhe-native-manifest.json");
        var identityPath = Path.Combine(output, "build-identity.json");
        var runtimePlanPath = Path.Combine(output, "runtime-plan", "dhe-runtime-plan.json");
        if (mode == "Release") productionEvidence = PrepareProductionEvidence(cli, mode, project, settings, baseline, target, output);
        var workflowPath = Path.Combine(output, "player-workflow-report.json");
        WriteJson(workflowPath, PlayerWorkflowReport(output, mode, target, playerPath, nativePath, identityPath,
            runtimePlanPath, productionEvidence));
        string? gatePath = null;
        string? archiveGatePath = null;
        if (mode == "Release")
        {
            gatePath = Path.Combine(output, "release-gate.json");
            if (ReleaseGate(new Cli("release-gate", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["workflowreport"] = workflowPath, ["projectplan"] = planPath, ["output"] = gatePath,
                ["target"] = target, ["requirecompletecoverage"] = "true"
            })) != 0) throw new DheException("DHE release gate failed: " + gatePath);
            var archiveRoot = SafeOutputRoot(cli.Require("archiveroot"), new[] { output, project, baseline });
            archiveGatePath = Path.Combine(output, "archive-gate.json");
            var archiveValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["inputroot"] = output, ["archiveroot"] = archiveRoot, ["output"] = archiveGatePath,
                ["requirecompletecoverage"] = "true"
            };
            if (cli.Has("forceoutput")) archiveValues["forceoutput"] = "true";
            if (Archive(new Cli("archive", archiveValues)) != 0) throw new DheException("DHE archive gate failed: " + archiveGatePath);
        }
        var projectWorkflowPath = Path.Combine(output, "project-workflow-report.json");
        WriteJson(projectWorkflowPath, ProjectWorkflowReport(output, mode, target, project, settings, adapterClass,
            productionEvidence, preparePath, playerPath, nativePath, identityPath, runtimePlanPath, true,
            archiveGatePath, gatePath));
        Console.WriteLine("DHE workflow passed: " + projectWorkflowPath);
        return 0;
    }

    private static object ProjectWorkflowReport(string output, string mode, string target, string project, string settings,
        string adapter, ProductionEvidence evidence, string prepare, string? player, string? native, string? identity,
        string? runtimePlan, bool playerExecuted, string? archiveGate, string? releaseGate)
    {
        var preflight = Path.Combine(output, "project-preflight", "project-preflight-report.json");
        var plan = Path.Combine(output, "project-preflight", "dhe-project-plan.json");
        var planValidation = Path.Combine(output, "project-preflight", "project-plan-validation.json");
        var resource = Path.Combine(output, "adapter", "resource-evidence.json");
        var corePassed = evidence.Passed && File.Exists(preflight) && File.Exists(plan) && File.Exists(planValidation) && (!playerExecuted || player != null && File.Exists(player));
        var passed = corePassed && (mode != "Release" || archiveGate != null && File.Exists(archiveGate) && releaseGate != null && File.Exists(releaseGate));
        var stages = new Dictionary<string, object?>
        {
            ["toolchain"] = new { passed = evidence.ToolchainPassed, report = evidence.ToolchainGate },
            ["prepare"] = new { passed = File.Exists(prepare), report = prepare },
            ["cleanCheckout"] = new { passed = evidence.CleanCheckoutPassed, report = evidence.CleanCheckout },
            ["sourcePreflight"] = new { passed = evidence.SourcePreflightPassed, report = evidence.SourcePreflight },
            ["projectPreflight"] = new { passed = File.Exists(preflight), report = preflight },
            ["player"] = new { passed = playerExecuted && player != null && File.Exists(player), report = player },
            ["archive"] = new { passed = archiveGate != null && File.Exists(archiveGate), report = archiveGate },
            ["release"] = new { passed = releaseGate != null && File.Exists(releaseGate), report = releaseGate }
        };
        return new
        {
            schemaVersion = 1, format = "hybridclr.dhe-project-workflow.json", generatedAtUtc = DateTimeOffset.UtcNow,
            passed, mode, toolchainContractVersion = 1, target, adapterScript = adapter,
            projectPath = project, settingsFile = settings, toolchainGate = evidence.ToolchainGate,
            expectedToolchainPackageId = evidence.ExpectedPackageId, sourcePreflight = evidence.SourcePreflight,
            cleanCheckout = evidence.CleanCheckout, projectPreflight = preflight,
            workflowReport = playerExecuted ? Path.Combine(output, "player-workflow-report.json") : null,
            archiveGate, releaseGate, stages, errors = Array.Empty<string>(),
            warnings = playerExecuted ? Array.Empty<string>() : new[] { "Player, archive, and release stages were not executed." }
        };
    }

    private static object PlayerWorkflowReport(string output, string mode, string target, string playerPath,
        string nativePath, string identityPath, string runtimePlanPath, ProductionEvidence evidence)
    {
        var planPath = Path.Combine(output, "project-preflight", "dhe-project-plan.json");
        var planValidationPath = Path.Combine(output, "project-preflight", "project-plan-validation.json");
        var batchPath = Path.Combine(output, "project-preflight", "batch", "dhe-batch-summary.json");
        var resourcePath = Path.Combine(output, "adapter", "resource-evidence.json");
        var plan = ReadJson<JsonElement>(planPath);
        var player = ReadJson<JsonElement>(playerPath);
        var native = ReadJson<JsonElement>(nativePath);
        var identity = ReadJson<JsonElement>(identityPath);
        var records = plan.GetProperty("assemblies").EnumerateArray().ToArray();
        var changed = records.Sum(record => GetInt(record, "changedMethodCount"));
        var methodCount = records.Sum(record =>
        {
            var path = GetString(record, "mvJson");
            return path != null && File.Exists(path) ? GetInt(ReadJson<JsonElement>(path).GetProperty("summary"), "methodCount") : 0;
        });
        var typeChanges = records.Sum(record =>
        {
            var path = GetString(record, "mvJson");
            return path != null && File.Exists(path) ? GetInt(ReadJson<JsonElement>(path).GetProperty("summary"), "typeChangeCount") : 0;
        });
        var aotNames = plan.GetProperty("dheAotAssemblies");
        var loadedNames = player.GetProperty("loadedDheAssemblies");
        var transactionStatus = GetString(player, "transactionStatus") ?? (changed == 0 ? "notApplicable" : "failed");
        return new
        {
            schemaVersion = 1, format = "hybridclr.dhe-project-player-workflow.json", generatedAtUtc = DateTimeOffset.UtcNow,
            passed = GetBool(player, "passed"), validationPassed = GetBool(player, "passed"), target, mode,
            coverageRequired = true, coverageGatePassed = GetBool(plan, "dheEqualsHotUpdate"),
            releaseReady = mode == "Release" && evidence.Passed && GetBool(player, "passed"), artifactValidationPassed = true,
            buildIdentityReady = true, identityVersion = GetInt(identity, "identityVersion"),
            aotSnapshotKind = GetString(identity, "aotSnapshotKind"),
            nativeGuardSourceSha256 = GetString(identity, "nativeGuardSourceSha256"), nativeManifestSha256 = GetString(identity, "nativeManifestSha256"),
            pathSemantics = "workspace-absolute-v1", projectPlan = planPath, projectPlanValidation = planValidationPath,
            batchReport = batchPath, runtimePlan = runtimePlanPath, runtimePlanProjectPath = runtimePlanPath,
            sourcePreflight = evidence.SourcePreflight, cleanCheckoutGate = evidence.CleanCheckout,
            toolchainGate = evidence.ToolchainGate, expectedToolchainPackageId = evidence.ExpectedPackageId,
            transaction = new { status = transactionStatus, retryValidated = GetBool(player, "retryValidated"), retryAssemblyName = GetString(player, "retryAssemblyName"), retryFailure = GetString(player, "retryFailure") },
            assemblyScope = new { strategy = "configured-hot-update-equals-dhe", aotAssemblies = aotNames, loadedDheAssemblies = loadedNames, stagedDependencies = Array.Empty<string>(), stagedDependenciesLoadedAsDhe = false },
            capability = new { methodCount, changedMethodCount = changed, typeChangeCount = typeChanges, compatibility = "compatible" },
            nativeGuardCoverage = new { manifestAvailable = true, changedMethodCount = GetInt(native, "changedMethodCount"), supportedChangedMethodCount = GetInt(native, "supportedChangedMethodCount"), unsupportedChangedMethodCount = GetInt(native, "unsupportedChangedMethodCount"), nativeEntryCount = GetInt(native, "nativeEntryCount"), guardedMethodCount = GetInt(native, "supportedChangedMethodCount"), complete = GetInt(native, "unsupportedChangedMethodCount") == 0 && GetInt(native, "changedMethodCount") == changed },
            player, playerResult = playerPath, nativeManifest = nativePath, buildIdentity = identityPath,
            resourceEvidence = resourcePath, resourceBuildPolicy = "skip", artifactValidation = (string?)null,
            archiveManifest = (string?)null, archiveGate = (string?)null, runtimeSource = evidence.RuntimeManifest
        };
    }

    private static object PlayerEvidence(string? path, bool executed)
    {
        if (path != null && File.Exists(path)) return ReadJson<JsonElement>(path);
        return new { passed = !executed, loadError = executed ? "missing" : "not-run" };
    }

    private static string ReadHash(string? path, string property)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return new string('0', 64);
        return GetString(ReadJson<JsonElement>(path), property) ?? new string('0', 64);
    }

    private static void ValidateEvidenceFiles(JsonElement evidence, string baseDirectory, string sourceRoot)
    {
        if (!evidence.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() < 4)
            throw new DheException("Release evidence must contain regression, changed, no-op, and native reports.");
        var sourceHead = GetString(evidence, "sourceHead");
        var sourceTree = GetString(evidence, "sourceTree");
        if (!IsHex(sourceHead, 40, 64) || !IsHex(sourceTree, 40, 64))
            throw new DheException("Release evidence source identity is invalid.");
        var roles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in files.EnumerateArray())
        {
            var path = GetString(item, "path");
            var expected = GetString(item, "sha256");
            var role = GetString(item, "role");
            if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(expected) || Path.IsPathRooted(path) || path.Contains("..", StringComparison.Ordinal))
                throw new DheException("Release evidence contains an unsafe file entry.");
            if (string.IsNullOrWhiteSpace(role) || !roles.Add(role)) throw new DheException("Release evidence contains a missing or duplicate role.");
            var full = RequireFile(Path.Combine(baseDirectory, path), "Release evidence file");
            if (!Sha256File(full).Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new DheException("Release evidence file hash mismatch: " + path);
            var report = ReadJson<JsonElement>(full);
            if (!GetBool(report, "passed")) throw new DheException("Release evidence report is not a passing result: " + path);
            ValidateEvidenceRole(role, report, full, sourceHead!, sourceTree!, sourceRoot);
        }
        foreach (var required in new[] { "regression", "demo-changed", "demo-noop", "native" })
            if (!roles.Contains(required)) throw new DheException("Release evidence is missing role: " + required);
    }

    private static void ValidateEvidenceRole(string role, JsonElement report, string reportPath,
        string sourceHead, string sourceTree, string sourceRoot)
    {
        switch (role)
        {
            case "regression":
                RequireEvidenceFormat(report, "hybridclr.dhe-regression.json", role);
                if (!string.Equals(GetString(report, "sourceHead"), sourceHead, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(GetString(report, "sourceTree"), sourceTree, StringComparison.OrdinalIgnoreCase) ||
                    !GetBool(report, "sourceClean"))
                    throw new DheException("Regression evidence is not bound to the clean release source identity.");
                var checkNames = report.GetProperty("checks").EnumerateArray()
                    .Where(check => GetBool(check, "passed"))
                    .Select(check => GetString(check, "name") ?? "")
                    .ToHashSet(StringComparer.Ordinal);
                foreach (var required in RequiredRegressionChecks)
                    if (!checkNames.Contains(required))
                        throw new DheException("Regression evidence is missing check: " + required);
                break;
            case "demo-changed":
            case "demo-noop":
                RequireEvidenceFormat(report, "hybridclr.dhe-project-player-workflow.json", role);
                if (!GetBool(report, "validationPassed") || !GetBool(report, "coverageGatePassed"))
                    throw new DheException(role + " evidence did not pass validation and coverage.");
                ValidateEvidenceToolIdentity(report, reportPath, sourceHead, sourceTree);
                var changed = GetInt(report.GetProperty("capability"), "changedMethodCount");
                var player = report.GetProperty("player");
                if (role == "demo-changed")
                {
                    if (changed <= 0 || GetInt(player, "changedMethodCount") != changed ||
                        GetInt(player, "interpreterEntryCount") <= 0 || GetInt(player, "aotEntryCount") <= 0 ||
                        !GetBool(player, "dispatchProbeValidated") || !GetBool(player, "changedProbeChanged") ||
                        GetBool(player, "unchangedProbeChanged") || !GetBool(player, "retryValidated") ||
                        GetString(player, "transactionStatus") != "validated")
                        throw new DheException("Changed Demo evidence does not prove interpreter/AOT dispatch and rollback.");
                }
                else if (changed != 0 || GetInt(player, "changedMethodCount") != 0 ||
                    GetInt(player, "interpreterEntryCount") != 0 || GetBool(player, "changedProbeChanged") ||
                    GetBool(player, "unchangedProbeChanged") || GetString(player, "transactionStatus") != "notApplicable")
                    throw new DheException("No-op Demo evidence contains changed/interpreter activity.");
                break;
            case "native":
                RequireEvidenceFormat(report, "hybridclr.dhe-native-gate.json", role);
                if (GetInt(report, "nativeExitCode") != 0 || !GetBool(report, "mergeReady") ||
                    GetBool(report, "surrogateHeadersAllowed") ||
                    !IsHex(GetString(report, "runtimeTreeSha256"), 64, 64) ||
                    !IsHex(GetString(report, "externalTreeSha256"), 64, 64))
                    throw new DheException("Native evidence does not prove a real-header passing native gate.");
                ValidateNativeReleaseEvidence(report, reportPath, sourceRoot);
                break;
            default:
                throw new DheException("Unknown release evidence role: " + role);
        }
    }

    private static void ValidateNativeReleaseEvidence(JsonElement report, string reportPath, string sourceRoot)
    {
        sourceRoot = RequireDirectory(sourceRoot, "DHE release source root");
        var reportRoot = Path.GetDirectoryName(reportPath)!;
        var runtimeManifestPath = ResolveEvidencePath(GetString(report, "runtimeManifest"), reportRoot,
            "Native evidence runtime manifest");
        var runtimeManifestHash = Sha256File(runtimeManifestPath);
        if (!runtimeManifestHash.Equals(GetString(report, "runtimeManifestSha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("Native evidence runtime manifest hash is invalid.");
        var runtime = ReadJson<JsonElement>(runtimeManifestPath);
        RequireEvidenceFormat(runtime, "hybridclr.dhe-runtime-manifest.json", "Native runtime manifest");
        if (GetString(runtime, "profile") != "DHE-Tuanjie2022" ||
            GetString(runtime, "engineWorkflow") != "Tuanjie2022Fgs")
            throw new DheException("Native evidence uses an unexpected runtime profile or engine workflow.");

        var runtimeRoot = RequireDirectory(GetString(report, "runtimeRoot") ?? "", "Native runtime root");
        var manifestRuntimeRoot = RequireDirectory(GetString(runtime, "stagedLibil2cpp") ?? "",
            "Native manifest runtime root");
        if (!Path.GetFullPath(runtimeRoot).Equals(Path.GetFullPath(manifestRuntimeRoot),
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("Native evidence runtime root does not match its manifest.");
        var runtimeTree = TreeHashForRelease(runtimeRoot, Array.Empty<string>());
        if (!runtimeTree.Equals(GetString(report, "runtimeTreeSha256"), StringComparison.OrdinalIgnoreCase) ||
            !runtimeTree.Equals(GetString(runtime, "stagedRuntimeSha256"), StringComparison.OrdinalIgnoreCase))
            throw new DheException("Native evidence runtime tree is not bound to its live staged runtime.");

        var headers = runtime.GetProperty("externalHeaders");
        if (GetBool(headers, "surrogate") || GetBool(headers, "explicitlyAllowed"))
            throw new DheException("Native evidence uses surrogate external headers.");
        var externalRoot = RequireDirectory(GetString(headers, "stagedPath") ?? "",
            "Native external headers root");
        var externalTree = TreeHashForRelease(externalRoot, Array.Empty<string>());
        if (!externalTree.Equals(GetString(report, "externalTreeSha256"), StringComparison.OrdinalIgnoreCase) ||
            !externalTree.Equals(GetString(headers, "stagedTreeSha256"), StringComparison.OrdinalIgnoreCase))
            throw new DheException("Native evidence external header tree is not bound to its live staged headers.");

        var currentRuntimeLock = RequireFile(Path.Combine(sourceRoot, "manifests", "dhe-runtime-lock.json"),
            "Current DHE runtime lock");
        if (!Sha256File(currentRuntimeLock).Equals(GetString(runtime, "dheRuntimeLockSha256"),
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("Native evidence runtime lock does not match the release source.");
        var repoLock = ReadJson<JsonElement>(RequireFile(Path.Combine(sourceRoot, "manifests", "repo-lock.json"),
            "Current repository lock"));
        var runtimeSources = runtime.GetProperty("source");
        foreach (var repository in new[] { "hybridclr", "il2cpp_plus", "hybridclr_unity" })
        {
            var expected = GetString(repoLock.GetProperty("repositories").GetProperty(repository), "commit");
            var actual = runtimeSources.GetProperty(repository);
            if (!string.Equals(expected, GetString(actual, "commit"), StringComparison.OrdinalIgnoreCase) ||
                GetBool(actual, "dirty") || !IsHex(GetString(actual, "treeSha256"), 64, 64))
                throw new DheException("Native evidence source identity is invalid: " + repository);
        }
        var workflows = ReadJson<JsonElement>(RequireFile(Path.Combine(sourceRoot, "manifests",
            "runtime-workflows.json"), "Current runtime workflows"));
        var workflow = workflows.GetProperty("workflows").EnumerateArray().Single(item =>
            GetString(item, "id") == GetString(runtime, "engineWorkflow"));
        var expectedHeaders = GetString(workflow.GetProperty("engine"), "externalHeadersTreeSha256");
        if (!externalTree.Equals(expectedHeaders, StringComparison.OrdinalIgnoreCase))
            throw new DheException("Native evidence headers do not match the locked engine workflow.");
    }

    private static void ValidateEvidenceToolIdentity(JsonElement report, string reportPath, string sourceHead,
        string sourceTree)
    {
        var cleanPath = ResolveEvidencePath(GetString(report, "cleanCheckoutGate"),
            Path.GetDirectoryName(reportPath)!, "Demo clean checkout evidence");
        var clean = ReadJson<JsonElement>(cleanPath);
        RequireEvidenceFormat(clean, "hybridclr.dhe-clean-checkout-gate.json", "Demo clean checkout");
        var tool = clean.GetProperty("toolGit");
        if (!GetBool(tool, "clean") || !GetBool(tool, "trackedSourcesComplete") ||
            !string.Equals(GetString(tool, "head"), sourceHead, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(tool, "tree"), sourceTree, StringComparison.OrdinalIgnoreCase))
            throw new DheException("Demo evidence tool identity does not match the release source.");
    }

    private static void RequireEvidenceFormat(JsonElement report, string expected, string description)
    {
        if (GetInt(report, "schemaVersion") != 1 || GetString(report, "format") != expected)
            throw new DheException(description + " evidence has an invalid format.");
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
                var parsed = ReadMvBinary(RequireFile(binary!, "MV binary"));
                var jsonTokens = ChangedTokensFromMvJson(doc);
                if (!string.Equals(parsed.AssemblyName, GetString(doc, "assemblyName"), StringComparison.Ordinal) ||
                    !string.Equals(parsed.BaselineSha256, GetString(doc.GetProperty("baseline"), "sha256"), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(parsed.CurrentSha256, GetString(doc.GetProperty("current"), "sha256"), StringComparison.OrdinalIgnoreCase) ||
                    !parsed.ChangedTokens.SequenceEqual(jsonTokens))
                    errors.Add("MV binary does not match the MV JSON document.");
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
        var input = RequireDirectory(cli.Require("inputroot"), "Workflow output");
        var archive = SafeOutputRoot(cli.Require("archiveroot"), new[] { input });
        EnsureOutputOutsideRoot(archive, input);
        if (cli.Has("requirecompletecoverage"))
        {
            var sourceReleaseGate = Path.Combine(input, "release-gate.json");
            var sourceArtifactValidation = Path.Combine(input, "release-gate.artifact-validation.json");
            var releaseReady = File.Exists(sourceReleaseGate) && File.Exists(sourceArtifactValidation) &&
                GetBool(ReadJson<JsonElement>(sourceReleaseGate), "passed") && GetBool(ReadJson<JsonElement>(sourceArtifactValidation), "passed");
            if (!releaseReady)
            {
                var error = "Release archive requires passing release and artifact validation reports.";
                var failedOutput = cli.Optional("output");
                if (!string.IsNullOrWhiteSpace(failedOutput)) WriteJson(SafeReportPath(failedOutput, new[] { input }), new
                {
                    schemaVersion = 1, format = "hybridclr.dhe-archive-gate.json", generatedAtUtc = DateTimeOffset.UtcNow,
                    passed = false, inputRoot = input, archiveRoot = archive,
                    archiveManifest = Path.Combine(archive, "dhe-archive-manifest.json"), archiveFileCount = 0,
                    copiedValidationPassed = false, requireCompleteCoverage = true, errors = new[] { error }
                });
                Console.Error.WriteLine(error); return 1;
            }
        }
        PrepareArchiveDestination(archive, cli.Has("forceoutput"));
        Directory.CreateDirectory(archive);
        var archiveMappings = new List<ArchivePathMapping>();
        foreach (var source in Directory.GetFiles(input, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(input, source);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0].Equals("player", StringComparison.OrdinalIgnoreCase)) continue;
            var destination = Path.Combine(archive, relative); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true);
        }
        var workflow = Path.Combine(archive, "player-workflow-report.json");
        var workflowDocument = ReadJson<JsonElement>(RequireFile(workflow, "Archive workflow report"));
        var archivedIdentityPath = Path.Combine(archive, "build-identity.json");
        var archivedIdentity = ReadJson<JsonElement>(RequireFile(archivedIdentityPath, "Archive build identity"));
        var archivedGeneratedCpp = new List<string>();
        if (archivedIdentity.TryGetProperty("generatedCppPaths", out var generatedPaths) && generatedPaths.ValueKind == JsonValueKind.Array)
        {
            var generatedRoot = GetString(archivedIdentity, "generatedCppRoot") ?? "";
            if (!string.IsNullOrWhiteSpace(generatedRoot))
                archiveMappings.Add(new ArchivePathMapping(Path.GetFullPath(generatedRoot), Path.Combine(archive, "generated-cpp")));
            foreach (var item in generatedPaths.EnumerateArray())
            {
                var value = item.GetString() ?? "";
                var source = Path.IsPathRooted(value) ? value : Path.Combine(generatedRoot, value);
                if (!File.Exists(source)) throw new DheException("Generated C++ evidence was not found: " + source);
                var relative = Path.GetRelativePath(generatedRoot, source);
                if (!SafeRelative(relative)) throw new DheException("Generated C++ evidence path is unsafe: " + source);
                var destinationRelative = Path.Combine("generated-cpp", relative).Replace(Path.DirectorySeparatorChar, '/');
                var destination = Path.Combine(archive, destinationRelative.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); archivedGeneratedCpp.Add(destinationRelative);
            }
        }
        var runtimeSource = GetString(workflowDocument, "runtimeSource");
        var archivedRuntimeManifest = Path.Combine(archive, "provenance", "runtime-manifest.json");
        if (!string.IsNullOrWhiteSpace(runtimeSource) && File.Exists(runtimeSource))
        {
            CopyArchiveExternalFile(runtimeSource, archivedRuntimeManifest, archiveMappings);
            var runtimeDocument = ReadJson<JsonElement>(runtimeSource);
            var runtimeLock = GetString(runtimeDocument, "dheRuntimeLock");
            if (!string.IsNullOrWhiteSpace(runtimeLock) && File.Exists(runtimeLock))
                CopyArchiveExternalFile(runtimeLock, Path.Combine(archive, "provenance", "dhe-runtime-lock.json"), archiveMappings);
        }
        var sourcePreflightSource = Path.Combine(input, "source-preflight", "source-preflight-report.json");
        if (File.Exists(sourcePreflightSource))
        {
            var sourcePreflightDocument = ReadJson<JsonElement>(sourcePreflightSource);
            var packageLock = GetString(sourcePreflightDocument, "packageLockPath");
            if (!string.IsNullOrWhiteSpace(packageLock) && File.Exists(packageLock))
                CopyArchiveExternalFile(packageLock, Path.Combine(archive, "provenance", "dhe-package-lock.json"), archiveMappings);
            var baselineManifest = GetString(sourcePreflightDocument, "baselineManifestPath");
            if (!string.IsNullOrWhiteSpace(baselineManifest) && File.Exists(baselineManifest))
                CopyArchiveExternalFile(baselineManifest, Path.Combine(archive, "provenance", "dhe-baseline-manifest.json"), archiveMappings);
            var settingsSource = GetString(sourcePreflightDocument, "settingsFile");
            if (!string.IsNullOrWhiteSpace(settingsSource) && File.Exists(settingsSource))
                CopyArchiveExternalFile(settingsSource, Path.Combine(archive, "provenance", "project", "ProjectSettings", "HybridCLRSettings.asset"), archiveMappings);
        }
        var cleanSource = Path.Combine(input, "clean-checkout", "clean-checkout-gate-report.json");
        if (File.Exists(cleanSource))
        {
            var cleanDocument = ReadJson<JsonElement>(cleanSource);
            var boundarySource = GetString(cleanDocument, "sourceBoundaryPath");
            if (!string.IsNullOrWhiteSpace(boundarySource) && File.Exists(boundarySource))
                CopyArchiveExternalFile(boundarySource, Path.Combine(archive, "provenance", "project", "dhe-source-boundary.json"), archiveMappings);
        }
        CopyArchiveResourceEvidence(archive, input, archiveMappings);
        RewriteArchiveJsonDocuments(archive, input, archiveMappings);
        BindArchivedNativeIdentity(archive, Path.Combine(input, "native", "dhe-native-manifest.json"));

        var manifestPath = Path.Combine(archive, "dhe-archive-manifest.json");
        var projectPlan = Path.Combine(archive, "project-preflight", "dhe-project-plan.json");
        var projectPlanValidation = Path.Combine(archive, "project-preflight", "project-plan-validation.json");
        var identity = Path.Combine(archive, "build-identity.json");
        var native = Path.Combine(archive, "native", "dhe-native-manifest.json");
        var runtimePlan = Path.Combine(archive, "runtime-plan", "dhe-runtime-plan.json");
        var releaseGate = Path.Combine(archive, "release-gate.json");
        var artifactValidation = Path.Combine(archive, "release-gate.artifact-validation.json");
        var cleanCheckout = Path.Combine(archive, "clean-checkout", "clean-checkout-gate-report.json");
        foreach (var required in new[] { workflow, projectPlan, projectPlanValidation, identity, native, runtimePlan, cleanCheckout }) RequireFile(required, "Archive evidence");
        if (cli.Has("requirecompletecoverage"))
        {
            RequireFile(releaseGate, "Release gate evidence"); RequireFile(artifactValidation, "Artifact validation evidence");
            if (!GetBool(ReadJson<JsonElement>(releaseGate), "passed") || !GetBool(ReadJson<JsonElement>(artifactValidation), "passed"))
                throw new DheException("Release archive requires passing release and artifact validation reports.");
            var target = GetString(workflowDocument, "target") ?? throw new DheException("Archive workflow target is missing.");
            if (ReleaseGate(new Cli("release-gate", new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["workflowreport"] = workflow, ["projectplan"] = projectPlan, ["output"] = releaseGate,
                ["target"] = target, ["requirecompletecoverage"] = "true"
            })) != 0) throw new DheException("Archived evidence failed independent release revalidation.");
            RewriteArchiveJsonDocuments(archive, input, archiveMappings);
        }
        var identityDocument = ReadJson<JsonElement>(identity);
        var planDocument = ReadJson<JsonElement>(projectPlan);
        var cleanCheckoutDocument = ReadJson<JsonElement>(cleanCheckout);
        var assemblyRecords = planDocument.GetProperty("assemblies").EnumerateArray().Select(record => new
        {
            assemblyName = GetString(record, "assemblyName"), status = GetString(record, "status"),
            baseline = ArchiveDocumentRelative(archive, Path.GetDirectoryName(projectPlan)!, GetString(record, "baseline")), current = ArchiveDocumentRelative(archive, Path.GetDirectoryName(projectPlan)!, GetString(record, "current")),
            mvJson = ArchiveDocumentRelative(archive, Path.GetDirectoryName(projectPlan)!, GetString(record, "mvJson")), mvBytes = ArchiveDocumentRelative(archive, Path.GetDirectoryName(projectPlan)!, GetString(record, "mvBytes")),
            changedMethodCount = GetInt(record, "changedMethodCount")
        }).ToArray();
        WriteJson(manifestPath, new
        {
            schemaVersion = 1, format = "hybridclr.dhe-archive-manifest.json", generatedAtUtc = DateTimeOffset.UtcNow,
            sourceWorkflowRoot = (string?)null, pathSemantics = "archive-relative-v1",
            sourceIdentities = new { projectGit = ArchiveSourceIdentity(cleanCheckoutDocument.GetProperty("projectGit")), toolGit = ArchiveSourceIdentity(cleanCheckoutDocument.GetProperty("toolGit")) },
            workflowReport = "player-workflow-report.json", artifactValidation = "release-gate.artifact-validation.json",
            buildIdentity = "build-identity.json", nativeManifest = "native/dhe-native-manifest.json",
            immutableNativeManifest = "provenance/native-manifest.original.bin",
            immutableNativeManifestSha256 = Sha256File(Path.Combine(archive, "provenance", "native-manifest.original.bin")),
            runtimeManifest = File.Exists(archivedRuntimeManifest) ? "provenance/runtime-manifest.json" : "not-archived",
            runtimePlan = "runtime-plan/dhe-runtime-plan.json", projectPlan = "project-preflight/dhe-project-plan.json",
            projectPlanValidation = "project-preflight/project-plan-validation.json",
            generatedCppRoot = "generated-cpp", generatedCppPaths = archivedGeneratedCpp,
            assemblies = assemblyRecords,
            offlineReleaseRevalidated = cli.Has("requirecompletecoverage")
        });
        var files = Directory.GetFiles(archive, "*", SearchOption.AllDirectories)
            .Where(path => !path.Equals(manifestPath, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetRelativePath(archive, path).Replace(Path.DirectorySeparatorChar, '/'), StringComparer.Ordinal)
            .Select(path => new { path = Path.GetRelativePath(archive, path).Replace(Path.DirectorySeparatorChar, '/'), size = new FileInfo(path).Length, sha256 = Sha256File(path) }).ToArray();
        var manifestDocument = ReadJson<JsonElement>(manifestPath);
        var manifestNode = System.Text.Json.Nodes.JsonNode.Parse(manifestDocument.GetRawText())!.AsObject();
        manifestNode["files"] = System.Text.Json.JsonSerializer.SerializeToNode(files, Json);
        manifestNode["fileCount"] = files.Length;
        manifestNode["fileSetSha256"] = Sha256Text(string.Join("\n", files.Select(file => file.path + "|" + file.size + "|" + file.sha256)));
        File.WriteAllText(manifestPath, manifestNode.ToJsonString(Json), new UTF8Encoding(false));
        var copiedValid = files.All(file => File.Exists(Path.Combine(archive, file.path.Replace('/', Path.DirectorySeparatorChar))) && Sha256File(Path.Combine(archive, file.path.Replace('/', Path.DirectorySeparatorChar))).Equals(file.sha256, StringComparison.OrdinalIgnoreCase));
        var output = cli.Optional("output");
        if (!string.IsNullOrWhiteSpace(output)) WriteJson(SafeReportPath(output, new[] { input }), new
        {
            schemaVersion = 1, format = "hybridclr.dhe-archive-gate.json", generatedAtUtc = DateTimeOffset.UtcNow,
            passed = copiedValid, inputRoot = input, archiveRoot = archive, archiveManifest = manifestPath,
            archiveFileCount = files.Length + 1, copiedValidationPassed = copiedValid,
            requireCompleteCoverage = cli.Has("requirecompletecoverage"), errors = copiedValid ? Array.Empty<string>() : new[] { "Archived file hash validation failed." }
        });
        Console.WriteLine("DHE archive: " + archive); return copiedValid ? 0 : 1;
    }

    private static string? ArchiveRelative(string inputRoot, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        var full = Path.GetFullPath(sourcePath);
        var relative = Path.GetRelativePath(inputRoot, full);
        return relative.StartsWith("..", StringComparison.Ordinal) ? null : relative.Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? ArchiveDocumentRelative(string archiveRoot, string documentRoot, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;
        var full = Path.GetFullPath(Path.IsPathRooted(sourcePath) ? sourcePath : Path.Combine(documentRoot, sourcePath));
        var relative = Path.GetRelativePath(archiveRoot, full).Replace(Path.DirectorySeparatorChar, '/');
        return SafeRelative(relative) ? relative : null;
    }

    private static object ArchiveSourceIdentity(JsonElement identity) => new
    {
        vcs = GetString(identity, "vcs"), head = GetString(identity, "head"), tree = GetString(identity, "tree"),
        revision = GetString(identity, "revision"), revisionSpec = GetString(identity, "revisionSpec"),
        repository = GetString(identity, "repository"), sourceBoundarySha256 = GetString(identity, "sourceBoundarySha256") ?? new string('0', 64)
    };

    private static int Doctor(Cli cli)
    {
        var root = RequireDirectory(cli.Root, "DHE tool root");
        var expected = cli.Optional("expectedpackageid");
        var requireRelease = cli.Has("requirerelease");
        var inspection = InspectPackage(root, expected, requireRelease);
        var errors = inspection.Errors.ToList();

        var projectPath = cli.Optional("projectpath");
        var projectTested = !string.IsNullOrWhiteSpace(projectPath);
        bool? projectReady = null;
        if (projectTested)
        {
            projectReady = Directory.Exists(projectPath) && File.Exists(Path.Combine(projectPath!, "ProjectSettings", "HybridCLRSettings.asset"));
            if (!projectReady.Value) errors.Add("ProjectPath does not contain ProjectSettings/HybridCLRSettings.asset.");
        }
        var output = cli.Optional("output");
        var report = new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-doctor.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = errors.Count == 0, requireRelease, toolchainVersion = inspection.ToolchainVersion, contractVersion = inspection.ContractVersion, packageId = inspection.PackageId, expectedPackageId = expected, packageGatePassed = inspection.Passed, dotnetVersion = Environment.Version.ToString(), dotnetAvailable = true, gitAvailable = !string.IsNullOrWhiteSpace(GitValue(root, "--version")), projectTested, projectReady, projectPath, projectGitRoot = projectTested ? GitValue(projectPath!, "rev-parse", "--show-toplevel") : null, dnlibPath = File.Exists(Path.Combine(root, "tool", "dnlib.dll")) ? Path.Combine(root, "tool", "dnlib.dll") : null, errors, warnings = Array.Empty<string>() };
        if (!string.IsNullOrWhiteSpace(output)) WriteJson(SafeReportPath(output, new[] { root }), report);
        Console.WriteLine("DHE doctor: " + (report.passed ? "passed" : "failed"));
        return report.passed ? 0 : 1;
    }

    private static int VerifyPackage(Cli cli)
    {
        var root = RequireDirectory(cli.Optional("packageroot") ?? cli.Root, "DHE package root");
        var expectedPackageId = cli.Optional("expectedpackageid");
        var requireRelease = cli.Has("requirerelease");
        var inspection = InspectPackage(root, expectedPackageId, requireRelease);
        var output = cli.Optional("output");
        if (!string.IsNullOrWhiteSpace(output))
        {
            WriteJson(SafeReportPath(output, new[] { root }), new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-gate.json", generatedAtUtc = DateTimeOffset.UtcNow, passed = inspection.Passed, packageRoot = root, manifest = inspection.ManifestPath, toolchainVersion = inspection.ToolchainVersion, contractVersion = inspection.ContractVersion, packageId = inspection.PackageId, computedPackageId = inspection.ComputedPackageId, packageIdAlgorithm = PackageIdAlgorithm, expectedPackageId, packageIdValid = inspection.PackageIdValid, packageTreeSafe = inspection.PackageTreeSafe, releaseReady = inspection.ReleaseReady, releaseIdentityValid = inspection.ReleaseIdentityValid, requireRelease, expectedFileCount = inspection.ExpectedFileCount, actualFileCount = inspection.ActualFileCount, hashesValid = inspection.HashesValid, scriptsValid = inspection.ProhibitedFilesValid, jsonValid = inspection.JsonValid, schemaValid = inspection.SchemaValid, layoutValid = inspection.LayoutValid, boundaryValid = inspection.BoundaryValid, errors = inspection.Errors, warnings = Array.Empty<string>() });
        }
        if (!inspection.Passed) { Console.Error.WriteLine(string.Join(Environment.NewLine, inspection.Errors)); return 1; }
        Console.WriteLine("DHE package verification passed: " + root); return 0;
    }

    private static PackageInspection InspectPackage(string root, string? expectedPackageId, bool requireRelease)
    {
        var result = new PackageInspection { ManifestPath = Path.Combine(root, "dhe-toolchain-manifest.json") };
        if (!File.Exists(result.ManifestPath))
        {
            result.Errors.Add("DHE toolchain manifest is missing.");
            return result;
        }

        JsonElement manifest;
        try
        {
            manifest = ReadJson<JsonElement>(result.ManifestPath);
            result.JsonValid = manifest.ValueKind == JsonValueKind.Object;
        }
        catch (Exception ex)
        {
            result.Errors.Add("DHE toolchain manifest is invalid: " + ex.Message);
            return result;
        }
        if (!result.JsonValid)
        {
            result.Errors.Add("DHE toolchain manifest must be a JSON object.");
            return result;
        }

        result.ToolchainVersion = GetString(manifest, "toolchainVersion");
        result.ContractVersion = GetInt(manifest, "contractVersion");
        result.PackageId = GetString(manifest, "packageId");
        var mode = GetString(manifest, "mode") ?? "";
        result.ReleaseReady = GetBool(manifest, "releaseReady");
        result.SchemaValid = GetInt(manifest, "schemaVersion") == 1 &&
            GetString(manifest, "format") == "hybridclr.dhe-toolchain-manifest.json" &&
            GetString(manifest, "pathSemantics") == "package-relative-v1" &&
            GetString(manifest, "entryPoint") == "tool/HybridCLR.DheTool.csproj" &&
            GetString(manifest, "packageIdAlgorithm") == PackageIdAlgorithm &&
            !string.IsNullOrWhiteSpace(result.ToolchainVersion) && result.ContractVersion == 1;
        if (!result.SchemaValid) result.Errors.Add("DHE toolchain manifest contract is invalid.");

        var entries = new List<PackageFileEntry>();
        var declared = new HashSet<string>(StringComparer.Ordinal);
        result.PackageTreeSafe = true;
        result.HashesValid = true;
        if (!manifest.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
        {
            result.Errors.Add("DHE toolchain manifest files are missing.");
            result.PackageTreeSafe = false;
            result.HashesValid = false;
        }
        else
        {
            foreach (var item in files.EnumerateArray())
            {
                var relative = GetString(item, "path") ?? "";
                var size = item.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var sizeValue) ? sizeValue : -1;
                var hash = GetString(item, "sha256") ?? "";
                if (!IsPortableRelativePath(relative) || relative.Contains('\\') || !declared.Add(relative))
                {
                    result.Errors.Add("Package manifest contains an unsafe or duplicate path: " + relative);
                    result.PackageTreeSafe = false;
                    continue;
                }
                if (size < 0 || !IsHex(hash, 64, 64))
                {
                    result.Errors.Add("Package manifest contains invalid file metadata: " + relative);
                    result.HashesValid = false;
                    continue;
                }
                entries.Add(new PackageFileEntry(relative, size, hash.ToLowerInvariant()));
                var full = ResolveContainedPath(root, relative, "Package file");
                if (!File.Exists(full))
                {
                    result.Errors.Add("Missing package file: " + relative);
                    result.HashesValid = false;
                    continue;
                }
                var info = new FileInfo(full);
                if (info.Length != size || !Sha256File(full).Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("Package size or hash mismatch: " + relative);
                    result.HashesValid = false;
                }
            }
        }

        result.ExpectedFileCount = manifest.TryGetProperty("fileCount", out var count) && count.TryGetInt32(out var countValue) ? countValue : -1;
        var actualFiles = Directory.GetFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .Where(path => path != "dhe-toolchain-manifest.json" && !IsPackageGeneratedPath(path))
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        result.ActualFileCount = actualFiles.Length;
        if (result.ExpectedFileCount != entries.Count || !declared.OrderBy(path => path, StringComparer.Ordinal)
                .SequenceEqual(actualFiles, StringComparer.Ordinal))
        {
            result.Errors.Add("Package actual file set does not match the authenticated manifest.");
            result.PackageTreeSafe = false;
        }
        if (Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Any(path => (File.GetAttributes(path) & System.IO.FileAttributes.ReparsePoint) != 0))
        {
            result.Errors.Add("Package contains a reparse point.");
            result.PackageTreeSafe = false;
        }
        result.ProhibitedFilesValid = !actualFiles.Any(path => path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase));
        if (!result.ProhibitedFilesValid) result.Errors.Add("Package contains a prohibited host file.");

        result.LayoutValid = ValidateInstalledLayout(root, manifest, result.Errors);
        result.BoundaryValid = ValidateInstalledBoundary(root, result.Errors);
        var source = manifest.TryGetProperty("sourceIdentity", out var sourceValue) && sourceValue.ValueKind == JsonValueKind.Object
            ? sourceValue : default;
        var sourceHead = GetString(source, "head") ?? "";
        var sourceTree = GetString(source, "tree") ?? "";
        var sourceClean = GetBool(source, "clean");
        var sourceTracked = GetBool(source, "tracked");
        var commands = manifest.TryGetProperty("commands", out var commandsValue) && commandsValue.ValueKind == JsonValueKind.Array
            ? commandsValue.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString() ?? "").ToArray()
            : Array.Empty<string>();
        var layoutHash = GetString(manifest, "layoutSha256") ?? "";
        result.ComputedPackageId = CalculatePackageId(result.ToolchainVersion ?? "", result.ContractVersion ?? 0,
            mode, result.ReleaseReady, sourceHead, sourceTree, sourceClean, sourceTracked, layoutHash, commands, entries);
        result.PackageIdValid = IsHex(result.PackageId, 64, 64) &&
            string.Equals(result.PackageId, result.ComputedPackageId, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(expectedPackageId) || string.Equals(result.PackageId, expectedPackageId, StringComparison.OrdinalIgnoreCase));
        if (!result.PackageIdValid) result.Errors.Add("Package ID does not match its canonical contents or ExpectedPackageId.");

        result.ReleaseIdentityValid = mode is "Release" or "Exploratory" &&
            (mode == "Release" ? result.ReleaseReady && sourceClean && sourceTracked && IsHex(sourceHead, 40, 64) && IsHex(sourceTree, 40, 64) : !result.ReleaseReady);
        if (!result.ReleaseIdentityValid) result.Errors.Add("Package release/source identity is invalid.");
        if (requireRelease && (mode != "Release" || !result.ReleaseReady)) result.Errors.Add("A release-ready package is required.");
        result.Passed = result.Errors.Count == 0;
        return result;
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
        var files = Directory.GetFiles(output, "*", SearchOption.AllDirectories)
            .Where(path => !path.Equals(Path.Combine(output, "dhe-toolchain-manifest.json"), StringComparison.OrdinalIgnoreCase))
            .Select(path => new PackageFileEntry(Path.GetRelativePath(output, path).Replace(Path.DirectorySeparatorChar, '/'),
                new FileInfo(path).Length, Sha256File(path)))
            .OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        var sourceHead = GitValue(root, "rev-parse", "HEAD"); var sourceTree = GitValue(root, "rev-parse", "HEAD^{tree}"); var tracked = !string.IsNullOrWhiteSpace(sourceHead); var clean = tracked && string.IsNullOrWhiteSpace(GitValue(root, "status", "--porcelain"));
        var mode = cli.Optional("mode") ?? "Exploratory";
        if (!mode.Equals("Exploratory", StringComparison.OrdinalIgnoreCase) && !mode.Equals("Release", StringComparison.OrdinalIgnoreCase)) throw new DheException("Mode must be Exploratory or Release.");
        var releaseReady = false;
        if (mode.Equals("Release", StringComparison.OrdinalIgnoreCase))
        {
            var evidencePath = RequireFile(cli.Require("releaseevidence"), "DHE toolchain release evidence");
            var evidence = ReadJson<JsonElement>(evidencePath);
            if (GetInt(evidence, "schemaVersion") != 1 || GetString(evidence, "format") != "hybridclr.dhe-toolchain-release-evidence.json" || !GetBool(evidence, "passed"))
                throw new DheException("Toolchain release evidence is not a passing production evidence report.");
            if (!string.Equals(GetString(evidence, "sourceHead"), sourceHead, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(evidence, "sourceTree"), sourceTree, StringComparison.OrdinalIgnoreCase))
                throw new DheException("Toolchain release evidence does not match the source HEAD/tree being published.");
            ValidateEvidenceFiles(evidence, Path.GetDirectoryName(evidencePath)!, root);
            releaseReady = true;
        }
        if (releaseReady && (!clean || !tracked)) throw new DheException("Release publishing requires a clean Git-tracked source tree.");
        var layoutHash = Sha256File(layoutPath);
        var commands = layout.GetProperty("commands").EnumerateArray().Select(value => value.GetString() ?? "").ToArray();
        var packageId = CalculatePackageId(GetString(layout, "toolchainVersion") ?? "", GetInt(layout, "contractVersion"),
            mode, releaseReady, sourceHead, sourceTree, clean, tracked, layoutHash, commands, files);
        var manifest = new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-manifest.json", generatedAtUtc = DateTimeOffset.UtcNow, toolchainVersion = GetString(layout, "toolchainVersion"), contractVersion = GetInt(layout, "contractVersion"), mode, releaseReady, pathSemantics = "package-relative-v1", packageIdAlgorithm = PackageIdAlgorithm, packageId, entryPoint = "tool/HybridCLR.DheTool.csproj", commands, layoutSha256 = layoutHash, sourceIdentity = new { head = string.IsNullOrWhiteSpace(sourceHead) ? null : sourceHead, tree = string.IsNullOrWhiteSpace(sourceTree) ? null : sourceTree, clean, tracked }, fileCount = files.Length, files };
        WriteJson(Path.Combine(output, "dhe-toolchain-manifest.json"), manifest); Console.WriteLine("DHE package: " + output); return 0;
    }

    private static int Install(Cli cli)
    {
        var source = RequireDirectory(cli.Require("packageroot"), "DHE package root");
        var verifyValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["packageroot"] = source };
        var expectedPackageId = cli.Optional("expectedpackageid");
        if (!string.IsNullOrWhiteSpace(expectedPackageId)) verifyValues["expectedpackageid"] = expectedPackageId;
        if (cli.Has("requirerelease")) verifyValues["requirerelease"] = "true";
        if (VerifyPackage(new Cli("verify-package", verifyValues)) != 0)
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
            baselineManifestPath = "releases/previous/stripped-aot/dhe-baseline-manifest.json",
            runtimeManifestPath = "releases/runtime/runtime-manifest.json",
            packageLockPath = "Assets/Editor/DHE/dhe-package-lock.json",
            sourceBoundaryPath = "Assets/Editor/DHE/dhe-source-boundary.json",
            archiveRoot = "artifacts/dhe-workflow-archive",
            toolchainRoot = "Tools/HybridCLRDhe",
            expectedToolchainPackageId = new string('0', 64),
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

    private static bool IsWorkflowPathKey(string key) => key is "projectpath" or "settingsfile" or "outputroot" or "baselineaotroot" or "baselinemanifestpath" or "runtimemanifestpath" or "packagelockpath" or "sourceboundarypath" or "archiveroot" or "toolchainroot" or "dnlibpath" or "unity" or "dhecurrentinputroot";

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
        if (GetString(evidence, "strategy") == "cat-yooasset-structured-report")
        {
            var reportPath = ResolveEvidencePath(GetString(evidence, "yooAssetBuild"), Path.GetDirectoryName(path)!,
                "YooAsset structured report");
            var report = ReadJson<JsonElement>(reportPath);
            if (GetInt(report, "schemaVersion") != 1 || GetString(report, "format") != "hybridclr.dhe-yooasset-build.json" ||
                !GetBool(report, "passed") || !string.Equals(GetString(report, "target"), target, StringComparison.OrdinalIgnoreCase))
                throw new DheException("YooAsset structured report contract is invalid: " + reportPath);
            var packageValue = GetString(report, "packageDirectory") ?? "";
            var packageDirectory = Path.GetFullPath(Path.IsPathRooted(packageValue) ? packageValue :
                Path.Combine(Path.GetDirectoryName(reportPath)!, packageValue));
            if (!Directory.Exists(packageDirectory)) throw new DheException("Archived YooAsset bundle directory is missing.");
            var buildReport = ResolveEvidencePath(GetString(report, "buildReport"), Path.GetDirectoryName(reportPath)!,
                "YooAsset build report");
            if (!Sha256File(buildReport).Equals(GetString(report, "buildReportSha256"), StringComparison.OrdinalIgnoreCase))
                throw new DheException("YooAsset build report hash mismatch.");
            if (!report.TryGetProperty("requiredAssets", out var assets) || assets.ValueKind != JsonValueKind.Array ||
                GetInt(evidence, "requiredAssetCount") != assets.GetArrayLength())
                throw new DheException("YooAsset required asset evidence is incomplete.");
            var assetNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var asset in assets.EnumerateArray())
            {
                var assetPath = GetString(asset, "assetPath") ?? "";
                var bundleName = GetString(asset, "bundleFileName") ?? "";
                if (!GetBool(asset, "present") || !assetNames.Add(assetPath) || !IsPortableRelativePath(bundleName))
                    throw new DheException("YooAsset required asset entry is invalid: " + assetPath);
                var bundlePath = ResolveContainedPath(packageDirectory, bundleName, "YooAsset bundle");
                if (!File.Exists(bundlePath) || new FileInfo(bundlePath).Length != GetLong(asset, "bundleSize") ||
                    !Sha256File(bundlePath).Equals(GetString(asset, "bundleSha256"), StringComparison.OrdinalIgnoreCase))
                    throw new DheException("YooAsset required bundle hash/size mismatch: " + assetPath);
            }
        }
    }

    private static void PrintHelp() => Console.WriteLine("HybridCLR DHE C# tool\nCommands: version, mv, batch, baseline-manifest, aot-metadata-manifest, preflight, workflow, release-gate, regression, schema-validate, schema-gate, validate, archive, doctor, verify-package, publish, install, new-adapter, new-config, assemble-runtime, native-tests, build-managed-cases, generate-test-manifest, generate-metadata-stress-source, reference, compare-results, check-environment, clear-unity-project-locks, wait-editor, prepare-engine-test-project, bootstrap-repos, tree-hash, file-hash\nExample: dotnet run --project tool/HybridCLR.DheTool.csproj -- workflow -Config <project/dhe-workflow-config.json>");

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

    private static MvBinaryDocument ReadMvBinary(string path)
    {
        var bytes = File.ReadAllBytes(path);
        if (bytes.Length < 88 || Encoding.ASCII.GetString(bytes, 0, 8) != "DHEMVLT1")
            throw new DheException("MV binary header is invalid: " + path);
        var version = BitConverter.ToUInt32(bytes, 8);
        var nameLength = BitConverter.ToUInt32(bytes, 12);
        var changedCount = BitConverter.ToUInt32(bytes, 16);
        var flags = BitConverter.ToUInt32(bytes, 20);
        if (version != 1 || flags != 1) throw new DheException("MV binary version or flags are unsupported: " + path);
        var expectedLength = 88L + nameLength + changedCount * 4L;
        if (nameLength == 0 || nameLength > 4096 || expectedLength != bytes.Length)
            throw new DheException("MV binary length is invalid: " + path);
        string assemblyName;
        try { assemblyName = new UTF8Encoding(false, true).GetString(bytes, 88, checked((int)nameLength)); }
        catch (Exception ex) { throw new DheException("MV binary assembly name is invalid UTF-8: " + ex.Message); }
        if (string.IsNullOrWhiteSpace(assemblyName)) throw new DheException("MV binary assembly name is empty.");
        var tokens = new uint[checked((int)changedCount)];
        var tokenOffset = checked(88 + (int)nameLength);
        for (var index = 0; index < tokens.Length; index++)
        {
            tokens[index] = BitConverter.ToUInt32(bytes, tokenOffset + index * 4);
            if ((tokens[index] & 0xff000000u) != 0x06000000u || (tokens[index] & 0x00ffffffu) == 0)
                throw new DheException("MV binary contains a non-MethodDef token: " + tokens[index].ToString("x8"));
            if (index > 0 && tokens[index] <= tokens[index - 1])
                throw new DheException("MV binary method tokens must be unique and sorted.");
        }
        return new MvBinaryDocument(assemblyName, Convert.ToHexString(bytes.AsSpan(24, 32)).ToLowerInvariant(),
            Convert.ToHexString(bytes.AsSpan(56, 32)).ToLowerInvariant(), tokens);
    }

    private static uint[] ChangedTokensFromMvJson(JsonElement document)
    {
        if (!document.TryGetProperty("methods", out var methods) || methods.ValueKind != JsonValueKind.Array)
            throw new DheException("MV JSON methods are missing.");
        return methods.EnumerateArray().Where(method => GetString(method, "kind") == "changed")
            .Select(method => method.TryGetProperty("currentToken", out var token) && token.TryGetUInt32(out var value)
                ? value : throw new DheException("MV JSON changed method token is invalid."))
            .OrderBy(token => token).ToArray();
    }

    private static T ReadJson<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new DheException("Invalid JSON: " + path);
    private static void WriteJson(string path, object value) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, JsonSerializer.Serialize(value, Json), new UTF8Encoding(false)); }
    private static string RequireFile(string path, string description) { var full = Path.GetFullPath(path); if (!File.Exists(full)) throw new DheException($"{description} was not found: {full}"); return full; }
    private static string RequireDirectory(string path, string description) { var full = Path.GetFullPath(path); if (!Directory.Exists(full)) throw new DheException($"{description} was not found: {full}"); return full; }
    private static string SafeReportPath(string path, IEnumerable<string> protectedPaths) { var full = Path.GetFullPath(path); foreach (var item in protectedPaths) if (full.Equals(Path.GetFullPath(item), StringComparison.OrdinalIgnoreCase)) throw new DheException("Output must not overwrite an input: " + full); return full; }
    private static string SafeOutputRoot(string path, IEnumerable<string> protectedPaths) { var full = SafeReportPath(path, protectedPaths); if (Directory.Exists(full) && (File.Exists(Path.Combine(full, ".git")) || Directory.Exists(Path.Combine(full, ".git")) || Directory.Exists(Path.Combine(full, ".svn")))) throw new DheException("Output root cannot be a repository root: " + full); return full; }
    private static void EnsureOutputNotAncestor(string output, string root) { var outPath = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar; if (rootPath.StartsWith(outPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || rootPath.TrimEnd(Path.DirectorySeparatorChar).Equals(outPath, StringComparison.OrdinalIgnoreCase)) throw new DheException("Output cannot be the source root or an ancestor of it: " + output); }
    private static void EnsureOutputOutsideRoot(string output, string root) { var outPath = Path.GetFullPath(output).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); var rootPath = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar); if (outPath.StartsWith(rootPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || rootPath.StartsWith(outPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || rootPath.Equals(outPath, StringComparison.OrdinalIgnoreCase)) throw new DheException("Output must be external to the source root: " + output); }
    private static string Sha256File(string path) { using var sha = SHA256.Create(); using var input = File.OpenRead(path); return Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant(); }
    private static string CalculatePackageId(string version, int contractVersion, string mode, bool releaseReady,
        string? sourceHead, string? sourceTree, bool sourceClean, bool sourceTracked, string layoutHash,
        IEnumerable<string> commands, IEnumerable<PackageFileEntry> files)
    {
        var lines = new List<string>
        {
            PackageIdAlgorithm, version, contractVersion.ToString(), mode, releaseReady ? "1" : "0",
            (sourceHead ?? "").ToLowerInvariant(), (sourceTree ?? "").ToLowerInvariant(),
            sourceClean ? "1" : "0", sourceTracked ? "1" : "0", layoutHash.ToLowerInvariant()
        };
        lines.AddRange(commands.OrderBy(value => value, StringComparer.Ordinal).Select(value => "command|" + value));
        lines.AddRange(files.OrderBy(file => file.Path, StringComparer.Ordinal)
            .Select(file => $"file|{file.Path}|{file.Size}|{file.Sha256.ToLowerInvariant()}"));
        return Sha256Text(string.Join("\n", lines));
    }

    private static bool ValidateInstalledLayout(string root, JsonElement manifest, List<string> errors)
    {
        try
        {
            var path = ResolveContainedPath(root, "manifests/dhe-toolchain-layout.json", "Toolchain layout");
            if (!File.Exists(path) || !Sha256File(path).Equals(GetString(manifest, "layoutSha256"), StringComparison.OrdinalIgnoreCase))
            {
                errors.Add("Toolchain layout hash does not match the manifest.");
                return false;
            }
            var layout = ReadJson<JsonElement>(path);
            var valid = GetInt(layout, "schemaVersion") == 1 &&
                GetString(layout, "format") == "hybridclr.dhe-toolchain-layout.json" &&
                GetString(layout, "toolchainVersion") == GetString(manifest, "toolchainVersion") &&
                GetInt(layout, "contractVersion") == GetInt(manifest, "contractVersion");
            var layoutCommands = layout.GetProperty("commands").EnumerateArray().Select(value => value.GetString() ?? "")
                .OrderBy(value => value, StringComparer.Ordinal);
            var manifestCommands = manifest.GetProperty("commands").EnumerateArray().Select(value => value.GetString() ?? "")
                .OrderBy(value => value, StringComparer.Ordinal);
            valid &= layoutCommands.SequenceEqual(manifestCommands, StringComparer.Ordinal);
            foreach (var entry in layout.GetProperty("exactPaths").EnumerateArray())
            {
                var relative = entry.GetString() ?? "";
                valid &= IsPortableRelativePath(relative) && File.Exists(ResolveContainedPath(root, relative, "Layout file"));
            }
            foreach (var entry in layout.GetProperty("prefixes").EnumerateArray())
            {
                var relative = (entry.GetString() ?? "").TrimEnd('/', '\\');
                valid &= IsPortableRelativePath(relative) && Directory.Exists(ResolveContainedPath(root, relative, "Layout prefix"));
            }
            if (!valid) errors.Add("Installed toolchain layout contract is invalid.");
            return valid;
        }
        catch (Exception ex)
        {
            errors.Add("Installed toolchain layout: " + ex.Message);
            return false;
        }
    }

    private static bool ValidateInstalledBoundary(string root, List<string> errors)
    {
        try
        {
            var path = ResolveContainedPath(root, "dhe-source-boundary.json", "Installed source boundary");
            var boundary = ReadJson<JsonElement>(RequireFile(path, "Installed source boundary"));
            var valid = GetInt(boundary, "schemaVersion") == 1 &&
                GetString(boundary, "format") == "hybridclr.dhe-source-boundary.json" &&
                GetString(boundary, "pathBase") == "manifest-directory-v1";
            foreach (var entry in boundary.GetProperty("exactPaths").EnumerateArray())
            {
                var relative = entry.GetString() ?? "";
                valid &= IsPortableRelativePath(relative) && File.Exists(ResolveContainedPath(root, relative, "Boundary file"));
            }
            foreach (var entry in boundary.GetProperty("prefixes").EnumerateArray())
            {
                var relative = (entry.GetString() ?? "").TrimEnd('/', '\\');
                valid &= IsPortableRelativePath(relative) && Directory.Exists(ResolveContainedPath(root, relative, "Boundary prefix"));
            }
            if (!valid) errors.Add("Installed source boundary is invalid.");
            return valid;
        }
        catch (Exception ex)
        {
            errors.Add("Installed source boundary: " + ex.Message);
            return false;
        }
    }

    private static string ResolveContainedPath(string root, string relative, string description)
    {
        if (!IsPortableRelativePath(relative)) throw new DheException(description + " path is unsafe: " + relative);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var full = Path.GetFullPath(Path.Combine(normalizedRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new DheException(description + " escapes its root: " + relative);
        return full;
    }

    private static bool IsPackageGeneratedPath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return normalized.StartsWith("tool/bin/", StringComparison.OrdinalIgnoreCase) ||
            normalized.StartsWith("tool/obj/", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPortableRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value) || value.StartsWith('/') ||
            value.StartsWith('\\') || value.Contains(':')) return false;
        var parts = value.Replace('\\', '/').Split('/');
        return parts.All(part => part.Length > 0 && part is not "." and not "..");
    }

    private static bool IsHex(string? value, int minimumLength, int maximumLength) =>
        value != null && value.Length >= minimumLength && value.Length <= maximumLength &&
        value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
    private static string NormalizeName(string value) { var trimmed = value.Trim(); var name = trimmed.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? trimmed[..^4] : trimmed; if (name.Length == 0 || name.Contains('/') || name.Contains('\\') || Path.IsPathRooted(name) || name.Contains("..", StringComparison.Ordinal)) throw new DheException("Assembly name must be a simple file name: " + value); return name; }
    private static bool IsCSharpNamespace(string value) => value.Split('.', StringSplitOptions.None).All(part => part.Length > 0 && (char.IsLetter(part[0]) || part[0] == '_') && part.Skip(1).All(ch => char.IsLetterOrDigit(ch) || ch == '_'));
    private static bool SetEquals(IEnumerable<string> a, IEnumerable<string> b) => new HashSet<string>(a, StringComparer.OrdinalIgnoreCase).SetEquals(b);
    private static string GetString(Dictionary<string, JsonElement> d, string key) => d.TryGetValue(key, out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : "";
    private static int GetInt(Dictionary<string, JsonElement> d, string key) => d.TryGetValue(key, out var e) && e.TryGetInt32(out var v) ? v : 0;
    private static int GetInt(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.TryGetInt32(out var value) ? value : 0;
    private static long GetLong(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.TryGetInt64(out var value) ? value : 0;
    private static string? GetString(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
    private static bool GetBool(JsonElement e, string key) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False && p.GetBoolean();
    private static void CopyDirectory(string source, string destination) { Directory.CreateDirectory(destination); foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories)) { var relative = Path.GetRelativePath(source, file); var target = Path.Combine(destination, relative); Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target, true); } }
    private static void CopyRelative(string sourceRoot, string destinationRoot, string relative) { var source = Path.Combine(sourceRoot, relative.Replace('/', Path.DirectorySeparatorChar)); if (!File.Exists(source)) throw new DheException("Layout source file was not found: " + source); var destination = Path.Combine(destinationRoot, relative.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(source, destination, true); }
    private static string GitValue(string root, params string[] arguments) { try { var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true }; start.ArgumentList.Add("-C"); start.ArgumentList.Add(root); foreach (var arg in arguments) start.ArgumentList.Add(arg); using var process = Process.Start(start); if (process == null) return ""; var output = process.StandardOutput.ReadToEnd().Trim(); process.WaitForExit(); return process.ExitCode == 0 ? output : ""; } catch { return ""; } }
    private static string Sha256Text(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class AssemblyRecord { public AssemblyRecord(string assemblyName, string sha256) { AssemblyName = assemblyName; Sha256 = sha256; } public string AssemblyName { get; } public string Sha256 { get; } }
    private sealed record PackageFileEntry(string Path, long Size, string Sha256);
    private sealed record MvBinaryDocument(string AssemblyName, string BaselineSha256, string CurrentSha256,
        uint[] ChangedTokens);
    private sealed class PackageInspection
    {
        public bool Passed { get; set; }
        public string ManifestPath { get; set; } = "";
        public string? ToolchainVersion { get; set; }
        public int? ContractVersion { get; set; }
        public string? PackageId { get; set; }
        public string? ComputedPackageId { get; set; }
        public bool PackageIdValid { get; set; }
        public bool PackageTreeSafe { get; set; }
        public bool ReleaseReady { get; set; }
        public bool ReleaseIdentityValid { get; set; }
        public int ExpectedFileCount { get; set; }
        public int ActualFileCount { get; set; }
        public bool HashesValid { get; set; }
        public bool ProhibitedFilesValid { get; set; }
        public bool JsonValid { get; set; }
        public bool SchemaValid { get; set; }
        public bool LayoutValid { get; set; }
        public bool BoundaryValid { get; set; }
        public List<string> Errors { get; } = new();
    }
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
        if (!string.Equals(ModuleShape(b), ModuleShape(c), StringComparison.Ordinal)) result.Reasons.Add("assembly or module metadata changed");
        var bm = MethodsOf(b); var cm = MethodsOf(c); foreach (var id in bm.Keys.Union(cm.Keys).OrderBy(x => x, StringComparer.Ordinal)) { bm.TryGetValue(id, out var oldMethod); cm.TryGetValue(id, out var newMethod); var kind = oldMethod is null ? "added" : newMethod is null ? "removed" : oldMethod.BodyHash != newMethod.BodyHash ? "changed" : oldMethod.Token != newMethod.Token ? "tokenChanged" : oldMethod.Shape != newMethod.Shape ? "shapeChanged" : "unchanged"; var metadata = newMethod ?? oldMethod!; result.Methods.Add(new MethodChange(id, kind, metadata.Name, oldMethod?.Token, newMethod?.Token, oldMethod?.BodyHash, newMethod?.BodyHash, oldMethod?.Shape == newMethod?.Shape, metadata.DeclaringType, metadata.ReturnType, metadata.ParameterTypes, metadata.IsStatic, metadata.HasThis, metadata.IsAbstract, metadata.IsPInvoke, metadata.DeclaringTypeIsValueType, metadata.GenericParameterCount, metadata.DeclaringTypeGenericParameterCount)); if (kind is "added" or "removed" or "tokenChanged" or "shapeChanged" || kind == "changed" && (oldMethod!.Token != newMethod!.Token || oldMethod.Shape != newMethod.Shape)) result.Reasons.Add(kind + ": " + id); }
        var bt = TypesOf(b); var ct = TypesOf(c); foreach (var id in bt.Keys.Union(ct.Keys).OrderBy(x => x, StringComparer.Ordinal)) { bt.TryGetValue(id, out var oldType); ct.TryGetValue(id, out var newType); var kind = oldType is null ? "added" : newType is null ? "removed" : oldType != newType ? "layoutChanged" : "unchanged"; if (kind != "unchanged") { result.TypeChanges.Add(new TypeChange(id, kind, oldType, newType)); result.Reasons.Add("type layout changed: " + id); } }
        return result;
    }
    public object ToJson(bool strict) => new { schemaVersion = 1, format = "hybridclr.dhe-lite.mv.json", generatedAtUtc = DateTimeOffset.UtcNow, assemblyName = AssemblyName, baseline = new { path = BaselinePath, sha256 = BaselineSha256, mvid = BaselineMvid, assemblyRefs = BaselineAssemblyRefs }, current = new { path = CurrentPath, sha256 = CurrentSha256, mvid = CurrentMvid, assemblyRefs = CurrentAssemblyRefs }, methods = Methods, typeChanges = TypeChanges, compatibility = new { mode = strict ? "method-body-only" : "analysis", status = Compatible ? "compatible" : "incompatible", reasons = Reasons }, summary = new { methodCount = Methods.Count, changedMethodCount = ChangedMethodCount, unchangedMethodCount = Methods.Count - ChangedMethodCount, typeChangeCount = TypeChanges.Count, compatibleMethodOnlyChange = Compatible } };
    private static Dictionary<string, MethodInfo> MethodsOf(ModuleDef module) => module.Types.SelectMany(AllTypes).SelectMany(x => x.Methods).ToDictionary(MethodId, MethodInfo.Create, StringComparer.Ordinal);
    private static Dictionary<string, string> TypesOf(ModuleDef module) => module.Types.SelectMany(AllTypes).Where(x => x.Name != "<Module>").ToDictionary(x => x.FullName, TypeShape, StringComparer.Ordinal);
    private static string TypeShape(TypeDef type)
    {
        var fields = type.Fields.Select(field => string.Join(":",
            field.MDToken.Raw.ToString("x8"), field.Name.String, field.FieldType.FullName,
            ((uint)field.Attributes).ToString("x8"), field.FieldOffset.ToString(),
            ConstantShape(field.HasConstant ? field.Constant : null), field.RVA.ToString(), BytesHash(field.InitialValue),
            field.MarshalType?.ToString() ?? "", field.ImplMap?.ToString() ?? "", CustomAttributes(field.CustomAttributes)));
        var genericParameters = type.GenericParameters.Select(GenericParameterShape);
        var properties = type.Properties.Select(property => string.Join(":", property.MDToken.Raw.ToString("x8"), property.Name.String,
            property.Type?.ToString() ?? "", ((uint)property.Attributes).ToString("x8"),
            property.GetMethod?.MDToken.Raw.ToString("x8") ?? "", property.SetMethod?.MDToken.Raw.ToString("x8") ?? "",
            string.Join(",", property.OtherMethods.Select(method => method.MDToken.Raw.ToString("x8"))),
            ConstantShape(property.HasConstant ? property.Constant : null), CustomAttributes(property.CustomAttributes)));
        var events = type.Events.Select(@event => string.Join(":", @event.MDToken.Raw.ToString("x8"), @event.Name.String,
            @event.EventType?.FullName ?? "", ((uint)@event.Attributes).ToString("x8"),
            @event.AddMethod?.MDToken.Raw.ToString("x8") ?? "", @event.RemoveMethod?.MDToken.Raw.ToString("x8") ?? "",
            @event.InvokeMethod?.MDToken.Raw.ToString("x8") ?? "",
            string.Join(",", @event.OtherMethods.Select(method => method.MDToken.Raw.ToString("x8"))),
            CustomAttributes(@event.CustomAttributes)));
        return string.Join("|", type.MDToken.Raw.ToString("x8"), type.BaseType?.FullName,
            ((uint)type.Attributes).ToString("x8"), type.IsValueType,
            type.ClassLayout?.PackingSize.ToString() ?? "", type.ClassLayout?.ClassSize.ToString() ?? "",
            string.Join(",", type.Interfaces.Select(x => string.Join(":", x.MDToken.Raw.ToString("x8"),
                x.Interface.FullName, CustomAttributes(x.CustomAttributes)))),
            string.Join(",", genericParameters), string.Join(",", fields),
            string.Join(",", properties), string.Join(",", events), CustomAttributes(type.CustomAttributes),
            DeclSecurities(type.DeclSecurities));
    }
    private static string GenericParameterShape(GenericParam parameter) => string.Join(":", parameter.Number,
        parameter.MDToken.Raw.ToString("x8"), parameter.Name.String, ((uint)parameter.Flags).ToString("x8"),
        string.Join(",", parameter.GenericParamConstraints.Select(x => x.Constraint.FullName)),
        CustomAttributes(parameter.CustomAttributes));
    private static string CustomAttributes(IEnumerable<CustomAttribute> attributes) => string.Join(",",
        attributes.Select(attribute => string.Join("", attribute.Constructor?.FullName ?? attribute.TypeFullName, "(",
            string.Join(";", attribute.ConstructorArguments.Select(AttributeArgument)), ")",
            "{", string.Join(";", attribute.NamedArguments.Select(argument => string.Join(":",
                argument.IsField ? "field" : "property", argument.Name.String, argument.Type?.FullName ?? "",
                AttributeArgument(argument.Argument)))), "}")));
    private static string AttributeArgument(CAArgument argument) => (argument.Type?.FullName ?? "") + "=" + AttributeValue(argument.Value);
    private static string AttributeValue(object? value)
    {
        if (value == null) return "null";
        if (value is IList<CAArgument> arguments) return "[" + string.Join(",", arguments.Select(AttributeArgument)) + "]";
        if (value is UTF8String utf8) return utf8.String;
        if (value is IType type) return type.FullName;
        return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    }
    private static string ConstantShape(Constant? constant) => constant == null ? "" :
        ((uint)constant.Type).ToString("x8") + ":" + AttributeValue(constant.Value);
    private static string DeclSecurities(IEnumerable<DeclSecurity> securities) => string.Join(",",
        securities.Select(security => string.Join(":", security.MDToken.Raw.ToString("x8"),
            ((uint)security.Action).ToString("x8"), BytesHash(security.GetBlob()), CustomAttributes(security.CustomAttributes))));
    private static string BytesHash(byte[]? bytes) => bytes == null || bytes.Length == 0 ? "" :
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static string ModuleShape(ModuleDef module)
    {
        var assembly = module.Assembly;
        var assemblyShape = assembly == null ? "" : string.Join("|", assembly.FullName,
            ((uint)assembly.Attributes).ToString("x8"), ((uint)assembly.HashAlgorithm).ToString("x8"),
            assembly.PublicKey?.ToString() ?? "", CustomAttributes(assembly.CustomAttributes),
            DeclSecurities(assembly.DeclSecurities));
        var resources = module.Resources.Select(resource => string.Join(":", resource.MDToken.Raw.ToString("x8"),
            resource.Name.String, ((uint)resource.Attributes).ToString("x8"), resource.ResourceType,
            resource is EmbeddedResource embedded ? BytesHash(embedded.CreateReader().ToArray()) : resource.ToString(),
            CustomAttributes(resource.CustomAttributes)));
        var exportedTypes = module.ExportedTypes.Select(type => string.Join(":", type.MDToken.Raw.ToString("x8"),
            type.FullName, ((uint)type.Attributes).ToString("x8"), type.TypeDefId,
            type.Implementation?.ToString() ?? "", CustomAttributes(type.CustomAttributes)));
        return string.Join("|", assemblyShape, module.Name.String, module.Generation, module.EncId, module.EncBaseId,
            module.Kind, module.Characteristics, module.DllCharacteristics, module.RuntimeVersion, module.Machine,
            module.Cor20HeaderFlags, module.Cor20HeaderRuntimeVersion, module.TablesHeaderVersion,
            module.ManagedEntryPoint?.MDToken.Raw.ToString("x8") ?? "", CustomAttributes(module.CustomAttributes),
            string.Join(",", resources), string.Join(",", exportedTypes));
    }
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
            method.MethodSig.ToString() + ":" + method.Attributes + ":" + method.ImplAttributes + ":" +
                string.Join(",", method.GenericParameters.Select(GenericParameterShape)) + ":" +
                string.Join(",", method.ParamDefs.Select(parameter => string.Join("/", parameter.Sequence,
                    parameter.MDToken.Raw.ToString("x8"), parameter.Name.String, parameter.Attributes,
                    ConstantShape(parameter.HasConstant ? parameter.Constant : null),
                    parameter.MarshalType?.ToString() ?? "", CustomAttributes(parameter.CustomAttributes)))) + ":" +
                CustomAttributes(method.CustomAttributes) + ":" + method.ImplMap?.ToString() + ":" +
                string.Join(",", method.Overrides.Select(@override => @override.ToString())) + ":" +
                DeclSecurities(method.DeclSecurities),
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
    private static string BodyHash(MethodDef method) { if (!method.HasBody) return ""; var text = new StringBuilder().Append(method.Body!.MaxStack).Append('|').Append(method.Body.InitLocals).Append('|').Append(method.Body.KeepOldMaxStack); foreach (var local in method.Body.Variables) text.Append("|local:").Append(local.Type.FullName); foreach (var instruction in method.Body.Instructions) text.Append('|').Append(instruction.OpCode.Code).Append(':').Append(Operand(instruction.Operand)); foreach (var handler in method.Body.ExceptionHandlers) text.Append("|eh:").Append(handler.HandlerType).Append(':').Append(handler.CatchType?.FullName).Append(':').Append(handler.TryStart?.Offset).Append(':').Append(handler.TryEnd?.Offset).Append(':').Append(handler.HandlerStart?.Offset).Append(':').Append(handler.HandlerEnd?.Offset).Append(':').Append(handler.FilterStart?.Offset); return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant(); }
    private static string Operand(object? operand)
    {
        if (operand == null) return "";
        if (operand is Instruction instruction) return "target:" + instruction.Offset;
        if (operand is IList<Instruction> targets) return "targets:" + string.Join(",", targets.Select(target => target.Offset));
        if (operand is Local local) return "local:" + local.Index + ":" + local.Type.FullName;
        if (operand is Parameter parameter) return "parameter:" + parameter.Index + ":" + parameter.Type.FullName;
        var fullName = operand.GetType().GetProperty("FullName")?.GetValue(operand)?.ToString();
        return operand.GetType().FullName + ":" + (fullName ?? Convert.ToString(operand, System.Globalization.CultureInfo.InvariantCulture) ?? "");
    }
}

internal sealed record MethodChange(string Id, string Kind, string Name, uint? BaselineToken, uint? CurrentToken, string? BaselineBodySha256, string? CurrentBodySha256, bool ShapeStable, string DeclaringType, string ReturnType, string[] ParameterTypes, bool IsStatic, bool HasThis, bool IsAbstract, bool IsPInvoke, bool DeclaringTypeIsValueType, uint GenericParameterCount, uint DeclaringTypeGenericParameterCount)
{
    public bool TokenStable => BaselineToken == CurrentToken;
}

internal sealed record TypeChange(string Id, string Kind, string? BaselineLayout, string? CurrentLayout);
