using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HybridCLR;
using HybridCLR.DheTool;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

if (args.Length != 3) throw new ArgumentException("<public-reflection artifact root> <new report.json> <DheTool.dll>");
string root = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Report must be new.");
var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
string HashText(string text) => Hash(Encoding.UTF8.GetBytes(text));
JsonNode Node(object value) => JsonSerializer.SerializeToNode(value, json);
JsonNode Clone(JsonNode value) => JsonNode.Parse(value.ToJsonString());
object SetupIdentity(string method, params object[] arguments) => typeof(DheRuntime)
    .GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, arguments);
string SetHash(IEnumerable<KeyValuePair<string, byte[]>> values) => (string)SetupIdentity("Sha256NamedByteSet", values);
const string assets = "Assets/DHE/", baseRoot = assets + "base/", manifestPath = assets + "dhe-resource-update.json";
string emptyHash = Hash(Array.Empty<byte>());
var cases = new Dictionary<string, bool>();
var errors = new Dictionary<string, string>();
var identities = new List<DheRuntimeIdentity>();
var providers = new List<Provider>();
var supportedBases = new List<JsonNode>();
var selections = new List<JsonNode>();
var assemblyRecords = new List<JsonNode>();
string currentSetHash = null;
foreach (string version in new[] { "old", "new" })
{
    string payload = Path.Combine(root, "payload-latest-" + version);
    using var input = JsonDocument.Parse(File.ReadAllText(Path.Combine(payload, "plan.json")));
    string[] names = input.RootElement.GetProperty("assemblies").EnumerateArray().Select(item => item.GetProperty("name").GetString()).ToArray();
    var provider = new Provider(); providers.Add(provider);
    var beforeBytes = names.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(payload, "base", name + ".mv")));
    var currentBytes = names.ToDictionary(name => name, name => File.ReadAllBytes(Path.Combine(payload, "current", name + ".dll")));
    currentSetHash ??= SetHash(currentBytes);
    if (currentSetHash != SetHash(currentBytes)) throw new InvalidDataException("Inputs do not share Current DLLs.");
    var identity = new DheRuntimeIdentity
    {
        IdentityVersion = 1, Target = "StandaloneWindows64", EngineWorkflow = "Unity2022Fgs", Il2CppCodeGeneration = "OptimizeSize",
        AotSnapshotKind = "managed-assembly-plus-generated-cpp-v1", ManagedAssemblySetSha256 = HashText("managed" + version),
        AotAssemblyNames = names, AotAssemblySetSha256 = HashText(string.Concat(names.OrderBy(name => name, StringComparer.Ordinal).Select(name => name + "\n"))),
        AotSnapshotSha256 = HashText("snapshot" + version), BaseMetaVersionSetSha256 = SetHash(beforeBytes),
        NativeGuardSourceSha256 = HashText("guard" + version), NativeManifestSha256 = HashText("native" + version),
        AotMetadataSetId = emptyHash, RuntimeProtocol = "dhe-runtime-protocol-v1", RuntimeContract = "dhe-runtime-v27",
        RuntimeCapabilities = (string[])typeof(DheRuntime).GetField("NativeRuntimeCapabilities", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null),
        RuntimeAssetRoot = assets, BaseMetaVersionAssetRoot = baseRoot, AssemblyNames = names,
        BaseMetaVersionHashes = names.Select(name => Hash(beforeBytes[name])).ToArray(),
    };
    identity.BaseId = (string)SetupIdentity("ComputeBaseId", identity); identities.Add(identity);
    var modes = new List<JsonNode>();
    foreach (var record in input.RootElement.GetProperty("assemblies").EnumerateArray())
    {
        string name = record.GetProperty("name").GetString();
        byte[] currentMv = File.ReadAllBytes(Path.Combine(payload, "current", name + ".mv"));
        var plan = new DheExecutionPlan
        {
            schemaVersion = 1, assemblyName = name, baseMetaVersionSha256 = Hash(beforeBytes[name]), currentMetaVersionSha256 = Hash(currentMv),
            currentStorageTypeTokens = record.GetProperty("types").EnumerateArray().Select(value => value.GetUInt32()).ToArray(),
            currentExecutionMethodTokens = record.GetProperty("methods").EnumerateArray().Select(value => value.GetUInt32()).ToArray(),
        };
        plan.Validate(name, beforeBytes[name], currentMv);
        provider.Bytes[baseRoot + name + ".mv.bytes"] = beforeBytes[name];
        provider.Bytes[assets + name + ".dll.bytes"] = currentBytes[name];
        provider.Bytes[assets + name + ".mv.bytes"] = currentMv;
        modes.Add(Node(new { assemblyName = name, executionMode = "dhe-differential", executionPlan = plan }));
        if (version == "old") assemblyRecords.Add(Node(new { assemblyName = name, executionMode = "dhe-differential",
            current = assets + name + ".dll.bytes", currentSha256 = Hash(currentBytes[name]),
            currentMetaVersion = assets + name + ".mv.bytes", currentMetaVersionSha256 = Hash(currentMv),
            baseMetaVersion = baseRoot + name + ".mv.bytes" }));
    }
    var modeArray = new JsonArray(modes.ToArray());
    selections.Add(Node(new { baseId = identity.BaseId, aotMetadataSetId = emptyHash, payloadVariantId = "default",
        currentAssemblySetSha256 = currentSetHash, assemblyModes = Clone(modeArray) }));
    supportedBases.Add(Node(new
    {
        baseId = identity.BaseId, target = identity.Target, engineWorkflow = identity.EngineWorkflow, il2cppCodeGeneration = identity.Il2CppCodeGeneration,
        managedAssemblySetSha256 = identity.ManagedAssemblySetSha256, aotAssemblySetSha256 = identity.AotAssemblySetSha256,
        aotAssemblyNames = names, aotSnapshotSha256 = identity.AotSnapshotSha256, baseMetaVersionSetSha256 = identity.BaseMetaVersionSetSha256,
        nativeGuardSourceSha256 = identity.NativeGuardSourceSha256, nativeManifestSha256 = identity.NativeManifestSha256,
        runtimeProtocol = identity.RuntimeProtocol, nativeRuntimeContract = identity.RuntimeContract, runtimeCapabilities = identity.RuntimeCapabilities,
        requiredRuntimeCapabilities = new[] { DheExecutionPlan.Capability, "atomic-multi-assembly-registration-v1" },
        runtimeAssetRoot = assets, baseMetaVersionAssetRoot = baseRoot, buildIdentitySha256 = HashText(JsonSerializer.Serialize(identity, json)),
        aotMetadataSetId = emptyHash, payloadVariantId = "default", currentAssemblySetSha256 = currentSetHash,
        compatibilityPolicy = "dhe-proven-safe-subset-v1", compatible = true, guardCoverageValidated = true, unsupportedChangeCount = 0,
        assemblyModes = Clone(modeArray),
    }));
}
JsonNode planDocument = Node(new { schemaVersion = 1, format = "hybridclr.dhe-runtime-asset-plan.json",
    selection = "embedded-base-metaversion-and-aot-metadata-set", currentAssemblySetSha256 = currentSetHash,
    runtimeAssetRoot = assets, baseMetaVersionAssetRoot = baseRoot, aotMetadataSetId = "", aotMetadata = Array.Empty<object>(),
    aotMetadataSets = new[] { new { aotMetadataSetId = emptyHash, assemblies = Array.Empty<object>() } },
    baseSelections = selections.ToArray(), assemblies = assemblyRecords.ToArray() });
