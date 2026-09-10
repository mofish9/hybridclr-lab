using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HybridCLR.DheTool;

internal static class FrozenResourceBinding
{
    public static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("frozen-resource-binding <frozen entry proof> <new output> <DheTool.dll>");
        string proof = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]), tool = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string build = Path.Combine(proof, "base"), identityPath = Path.Combine(build, "build-identity.json");
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var json = new JsonSerializerOptions { WriteIndented = true };
        void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, json));
        var checks = new Dictionary<string, bool>(); var errors = new Dictionary<string, string>();
        (int Code, string Text) Execute(string exe, params string[] arguments)
        {
            var start = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start) ?? throw new IOException(exe);
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(300000)) { process.Kill(true); throw new TimeoutException(exe); }
            string text = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), text);
            return (process.ExitCode, text.Trim());
        }
        string lab = Path.GetFullPath("../../../../../..", AppContext.BaseDirectory);
        string package = typeof(FrozenResourceBinding).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "DhePackageRoot").Value!;
        foreach (string root in new[] { lab, package })
        {
            var status = Execute("git", "-C", root, "status", "--porcelain");
            if (status.Code != 0 || status.Text.Length != 0) throw new InvalidDataException("Commit candidate sources before verification: " + root);
        }
        var identity = Read(identityPath);
        string baseId = identity.GetProperty("baseId").GetString()!;
        string assetRoot = identity.GetProperty("runtimeAssetRoot").GetString()!;
        string baseAssetRoot = identity.GetProperty("baseMetaVersionAssetRoot").GetString()!;
        string[] names = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(value => value.GetString()!), names)!;
        string[] before = names.Select(name => Path.Combine(build, "baseline", name + ".dll")).ToArray();
        string evolvedRoot = Path.Combine(proof, "frozen-entry-current");
        string[] current = names.Select(name => Path.Combine(evolvedRoot, name + ".dll")).ToArray();
        string materialized = Path.Combine(output, "materialized");
        FrozenAotMaterialize.Run(new[] { identityPath, evolvedRoot, materialized, assetRoot, baseAssetRoot });
        string planFile = Path.Combine(materialized, "frozen-aot-source-plan.json");
        JsonNode original = JsonNode.Parse(File.ReadAllText(planFile))!;
        string currentHash = FrozenAotSourcePlan.CurrentSetHash(current);
        var compilation = FrozenAotAdaptation.Compile(snapshot, before, current);
        JsonElement Document(JsonNode value) => JsonSerializer.SerializeToElement(value);
        JsonElement[] Validate(JsonNode value) => FrozenAotSourcePlan.ValidateCompiled(Document(value), baseId, snapshot.Sha256, currentHash, compilation);
        checks["actual-current-frozen-selections-accepted"] = Validate(original).Length == compilation.Assemblies.Length && compilation.Assemblies.Length > 0;
        checks["frozen-mvs-outside-embedded-base"] = original["sources"]!.AsArray().All(row =>
            row!["baseMetaVersion"]!.GetValue<string>().StartsWith(assetRoot + "payload/frozen-aot/" + baseId.ToLowerInvariant() + "/", StringComparison.Ordinal) &&
            !row["baseMetaVersion"]!.GetValue<string>().StartsWith(baseAssetRoot, StringComparison.OrdinalIgnoreCase));
        void Reject(string name, Action<JsonNode> edit)
        {
            JsonNode changed = JsonNode.Parse(original.ToJsonString())!; edit(changed);
            try { Validate(changed); checks[name] = false; }
            catch (InvalidDataException error) { checks[name] = true; errors[name] = error.Message; }
        }
        Reject("stale-current-set-rejected", value => value["currentAssemblySetSha256"] = FrozenAotSourcePlan.CurrentSetHash(before));
        Reject("missing-current-set-rejected", value => value.AsObject().Remove("currentAssemblySetSha256"));
        Reject("missing-source-rejected", value => { value["sources"]!.AsArray().RemoveAt(0); value["sourceCount"] = value["sources"]!.AsArray().Count; });
        Reject("duplicate-source-rejected", value => value["sources"]![1] = JsonNode.Parse(value["sources"]![0]!.ToJsonString()));
        Reject("substituted-source-hash-rejected", value => value["sources"]![0]!["sourceSha256"] = new string('F', 64));
        Reject("mutable-source-role-rejected", value => value["sources"]![0]!["sourceKind"] = "mutable-hotfix");
        foreach (string field in new[] { "currentStorageTypeTokens", "currentExecutionMethodTokens", "excludedBaseTypeTokens", "genericContextMethodTokens" })
            Reject("edited-" + field + "-rejected", value => value["sources"]![0]![field]!.AsArray().Add(JsonValue.Create(0x0600ffffu)));
        string badMv = Path.Combine(output, "substituted.mv.bytes"); File.WriteAllBytes(badMv, Array.Empty<byte>());
        Reject("substituted-mv-file-rejected", value => value["sources"]![0]!["baseMetaVersionFile"] = badMv);
        Reject("substituted-dll-file-rejected", value => value["sources"]![0]!["sourceFile"] = badMv);

        string noopRoot = Path.Combine(build, "current"), noopMaterialized = Path.Combine(output, "noop-materialized");
        FrozenAotMaterialize.Run(new[] { identityPath, noopRoot, noopMaterialized, assetRoot, baseAssetRoot });
        string noopPlan = Path.Combine(noopMaterialized, "frozen-aot-source-plan.json");
        JsonNode stale = JsonNode.Parse(File.ReadAllText(noopPlan))!;
        stale["currentAssemblySetSha256"] = currentHash;
        bool staleSelectionRejected = false;
        try { Validate(stale); }
        catch (InvalidDataException error) { staleSelectionRejected = true; errors["relabelled-stale-selection-rejected"] = error.Message; }
        checks["relabelled-stale-selection-rejected"] = staleSelectionRejected;

        (int Code, string Text) Resource(string root, string selectedPlan, string destination) => Execute("dotnet", tool, "resource-update",
            "-CurrentRoot", root, "-SettingsFile", Path.Combine(proof, "project/ProjectSettings/HybridCLRSettings.asset"),
            "-BaselineRoot", Path.Combine(build, "baseline"), "-BaseNativeManifest", Path.Combine(build, "native/dhe-native-manifest.json"),
            "-BaseBuildIdentity", identityPath, "-AotMetadataRoot", Path.Combine(Path.GetDirectoryName(snapshot.ManifestPath)!, "assemblies"),
            "-FrozenAotPlans", selectedPlan, "-Mode", "Exploratory", "-OutputRoot", destination);
        Console.WriteLine("Checking resource CLI against stale and freshly compiled plans.");
        var staleResult = Resource(noopRoot, planFile, Path.Combine(output, "stale-resource"));
        checks["resource-cli-rejects-stale-plan"] = staleResult.Code != 0 && staleResult.Text.Contains("selected Base and Current payload");
        string editedPlan = Path.Combine(output, "relabelled-stale-plan.json"); File.WriteAllText(editedPlan, stale.ToJsonString(json));
        var editedResult = Resource(evolvedRoot, editedPlan, Path.Combine(output, "edited-resource"));
        checks["resource-cli-recompiles-selection"] = editedResult.Code != 0 && editedResult.Text.Contains("selected Base and Current payload");
        string evolvedOutput = Path.Combine(output, "evolved-resource");
        var evolvedResult = Resource(evolvedRoot, planFile, evolvedOutput);
        string validationPath = Path.Combine(evolvedOutput, "dhe-resource-update-validation.json");
        var validation = Read(validationPath);
        checks["valid-plan-keeps-unresolved-native-abi-gate"] = evolvedResult.Code != 0 && !validation.GetProperty("passed").GetBoolean() &&
            validation.GetProperty("bases")[0].GetProperty("unsupportedChanges").EnumerateArray()
                .Any(reason => reason.GetString()!.StartsWith("current-storage-native-abi:"));
        checks["rejected-resource-has-no-publishable-manifest"] = !File.Exists(Path.Combine(evolvedOutput, "dhe-resource-update.json"));
        string noopOutput = Path.Combine(output, "noop-resource");
        var noopResult = Resource(noopRoot, noopPlan, noopOutput);
        checks["noop-resource-cli-passes"] = noopResult.Code == 0;
        if (noopResult.Code != 0) throw new InvalidDataException(noopResult.Text);
        checks["current-set-hash-matches-resource-manifest"] = Read(Path.Combine(noopOutput, "dhe-resource-update.json"))
            .GetProperty("currentAssemblySetSha256").GetString() == FrozenAotSourcePlan.CurrentSetHash(Directory.GetFiles(noopRoot, "*.dll"));
        // Exercise the payload resolver independently of admission. These are
        // in-memory protocol fixtures, never written as publishable manifests.
        JsonNode fixtureManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(noopOutput, "dhe-resource-update.json")))!;
        JsonNode fixturePlan = JsonNode.Parse(File.ReadAllText(Path.Combine(noopOutput, "dhe-runtime-plan.json")))!;
        var frozenRows = JsonNode.Parse(validation.GetProperty("bases")[0].GetProperty("frozenAotSources").GetRawText())!;
        fixtureManifest["supportedBases"]![0]!["frozenAotSources"] = JsonNode.Parse(frozenRows.ToJsonString());
        fixturePlan["baseSelections"]![0]!["frozenAotSources"] = JsonNode.Parse(frozenRows.ToJsonString());
        string resolverRoot = Path.Combine(output, "resolver-payload-fixture");
        foreach (string root in new[] { noopOutput, materialized })
            foreach (string file in Directory.GetFiles(Path.Combine(root, "payload"), "*", SearchOption.AllDirectories))
            {
                string target = Path.Combine(resolverRoot, Path.GetRelativePath(root, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
            }
        var resolver = Assembly.LoadFrom(tool).GetType("HybridCLR.DheTool.Program", true)!
            .GetMethod("ValidateResourceUpdatePayload", BindingFlags.NonPublic | BindingFlags.Static)!;
        Array Resolve(JsonNode manifest, JsonNode plan) => (Array)resolver.Invoke(null, new object[] {
            resolverRoot, Document(manifest), Document(plan), Document(manifest["supportedBases"]![0]!), assetRoot, baseAssetRoot })!;
        Array resolved = Resolve(fixtureManifest, fixturePlan);
        string Property(object value, string name) => (string)value.GetType().GetProperty(name)!.GetValue(value)!;
        var resolvedFrozen = resolved.Cast<object>().Where(row => Property(row, "RelativePath").StartsWith("payload/frozen-aot/", StringComparison.Ordinal)).ToArray();
        checks["frozen-dll-mv-and-snapshot-resolve-as-resource-payload"] = resolvedFrozen.Length == compilation.Assemblies.Length * 2 + 1 &&
            resolvedFrozen.All(row => Property(row, "AssetRoot") == assetRoot && File.Exists(Property(row, "SourcePath")));
        foreach (string kind in new[] { "immutable-root", "other-base", "hash" })
        {
            JsonNode manifest = JsonNode.Parse(fixtureManifest.ToJsonString())!, plan = JsonNode.Parse(fixturePlan.ToJsonString())!;
            foreach (var row in new[] { manifest["supportedBases"]![0]!["frozenAotSources"]![0]!, plan["baseSelections"]![0]!["frozenAotSources"]![0]! })
                if (kind == "hash") row["baseMetaVersionSha256"] = new string('F', 64);
                else row["baseMetaVersion"] = kind == "immutable-root" ? baseAssetRoot + "frozen.mv.bytes" :
                    row["baseMetaVersion"]!.GetValue<string>().Replace(baseId.ToLowerInvariant(), new string('f', 64));
            bool rejected = false;
            try { Resolve(manifest, plan); }
            catch (TargetInvocationException error) when (error.InnerException?.GetType().Name == "DheException")
            { rejected = true; errors["frozen-payload-rejects-" + kind] = error.InnerException.Message; }
            checks["frozen-payload-rejects-" + kind] = rejected;
        }
        string staged = Path.Combine(output, "noop-stage");
        string embedded = Path.Combine(build, "player/Snapshot_Data/StreamingAssets/SnapshotDHE");
        foreach (string file in Directory.GetFiles(embedded, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(staged, Path.GetRelativePath(embedded, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        string stageReport = Path.Combine(output, "noop-stage.json");
        var stageResult = Execute("dotnet", tool, "stage-resource-update", "-UpdateRoot", noopOutput, "-AssetRoot", staged,
            "-BaseBuildIdentity", identityPath, "-ImmutableFiles", Path.Combine(build, "player/Snapshot.exe") + "," + Path.Combine(build, "player/GameAssembly.dll"),
            "-Output", stageReport);
        checks["noop-staging-passes"] = stageResult.Code == 0;
        if (stageResult.Code != 0) throw new InvalidDataException(stageResult.Text);
        var stage = Read(stageReport);
        checks["staged-report-paths-resolve-to-exact-bytes"] = stage.GetProperty("stagedFiles").EnumerateArray().All(row =>
            row.GetProperty("path").GetString()!.StartsWith("payload/") &&
            Hash(Path.Combine(staged, row.GetProperty("path").GetString()!)).Equals(row.GetProperty("sha256").GetString(), StringComparison.OrdinalIgnoreCase) &&
            row.GetProperty("assetPath").GetString() == assetRoot + row.GetProperty("path").GetString());
        checks["staging-keeps-base-mvs-immutable"] = stage.GetProperty("baseMetaVersionUnchanged").GetBoolean();
        var schema = Execute("dotnet", tool, "schema-validate", "-Schema", Path.Combine(lab, "schemas/dhe-resource-stage.schema.json"),
            "-Document", stageReport, "-Output", Path.Combine(output, "stage-schema.json"));
        checks["stage-report-schema-passes"] = schema.Code == 0;
        Write(Path.Combine(output, "result.json"), new { passed = checks.Values.All(value => value), checks, errors,
            scope = "Real Base snapshot compiler/CLI and disk staging checks; no evolved resource Player or release admission claim",
            labHead = Execute("git", "-C", lab, "rev-parse", "HEAD").Text,
            packageHead = Execute("git", "-C", package, "rev-parse", "HEAD").Text,
            hostSha256 = Hash(typeof(FrozenResourceBinding).Assembly.Location), toolSha256 = Hash(tool),
            identityPath, identitySha256 = Hash(identityPath), snapshot.Sha256, currentSetHash = currentHash,
            materializedPlanSha256 = Hash(planFile), evolvedValidationSha256 = Hash(validationPath), stageReportSha256 = Hash(stageReport) });
        foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
        return checks.Values.All(value => value) ? 0 : 1;
    }
}
