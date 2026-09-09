using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HybridCLR;

if (args.Length != 2) throw new ArgumentException("<public-reflection artifact root> <new report.json>");
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
void RunCase(string name, int baseIndex, Action<JsonNode, JsonNode, JsonNode, Provider> mutate, bool expected)
{
    DheRuntime.Reset(); RuntimeApi.Calls = 0; RuntimeApi.LastTypes = RuntimeApi.LastMethods = null;
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
            accepted = RuntimeApi.Calls == 1 && RuntimeApi.LastTypes != null && names.Select((item, index) =>
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
RunCase("valid-retry-after-rejections", 0, null, true);
Directory.CreateDirectory(Path.GetDirectoryName(output));
File.WriteAllText(output, JsonSerializer.Serialize(new { passed = cases.Values.All(value => value),
    scope = "Package resource validation and native argument selection on .NET host; native calls are recorded, not executed",
    cases, errors, sourceInputs = root }, json));
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
