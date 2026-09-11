using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.Editor.Commands;

internal static class AssetProvenanceTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("asset-provenance <Current> <resource> <authored root> <new output>");
        if (Directory.Exists(args[3])) throw new IOException("New output required.");
        Directory.CreateDirectory(args[3]); var checks = new Dictionary<string, bool>();
        string model = Path.Combine(args[0], "HybridCLR.ValueLayoutModel.dll");
        string record = Path.Combine(args[2], "asset-build.json");
        var assets = new Dictionary<string, string> { ["prefab"] = Path.Combine(args[2], "asset-bundles/dhe-prefab"),
            ["scene"] = Path.Combine(args[2], "asset-bundles/dhe-scene") };
        string setHash = DheAssetBuildProvenance.CurrentSetHash(Directory.GetFiles(args[0], "*.dll"));
        bool Reject(Action action) { try { action(); return false; } catch (InvalidDataException) { return true; } }
        void Check(string name, bool value) { checks.Add(name, value); if (!value) throw new InvalidOperationException(name); }
        Check("actual-asset-build-record", DheAssetBuildProvenance.Validate(record, setHash, assets, "StandaloneWindows64", "Unity2022Fgs").Length == 64);
        string expected = DheAssetBuildProvenance.CompareSchemas(model, model);
        void Schema(string name, Action<ModuleDefMD> mutate, bool pass)
        {
            string path = Path.Combine(args[3], name + ".dll");
            using (var module = ModuleDefMD.Load(File.ReadAllBytes(model))) { mutate(module); module.Write(path); }
            Check(name, pass ? DheAssetBuildProvenance.CompareSchemas(model, path) == expected : Reject(() => DheAssetBuildProvenance.CompareSchemas(model, path)));
        }
        TypeDef State(ModuleDef module) => module.Find("HybridCLR.Lab.UnityAssets.AssetState", false);
        Schema("method-body-difference-allowed", module => {
            var method = module.Find("HybridCLR.Lab.UnityAssets.AssetNode", false).Methods.Single(method => method.Name == "Read");
            method.Body = new CilBody(); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 400)); method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }, true);
        Schema("serialized-field-type-rejected", module => State(module).Fields.Single(field => field.Name == "RenamedNumber").FieldSig.Type = module.CorLibTypes.Int64, false);
        Schema("serialized-field-order-rejected", module => { var type = State(module); var field = type.Fields[0]; type.Fields.RemoveAt(0); type.Fields.Add(field); }, false);
        Schema("serialized-field-removal-rejected", module => State(module).Fields.RemoveAt(0), false);
        Schema("former-name-removal-rejected", module => State(module).Fields.Single(field => field.Name == "RenamedNumber").CustomAttributes.Clear(), false);
        Schema("serializable-flag-rejected", module => State(module).IsSerializable = false, false);
        Schema("serialized-field-addition-rejected", module => State(module).Fields.Add(new FieldDefUser("EditorOnly", new FieldSig(module.CorLibTypes.Int32), FieldAttributes.Public)), false);
        Schema("nonserialized-private-field-allowed", module => State(module).Fields.Add(new FieldDefUser("_debug", new FieldSig(module.CorLibTypes.Int32), FieldAttributes.Private | FieldAttributes.NotSerialized)), true);
        Check("wrong-current-rejected", Reject(() => DheAssetBuildProvenance.Validate(record, new string('0', 64), assets, "StandaloneWindows64", "Unity2022Fgs")));
        Check("wrong-target-rejected", Reject(() => DheAssetBuildProvenance.Validate(record, setHash, assets, "Android", "Unity2022Fgs")));
        Check("missing-inventory-rejected", Reject(() => DheAssetBuildProvenance.Validate(record, setHash, new Dictionary<string, string> { ["prefab"] = assets["prefab"] }, "StandaloneWindows64", "Unity2022Fgs")));
        string corrupt = Path.Combine(args[3], "corrupt.bundle"); byte[] bytes = File.ReadAllBytes(assets["prefab"]); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(corrupt, bytes);
        var wrongAssets = new Dictionary<string, string>(assets) { ["prefab"] = corrupt };
        Check("changed-asset-rejected", Reject(() => DheAssetBuildProvenance.Validate(record, setHash, wrongAssets, "StandaloneWindows64", "Unity2022Fgs")));
        string rejectedOutput = Path.Combine(args[3], "invalid-delivery");
        Check("builder-rejects-before-output", Reject(() => DheDeliveryBuilder.Build(args[1], wrongAssets, "StandaloneWindows64", "Unity2022Fgs", rejectedOutput, record)) && !Directory.Exists(rejectedOutput));
        var parsed = UnityEngine.JsonUtility.FromJson<DheAssetBuildProvenance>(File.ReadAllText(record));
        parsed.assemblies[0].currentSha256 = new string('0', 64);
        string wrongRecord = Path.Combine(args[3], "wrong-assembly-record.json"); File.WriteAllText(wrongRecord, UnityEngine.JsonUtility.ToJson(parsed));
        Check("individual-assembly-binding-rejected", Reject(() => DheDeliveryBuilder.Build(args[1], assets, "StandaloneWindows64", "Unity2022Fgs", rejectedOutput, wrongRecord)) && !Directory.Exists(rejectedOutput));
        string hash = DheDeliveryBuilder.Build(args[1], assets, "StandaloneWindows64", "Unity2022Fgs", Path.Combine(args[3], "valid-delivery"), record);
        Check("provenance-embedded", File.Exists(Path.Combine(args[3], "valid-delivery/assets/dhe/asset-build.json")));
        File.WriteAllText(Path.Combine(args[3], "result.json"), JsonSerializer.Serialize(new { passed = true, checks, setHash, deliveryHash = hash }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Asset provenance checks passed: " + checks.Count); return 0;
    }
}