JsonNode validationDocument = Node(new { schemaVersion = 1, format = "hybridclr.dhe-resource-update-validation.json",
    passed = true, compatibilityPolicy = "dhe-proven-safe-subset-v1", runtimeProtocol = "dhe-runtime-protocol-v1",
    currentAssemblySetSha256 = currentSetHash, bases = supportedBases.ToArray() });
JsonNode manifestDocument = Node(new { schemaVersion = 1, format = "hybridclr.dhe-resource-update.json", payloadModel = "single-current-payload",
    metaVersionSchema = 1, runtimeComparison = "embedded-base-mv-vs-current-mv", compatibilityPolicy = "dhe-proven-safe-subset-v1",
    runtimeProtocol = "dhe-runtime-protocol-v1", compatibilityValidated = true, playerUpdateRequired = false, guardCoverageValidated = true,
    currentAssemblySetSha256 = currentSetHash, runtimeAssetRoot = assets, baseMetaVersionAssetRoot = baseRoot,
    runtimePlan = "dhe-runtime-plan.json", validation = "validation.json", supportedBases = supportedBases.ToArray() });
void RunCase(string name, int baseIndex, Action<JsonNode, JsonNode, JsonNode, Provider> mutate, bool expected,
    bool reset = true, bool expectPlans = true)
{
    if (reset) { DheRuntime.Reset(); RuntimeApi.Calls = 0; RuntimeApi.LastTypes = RuntimeApi.LastMethods = null; }
    var provider = providers[baseIndex].Copy();
    var manifest = Clone(manifestDocument); var validation = Clone(validationDocument); var plan = Clone(planDocument);
    mutate?.Invoke(manifest, validation, plan, provider);
    void Put(string path, JsonNode node) => provider.Bytes[path] = Encoding.UTF8.GetBytes(node.ToJsonString());
    Put(assets + "validation.json", validation); Put(assets + "dhe-runtime-plan.json", plan);
    manifest["runtimePlanSha256"] = Hash(provider.Bytes[assets + "dhe-runtime-plan.json"]);
    manifest["validationSha256"] = Hash(provider.Bytes[assets + "validation.json"]); Put(manifestPath, manifest);
    bool initialized = DheRuntime.InitializeFromResourceUpdate(provider, identities[baseIndex], manifestPath, out string error, assets);
    bool accepted = initialized;
    if (initialized)
    {
        string[] names = identities[baseIndex].AssemblyNames.Reverse().ToArray();
        accepted = DheRuntime.LoadAssemblyImages(names, names.Select(item => provider.Bytes[assets + item + ".dll.bytes"]).ToArray(), out _, out error);
        if (accepted)
        {
            var expectedModes = plan["baseSelections"][baseIndex]["assemblyModes"].AsArray();
            accepted = !expectPlans ? RuntimeApi.Calls == 1 && RuntimeApi.LastTypes == null && RuntimeApi.LastMethods == null :
                RuntimeApi.Calls == 1 && RuntimeApi.LastTypes != null && names.Select((item, index) =>
            {
                var mode = expectedModes.Single(value => value["assemblyName"].GetValue<string>() == item);
                return RuntimeApi.LastTypes[index].SequenceEqual(mode["executionPlan"]["currentStorageTypeTokens"].AsArray().Select(token => token.GetValue<uint>())) &&
                    RuntimeApi.LastMethods[index].SequenceEqual(mode["executionPlan"]["currentExecutionMethodTokens"].AsArray().Select(token => token.GetValue<uint>()));
            }).All(value => value);
        }
    }
    cases[name] = expected ? accepted : !initialized && RuntimeApi.Calls == 0 && !DheRuntime.Enabled;
    errors[name] = error;
}
RunCase("old-base-public-loader", 0, null, true);
RunCase("new-base-same-current-public-loader", 1, null, true);
RunCase("method-only-base-keeps-mv-dispatch-without-plan", 1, (m, v, p, _) =>
{
    foreach (var table in new[] { m["supportedBases"][1]["assemblyModes"], v["bases"][1]["assemblyModes"], p["baseSelections"][1]["assemblyModes"] })
        foreach (var mode in table.AsArray()) mode.AsObject().Remove("executionPlan");
}, true, expectPlans: false);
RunCase("plan-manifest-selection-mismatch", 0, (_, _, plan, _) =>
    plan["baseSelections"][0]["assemblyModes"][0]["executionPlan"]["currentStorageTypeTokens"] = new JsonArray(), false);
