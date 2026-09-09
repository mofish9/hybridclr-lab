using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class FrozenEntryWorkflow
{
    internal const string NativeName = "HybridCLR.ValueLayoutNative";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("frozen-entry-workflow <lab> <package> <editor> <runtime manifest> <old fixture DLL root> <new output>");
        string fixtures = Path.GetFullPath(args[4]), inputs = Path.GetFullPath(args[5]) + ".inputs";
        if (Directory.Exists(inputs)) throw new IOException("Inputs must be new: " + inputs);
        Directory.CreateDirectory(inputs);
        foreach (string file in Directory.GetFiles(fixtures, "*.dll")) File.Copy(file, Path.Combine(inputs, Path.GetFileName(file)));
        string nativeFile = Path.Combine(inputs, NativeName + ".dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(nativeFile)))
        {
            var owner = module.Find("HybridCLR.Lab.ValueLayoutNative.NativeBoundary", false);
            var container = module.Find("HybridCLR.Lab.ValueLayoutNative.NativeInlineOwner", false);
            var valueField = container.Fields.Single(field => field.Name == "Value");
            var payload = valueField.FieldType.ToTypeDefOrRef();
            MethodDef Add(string name, TypeSig result, params TypeSig[] parameters)
            {
                if (owner.Methods.Any(method => method.Name == name)) throw new InvalidDataException("Fixture method already exists: " + name);
                var method = new MethodDefUser(name, MethodSig.CreateStatic(result, parameters),
                    MethodImplAttributes.IL | MethodImplAttributes.Managed | MethodImplAttributes.NoInlining,
                    MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
                owner.Methods.Add(method); return method;
            }
            var copy = Add("FrozenCopyBox", module.CorLibTypes.Object, module.CorLibTypes.Object);
            foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Unbox_Any, payload),
                Instruction.Create(OpCodes.Box, payload), Instruction.Create(OpCodes.Ret) }) copy.Body.Instructions.Add(instruction);
            var inline = Add("FrozenInlineBox", module.CorLibTypes.Object, module.CorLibTypes.Object);
            foreach (var instruction in new[] { Instruction.Create(OpCodes.Newobj, container.Methods.Single(method => method.IsInstanceConstructor)),
                Instruction.Create(OpCodes.Dup), Instruction.Create(OpCodes.Ldarg_0), Instruction.Create(OpCodes.Unbox_Any, payload),
                Instruction.Create(OpCodes.Stfld, valueField), Instruction.Create(OpCodes.Ldfld, valueField),
                Instruction.Create(OpCodes.Box, payload), Instruction.Create(OpCodes.Ret) }) inline.Body.Instructions.Add(instruction);
            var sentinel = Add("FrozenSentinel", module.CorLibTypes.Int32);
            sentinel.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 137)); sentinel.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            module.Write(nativeFile);
        }
        return UnityWorkflow.Run(args.Take(4).Concat(new[] { inputs, args[5], "41", ":frozen-entry:" }).ToArray());
    }

    internal static void WriteGuardJson(string dll, string output, Func<MetaVersionMethod, bool> include = null)
    {
        var mv = MetaVersionSnapshot.Create(dll);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(new { mv.AssemblyName,
            methods = mv.Methods.Where(method => include == null || include(method)),
            mv.AssemblySha256 }, Json));
    }

    internal static int Verify(string build, string output)
    {
        string identityPath = Path.Combine(build, "build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(identityPath));
        string[] names = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()).ToArray();
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()), names);
        string current = Path.Combine(output, "frozen-entry-current");
        EvolutionCurrent.Run(new[] { Path.Combine(build, "current"), current });
        string[] bases = names.Select(name => Path.Combine(build, "baseline", name + ".dll")).ToArray();
        string[] after = names.Select(name => Path.Combine(current, name + ".dll")).ToArray();
        var mutable = ResourceExecutionPlanner.Compile(bases, after, snapshot.OrdinaryAssemblyPaths);
        var frozen = FrozenAotAdaptation.Compile(snapshot, bases, after);
        string nativeManifestPath = Path.Combine(build, "native/dhe-native-manifest.json");
        var nativeManifest = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(nativeManifestPath));
        var guarded = nativeManifest.GetProperty("methods").EnumerateArray().Select(row =>
            row.GetProperty("assemblyName").GetString() + ":" + row.GetProperty("methodToken").GetUInt32()).ToHashSet();
        var absent = nativeManifest.GetProperty("interpreterOnlyMethods").EnumerateArray().Select(row =>
            row.GetProperty("assemblyName").GetString() + ":" + row.GetProperty("methodToken").GetUInt32()).ToHashSet();
        var missingGuards = frozen.Assemblies.SelectMany(plan => plan.Methods.Select(method => plan.AssemblyName + ":" + method.Token))
            .Where(key => !guarded.Contains(key) && !absent.Contains(key)).ToArray();
        File.WriteAllText(Path.Combine(output, "frozen-entry-coverage.json"), JsonSerializer.Serialize(new
        { missingGuards, frozen.Obligations, mutable.UnsupportedChanges, snapshotSha256 = snapshot.Sha256 }, Json));
        if (missingGuards.Length != 0) throw new InvalidDataException("Frozen native guard coverage missing: " + string.Join(",", missingGuards));
        var records = new List<object>();
        string payloadRoot = Path.Combine(output, "frozen-entry-payload"); Directory.CreateDirectory(payloadRoot);
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        void Add(string name, string source, int kind, uint[] types, uint[] methods, uint[] excluded)
        {
            string dll = Path.Combine(payloadRoot, name + ".dll"), before = Path.Combine(payloadRoot, name + ".base.mv"), next = Path.Combine(payloadRoot, name + ".current.mv");
            File.Copy(source, dll);
            MetaVersionSnapshot.Create(kind == 1 ? source : Path.Combine(build, "baseline", name + ".dll")).WriteBinary(before);
            MetaVersionSnapshot.Create(source).WriteBinary(next);
            string invalidBefore = null, invalidBeforeSha256 = null;
            if (kind == 0 && name == "HybridCLR.ValueLayoutModel")
            {
                var invalid = MetaVersionSnapshot.Create(Path.Combine(build, "baseline", name + ".dll"));
                int index = Array.FindIndex(invalid.Methods, method => method.Name == "UnchangedRevision");
                if (index < 0 || methods.Contains(invalid.Methods[index].Token))
                    throw new InvalidDataException("Retry probe requires an unselected Base method.");
                invalid.Methods[index] = invalid.Methods[index] with { StableId = new string('F', 64), Token = 0x0600ffffu };
                invalidBefore = Path.Combine(payloadRoot, name + ".invalid-base.mv");
                invalid.WriteBinary(invalidBefore); invalidBeforeSha256 = Hash(invalidBefore);
            }
            records.Add(new { name, dll, before, after = next, sourceKind = kind, types, methods, excluded,
                dllSha256 = Hash(dll), beforeSha256 = Hash(before), afterSha256 = Hash(next), invalidBefore, invalidBeforeSha256 });
        }
        foreach (var plan in frozen.Assemblies)
            Add(plan.AssemblyName, snapshot.Assemblies.Single(source => source.AssemblyName == plan.AssemblyName).Path, 1,
                plan.ExecutionPlan.CurrentStorageTypeTokens, plan.ExecutionPlan.CurrentExecutionMethodTokens, plan.ExcludedBaseTypeTokens);
        foreach (string name in names)
            Add(name, Path.Combine(current, name + ".dll"), 0, mutable.Plans[name].CurrentStorageTypeTokens,
                mutable.Plans[name].CurrentExecutionMethodTokens, Array.Empty<uint>());
        string planPath = Path.Combine(output, "frozen-entry-plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(new { format = "hybridclr.frozen-entry-probe", releaseReady = false,
            baseId = identity.GetProperty("baseId").GetString(), records }, Json));
        return RunPlayer(build, output, planPath, snapshot.Sha256);
    }

    public static int Replay(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("replay-frozen-entry <existing proof root> <new output> <complete assembly order, comma separated>");
        string proof = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]), build = Path.Combine(proof, "base");
        if (Directory.Exists(output)) throw new IOException("Replay output must be new.");
        string sourcePlan = Path.Combine(proof, "frozen-entry-plan.json"), sourceEvidence = Path.Combine(proof, "frozen-entry-evidence.json");
        var evidence = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(sourceEvidence));
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        void Bind(string path, string property)
        {
            if (!Hash(path).Equals(evidence.GetProperty(property).GetString(), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Replay input changed: " + path);
        }
        Bind(sourcePlan, "planSha256");
        Bind(Path.Combine(build, "player/Snapshot.exe"), "playerHash");
        Bind(Path.Combine(build, "player/GameAssembly.dll"), "gameHash");
        if (!evidence.GetProperty("unchanged").GetBoolean()) throw new InvalidDataException("Prior Player identity was not stable.");
        var plan = JsonNode.Parse(File.ReadAllText(sourcePlan))!;
        if (plan["format"]!.GetValue<string>() != "hybridclr.frozen-entry-probe" || plan["releaseReady"]!.GetValue<bool>())
            throw new InvalidDataException("Replay requires a research probe plan.");
        var records = plan["records"]!.AsArray().ToDictionary(row => row!["name"]!.GetValue<string>(), StringComparer.Ordinal);
        string[] order = args[2].Split(',');
        if (order.Length != records.Count || order.Distinct(StringComparer.Ordinal).Count() != order.Length || order.Any(name => !records.ContainsKey(name)))
            throw new InvalidDataException("Replay order must be a permutation of all original sources.");
        plan["records"] = new JsonArray(order.Select(name => JsonNode.Parse(records[name]!.ToJsonString())).ToArray());
        Directory.CreateDirectory(output);
        string planPath = Path.Combine(output, "frozen-entry-plan.json");
        File.WriteAllText(planPath, plan.ToJsonString(Json));
        return RunPlayer(build, output, planPath, evidence.GetProperty("snapshotSha256").GetString()!, Hash(sourceEvidence));
    }

    private static int RunPlayer(string build, string output, string planPath, string snapshotSha256, string sourceEvidenceSha256 = null)
    {
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        string nativeManifestPath = Path.Combine(build, "native/dhe-native-manifest.json");
        string player = Path.Combine(build, "player/Snapshot.exe"), game = Path.Combine(build, "player/GameAssembly.dll");
        string playerHash = Hash(player), gameHash = Hash(game), result = Path.Combine(output, "frozen-entry-result.json");
        var start = new ProcessStartInfo(player) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        foreach (string argument in new[] { "-batchmode", "-nographics", "-frozenEntryPlan", planPath, "-frozenEntryResult", result, "-logFile", result + ".log" }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start); Console.WriteLine("Frozen entry Player PID " + process.Id);
        if (!process.WaitForExit(120000)) { process.Kill(true); process.WaitForExit(); throw new TimeoutException("Frozen entry Player"); }
        bool unchanged = playerHash == Hash(player) && gameHash == Hash(game);
        var playerResult = File.Exists(result) ? JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(result)) : default;
        bool passed = unchanged && process.ExitCode == 0 && playerResult.ValueKind == JsonValueKind.Object && playerResult.GetProperty("passed").GetBoolean();
        File.WriteAllText(Path.Combine(output, "frozen-entry-evidence.json"), JsonSerializer.Serialize(new { passed, unchanged,
            pid = process.Id, exitCode = process.ExitCode, playerHash, gameHash, planSha256 = Hash(planPath),
            resultSha256 = File.Exists(result) ? Hash(result) : null, nativeManifestSha256 = Hash(nativeManifestPath),
            snapshotSha256, sourceEvidenceSha256, hostSha256 = Hash(typeof(FrozenEntryWorkflow).Assembly.Location),
            scope = "Research public native source transaction and direct ordinary AOT entries; not resource release admission or performance qualification" }, Json));
        return passed ? 0 : 1;
    }
}
