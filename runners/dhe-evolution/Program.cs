using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

internal static class Program
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
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
        var errors = new List<string>();
        int distinctBaseCount = 0;
        try
        {
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
                for (int index = item.SkipFirstUpdate ? 1 : 0; index < updates.Length; index++)
                {
                    string runRoot = Path.Combine(baseRoot, "update-" + (index + 1));
                    Directory.CreateDirectory(runRoot);
                    string stagePath = Path.Combine(runRoot, "stage.json");
                    string resultPath = Path.Combine(runRoot, "player.json");
                    string logPath = Path.Combine(runRoot, "player.log");
                    await Run("dotnet", new[] { tool, "stage-resource-update", "-UpdateRoot", updates[index],
                        "-AssetRoot", assets, "-BaseBuildIdentity", identityPath,
                        "-ImmutableFiles", string.Join(',', immutableFiles), "-Output", stagePath }, lab, config.TimeoutSeconds);
                    ProcessResult process = await Run(executable, new[] { "-batchmode", "-nographics", "-labMode", "dhe",
                        "-labTarget", "StandaloneWindows64", "-labResult", resultPath, "-logFile", logPath }, playerRoot, config.TimeoutSeconds);
                    JsonElement result = Read(resultPath);
                    Require(result.GetProperty("target").GetString() == "StandaloneWindows64" &&
                        result.GetProperty("engineWorkflow").GetString() == identity.GetProperty("engineWorkflow").GetString(),
                        "Player platform/engine does not match the archived Base.");
                    string[] failed = RequiredChecks.Where(name => !result.TryGetProperty(name, out JsonElement value) ||
                        value.ValueKind != JsonValueKind.True).ToArray();
                    Require(failed.Length == 0, item.Label + ": missing/false checks: " + string.Join(",", failed));
                    Require(result.GetProperty("selectedBaseId").GetString() == baseId, "Player selected the wrong Base.");
                    JsonElement manifest = Read(Path.Combine(updates[index], "dhe-resource-update.json"));
                    Require(result.GetProperty("selectedPayloadCurrentAssemblySetSha256").GetString() ==
                        manifest.GetProperty("currentAssemblySetSha256").GetString(), "Player selected the wrong current payload.");
                    Require(result.GetProperty("interpreterEntryCount").GetInt32() > 0 &&
                        result.GetProperty("aotEntryCount").GetInt32() > 0, "Both execution paths must be exercised.");
                    Require(originalHashes.All(pair => Hash(pair.Key) == pair.Value), "Player modified immutable Base files.");
                    results.Add(new
                    {
                        label = item.Label, baseId, update = index + 1, skippedFirstUpdate = item.SkipFirstUpdate,
                        process.Id, process.ExitCode, process.ElapsedMilliseconds,
                        resultPath, resultSha256 = Hash(resultPath), logPath, logSha256 = Hash(logPath),
                        stagePath, stageSha256 = Hash(stagePath),
                        manifestSha256 = Hash(Path.Combine(updates[index], "dhe-resource-update.json")),
                        currentAssemblySetSha256 = manifest.GetProperty("currentAssemblySetSha256").GetString(),
                        changedMethodCount = result.GetProperty("changedMethodCount").GetInt32(),
                        assertionCount = RequiredChecks.Length, immutableFileCount = immutableFiles.Length,
                        immutableHashes = originalHashes, passed = true,
                    });
                    Console.WriteLine(item.Label + " update " + (index + 1) + ": " + RequiredChecks.Length + " checks passed");
                }
            }
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
            sourceHead, sourceTree, sourceChanges, runnerSha256 = Hash(typeof(Program).Assembly.Location),
            toolSha256 = Hash(tool), configSha256 = Hash(configPath), requiredChecks = RequiredChecks, results, errors,
        }, JsonOptions));
        return errors.Count == 0 ? 0 : 1;
    }

    private static async Task<ProcessResult> Run(string executable, IEnumerable<string> arguments,
        string directory, int timeoutSeconds)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
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
    private sealed record Config(string LabRoot, string ToolAssembly, string OutputRoot, string[] Updates, Base[] Bases, int TimeoutSeconds = 120);
    private sealed record Base(string Label, string PlayerRoot, string BuildIdentity, bool SkipFirstUpdate = false);
    private sealed record ProcessResult(int Id, int ExitCode, long ElapsedMilliseconds, string Text);
}