RunCase("manifest-validation-selection-mismatch", 0, (_, validation, _, _) =>
    validation["bases"][0]["assemblyModes"][0]["executionPlan"]["baseMetaVersionSha256"] = emptyHash, false);
void MutateBoundPlan(JsonNode manifest, JsonNode validation, JsonNode plan, Action<JsonNode> change)
{
    change(manifest["supportedBases"][0]["assemblyModes"][0]["executionPlan"]);
    change(validation["bases"][0]["assemblyModes"][0]["executionPlan"]);
    change(plan["baseSelections"][0]["assemblyModes"][0]["executionPlan"]);
}
RunCase("wrong-base-mv-binding", 0, (m, v, p, _) => MutateBoundPlan(m, v, p, row => row["baseMetaVersionSha256"] = emptyHash), false);
RunCase("wrong-current-mv-binding", 0, (m, v, p, _) => MutateBoundPlan(m, v, p, row => row["currentMetaVersionSha256"] = emptyHash), false);
RunCase("absent-current-type-token", 0, (m, v, p, _) => MutateBoundPlan(m, v, p, row => row["currentStorageTypeTokens"] = new JsonArray(JsonValue.Create(0x02ffffffu))), false);
RunCase("duplicate-method-token", 0, (m, v, p, _) => MutateBoundPlan(m, v, p, row => row["currentExecutionMethodTokens"] = new JsonArray(JsonValue.Create(0x06000001u), JsonValue.Create(0x06000001u))), false);
RunCase("missing-selection-array", 0, (m, v, p, _) => MutateBoundPlan(m, v, p, row => row.AsObject().Remove("currentStorageTypeTokens")), false);
RunCase("wrong-base-provider", 0, (_, _, _, provider) =>
{
    foreach (var item in providers[1].Bytes.Where(item => item.Key.StartsWith(baseRoot))) provider.Bytes[item.Key] = item.Value;
}, false);
RunCase("duplicate-base-selection", 0, (_, _, p, _) => p["baseSelections"].AsArray().Add(Clone(p["baseSelections"][0])), false);
RunCase("valid-retry-without-reset-after-rejection", 0, null, true, reset: false);
var toolAssembly = Assembly.LoadFrom(Path.GetFullPath(args[2]));
var stagingCanonical = toolAssembly.GetType("HybridCLR.DheTool.Program", throwOnError: true)
    .GetMethod("CanonicalResourceAssemblyModes", BindingFlags.Static | BindingFlags.NonPublic);
