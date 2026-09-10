using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;

internal static class FrozenResourceAudit
{
    // Recheck the original Base evidence, not only before/after hashes taken
    // by the update runner. This command never starts or modifies a Player.
    internal static int Run(string[] args)
    {
        if (args.Length < 3 || args.Length > 4)
            throw new ArgumentException("frozen-resource-audit <workflow output> <original Current root> <new audit file> [comma-separated module initializer assembly names]");
        string output = Path.GetFullPath(args[0]), current = Path.GetFullPath(args[1]);
        string[] modules = args.Length == 4 ? args[3].Split(',') : Array.Empty<string>();
        if (modules.Any(string.IsNullOrWhiteSpace) || modules.Distinct(StringComparer.Ordinal).Count() != modules.Length)
            throw new ArgumentException("Expected module initializer names must be distinct and nonempty.");
        if (File.Exists(args[2])) throw new IOException("Audit output must be new.");
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var files = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var checks = new SortedDictionary<string, bool>(StringComparer.Ordinal);
        void Require(string name, bool value)
        {
            checks.Add(name, value);
            if (!value) throw new InvalidDataException("Frozen resource audit failed: " + name);
        }
        void Verify(string path, string expected)
        {
            path = Path.GetFullPath(path);
            string actual = Hash(path);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Frozen resource audit hash mismatch: " + path);
            if (files.TryGetValue(path, out var prior) && prior != actual)
                throw new InvalidDataException("File changed during audit: " + path);
            files[path] = actual;
        }
        string resultPath = Path.Combine(output, "result.json");
        var result = Read(resultPath);
        string validationMode = result.TryGetProperty("validationMode", out var mode) ? mode.GetString()! : "";
        string[] precommitExpected = {
            "bad-current-hash-rejected", "hash-rejection-has-no-native-effects", "native-registration-rejected",
            "native-metadata-prepared-before-rejection", "public-rejection-remains-retryable", "no-publication-or-initializer-effects",
            "old-native-dispatch-preserved", "old-reflection-view-preserved", "pending-new-assemblies-hidden",
            "prepared-native-graph-cannot-reset", "original-mv-restored", "corrected-public-retry-committed"
        };
        string[] unityExpected = {
            "awake-current-value", "on-enable-current-value", "awake-diff-selection", "added-instance-field", "added-component-awake",
            "start-current-value", "update-current-value", "late-update-current-value", "coroutine-resumed-across-frames",
            "on-disable-current-value", "layout-dependent-reader-selection", "layout-dependent-reader-execution", "unaffected-method-stays-aot", "added-reference-survives-gc",
            "disable-not-repeated-on-destroy", "on-destroy-current-value", "real-multiple-frames"
        };
        string[] serializationExpected = {
            "inactive-source-has-no-callback-effects", "json-reads-existing-field", "json-reads-added-field",
            "json-writes-existing-field", "json-writes-added-field", "old-json-preserves-added-field",
            "clone-copies-existing-field", "clone-copies-added-field", "cloned-storage-is-independent",
            "cloned-storage-survives-gc", "fixture-preserves-lifecycle-state"
        };
        Require("workflow-passed", result.GetProperty("passed").GetBoolean() &&
            result.GetProperty("checks").EnumerateObject().All(check => check.Value.GetBoolean()));
        string[] expected = result.GetProperty("expected").EnumerateArray().Select(row => row.GetString()!).ToArray();
        var reference = Read(Path.Combine(output, "reference.json"));
        Require("reference-passed-once", reference.GetProperty("passed").GetBoolean() && reference.GetProperty("revision").GetInt32() == 73 &&
            reference.GetProperty("records").EnumerateArray().Select(row => row.GetString()).SequenceEqual(expected) &&
            expected.Distinct(StringComparer.Ordinal).Count() == expected.Length);
        string resource = Path.Combine(output, "resource");
        string manifestPath = Path.Combine(resource, "dhe-resource-update.json");
        Verify(manifestPath, result.GetProperty("resourceManifestSha256").GetString()!);
        var manifest = Read(manifestPath);
        string currentSet = result.GetProperty("currentAssemblySetSha256").GetString()!;
        Require("single-current-identity", manifest.GetProperty("payloadModel").GetString() == "single-current-payload" &&
            manifest.GetProperty("currentAssemblySetSha256").GetString() == currentSet &&
            manifest.GetProperty("supportedBases").EnumerateArray().All(row => row.GetProperty("currentAssemblySetSha256").GetString() == currentSet));
        string[] originalNames = Directory.GetFiles(current, "*.dll").Select(Path.GetFileNameWithoutExtension).OrderBy(name => name, StringComparer.Ordinal).ToArray()!;
        int? moduleConstant = null, literalNumber = null;
        bool selectedInitializer = false, secondInitializer = false, inlineHotfix = false;
        using (var model = ModuleDefMD.Load(Path.Combine(current, "HybridCLR.ValueLayoutModel.dll")))
        {
            moduleConstant = model.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)?
                .Fields.Single(field => field.Name == "ExpectedVersion").Constant?.Value as int?;
            selectedInitializer = model.GlobalType.Methods.Any(method => method.IsStaticConstructor);
            secondInitializer = model.Find("HybridCLR.Lab.ModuleEvolution.ModuleState", false)?
                .Methods.Any(method => method.Name == "InitializeSecond") == true;
            literalNumber = model.Find("HybridCLR.Lab.ModuleEvolution.LiteralFieldCases", false)?
                .Fields.Single(field => field.Name == "Number").Constant?.Value as int?;
            inlineHotfix = model.Find("HybridCLR.Lab.ModuleEvolution.InlineHotfixCases", false) != null;
        }
        var payloads = manifest.GetProperty("payloadVariants")[0].GetProperty("assemblies").EnumerateArray().ToArray();
        Require("complete-original-current", payloads.Select(row => row.GetProperty("assemblyName").GetString()).OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(originalNames));
        foreach (var row in payloads)
        {
            string name = row.GetProperty("assemblyName").GetString()!;
            string hash = row.GetProperty("dllSha256").GetString()!;
            Verify(Path.Combine(current, name + ".dll"), hash);
            Verify(Path.Combine(output, "current", name + ".dll"), hash);
            Verify(Path.Combine(resource, row.GetProperty("dll").GetString()!), hash);
            Verify(Path.Combine(resource, row.GetProperty("currentMetaVersion").GetString()!), row.GetProperty("currentMetaVersionSha256").GetString()!);
        }
        var bases = new HashSet<string>(StringComparer.Ordinal);
        var moduleInitializerTransitions = new List<object>();
        int index = 0;
        foreach (var player in result.GetProperty("players").EnumerateArray())
        {
            string proof = player.GetProperty("proof").GetString()!, build = Path.Combine(proof, "base");
            var original = Read(Path.Combine(proof, "result.json"));
            Require("base-passed-" + index, original.GetProperty("passed").GetBoolean());
            string identityPath = Path.Combine(build, "build-identity.json");
            Verify(identityPath, original.GetProperty("identitySha256").GetString()!);
            Verify(Path.Combine(build, "player/Snapshot.exe"), original.GetProperty("playerSha256").GetString()!);
            Verify(Path.Combine(build, "player/GameAssembly.dll"), original.GetProperty("gameAssemblySha256").GetString()!);
            Verify(Path.Combine(build, "player/Snapshot.exe"), player.GetProperty("playerSha256").GetString()!);
            Verify(Path.Combine(build, "player/GameAssembly.dll"), player.GetProperty("gameAssemblySha256").GetString()!);
            Verify(Path.Combine(output, "player-" + index + ".json"), player.GetProperty("resultSha256").GetString()!);
            var identity = Read(identityPath);
            string baseId = identity.GetProperty("baseId").GetString()!;
            Require("distinct-base-" + index, bases.Add(baseId));
            string snapshotPath = Path.Combine(build, identity.GetProperty("aotAnalysisSnapshot").GetString()!);
            Verify(snapshotPath, original.GetProperty("snapshotManifestSha256").GetString()!);
            Verify(snapshotPath, identity.GetProperty("aotAnalysisSnapshotSha256").GetString()!);
            foreach (var source in Read(snapshotPath).GetProperty("assemblies").EnumerateArray())
            {
                string sourcePath = Path.Combine(Path.GetDirectoryName(snapshotPath)!, source.GetProperty("file").GetString()!);
                Verify(sourcePath, source.GetProperty("sha256").GetString()!);
                if (source.GetProperty("assemblyName").GetString() == "HybridCLR.ValueLayoutModel")
                {
                    using var baseModule = ModuleDefMD.Load(sourcePath);
                    bool baseInitializer = baseModule.GlobalType.Methods.Any(method => method.IsStaticConstructor);
                    moduleInitializerTransitions.Add(new { baseId, baseModuleInitializer = baseInitializer,
                        currentModuleInitializer = selectedInitializer });
                }
            }
            index++;
        }
        Require("exact-base-set", bases.SetEquals(manifest.GetProperty("supportedBases").EnumerateArray().Select(row => row.GetProperty("baseId").GetString()!)));
        Require("module-transition-per-base", moduleInitializerTransitions.Count == bases.Count);
        int successfulRuns = 0, rejectedRuns = 0;
        foreach (string reportPath in Directory.GetFiles(output, "player-*.json").OrderBy(path => path, StringComparer.Ordinal))
        {
            var report = Read(reportPath);
            string[] log = File.ReadAllLines(reportPath + ".log");
            string[] begun = log.Where(line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal))
                .Select(line => line.Substring(16)).ToArray();
            string key = Path.GetFileNameWithoutExtension(reportPath);
            Require(key + "-known-base", bases.Contains(report.GetProperty("baseId").GetString()!));
            if (moduleConstant.HasValue)
                Require(key + "-no-early-hotfix-module", report.GetProperty("moduleRunsBeforeLoad").GetInt32() == 0);
            if (report.TryGetProperty("ordinaryModuleRunsBeforeLoad", out var ordinaryBefore) && ordinaryBefore.GetInt32() >= 0)
                Require(key + "-ordinary-module-eager-once", ordinaryBefore.GetInt32() == 1);
            if (report.GetProperty("passed").GetBoolean())
            {
                void ProbeSequence(string property, string prefix, string[] sequence)
                {
                    Require(key + "-" + property + "-exact-sequence", report.GetProperty(property).EnumerateArray()
                        .Select(row => row.GetString()).SequenceEqual(sequence) &&
                        log.Where(line => line.StartsWith(prefix, StringComparison.Ordinal)).Select(line => line.Substring(prefix.Length)).SequenceEqual(sequence));
                }
                if (validationMode.Contains("precommit"))
                {
                    ProbeSequence("precommitChecks", "DHE public precommit check: ", precommitExpected);
                    Require(key + "-retry-before-business-entry", Array.FindIndex(log, line => line == "DHE public precommit check: corrected-public-retry-committed") <
                        Array.FindIndex(log, line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)));
                }
                if (validationMode.Contains("unity"))
                {
                    ProbeSequence("unityChecks", "DHE Unity check: ", unityExpected);
                    Require(key + "-real-unity-completed", report.GetProperty("stage").GetString() == "unity-component-complete" &&
                        log.Count(line => line == "DHE Unity component pass: 2:17") == 1 &&
                        Array.FindLastIndex(log, line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)) <
                        Array.FindIndex(log, line => line == "DHE Unity check: awake-current-value"));
                }
                if (validationMode.Contains("serialization"))
                {
                    const string prefix = "DHE Unity serialization check: ";
                    Require(key + "-serialization-exact-sequence", log.Where(line => line.StartsWith(prefix, StringComparison.Ordinal))
                        .Select(line => line.Substring(prefix.Length)).SequenceEqual(serializationExpected) &&
                        log.Count(line => line == "DHE Unity serialization pass: 11") == 1);
                    Require(key + "-serialization-after-business-cases", Array.FindLastIndex(log,
                        line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)) <
                        Array.FindIndex(log, line => line == prefix + serializationExpected[0]));
                }
                Require(key + "-complete-sequence", begun.SequenceEqual(expected) && report.GetProperty("revision").GetInt32() == 73 &&
                    report.GetProperty("loadedAssemblies").GetInt32() == originalNames.Length);
                if (moduleConstant.HasValue)
                {
                    Require(key + "-current-constant-reflection", new[] { "moduleConstantAfterLoad", "moduleConstantFresh", "moduleConstantValue" }
                        .All(name => report.GetProperty(name).GetInt32() == moduleConstant.Value));
                    string[] starts = log.Where(line => line.StartsWith("DHE selected module: ", StringComparison.Ordinal)).ToArray();
                    Require(key + "-selected-initializer-once", selectedInitializer
                        ? starts.SequenceEqual(new[] { "DHE selected module: " + moduleConstant.Value + ":1" }) : starts.Length == 0);
                    Require(key + "-module-entry-verification", log.Count(line => line == "DHE module evolution pass: " +
                        (selectedInitializer ? moduleConstant.Value + ":1" : "0:0")) == 1);
                    if (secondInitializer)
                        Require(key + "-second-module-initializer-once", log.Count(line => line == "DHE second AOT module initializer: 1") == 1);
                }
                if (literalNumber.HasValue)
                    Require(key + "-literal-reflection-suite", log.Count(line => line == "DHE literal reflection pass: " + literalNumber.Value + ":74") == 1);
                if (inlineHotfix)
                {
                    string[] inlineRecords = log.Where(line => line.StartsWith("DHE inline hotfix pass: ", StringComparison.Ordinal)).ToArray();
                    Require(key + "-inline-hotfix-measured-once", inlineRecords.Length == 1);
                    string[] counts = inlineRecords[0].Substring("DHE inline hotfix pass: ".Length).Split(':');
                    // The resource fixture updates only the callee (17 -> 18).
                    // Keep its unchanged AOT caller and one interpreted entry
                    // as independent evidence beyond the general case sequence.
                    Require(key + "-inline-hotfix-current-with-aot-caller", counts.Length == 3 && counts[0] == "18" &&
                        int.TryParse(counts[1], out int aotEntries) && aotEntries >= 2 && counts[2] == "1");
                }
                if (report.TryGetProperty("ordinaryModuleRunsAfterLoad", out var ordinaryAfter) && ordinaryBefore.ValueKind != JsonValueKind.Undefined && ordinaryBefore.GetInt32() >= 0)
                    Require(key + "-ordinary-module-not-reinitialized", ordinaryAfter.GetInt32() == 1);
                if (modules.Length != 0)
                {
                    string[] moduleBegins = log.Where(line => line.StartsWith("DHE module begin: ", StringComparison.Ordinal)).Select(line => line.Substring(18)).ToArray();
                    string[] modulePasses = log.Where(line => line.StartsWith("DHE module pass: ", StringComparison.Ordinal)).Select(line => line.Substring(17)).ToArray();
                    Require(key + "-complete-module-initialization", moduleBegins.SequenceEqual(modulePasses) &&
                        modulePasses.OrderBy(name => name, StringComparer.Ordinal).SequenceEqual(modules.OrderBy(name => name, StringComparer.Ordinal)) &&
                        Array.FindLastIndex(log, line => line.StartsWith("DHE module pass: ", StringComparison.Ordinal)) <
                        Array.FindIndex(log, line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)));
                }
                successfulRuns++;
            }
            else
            {
                if (validationMode.Contains("unity"))
                    Require(key + "-no-unity-entry", !log.Any(line => line.StartsWith("DHE Unity check: ", StringComparison.Ordinal)));
                Require(key + "-no-business-entry", begun.Length == 0 && report.GetProperty("revision").GetInt32() == 0 &&
                    report.GetProperty("loadedAssemblies").GetInt32() == 0);
                if (modules.Length != 0)
                    Require(key + "-no-module-side-effects", !log.Any(line => line.StartsWith("DHE module begin: ", StringComparison.Ordinal)));
                if (moduleConstant.HasValue)
                    Require(key + "-no-hotfix-module-side-effects", !log.Any(line => line.StartsWith("DHE selected module: ", StringComparison.Ordinal) ||
                        line.StartsWith("DHE second AOT module initializer: ", StringComparison.Ordinal)));
                if (inlineHotfix)
                    Require(key + "-no-inline-business-entry", !log.Any(line => line.StartsWith("DHE inline hotfix pass: ", StringComparison.Ordinal)));
                rejectedRuns++;
            }
            files[Path.GetFullPath(reportPath)] = Hash(reportPath);
            files[Path.GetFullPath(reportPath + ".log")] = Hash(reportPath + ".log");
        }
        Require("successful-run-per-base", successfulRuns >= bases.Count);
        File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed = true, workflowResultSha256 = Hash(resultPath),
            auditHostSha256 = Hash(typeof(FrozenResourceAudit).Assembly.Location),
            caseCount = expected.Length, baseCount = bases.Count, successfulRuns, rejectedRuns, expectedModuleInitializers = modules,
            moduleInitializerTransitions, validationMode,
            checks, rehashedFileCount = files.Count, files,
            scope = "Independent read-only original Base/Current identity and full successful/restored case-sequence audit; no performance or platform extrapolation"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Frozen resource audit passed: {bases.Count} Bases, {expected.Length} cases, {successfulRuns} successful runs, {files.Count} files.");
        return 0;
    }
}
