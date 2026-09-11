using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HybridCLR;
using HybridCLR.Editor.Commands;

internal static class DeliveryTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("delivery <resource> <Base117> <Base118> <bundle proof> <new output>");
        if (Directory.Exists(args[4])) throw new IOException("New output required.");
        Directory.CreateDirectory(args[4]);
        var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        var checks = new Dictionary<string, bool>();
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        var assets = new Dictionary<string, string> {
            ["prefab"] = Path.Combine(args[3], "asset-bundles/dhe-prefab"),
            ["scene"] = Path.Combine(args[3], "asset-bundles/dhe-scene") };
        string deliveryRoot = Path.Combine(args[4], "delivery");
        string manifestHash = DheDeliveryBuilder.Build(args[0], assets, "StandaloneWindows64", "Unity2022Fgs", deliveryRoot, Path.Combine(args[3], "asset-build.json"));
        string repeated = DheDeliveryBuilder.Build(args[0], assets, "StandaloneWindows64", "Unity2022Fgs", Path.Combine(args[4], "delivery-repeat"), Path.Combine(args[3], "asset-build.json"));
        if (manifestHash != repeated) throw new InvalidOperationException("Delivery builder is nondeterministic.");
        var source = new MemoryProvider();
        foreach (string file in Directory.GetFiles(deliveryRoot, "*", SearchOption.AllDirectories))
            source.Bytes[Path.GetRelativePath(deliveryRoot, file).Replace('\\', '/')] = File.ReadAllBytes(file);
        var identities = new List<DheRuntimeIdentity>(); var baseProviders = new List<MemoryProvider>();
        foreach (string proof in args.Skip(1).Take(2))
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(proof, "base/build-identity.json")));
            var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateObject()) fields[item.Name] = item.Value.Clone();
            var identity = new DheRuntimeIdentity();
            foreach (var field in typeof(DheRuntimeIdentity).GetFields())
                if (fields.TryGetValue(field.Name, out var value)) field.SetValue(identity, JsonSerializer.Deserialize(value.GetRawText(), field.FieldType));
            var assemblies = doc.RootElement.GetProperty("assemblies").EnumerateArray().ToArray();
            identity.AssemblyNames = assemblies.Select(row => row.GetProperty("assemblyName").GetString()).ToArray();
            identity.BaseMetaVersionHashes = assemblies.Select(row => row.GetProperty("embeddedBaseMetaVersionSha256").GetString()).ToArray();
            identities.Add(identity);
            var embedded = new MemoryProvider();
            string baseAssets = Path.Combine(proof, "base/player/Snapshot_Data/StreamingAssets/SnapshotDHE");
            foreach (string file in Directory.GetFiles(baseAssets, "*", SearchOption.AllDirectories))
                embedded.Bytes[identity.RuntimeAssetRoot + Path.GetRelativePath(baseAssets, file).Replace('\\', '/')] = File.ReadAllBytes(file);
            baseProviders.Add(embedded);
        }
        bool Prepare(MemoryProvider provider, int index, string hash, out DheDelivery delivery, out string error) =>
            DheRuntime.TryPrepareDelivery(provider, baseProviders[index], identities[index], "dhe-delivery.json", hash, out delivery, out error);
        void Check(string name, bool value) { checks.Add(name, value); if (!value) throw new InvalidOperationException(name); }
        bool Denied(Action action) { try { action(); return false; } catch { return true; } }
        for (int index = 0; index < 2; ++index)
        {
            RuntimeApi.SimulateNewProcess();
            Check("prepare-" + index, Prepare(source.Copy(), index, manifestHash, out var delivery, out var error));
            Check("no-native-effects-" + index, RuntimeApi.Calls == 0 && !DheRuntime.MetadataCommitted);
            Check("assets-before-load-rejected-" + index, Denied(() => delivery.LoadAssetBytes("prefab")));
            Check("load-" + index, delivery.LoadCurrentAssemblies(out _, out error));
            Check("native-load-" + index, RuntimeApi.Calls > 0 && delivery.IsReady);
            Check("asset-identity-" + index, Hash(delivery.LoadAssetBytes("prefab")) == Hash(File.ReadAllBytes(assets["prefab"])));
            byte[] changed = delivery.LoadAssetBytes("prefab"); changed[0] ^= 1;
            Check("asset-return-copy-" + index, Hash(delivery.LoadAssetBytes("prefab")) == Hash(File.ReadAllBytes(assets["prefab"])));
        }
        void Reject(string name, Action<MemoryProvider> mutate, bool rehash = false)
        {
            RuntimeApi.SimulateNewProcess(); var bad = source.Copy(); mutate(bad);
            Check(name, !Prepare(bad, 0, rehash ? Hash(bad.Bytes["dhe-delivery.json"]) : manifestHash, out _, out _) && RuntimeApi.Calls == 0);
        }
        void Edit(MemoryProvider provider, Action<DheDeliveryManifest> edit)
        {
            var manifest = UnityEngine.JsonUtility.FromJson<DheDeliveryManifest>(Encoding.UTF8.GetString(provider.Bytes["dhe-delivery.json"]));
            edit(manifest); provider.Bytes["dhe-delivery.json"] = Encoding.UTF8.GetBytes(UnityEngine.JsonUtility.ToJson(manifest));
        }
        Reject("manifest-digest", provider => provider.Bytes["dhe-delivery.json"][0] ^= 1);
        Reject("missing-asset", provider => provider.Bytes.Remove("assets/prefab"));
        Reject("corrupt-asset", provider => provider.Bytes["assets/prefab"][0] ^= 1);
        Reject("missing-code", provider => provider.Bytes.Remove("code/dhe-runtime-plan.json"));
        Reject("wrong-target", provider => Edit(provider, m => m.target = "Android"), true);
        Reject("wrong-current", provider => Edit(provider, m => m.currentAssemblySetSha256 = new string('A', 64)), true);
        Reject("path-traversal", provider => Edit(provider, m => m.files[0].path = "code/../escape"), true);
        Reject("duplicate-case-path", provider => Edit(provider, m => m.files = m.files.Append(new DheDeliveryFile {
            path = m.files[0].path.ToUpperInvariant(), kind = m.files[0].kind, length = m.files[0].length, sha256 = m.files[0].sha256 }).ToArray()), true);
        Reject("base-mv-override", provider => Edit(provider, m => m.files = m.files.Append(new DheDeliveryFile {
            path = "code/BaseMetaVersion/override.mv.bytes", kind = "code", length = 0, sha256 = Hash(Array.Empty<byte>()) }).ToArray()), true);
        foreach (string phase in new[] { "before-load", "after-load" })
        {
            RuntimeApi.SimulateNewProcess(); var mutable = source.Copy();
            Check("mutation-prepare-" + phase, Prepare(mutable, 0, manifestHash, out var delivery, out _));
            if (phase == "after-load") Check("mutation-load", delivery.LoadCurrentAssemblies(out _, out _));
            mutable.Bytes["assets/prefab"][0] ^= 1;
            Check("mutation-rejected-" + phase, phase == "before-load"
                ? !delivery.LoadCurrentAssemblies(out _, out _) && RuntimeApi.Calls == 0
                : Denied(() => delivery.LoadAssetBytes("prefab")));
        }
        RuntimeApi.SimulateNewProcess();
        Check("stale-prepare", Prepare(source.Copy(), 0, manifestHash, out var stale, out _));
        DheRuntime.Reset();
        Check("stale-rejected", !stale.LoadCurrentAssemblies(out _, out _) && RuntimeApi.Calls == 0);
        Check("replacement-prepare", Prepare(source.Copy(), 1, manifestHash, out var replacement, out _));
        Check("replacement-load", replacement.LoadCurrentAssemblies(out _, out _));
        RuntimeApi.SimulateNewProcess();
        Check("superseded-prepare", Prepare(source.Copy(), 0, manifestHash, out var superseded, out _));
        Check("superseding-prepare", Prepare(source.Copy(), 1, manifestHash, out var superseding, out _));
        Check("superseded-rejected", !superseded.LoadCurrentAssemblies(out _, out _) && RuntimeApi.Calls == 0);
        Check("superseding-load", superseding.LoadCurrentAssemblies(out _, out _));
        RuntimeApi.SimulateNewProcess(); var codeMutation = source.Copy();
        Check("code-mutation-prepare", Prepare(codeMutation, 0, manifestHash, out var codeDelivery, out _));
        string dllPath = codeMutation.Bytes.Keys.First(path => path.EndsWith(".dll.bytes", StringComparison.Ordinal));
        codeMutation.Bytes[dllPath][0] ^= 1;
        Check("code-mutation-before-native", !codeDelivery.LoadCurrentAssemblies(out _, out _) && RuntimeApi.Calls == 0);
        RuntimeApi.SimulateNewProcess();
        Check("concurrent-prepare", Prepare(source.Copy(), 0, manifestHash, out var concurrent, out _));
        using (var entered = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            RuntimeApi.OnCall = () => { entered.Set(); if (!release.Wait(10000)) throw new TimeoutException("concurrent fixture"); };
            var loading = Task.Run(() => concurrent.LoadCurrentAssemblies(out _, out _));
            if (!entered.Wait(10000)) throw new TimeoutException("native entry");
            try
            {
                Check("concurrent-second-load", !concurrent.LoadCurrentAssemblies(out var busy, out _) && busy == LoadImageErrorCode.DHE_LOAD_IN_PROGRESS);
                Check("concurrent-reconfigure", !Prepare(source.Copy(), 1, manifestHash, out _, out _));
                Check("concurrent-assets-hidden", !concurrent.IsReady && Denied(() => concurrent.LoadAssetBytes("prefab")));
            }
            finally { release.Set(); }
            Check("concurrent-completes", loading.GetAwaiter().GetResult() && concurrent.IsReady);
        }
        RuntimeApi.SimulateNewProcess(); var restored = source.Copy();
        Check("retry-prepare", Prepare(restored, 0, manifestHash, out var retry, out _));
        restored.Bytes.Remove("assets/prefab");
        Check("retry-first-rejected", !retry.LoadCurrentAssemblies(out _, out _) && RuntimeApi.Calls == 0);
        restored.Bytes["assets/prefab"] = (byte[])source.Bytes["assets/prefab"].Clone();
        Check("retry-restored-load", retry.LoadCurrentAssemblies(out _, out _));
        RuntimeApi.SimulateNewProcess();
        Check("failure-prepare", Prepare(source.Copy(), 0, manifestHash, out var failing, out _));
        RuntimeApi.NextPhase = 3; RuntimeApi.NextCode = LoadImageErrorCode.DHE_MV_REGISTRATION_FAILED;
        Check("failure-requires-restart", !failing.LoadCurrentAssemblies(out _, out _) && DheRuntime.RestartRequired && !failing.IsReady);
        Check("failure-assets-rejected", Denied(() => failing.LoadAssetBytes("prefab")));
        File.WriteAllText(Path.Combine(args[4], "result.json"), JsonSerializer.Serialize(new { passed = true, checks, manifestHash,
            hostSha256 = Hash(File.ReadAllBytes(typeof(DeliveryTests).Assembly.Location)), scope = "Real package builder/provider/load state, mocked native execution; Player verification remains required" }, options));
        Console.WriteLine("Delivery checks passed: " + checks.Count); return 0;
    }
    private sealed class MemoryProvider : IDheRuntimeStreamingAssetProvider
    {
        internal Dictionary<string, byte[]> Bytes = new(StringComparer.Ordinal);
        public bool Exists(string path) => Bytes.ContainsKey(path);
        public byte[] LoadBytes(string path) => Bytes[path];
        public string LoadText(string path) => Encoding.UTF8.GetString(LoadBytes(path));
        public Stream OpenRead(string path) => new MemoryStream(Bytes[path], false);
        internal MemoryProvider Copy() => new() { Bytes = Bytes.ToDictionary(item => item.Key, item => (byte[])item.Value.Clone()) };
    }
}
