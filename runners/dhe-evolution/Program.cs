using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HybridCLR.Lab;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        IncludeFields = true,
    };

    private static readonly string[] RequiredChecks =
    {
        "passed", "resourceUpdateValidated", "buildIdentityValidated", "mvValidated",
        "multiAssemblyValidated", "dispatchProbeValidated", "capabilityPassed",
        "capabilityDirectPassed", "structuralExpected", "structuralDispatchExpected",
        "structuralPassed", "structuralAddedReferenceTypeFound", "structuralAddedGenericTypeFound",
        "structuralAddedNestedTypeFound", "structuralNestedDeclaringTypeValidated",
        "structuralAddedStaticMethodFound", "structuralAddedInstanceMethodFound",
        "structuralAddedStaticFieldsFound", "structuralAddedStaticFieldDeclaringTypeValidated",
        "structuralAddedStaticFieldReflectionValueValidated", "structuralAddedInstanceFieldsFound",
        "structuralAddedInstanceFieldDeclaringTypeValidated", "structuralAddedInstanceFieldDefaultValueValidated",
        "structuralAddedInstanceFieldReflectionValueValidated", "structuralAddedInstanceFieldGcValidated",
        "structuralRemovedMethodHidden", "structuralRemovedMethodGuardValidated",
        "structuralRemovedFieldsHidden", "structuralRemovedFieldGuardValidated",
        "structuralFieldSignatureReplacementVisible", "structuralFieldSignatureReplacementRoundTripValidated",
        "structuralLogicalPropertiesValidated", "structuralLogicalPropertyRoundTripValidated",
        "structuralLogicalEventsValidated", "structuralLogicalEventRoundTripValidated",
        "structuralLogicalEventAccessorsValidated", "structuralRemovedPropertyGuardValidated",
        "structuralReplacedPropertyGuardValidated", "structuralRemovedEventGuardValidated",
        "structuralReplacedEventGuardValidated", "structuralRemovedTypeHidden",
        "structuralRemovedTypeEnumerationHidden", "structuralRemovedTypeGuardValidated",
        "structuralOldSignatureHidden", "structuralOldSignatureGuardValidated", "structuralNewSignatureFound",
        "structuralAssemblyEnumerationValidated", "structuralTypeAssemblyMatchesBase",
    };

    private static async Task<int> Main(string[] args)
    {
        if (args.Length != 1) throw new ArgumentException("Pass the evolution replay configuration JSON path.");
        string configPath = Path.GetFullPath(args[0]);
        string configText = File.ReadAllText(configPath);
        Config config = JsonSerializer.Deserialize<Config>(configText, JsonOptions) ??
            throw new InvalidDataException("Missing replay configuration.");
        string Resolve(string path) => Path.GetFullPath(Path.Combine(Path.GetDirectoryName(configPath)!, path));
        string output = Resolve(config.OutputRoot);
        string tool = Resolve(config.ToolAssembly);
        string lab = Resolve(config.LabRoot);
        string[] updates = config.Updates.Select(Resolve).ToArray();
        string[] references = (config.ReferenceResults ?? Array.Empty<string>()).Select(Resolve).ToArray();
        string[] differentialReferences = (config.DifferentialReferences ?? Array.Empty<string>()).Select(Resolve).ToArray();
        bool[] interpretedEntries = config.RequireInterpretedCaseEntries ?? Array.Empty<bool>();
        bool differentialRequested = differentialReferences.Length != 0 || interpretedEntries.Length != 0 ||
            config.DifferentialManifest != null || config.DifferentialGolden != null;
        Require(!differentialRequested || (differentialReferences.Length == updates.Length &&
            interpretedEntries.Length == updates.Length && !string.IsNullOrWhiteSpace(config.DifferentialManifest) &&
            !string.IsNullOrWhiteSpace(config.DifferentialGolden)), "Full differential configuration is incomplete.");
        Require(references.Length == 0 || references.Length == updates.Length,
            "Each resource update requires one matching CLR reference result.");
        Require(!config.RequireStructuralBaseGenerations || references.Length == updates.Length,
            "Structural generation qualification requires observable CLR results for every update.");
        string[][] checkRequirements = config.EvolutionCheckRequirements ?? Array.Empty<string[]>();
        Require(checkRequirements.Length == 0 || (checkRequirements.Length == updates.Length &&
            checkRequirements.All(checks => checks != null && checks.Distinct(StringComparer.Ordinal).Count() == checks.Length &&
                checks.All(check => Regex.IsMatch(check, "^[a-z0-9-]+$")))),
            "Evolution check requirements must match the updates and contain unique check names.");
        if (updates.Length < 2 || config.Bases.Length < 2 || config.TimeoutSeconds < 1 ||
            config.TimeoutSeconds > 600 || config.Bases.Select(item => item.Label).Distinct().Count() != config.Bases.Length)
            throw new InvalidDataException("Replay requires multiple updates, unique Base labels, and a bounded timeout.");
        foreach (string input in updates.Concat(config.Bases.Select(item => Resolve(item.PlayerRoot))).Append(lab))
        {
            Require(!Within(output, input) && !Within(input, output), "Replay output overlaps an input tree.");
        }
        Require(!File.Exists(output) && (!Directory.Exists(output) || !Directory.EnumerateFileSystemEntries(output).Any()),
            "Replay output must be a new or empty directory.");
        foreach (Base item in config.Bases)
            Require(Regex.IsMatch(item.Label, "^[a-z0-9-]+$"), "Invalid Base label.");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "config.json"), configText);
        string sourceHead = (await Run("git", new[] { "-C", lab, "rev-parse", "HEAD" }, lab, 30)).Text.Trim();
        string sourceTree = (await Run("git", new[] { "-C", lab, "rev-parse", "HEAD^{tree}" }, lab, 30)).Text.Trim();
        string sourceChanges = (await Run("git", new[] { "-C", lab, "status", "--porcelain" }, lab, 30)).Text.Trim();
        var results = new List<object>();
        var failedRuns = new List<object>();
        var errors = new List<string>();
        int distinctBaseCount = 0;
        var processIds = new HashSet<int>();
        var baseGenerations = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var referenceRecords = new List<object>();
        try
        {
            var observableGenerations = new HashSet<int>();
            for (int index = 0; index < references.Length; index++)
            {
                JsonElement reference = Read(references[index]);
                JsonElement manifest = Read(Path.Combine(updates[index], "dhe-resource-update.json"));
                Require(reference.GetProperty("passed").GetBoolean() &&
                    reference.GetProperty("format").GetString() == "hybridclr.dhe-capability-reference.json",
                    "Missing passing CLR reference result.");
                foreach (JsonElement variant in manifest.GetProperty("payloadVariants").EnumerateArray())
                {
                    JsonElement main = variant.GetProperty("assemblies").EnumerateArray().Single(value =>
                        value.GetProperty("assemblyName").GetString() == "HybridCLR.ManagedCasesAot");
                    Require(string.Equals(reference.GetProperty("inputSha256").GetString(),
                        main.GetProperty("dllSha256").GetString(), StringComparison.OrdinalIgnoreCase),
                        "CLR reference does not execute the exact current DLL.");
                }
                JsonElement observations = reference.GetProperty("observations");
                Require(DheObservedResults.Fields.All(name => observations.TryGetProperty(name, out _)),
                    "CLR reference lacks required observations.");
                Require(observableGenerations.Add(observations.GetProperty("addResult").GetInt32()),
                    "Consecutive resources must expose different observable generations.");
                referenceRecords.Add(new { update = index + 1, path = references[index],
                    sha256 = Hash(references[index]), observations });
            }
            var configuredIds = config.Bases.Select(item => Read(Resolve(item.BuildIdentity))
                .GetProperty("baseId").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
            distinctBaseCount = configuredIds.Count;
            foreach (string update in updates)
            {
                JsonElement manifest = Read(Path.Combine(update, "dhe-resource-update.json"));
                var supportedIds = manifest.GetProperty("supportedBases").EnumerateArray()
                    .Select(item => item.GetProperty("baseId").GetString()!).ToHashSet(StringComparer.OrdinalIgnoreCase);
                Require(supportedIds.SetEquals(configuredIds), "Replay must cover every supported Base.");
            }
            foreach (Base item in config.Bases)
            {
                string baseRoot = Path.Combine(output, item.Label);
                string playerRoot = Path.Combine(baseRoot, "player");
                CopyTree(Resolve(item.PlayerRoot), playerRoot);
                string identityPath = Resolve(item.BuildIdentity);
                JsonElement identity = Read(identityPath);
                Require(identity.GetProperty("target").GetString() == "StandaloneWindows64",
                    "This runner only qualifies Windows execution.");
                string baseId = identity.GetProperty("baseId").GetString()!;
                string assets = Path.Combine(playerRoot, "HybridCLRLab_Data", "StreamingAssets", "HybridCLRLab", "DheDemo");
                string executable = Path.Combine(playerRoot, "HybridCLRLab.exe");
                string embeddedBase = Path.Combine(assets, "BaseMetaVersion");
                string[] immutableFiles = Directory.GetFiles(playerRoot).Where(path =>
                        Path.GetExtension(path) is ".exe" or ".dll")
                    .Concat(Directory.GetFiles(embeddedBase, "*", SearchOption.AllDirectories))
                    .Append(identityPath).ToArray();
                var originalHashes = immutableFiles.ToDictionary(path => path, Hash);
                var baseMv = DheFixtureMetaVersion.Read(File.ReadAllBytes(Path.Combine(embeddedBase,
                    "HybridCLR.ManagedCasesAot.mv.bytes")), "HybridCLR.ManagedCasesAot");
                baseGenerations[baseId] = DheFixturePolicy.HasStructuralFixture(baseMv);
                for (int index = item.SkipFirstUpdate ? 1 : 0; index < updates.Length; index++)
                {
                    string runRoot = Path.Combine(baseRoot, "update-" + (index + 1));
                    Directory.CreateDirectory(runRoot);
                    string stagePath = Path.Combine(runRoot, "stage.json");
                    string resultPath = Path.Combine(runRoot, "player.json");
                    string logPath = Path.Combine(runRoot, "player.log");
                    string evolutionEvidencePath = Path.Combine(runRoot, "evolution-checks.ids");
                    string differentialPath = Path.Combine(runRoot, "differential.bin");
                    string caseEntryRoot = Path.Combine(runRoot, "case-entries");
                    ProcessResult? attempt = null;
                    try
                    {
                        await Run("dotnet", new[] { tool, "stage-resource-update", "-UpdateRoot", updates[index],
                        "-AssetRoot", assets, "-BaseBuildIdentity", identityPath,
                        "-ImmutableFiles", string.Join(',', immutableFiles), "-Output", stagePath }, lab, config.TimeoutSeconds);
                        var environment = new Dictionary<string, string> { ["HYBRIDCLR_DHE_EVOLUTION_EVIDENCE"] = evolutionEvidencePath };
                        if (differentialReferences.Length != 0)
                        {
                            environment["HYBRIDCLR_DHE_DIFFERENTIAL_RESULT"] = differentialPath;
                            if (interpretedEntries[index]) environment["HYBRIDCLR_DHE_CASE_ENTRIES"] = caseEntryRoot;
                        }
                        ProcessResult process = await Run(executable, new[] { "-batchmode", "-nographics", "-labMode", "dhe",
                        "-labTarget", "StandaloneWindows64", "-labResult", resultPath, "-logFile", logPath }, playerRoot, config.TimeoutSeconds,
                            environment);
                        attempt = process;
                        Require(processIds.Add(process.Id), "Player process IDs must be unique.");
                        string[] requiredEvolutionChecks = checkRequirements.Length == 0 ? Array.Empty<string>() : checkRequirements[index];
                        string[] executedEvolutionChecks = DheEvolutionEvidence.Validate(evolutionEvidencePath, requiredEvolutionChecks);
                        JsonElement result = Read(resultPath);
                        if (references.Length != 0)
                            DheObservedResults.Validate(Read(references[index]).GetProperty("observations"), result);
                        Require(result.GetProperty("target").GetString() == "StandaloneWindows64" &&
                            result.GetProperty("engineWorkflow").GetString() == identity.GetProperty("engineWorkflow").GetString(),
                            "Player platform/engine does not match the archived Base.");
                        var legacyNames = DheFixturePolicy.LegacyProbes.Select(probe => probe.Check).ToHashSet(StringComparer.Ordinal);
                        string[] mandatoryChecks = RequiredChecks.Where(name => !legacyNames.Contains(name) &&
                            name != "structuralDispatchExpected").ToArray();
                        string[] failed = mandatoryChecks.Where(name => !result.TryGetProperty(name, out JsonElement value) ||
                            value.ValueKind != JsonValueKind.True).ToArray();
                        Require(failed.Length == 0, item.Label + ": missing/false checks: " + string.Join(",", failed));
                        Require(result.GetProperty("selectedBaseId").GetString() == baseId, "Player selected the wrong Base.");
                        JsonElement manifest = Read(Path.Combine(updates[index], "dhe-resource-update.json"));
                        JsonElement variant = manifest.GetProperty("payloadVariants").EnumerateArray().Single(value =>
                            value.GetProperty("variantId").GetString() == result.GetProperty("selectedPayloadVariantId").GetString());
                        JsonElement mainAssembly = variant.GetProperty("assemblies").EnumerateArray().Single(value =>
                            value.GetProperty("assemblyName").GetString() == "HybridCLR.ManagedCasesAot");
                        object? differential = null;
                        if (differentialReferences.Length != 0)
                        {
                            JsonElement caseAssembly = variant.GetProperty("assemblies").EnumerateArray().Single(value =>
                                value.GetProperty("assemblyName").GetString() == "HybridCLR.ManagedCases");
                            string caseDll = Path.Combine(updates[index], caseAssembly.GetProperty("dll").GetString()!);
                            differential = DheDifferentialEvidence.Validate(differentialReferences[index], differentialPath,
                                Resolve(config.DifferentialManifest!), Resolve(config.DifferentialGolden!), caseDll,
                                File.ReadAllBytes(Path.Combine(embeddedBase, "HybridCLR.ManagedCases.mv.bytes")),
                                caseEntryRoot, interpretedEntries[index]);
                        }
                        var currentMv = DheFixtureMetaVersion.Read(File.ReadAllBytes(Path.Combine(updates[index],
                            mainAssembly.GetProperty("currentMetaVersion").GetString()!)), "HybridCLR.ManagedCasesAot");
                        bool stableChanged = DheFixturePolicy.MethodChanged(baseMv, currentMv,
                            DheFixturePolicy.Calculator + "::Stable|System.Int32 (System.Int32)");
                        bool instanceStableChanged = DheFixturePolicy.MethodChanged(baseMv, currentMv,
                            DheFixturePolicy.Calculator + "::InstanceStable|System.Int32 (System.Int32)");
                        bool staticStableMatches;
                        if (result.TryGetProperty("stableMethodChanged", out JsonElement actualStable))
                        {
                            staticStableMatches = actualStable.GetBoolean() == stableChanged &&
                                result.GetProperty("instanceStableMethodChanged").GetBoolean() == instanceStableChanged;
                        }
                        else
                        {
                            // Archived reports name a different AOT control "unchangedMethod".
                            // Their changed caller records Stable's actual dispatch explicitly.
                            bool callerChanged = DheFixturePolicy.MethodChanged(baseMv, currentMv,
                                DheFixturePolicy.Calculator + "::AddViaStable|System.Int32 (System.Int32)");
                            staticStableMatches = callerChanged && result.GetProperty("changedCallingUnchangedMethod").GetString() ==
                                (stableChanged ? "interpreter + interpreter callee" : "interpreter + AOT callee");
                        }
                        Require(result.GetProperty("structuralDispatchExpected").GetBoolean() == (stableChanged || instanceStableChanged) &&
                            staticStableMatches &&
                            result.GetProperty("unchangedInstanceMethod").GetString() == (instanceStableChanged ? "interpreter" : "aot"),
                            "Stable dispatch disagrees with actual Base/Current MV.");
                        DheFixturePolicy.LegacyProbeResult[] legacyEvidence;
                        if (result.TryGetProperty("structuralLegacyProbes", out JsonElement probes) && probes.ValueKind == JsonValueKind.Array)
                        {
                            legacyEvidence = JsonSerializer.Deserialize<DheFixturePolicy.LegacyProbeResult[]>(probes.GetRawText(), JsonOptions)!;
                            DheFixturePolicy.ValidateLegacyEvidence(baseMv, currentMv, legacyEvidence);
                            Require(legacyEvidence.All(probe => result.GetProperty(probe.check).GetBoolean() == probe.passed),
                                "Flat legacy check disagrees with its execution evidence.");
                        }
                        else
                        {
                            Require(DheFixturePolicy.LegacyProbes.All(probe => baseMv.methods.ContainsKey(probe.StableId) &&
                                !currentMv.methods.ContainsKey(probe.StableId) && result.GetProperty(probe.Check).GetBoolean()),
                                "Archived Player lacks applicable legacy probe evidence.");
                            legacyEvidence = DheFixturePolicy.LegacyProbes.Select(probe => new DheFixturePolicy.LegacyProbeResult
                            {
                                check = probe.Check,
                                methodStableId = probe.StableId,
                                applicable = true,
                                executed = true,
                                passed = true,
                                reason = "removed-from-base",
                            }).ToArray();
                        }
                        Require(result.GetProperty("selectedPayloadCurrentAssemblySetSha256").GetString() ==
                            variant.GetProperty("currentAssemblySetSha256").GetString(), "Player selected the wrong current payload.");
                        Require(result.GetProperty("interpreterEntryCount").GetInt32() > 0 &&
                            result.GetProperty("aotEntryCount").GetInt32() > 0, "Both execution paths must be exercised.");
                        Require(originalHashes.All(pair => Hash(pair.Key) == pair.Value), "Player modified immutable Base files.");
                        results.Add(new
                        {
                            label = item.Label,
                            baseId,
                            update = index + 1,
                            skippedFirstUpdate = item.SkipFirstUpdate,
                            process.Id,
                            process.ExitCode,
                            process.ElapsedMilliseconds,
                            resultPath,
                            resultSha256 = Hash(resultPath),
                            logPath,
                            logSha256 = Hash(logPath),
                            stagePath,
                            stageSha256 = Hash(stagePath),
                            manifestSha256 = Hash(Path.Combine(updates[index], "dhe-resource-update.json")),
                            currentAssemblySetSha256 = manifest.GetProperty("currentAssemblySetSha256").GetString(),
                            changedMethodCount = result.GetProperty("changedMethodCount").GetInt32(),
                            assertionCount = mandatoryChecks.Length + 1 + legacyEvidence.Count(probe => probe.applicable),
                            skippedAssertionCount = legacyEvidence.Count(probe => !probe.applicable),
                            legacyEvidence,
                            baseContainsStructuralFixture = baseGenerations[baseId],
                            immutableFileCount = immutableFiles.Length,
                            immutableHashes = originalHashes,
                            passed = true,
                            referenceValidated = references.Length != 0,
                            differential,
                            requiredEvolutionChecks,
                            executedEvolutionChecks,
                            evolutionEvidencePath,
                            evolutionEvidenceSha256 = File.Exists(evolutionEvidencePath) ? Hash(evolutionEvidencePath) : null,
                            mainObservations = DheObservedResults.Fields.ToDictionary(name => name,
                                name => result.GetProperty(name).GetInt32()),
                        });
                        Console.WriteLine(item.Label + " update " + (index + 1) + ": " +
                            (mandatoryChecks.Length + 1 + legacyEvidence.Count(probe => probe.applicable)) + " checks passed, " +
                            legacyEvidence.Count(probe => !probe.applicable) + " not applicable");
                    }
                    catch (Exception exception)
                    {
                        string error = item.Label + " update " + (index + 1) + ": " + exception;
                        errors.Add(error);
                        var artifacts = new[] { stagePath, resultPath, logPath, differentialPath,
                            differentialPath + ".exceptions.log", evolutionEvidencePath }
                            .Where(File.Exists).ToDictionary(path => path, Hash);
                        bool immutable = originalHashes.All(pair => File.Exists(pair.Key) && Hash(pair.Key) == pair.Value);
                        failedRuns.Add(new
                        {
                            label = item.Label,
                            baseId,
                            update = index + 1,
                            skippedFirstUpdate = item.SkipFirstUpdate,
                            process = attempt,
                            passed = false,
                            error,
                            artifacts,
                            immutableFilesUnchanged = immutable,
                            immutableHashes = originalHashes
                        });
                        Console.Error.WriteLine(item.Label + " update " + (index + 1) + ": " + exception.Message);
                        Require(immutable, "Stop replay: failed run modified immutable Base files.");
                    }
                }
            }
            if (config.RequireStructuralBaseGenerations)
                Require(baseGenerations.Values.Contains(false) && baseGenerations.Values.Contains(true),
                    "Replay must include an original Base and a Base already containing structural evolution.");
        }
        catch (Exception exception)
        {
            errors.Add(exception.ToString());
            Console.Error.WriteLine(exception.Message);
        }
        File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
        {
            format = "hybridclr.dhe-evolution-smoke.json", schemaVersion = 1,
            generatedAtUtc = DateTimeOffset.UtcNow, passed = errors.Count == 0,
            scope = "Windows structural resource replay; not production qualification",
            distinctBaseCount,
            baseGenerations, requiredStructuralBaseGenerations = config.RequireStructuralBaseGenerations,
            sourceHead, sourceTree, sourceChanges, runnerSha256 = Hash(typeof(Program).Assembly.Location),
            referenceRecords,
            toolSha256 = Hash(tool), configSha256 = Hash(configPath), requiredChecks = RequiredChecks,
            results, failedRuns, errors,
        }, JsonOptions));
        return errors.Count == 0 ? 0 : 1;
    }

    private static async Task<ProcessResult> Run(string executable, IEnumerable<string> arguments,
        string directory, int timeoutSeconds, IReadOnlyDictionary<string, string>? environment = null)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        start.Environment.Remove("HYBRIDCLR_DHE_DIFFERENTIAL_RESULT");
        start.Environment.Remove("HYBRIDCLR_DHE_CASE_ENTRIES");
        start.Environment.Remove("HYBRIDCLR_DHE_EVOLUTION_EVIDENCE");
        if (environment != null)
            foreach (var entry in environment) start.Environment[entry.Key] = entry.Value;
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Process start failed.");
        var watch = Stopwatch.StartNew();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try { await process.WaitForExitAsync(cancellation.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Process timed out: " + executable);
        }
        string text = await stdout + await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(executable + " exited with " + process.ExitCode + ": " + text);
        return new ProcessResult(process.Id, process.ExitCode, watch.ElapsedMilliseconds, text);
    }

    private static void CopyTree(string source, string destination)
    {
        Require((File.GetAttributes(source) & FileAttributes.ReparsePoint) == 0, "Replay input contains a reparse point.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            Require((File.GetAttributes(file) & FileAttributes.ReparsePoint) == 0, "Replay input contains a reparse point.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.GetDirectories(source))
            CopyTree(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static bool Within(string path, string root) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
    private static string Hash(string path)
    {
        using FileStream stream = File.OpenRead(path);
        using SHA256 sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
    private sealed record Config(string LabRoot, string ToolAssembly, string OutputRoot, string[] Updates, Base[] Bases,
        int TimeoutSeconds = 120, bool RequireStructuralBaseGenerations = false, string[]? ReferenceResults = null,
        string[][]? EvolutionCheckRequirements = null, string[]? DifferentialReferences = null,
        bool[]? RequireInterpretedCaseEntries = null, string? DifferentialManifest = null, string? DifferentialGolden = null);
    private sealed record Base(string Label, string PlayerRoot, string BuildIdentity, bool SkipFirstUpdate = false);
    private sealed record ProcessResult(int Id, int ExitCode, long ElapsedMilliseconds, string Text);
}