string[] StageBinding(JsonNode record)
{
    using var source = JsonDocument.Parse(record.ToJsonString());
    using var payload = JsonDocument.Parse(Node(new { assemblies = assemblyRecords.ToArray() }).ToJsonString());
    return (string[])stagingCanonical.Invoke(null, new object[] { source.RootElement, payload.RootElement, "fixture" });
}
var boundBase = Clone(supportedBases[0]);
string[] stageBefore = StageBinding(boundBase);
cases["staging-binds-execution-plan"] = stageBefore.All(value => value.Contains("|")) &&
    stageBefore.Any(value => value.Contains(boundBase["assemblyModes"][0]["executionPlan"]["baseMetaVersionSha256"].GetValue<string>()));
boundBase["assemblyModes"][0]["executionPlan"]["currentStorageTypeTokens"].AsArray().RemoveAt(0);
cases["staging-detects-selection-tamper"] = !stageBefore.SequenceEqual(StageBinding(boundBase));
boundBase["assemblyModes"][0]["executionPlan"]["currentExecutionMethodTokens"] = new JsonArray(JsonValue.Create(0x06000001u), JsonValue.Create(0x06000001u));
bool duplicateRejected = false;
try { StageBinding(boundBase); }
catch (TargetInvocationException error) when (error.InnerException is InvalidDataException) { duplicateRejected = true; }
cases["staging-rejects-duplicate-selection"] = duplicateRejected;
var validateSchema = toolAssembly.GetType("HybridCLR.DheTool.Program").GetMethod("ValidateJsonSchema", BindingFlags.Static | BindingFlags.NonPublic);
bool UniqueProperties(JsonElement value)
{
    if (value.ValueKind == JsonValueKind.Array) return value.EnumerateArray().All(UniqueProperties);
    if (value.ValueKind != JsonValueKind.Object) return true;
    var properties = value.EnumerateObject().ToArray();
    return properties.Select(property => property.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length &&
        properties.All(property => UniqueProperties(property.Value));
}
foreach (string file in new[] { "dhe-resource-update.schema.json", "dhe-resource-update-validation.schema.json", "dhe-runtime-plan.schema.json" })
{
    using var schema = JsonDocument.Parse(File.ReadAllText(Path.Combine(Path.GetFullPath("../../../../../..", AppContext.BaseDirectory), "schemas", file)));
    cases["schema-unique-keys-" + file] = UniqueProperties(schema.RootElement);
    bool SchemaAccepts(JsonNode value)
    {
        using var instance = JsonDocument.Parse(value?.ToJsonString() ?? "null");
        var failures = new List<string>();
        validateSchema.Invoke(null, new object[] { schema.RootElement.GetProperty("$defs").GetProperty("executionPlan"), instance.RootElement, schema.RootElement, "$", failures });
        return failures.Count == 0;
    }
    cases["schema-valid-plan-" + file] = SchemaAccepts(supportedBases[0]["assemblyModes"][0]["executionPlan"]);
    cases["schema-no-plan-" + file] = SchemaAccepts(null);
    cases["schema-duplicate-rejected-" + file] = !SchemaAccepts(boundBase["assemblyModes"][0]["executionPlan"]);
    if (file == "dhe-runtime-plan.schema.json")
    {
        bool CompletePlanAccepted(JsonNode value)
        {
            using var instance = JsonDocument.Parse(value.ToJsonString());
            var failures = new List<string>();
            validateSchema.Invoke(null, new object[] { schema.RootElement, instance.RootElement, schema.RootElement, "$", failures });
            return failures.Count == 0;
        }
        cases["runtime-schema-complete-plan"] = CompletePlanAccepted(planDocument);
        var invalidRelease = Clone(planDocument);
        invalidRelease["mode"] = "Release"; invalidRelease["releaseReady"] = false; invalidRelease["releaseChannelId"] = "test";
        invalidRelease["releaseRevision"] = 1; invalidRelease["parentReleaseLedgerSha256"] = null;
        cases["runtime-schema-release-contract-preserved"] = !CompletePlanAccepted(invalidRelease);
        var invalidSelection = Clone(planDocument); invalidSelection["selection"] = "embedded-base-metaversion";
        cases["runtime-schema-base-selection-contract-preserved"] = !CompletePlanAccepted(invalidSelection);
    }
}
string[] assemblyNames = identities[0].AssemblyNames;
string[] BaseFiles(string version) => assemblyNames.Select(name => Path.Combine(root,
    version == "old" ? "base-old-reflection" : "base-new", "baseline", name + ".dll")).ToArray();
string[] currentFiles = assemblyNames.Select(name => Path.Combine(root, "payload-latest-old/current", name + ".dll")).ToArray();
var compiled = ResourceExecutionPlanner.Compile(BaseFiles("old"), currentFiles, Array.Empty<string>());
cases["compiler-finds-layouts"] = compiled.Impact.ChangedValueTypes.Length == 5;
cases["compiler-binds-same-current-per-base"] = ResourceExecutionPlanner.Compile(BaseFiles("new"), currentFiles,
    Array.Empty<string>()).Plans.Count == 0; // Only method bodies differ on this Base; MV dispatch already handles them.
foreach (string name in assemblyNames)
{
    var plan = compiled.Plans[name];
    var packagePlan = UnityEngine.JsonUtility.FromJson<DheExecutionPlan>(JsonSerializer.Serialize(plan,
        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    packagePlan.Validate(name, providers[0].Bytes[baseRoot + name + ".mv.bytes"], providers[0].Bytes[assets + name + ".mv.bytes"]);
    cases["compiler-package-binding-" + name] = packagePlan.CanonicalBinding() == plan.CanonicalBinding();
    var before = MetaVersionSnapshot.Create(BaseFiles("old").Single(path => Path.GetFileNameWithoutExtension(path) == name));
    var current = MetaVersionSnapshot.Create(currentFiles.Single(path => Path.GetFileNameWithoutExtension(path) == name));
    var compatibility = ResourceUpdateCompatibility.Analyze(before, current,
        currentStorageTypes: current.Types.Where(type => plan.CurrentStorageTypeTokens.Contains(type.Token)).Select(type => type.StableId),
        currentExecutionMethodTokens: plan.CurrentExecutionMethodTokens);
    string[] layoutReasons = { "added-instance-field-on-existing-value-type:", "removed-instance-field-on-existing-value-type:",
        "existing-field-metadata-change:", "existing-type-layout-or-vtable-change:" };
    cases["compiler-layout-rejections-resolved-" + name] = !compatibility.UnsupportedChanges.Any(reason =>
        layoutReasons.Any(prefix => reason.StartsWith(prefix, StringComparison.Ordinal)));
    // These historical probe inputs compare a Unity-stripped Base with an SDK
    // Current DLL. Retargeted framework references are a separate real gate;
    // storage selection must not silently remove that identity rejection.
    cases["compiler-keeps-reference-scope-gate-" + name] = !compatibility.Compatible &&
        compatibility.UnsupportedChanges.Any(reason => reason.StartsWith("existing-type-reference-scope-change:"));
    errors["compiler-layout-rejections-resolved-" + name] = string.Join(";", compatibility.UnsupportedChanges);
}
var consumer = MetaVersionSnapshot.Create(currentFiles.Single(path => path.EndsWith("HybridCLR.ValueLayoutConsumer.dll")));
var consumerTokens = compiled.Plans[consumer.AssemblyName].CurrentExecutionMethodTokens;
cases["unchanged-layout-dependent-caller-selected"] = consumerTokens.Contains(consumer.Methods.Single(method => method.Name == "DirectCopy").Token);
cases["unaffected-caller-retains-aot"] = !consumerTokens.Contains(consumer.Methods.Single(method => method.Name == "Unrelated").Token);
var withNative = ResourceExecutionPlanner.Compile(BaseFiles("old"), currentFiles,
    new[] { Path.Combine(root, "base-old-reflection/baseline/HybridCLR.ValueLayoutNative.dll") });
cases["ordinary-aot-abi-obligations-explicit"] = withNative.UnsupportedChanges.Any(value => value.StartsWith("current-storage-native-abi:")) &&
    withNative.UnsupportedChanges.Any(value => value.StartsWith("current-storage-ordinary-aot-layout:"));
string mutations = output + ".inputs";
if (Directory.Exists(mutations)) throw new IOException("Mutation fixture directory must be new.");
var mutated = new List<string[]>();
foreach (var sourceSet in new[] { BaseFiles("old"), currentFiles })
{
    string destination = Path.Combine(mutations, mutated.Count == 0 ? "base" : "current");
    Directory.CreateDirectory(destination);
    foreach (string source in sourceSet)
    {
        string target = Path.Combine(destination, Path.GetFileName(source));
        if (!source.EndsWith("HybridCLR.ValueLayoutModel.dll")) { File.Copy(source, target); continue; }
        using var module = ModuleDefMD.Load(source);
        TypeDef owner = module.Find("HybridCLR.Lab.ValueLayout.Factory", false);
        module.Find("HybridCLR.Lab.ValueLayout.Payload", false).Methods.Add(new MethodDefUser("NativeStub",
            MethodSig.CreateStatic(module.CorLibTypes.Int32), dnlib.DotNet.MethodImplAttributes.Runtime | dnlib.DotNet.MethodImplAttributes.InternalCall,
            dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static));
        owner.Fields.Add(new FieldDefUser("StaticPayload", new FieldSig(module.Find("HybridCLR.Lab.ValueLayout.Payload", false).ToTypeSig()),
            dnlib.DotNet.FieldAttributes.Public | dnlib.DotNet.FieldAttributes.Static));
        owner.Fields.Add(new FieldDefUser("StaticReference", new FieldSig(module.Find("HybridCLR.Lab.ValueLayout.InlineOwner", false).ToTypeSig()),
            dnlib.DotNet.FieldAttributes.Public | dnlib.DotNet.FieldAttributes.Static));
        if (mutated.Count != 0)
        {
            var method = new MethodDefUser("NewHelper", MethodSig.CreateStatic(module.CorLibTypes.Int32),
                dnlib.DotNet.MethodImplAttributes.IL | dnlib.DotNet.MethodImplAttributes.Managed,
                dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static) { Body = new CilBody() };
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_1)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            owner.Methods.Add(method);
        }
        module.Write(target);
    }
    mutated.Add(assemblyNames.Select(name => Path.Combine(destination, name + ".dll")).ToArray());
}
var staticCompilation = ResourceExecutionPlanner.Compile(mutated[0], mutated[1], Array.Empty<string>());
cases["static-value-storage-obligation-explicit"] = staticCompilation.UnsupportedChanges.Any(value => value.Contains("StaticPayload"));
cases["static-reference-does-not-grow-storage"] = !staticCompilation.UnsupportedChanges.Any(value => value.Contains("StaticReference"));
cases["non-il-storage-member-requires-bridge"] = staticCompilation.UnsupportedChanges.Any(value =>
    value.StartsWith("current-storage-native-member:") && value.Contains("NativeStub"));
var addedSnapshot = MetaVersionSnapshot.Create(mutated[1].Single(path => path.EndsWith("HybridCLR.ValueLayoutModel.dll")));
cases["new-method-is-not-a-base-entry"] = !staticCompilation.Plans[addedSnapshot.AssemblyName].CurrentExecutionMethodTokens.Contains(
    addedSnapshot.Methods.Single(method => method.Name == "NewHelper").Token);
Directory.CreateDirectory(Path.GetDirectoryName(output));
string packageRoot = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "DhePackageRoot").Value;
string GitHead(string directory)
{
    var start = new System.Diagnostics.ProcessStartInfo("git") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true };
    foreach (string argument in new[] { "-C", directory, "rev-parse", "HEAD" }) start.ArgumentList.Add(argument);
    using var process = System.Diagnostics.Process.Start(start);
    string value = process.StandardOutput.ReadToEnd(); process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException("Cannot bind source identity: " + directory);
    return value.Trim();
}
File.WriteAllText(output, JsonSerializer.Serialize(new { passed = cases.Values.All(value => value),
    scope = "Package resource validation and native argument selection on .NET host; native calls are recorded, not executed",
    cases, errors, sourceInputs = root, labHead = GitHead(Path.GetFullPath("../../../../../..", AppContext.BaseDirectory)),
    packageHead = GitHead(packageRoot), hostSha256 = Hash(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location)),
    toolSha256 = Hash(File.ReadAllBytes(args[2])), packageSources = new[] { "DheRuntime.cs", "DheExecutionPlan.cs", "LoadImageErrorCode.cs", "HomologousImageMode.cs" }
        .Select(name => new { path = name, sha256 = Hash(File.ReadAllBytes(Path.Combine(packageRoot, "Runtime", name))) }).ToArray() }, json));
foreach (var check in cases) Console.WriteLine(check.Key + ": " + check.Value + (check.Value ? "" : " " + errors[check.Key]));
return cases.Values.All(value => value) ? 0 : 1;

sealed class Provider : IDheRuntimeAssetProvider
{
    internal Dictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);
    public bool Exists(string path) => Bytes.ContainsKey(path);
    public byte[] LoadBytes(string path) => Bytes[path];
    public string LoadText(string path) => Encoding.UTF8.GetString(LoadBytes(path));
    internal Provider Copy() => new() { Bytes = Bytes.ToDictionary(item => item.Key, item => (byte[])item.Value.Clone()) };
}
