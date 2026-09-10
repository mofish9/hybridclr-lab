using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class FrozenFieldValidationPolicy
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("frozen-field-policy <Base identity> <original Current root> <new output>");
        string identityPath = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        string[] names = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!), names)!;
        var source = snapshot.Assemblies.Single(row => row.AssemblyName == "mscorlib");
        string[] before = names.Select(name => Path.Combine(Path.GetDirectoryName(identityPath)!, "baseline", name + ".dll")).ToArray();
        string[] current = Directory.GetFiles(args[1], "*.dll");
        var checks = new Dictionary<string, bool>();
        var files = before.Concat(current).Append(source.Path).Distinct().ToDictionary(path => path, Hash);
        var plan = FrozenAotAdaptation.Compile(snapshot, before, current);
        var corlib = plan.Assemblies.Single(row => row.AssemblyName == "mscorlib");
        using (var module = ModuleDefMD.Load(source.ReadVerifiedBytes()))
        {
            var accessors = FrozenFieldValidation.Select(module);
            checks["real-base-getter-and-setter-validated"] = accessors.Length == 2;
            checks["both-original-methods-selected"] = accessors.All(method => corlib.Methods.Any(row =>
                row.Token == method.MDToken.Raw && row.Reasons.Contains(FrozenFieldValidation.Reason)));
            checks["both-original-entry-guards-required"] = accessors.All(method => plan.Obligations.Any(row =>
                row.Kind == "base-native-guard-required" && row.AssemblyName == "mscorlib" && row.Token == method.MDToken.Raw));
            checks["original-source-and-mv-preserved"] = corlib.SourceDllSha256 == source.Sha256 &&
                corlib.SourceMetaVersionSha256 == Hash(MetaVersionSnapshot.Create(source.Path).ToBinary()) &&
                corlib.ExecutionPlan.BaseMetaVersionSha256 == corlib.ExecutionPlan.CurrentMetaVersionSha256;
            checks["runtime-field-definition-not-replaced"] = !corlib.ExecutionPlan.CurrentStorageTypeTokens.Contains(accessors[0].DeclaringType.MDToken.Raw);
        }
        foreach (string mutation in new[] { "object-argument", "gettype-call", "assignability-call", "static-branch", "null-branch", "receiver-branch", "entry-into-expression", "accessor-signature", "static-method", "wrong-assembly" })
        {
            using var module = ModuleDefMD.Load(source.ReadVerifiedBytes());
            var method = FrozenFieldValidation.Select(module)[0]; var il = method.Body.Instructions;
            switch (mutation)
            {
                case "object-argument": il[10].OpCode = OpCodes.Ldarg_0; break;
                case "gettype-call": il[11].Operand = il[9].Operand; break;
                case "assignability-call": il[12].Operand = il[11].Operand; break;
                case "static-branch": il[2].Operand = il[8]; break;
                case "null-branch": il[4].Operand = il[11]; break;
                case "receiver-branch": il[13].Operand = il[8]; break;
                case "entry-into-expression": il[^1].OpCode = OpCodes.Br; il[^1].Operand = il[11]; break;
                case "accessor-signature": method.Name = "Other"; break;
                case "static-method": method.IsStatic = true; break;
                case "wrong-assembly": module.Assembly.Name = "Hotfix"; break;
            }
            try { FrozenFieldValidation.Validate(method); checks[mutation + ":rejected"] = false; }
            catch (InvalidDataException) { checks[mutation + ":rejected"] = true; }
        }
        var baselines = before.Select(MetaVersionSnapshot.Create).ToArray();
        checks["no-op-has-no-parent-change"] = !FrozenFieldValidation.HasParentChange(baselines, baselines);
        checks["current-parent-change-detected"] = FrozenFieldValidation.HasParentChange(baselines, current.Select(MetaVersionSnapshot.Create));
        checks["old-runtime-capability-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != FrozenFieldValidation.Capability),
            new[] { FrozenFieldValidation.Capability });
        checks["frozen-instance-frame-capability-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
            ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
            ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != ResourceUpdateCompatibility.FrozenBaseInstanceFrameCapability),
            new[] { ResourceUpdateCompatibility.FrozenBaseInstanceFrameCapability });
        checks["inputs-unchanged"] = files.All(row => Hash(row.Key) == row.Value);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, files, plan,
            hostSha256 = Hash(typeof(FrozenFieldValidationPolicy).Assembly.Location),
            scope = "Authenticated Base field validation and parent selection; mutations are in-memory negative controls" }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var row in checks.Where(row => !row.Value)) Console.WriteLine("FAILED: " + row.Key);
        Console.WriteLine("Frozen reflected-field policy: " + passed + "; checks=" + checks.Count);
        return passed ? 0 : 1;
    }
    private static string Hash(string path) => Hash(File.ReadAllBytes(path));
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}
