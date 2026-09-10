using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class UnitySerializationWorkflow
{
    internal static int Replay(string[] args)
    {
        string declarationMode = args.Length == 6 ? (args[5].EndsWith("-cached", StringComparison.Ordinal) ? args[5][..^7] : args[5]) : "";
        bool declarations = new[] { "declaration-nonvirtual-3", "declaration-virtual-7", "declaration-nonvirtual-11" }.Contains(declarationMode);
        bool parentMemberReceiverCache = args.Length == 6 && args[5] == "parent-members-cached";
        bool virtualReceiverCache = parentMemberReceiverCache || args.Length == 6 && args[5] == "virtual-signatures-cached";
        bool parentTransitionCache = args.Length == 6 && args[5] == "parent-transition-cached";
        bool virtualSignatures = args.Length == 6 && (args[5] == "virtual-signatures" || virtualReceiverCache || parentTransitionCache);
        if (args.Length != 6 || (!virtualSignatures && !declarations && !new[] { "read", "full", "full-unity", "reference", "full-unity-cached", "reference-cached", "reference-generic", "reference-generic-cached", "reference-dispatch", "reference-callbacks", "reference-callbacks-cached", "reference-callbacks-control", "reference-callbacks-control-cached", "hierarchy-query", "hierarchy-query-cached", "interface-remove", "interface-remove-cached", "interface-remove-methods", "interface-remove-methods-cached", "interface-replace", "interface-replace-cached", "interface-remove-compiler", "interface-remove-compiler-cached" }.Contains(args[5])))
            throw new ArgumentException("unity-serialization-replay <lab> <tool.dll> <Base proof> <resource workflow output> <new output> <read|full|full-unity|reference|full-unity-cached|reference-cached|reference-generic|reference-generic-cached|reference-dispatch|reference-callbacks|reference-callbacks-cached|reference-callbacks-control|reference-callbacks-control-cached|hierarchy-query|hierarchy-query-cached|interface-remove|interface-remove-cached|interface-remove-methods|interface-remove-methods-cached|interface-replace|interface-replace-cached|interface-remove-compiler|interface-remove-compiler-cached>");
        bool hierarchy = args[5].StartsWith("hierarchy-query", StringComparison.Ordinal);
        bool interfaceEvolution = args[5].StartsWith("interface-", StringComparison.Ordinal);
        bool dispatch = args[5] == "reference-dispatch";
        bool callbacks = args[5].StartsWith("reference-callbacks", StringComparison.Ordinal);
        bool callbackControl = args[5].StartsWith("reference-callbacks-control", StringComparison.Ordinal);
        bool reference = args[5].StartsWith("reference", StringComparison.Ordinal);
        bool generic = args[5].StartsWith("reference-generic", StringComparison.Ordinal);
        bool lifecycle = args[5].StartsWith("full-unity", StringComparison.Ordinal);
        bool cached = !virtualReceiverCache && !parentTransitionCache && args[5].EndsWith("-cached", StringComparison.Ordinal);
        string evolutionMode = interfaceEvolution ? (cached ? args[5].Substring(0, args[5].Length - "-cached".Length) : args[5]) : null;
        bool? currentReferenceStorageSelected = null;
        string cacheCurrentModelSha256 = null;
        int? cachedBaseMethodCount = null;
        string declarationCacheExpectation = null;
        string lab = Path.GetFullPath(args[0]), tool = Path.GetFullPath(args[1]), proof = Path.GetFullPath(args[2]),
            source = Path.GetFullPath(args[3]), output = Path.GetFullPath(args[4]);
        string lifecycleSelection = lifecycle ? UnityBehaviourSelection.Read(proof, source) : null;
        if (Directory.Exists(output)) throw new IOException("Replay output must be new.");
        Directory.CreateDirectory(output);
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        var records = new List<object>();
        string Execute(string executable, bool failureAllowed, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = lab, UseShellExecute = false, CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            if (parentTransitionCache && arguments.Contains("-snapshotResult"))
            {
                start.ArgumentList.Add("-parentTransitionCacheProbe"); start.ArgumentList.Add("true");
            }
            if (virtualReceiverCache && arguments.Contains("-snapshotResult"))
            {
                start.ArgumentList.Add("-virtualSignatureOldReceiverProbe"); start.ArgumentList.Add("true");
                if (parentMemberReceiverCache)
                {
                    start.ArgumentList.Add("-parentMemberOldReceiverProbe"); start.ArgumentList.Add("true");
                }
            }
            if (lifecycle && arguments.Contains("-snapshotResult"))
            {
                start.ArgumentList.Add("-unityBehaviourProbe"); start.ArgumentList.Add("current");
                start.ArgumentList.Add("-unityBehaviourSelection"); start.ArgumentList.Add(lifecycleSelection);
            }
            if (cached && arguments.Contains("-snapshotResult"))
            {
                start.ArgumentList.Add("-unityReferenceCacheProbe"); start.ArgumentList.Add("true");
                start.ArgumentList.Add("-unityReferenceStorageSelected");
                start.ArgumentList.Add(currentReferenceStorageSelected!.Value.ToString());
                if (declarationCacheExpectation != null)
                {
                    start.ArgumentList.Add("-unityDeclarationExpectation");
                    start.ArgumentList.Add(declarationCacheExpectation);
                }
                if (cachedBaseMethodCount.HasValue)
                {
                    start.ArgumentList.Add("-unityMethodDeclarationBaseMethods");
                    start.ArgumentList.Add(cachedBaseMethodCount.Value.ToString());
                }
            }
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            Console.WriteLine("Unity serialization replay: " + Path.GetFileName(executable) + " PID " + process.Id);
            if (!process.WaitForExit(120000)) { process.Kill(true); throw new TimeoutException(executable); }
            string trace = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
            File.WriteAllText(Path.Combine(output, "process-" + process.Id + ".log"), trace);
            records.Add(new { executable, arguments = start.ArgumentList.ToArray(), processId = process.Id, exitCode = process.ExitCode });
            if (!failureAllowed && process.ExitCode != 0) throw new InvalidOperationException(trace);
            return trace.Trim();
        }
        if (Execute("git", false, "-C", lab, "status", "--porcelain").Length != 0) throw new InvalidDataException("Commit sources first.");
        string labHead = Execute("git", false, "-C", lab, "rev-parse", "HEAD");
        var original = Read(Path.Combine(proof, "result.json"));
        string player = Path.Combine(proof, "base/player/Snapshot.exe"), game = Path.Combine(proof, "base/player/GameAssembly.dll");
        string playerHash = Hash(player), gameHash = Hash(game);
        if (!original.GetProperty("passed").GetBoolean() || playerHash != original.GetProperty("playerSha256").GetString() || gameHash != original.GetProperty("gameAssemblySha256").GetString())
            throw new InvalidDataException("Original Base identity mismatch.");
        string stage = Path.Combine(output, "stage"), embedded = Path.Combine(proof, "base/player/Snapshot_Data/StreamingAssets/SnapshotDHE");
        foreach (string file in Directory.GetFiles(embedded, "*", SearchOption.AllDirectories))
        {
            string target = Path.Combine(stage, Path.GetRelativePath(embedded, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.Copy(file, target);
        }
        Execute("dotnet", false, tool, "stage-resource-update", "-UpdateRoot", Path.Combine(source, "resource"), "-AssetRoot", stage,
            "-BaseBuildIdentity", Path.Combine(proof, "base/build-identity.json"), "-ImmutableFiles", player + "," + game, "-Output", Path.Combine(output, "stage.json"));
        var stageHashes = Directory.GetFiles(stage, "*", SearchOption.AllDirectories).ToDictionary(path => path, Hash);
        string reportPath = Path.Combine(output, "player.json"), log = reportPath + ".log";
        if (cached)
        {
            if (declarations) declarationCacheExpectation = DeclarationWorkflow.CacheExpectation(proof, source);
            if (evolutionMode == "interface-remove-compiler")
            {
                string identityPath = Path.Combine(proof, "base/build-identity.json");
                var identity = Read(identityPath);
                var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
                    identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
                    identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
                var baseModel = MetaVersionSnapshot.Create(snapshot.Assemblies.Single(row => row.AssemblyName == "HybridCLR.ValueLayoutModel").Path);
                cachedBaseMethodCount = baseModel.Methods.Count(method => method.DeclaringType == "HybridCLR.Lab.UnityCases.EvolvingBehaviour" &&
                    (method.Name == "OnBeforeSerialize" || method.Name == "OnAfterDeserialize"));
                if (cachedBaseMethodCount != 0 && cachedBaseMethodCount != 2) throw new InvalidDataException("Expected zero or two Base callback methods.");
            }
            string model = Path.Combine(source, "current/HybridCLR.ValueLayoutModel.dll");
            var modelMv = MetaVersionSnapshot.Create(model);
            uint typeToken = modelMv.Types.Single(type => type.Identity == "HybridCLR.Lab.UnityCases.EvolvingBehaviour").Token;
            string baseId = Read(Path.Combine(proof, "base/build-identity.json")).GetProperty("baseId").GetString()!;
            var resource = Read(Path.Combine(source, "resource/dhe-resource-update.json"));
            cacheCurrentModelSha256 = Hash(model);
            var declaredModel = resource.GetProperty("assemblies").EnumerateArray().Single(row =>
                row.GetProperty("assemblyName").GetString() == modelMv.AssemblyName);
            if (!string.Equals(cacheCurrentModelSha256, declaredModel.GetProperty("dllSha256").GetString(),
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Cache expectation Model DLL is not the bound Current payload.");
            var selection = resource.GetProperty("supportedBases").EnumerateArray().Single(row =>
                row.GetProperty("baseId").GetString() == baseId);
            var mode = selection.GetProperty("assemblyModes").EnumerateArray().Single(row =>
                row.GetProperty("assemblyName").GetString() == modelMv.AssemblyName);
            currentReferenceStorageSelected = mode.TryGetProperty("executionPlans", out var plans) &&
                plans.ValueKind == JsonValueKind.Array && plans.EnumerateArray().Any(plan =>
                    plan.GetProperty("currentStorageTypeTokens").EnumerateArray().Any(token => token.GetUInt32() == typeToken));
        }
        Execute(player, true, "-batchmode", "-nographics", "-snapshotResult", reportPath, "-snapshotResourceRoot", stage,
            "-expectedRevision", "73", "-expectedAssemblies", "4", "-expectedModuleConstant", "202",
            virtualSignatures ? "-virtualSignatureProbe" : declarations ? "-unityDeclarationProbe" : hierarchy ? "-unityHierarchyQueryProbe" : interfaceEvolution ? "-unityInterfaceEvolutionProbe" : callbacks ? "-unitySerializationCallbackProbe" : dispatch ? "-unityReferenceDispatchProbe" : generic ? "-unityReferenceGenericProbe" : reference ? "-unityReferenceProbe" : "-unitySerializationProbe", "true", "-logFile", log);
        var report = Read(reportPath); string[] lines = File.ReadAllLines(log);
        string[] sequence = { "inactive-source-has-no-callback-effects", "json-reads-existing-field", "json-reads-added-field",
            "json-writes-existing-field", "json-writes-added-field", "old-json-preserves-added-field", "clone-copies-existing-field",
            "clone-copies-added-field", "cloned-storage-is-independent", "cloned-storage-survives-gc", "fixture-preserves-lifecycle-state" };
        if (args[5] == "read") sequence = sequence.Skip(1).Take(2).ToArray();
        if (reference) sequence = new[] {
            "public-type-matches-current-type", "runtime-type-matches-public-type", "runtime-type-equals-public-type",
            "public-type-accepts-instance", "current-type-accepts-instance", "public-type-assignable-from-runtime-type",
            "runtime-type-assignable-from-public-type", "field-declaring-type-matches-public-type",
            "reflected-existing-field-read", "reflected-added-field-read", "reflected-fields-write-current-storage",
            "native-get-component-public-type", "clone-preserves-type-and-fields", "fixture-preserves-lifecycle-state"
        };
        if (generic) sequence = new[] {
            "owner-type-identity", "owner-public-type-accepts", "owner-reflection-field-roundtrip", "owner-reflection-construction",
            "list-type-identity", "list-public-type-accepts", "list-direct-content", "list-reflection-construction", "list-reflection-add",
            "array-type-identity", "array-public-type-accepts", "array-reflection-roundtrip", "nested-list-type-identity",
            "nested-list-public-type-accepts", "invariance-remains-strict", "array-object-covariance", "concurrent-type-identity",
            "fixture-preserves-lifecycle-state"
        };
        if (dispatch) sequence = new[] { "reader-value", "unaffected-value" };
        if (callbacks) sequence = new[] { "interface-current-type", "direct-interface-before", "direct-interface-after", "native-json-before",
            "native-json-after", "native-clone-before", "native-clone-after-and-fields", "native-query-current-type", "fixture-preserves-lifecycle-state" };
        if (hierarchy) sequence = new[] { "public-type-interface-list", "instance-type-interface-list", "public-interface-by-name",
            "public-base-type", "public-subclass", "subclass-not-reflexive", "generic-parameter-base", "generic-parameter-not-subclass-self",
            "current-field-data", "fixture-preserves-lifecycle-state" };
        if (interfaceEvolution) sequence = new[] { "current-interface-enumeration", "removed-interface-name", "removed-interface-type-query",
            "removed-interface-instance-query", "removed-interface-managed-cast", "removed-or-retained-method-behavior", "replacement-type-query",
            "replacement-managed-dispatch", "replacement-reflected-dispatch", "json-without-removed-before-callback",
            "json-without-removed-after-callback", "clone-without-removed-callbacks", "native-component-lookup", "public-component-identity",
            "current-fields-survive-gc", "fixture-preserves-lifecycle-state" };
        if (declarations) sequence = new[] { "current-method-flags", "current-method-base-definitions", "current-method-declaring-types",
            "current-method-enumeration", "current-parameter-default", "interface-parameter-default", "direct-explicit-argument",
            "direct-default-argument", "reflected-explicit-argument", "reflected-default-argument", "interface-public-queries",
            "interface-explicit-call-or-absence", "interface-default-call-or-absence", "interface-map-or-rejection",
            "native-before-callback-selection", "native-after-callback-selection", "fixture-preserves-lifecycle-state" };
        if (virtualSignatures) sequence = VirtualSignatureWorkflow.Expected;
        string prefix = virtualSignatures ? "DHE virtual signature" : declarations ? "DHE Unity declaration" : hierarchy ? "DHE Unity hierarchy query" : interfaceEvolution ? "DHE Unity interface evolution" : callbacks ? "DHE Unity serialization callback" : dispatch ? "DHE Unity reference dispatch" : generic ? "DHE Unity reference generic" : reference ? "DHE Unity reference" : "DHE Unity serialization";
        string[] checks = lines.Where(line => line.StartsWith(prefix + " check: ")).Select(line => line.Substring((prefix + " check: ").Length)).ToArray();
        string[] unityExpected = {
            "awake-current-value", "on-enable-current-value", "awake-diff-selection", "added-instance-field", "added-component-awake",
            "start-current-value", "update-current-value", "late-update-current-value", "coroutine-resumed-across-frames",
            "on-disable-current-value", "layout-dependent-reader-selection", "layout-dependent-reader-execution", "unaffected-method-stays-aot",
            "added-reference-survives-gc", "disable-not-repeated-on-destroy", "on-destroy-current-value", "real-multiple-frames"
        };
        string[] OptionalChecks(string property) => report.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(row => row.GetString()!).ToArray() : Array.Empty<string>();
        string[] unityChecks = lifecycle ? OptionalChecks("unityChecks") : Array.Empty<string>();
        string[] virtualReceiverExpected = { "logical-method-identity", "current-receiver-storage",
            "cached-method-rejects-old-receiver", "fresh-method-rejects-old-receiver",
            "current-receiver-invokes-body", "rejection-preserves-receivers" };
        string[] virtualReceiverChecks = OptionalChecks("virtualReceiverChecks");
        if (parentMemberReceiverCache)
            virtualReceiverExpected = virtualReceiverExpected.Concat(new[] {
                "parent-field-rejects-old-receiver", "parent-method-rejects-old-receiver",
                "parent-property-get-rejects-old-receiver", "parent-property-set-rejects-old-receiver",
                "parent-event-rejects-old-receiver", "parent-rejection-preserves-data"
            }).ToArray();
        string[] virtualReceiverRejections = lines.Where(line => line.StartsWith("DHE virtual receiver rejection: "))
            .Select(line => line["DHE virtual receiver rejection: ".Length..]).ToArray();
        bool virtualReceiverPassed = !virtualReceiverCache || virtualReceiverChecks.SequenceEqual(virtualReceiverExpected) &&
            virtualReceiverRejections.Length == 2 && virtualReceiverRejections.All(kind => kind == "TargetException" || kind == "old-AOT-frame") &&
            lines.Where(line => line.StartsWith("DHE virtual receiver check: "))
                .Select(line => line["DHE virtual receiver check: ".Length..]).SequenceEqual(virtualReceiverExpected);
        bool lifecyclePassed = !lifecycle || unityChecks.SequenceEqual(unityExpected) &&
            lines.Count(line => line == "DHE Unity expected selection: " + lifecycleSelection) == 1 &&
            lines.Where(line => line.StartsWith("DHE Unity check: ")).Select(line => line.Substring("DHE Unity check: ".Length)).SequenceEqual(unityExpected) &&
            lines.Count(line => line == "DHE Unity component pass: 2:17") == 1 &&
            report.GetProperty("stage").GetString() == "unity-component-complete";
        string[] cacheExpected = {
            "cached-type-identity-stable", "cached-type-hash-stable", "cached-type-dictionary-lookup", "old-object-public-type-stable",
            "cached-type-allocation-has-current-storage", "cached-field-reads-current-object", "cached-field-writes-current-object",
            "cached-field-retains-old-object-storage", "current-field-validates-physical-receiver", "rejected-access-preserves-both-objects",
            "current-code-casts-respect-physical-layout"
        };
        string[] cacheChecks = cached ? OptionalChecks("referenceCacheChecks") : Array.Empty<string>();
        if (declarations && cached)
            cacheExpected = cacheExpected.Concat(new[] { "declaration-base-flags-warmed", "declaration-method-flags-current",
                "declaration-method-identity-stable", "declaration-method-declaring-types", "declaration-method-base-definitions",
                "declaration-base-parameters-warmed", "declaration-parameter-default-current", "declaration-fresh-parameter-default-current",
                "declaration-interface-default-current", "declaration-parameter-member-stable", "declaration-cached-explicit-invocation",
                "declaration-cached-default-invocation" }).ToArray();
        if (cachedBaseMethodCount.HasValue)
            cacheExpected = cacheExpected.Concat(new[] { "current-method-nonvirtual-flags", "current-method-declaring-types",
                "current-method-base-definitions", "current-method-enumeration" }).Concat(cachedBaseMethodCount == 0
                ? new[] { "base-had-no-callback-methods" }
                : new[] { "cached-methods-were-virtual", "cached-methods-now-nonvirtual", "cached-method-identity-stable",
                    "cached-method-base-definitions", "cached-method-current-invocation" }).ToArray();
        bool cachePassed = !cached || cacheChecks.SequenceEqual(cacheExpected) &&
            (declarationCacheExpectation == null || lines.Count(line => line == "DHE declaration cache expectation: " + declarationCacheExpectation) == 1) &&
            (!cachedBaseMethodCount.HasValue || lines.Count(line => line == "DHE method declaration Base cache: " + cachedBaseMethodCount.Value) == 1) &&
            lines.Count(line => line == "DHE reference cache selected storage: " + currentReferenceStorageSelected!.Value) == 1 &&
            lines.Where(line => line.StartsWith("DHE reference cache check: ")).Select(line => line.Substring("DHE reference cache check: ".Length)).SequenceEqual(cacheExpected) &&
            lines.Count(line => line == "DHE reference cache pass: " + cacheExpected.Length) == 1;
        bool immutable = Hash(player) == playerHash && Hash(game) == gameHash && stageHashes.All(row => Hash(row.Key) == row.Value);
        bool callbackFixturePassed = !callbacks || lines.Count(line => line == "DHE callback fixture: " +
            (callbackControl ? "new-component-control" : "existing-interface")) == 1;
        bool evolutionFixturePassed = !interfaceEvolution || lines.Count(line => line == "DHE interface evolution mode: " + evolutionMode) == 1;
        bool declarationFixturePassed = !declarations || lines.Count(line => line == "DHE declaration mode: " + declarationMode) == 1;
        string[] parentTransitionCacheChecks = OptionalChecks("parentTransitionCacheChecks");
        bool parentTransitionCachePassed = !parentTransitionCache ||
            parentTransitionCacheChecks.SequenceEqual(ParentTransitionWorkflow.CacheExpected) &&
            lines.Where(line => line.StartsWith("DHE parent transition cache check: "))
                .Select(line => line["DHE parent transition cache check: ".Length..]).SequenceEqual(ParentTransitionWorkflow.CacheExpected);
        bool passed = immutable && parentTransitionCachePassed && virtualReceiverPassed && cachePassed && lifecyclePassed && callbackFixturePassed && evolutionFixturePassed && declarationFixturePassed && report.GetProperty("passed").GetBoolean() && checks.SequenceEqual(sequence) &&
            lines.Count(line => line == prefix + " pass: " + sequence.Length) == 1 &&
            lines.Where(line => line.StartsWith("DHE case begin: ")).Select(line => line.Substring(16)).SequenceEqual(
                Read(Path.Combine(source, "reference.json")).GetProperty("records").EnumerateArray().Select(row => row.GetString()));
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, immutable, checks, expected = sequence,
            lifecycle, lifecyclePassed, lifecycleSelection, unityChecks, cached, cachePassed, cacheChecks, currentReferenceStorageSelected,
            cacheCurrentModelSha256, cachedBaseMethodCount, callbacks, callbackControl, callbackFixturePassed, hierarchy, interfaceEvolution, evolutionMode, evolutionFixturePassed,
            virtualSignatures, virtualReceiverCache, virtualReceiverPassed, virtualReceiverChecks, virtualReceiverRejections,
            parentTransitionCache, parentTransitionCachePassed, parentTransitionCacheChecks,
            declarations, declarationMode, declarationFixturePassed, declarationCacheExpectation,
            declarationParameterObjects = lines.Where(line => line.StartsWith("DHE declaration parameter objects ")).ToArray(),
            error = report.GetProperty("error").GetString(), proof, source, labHead, playerSha256 = playerHash, gameAssemblySha256 = gameHash,
            hostSha256 = Hash(typeof(UnitySerializationWorkflow).Assembly.Location), toolSha256 = Hash(tool),
            resourceManifestSha256 = Hash(Path.Combine(source, "resource/dhe-resource-update.json")),
            resultSha256 = Hash(reportPath), logSha256 = Hash(log), records,
            scope = virtualSignatures ? "Managed reference/value/byref/generic virtual signatures, delegates, reflection, exceptions and concurrent calls" : declarations ? "Compiler declarations, interface map, direct/reflected calls, optional arguments and native callback selection" : hierarchy ? "Public Current interface enumeration, ancestry and generic parameter controls" : interfaceEvolution ? "Removed or replaced existing interfaces, managed dispatch and absence of native serialization callbacks" : callbackControl ? "Native serialization callback control using a new interpreter component; no existing-interface qualification" : callbacks ? "Added native serialization callbacks on an evolved component interface" : dispatch ? "Diagnostic value/selection/counter logging; not an execution-counter qualification" : reference ? "Public reference type identity and native allocation on an immutable Base" : lifecycle ?
                "Complete native serialization and lifecycle sequences on an immutable Base" :
                args[5] == "read" ? "Native JSON read diagnostic only; complete serialization is still required" : "Native JSON/overwrite/clone assertions on an immutable Base"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Unity serialization replay: " + passed); return passed ? 0 : 1;
    }
}
