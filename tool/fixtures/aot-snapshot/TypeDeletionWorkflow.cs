using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class TypeDeletionWorkflow
{
    private const string Prefix = "DHE type deletion check: ";
    internal static readonly string[] CacheExpected = {
        "cached-type-name-stable", "fresh-lookup-absent", "cached-child-type-stable", "logical-parent-removed",
        "physical-parent-retained", "current-parent-rejected", "cached-field-owner-stable", "cached-field-read-old",
        "cached-field-write-old", "cached-field-rejects-current", "cached-field-write-rejects-current", "cached-field-rejects-unrelated",
        "cached-method-removed", "cached-delegate-removed", "cached-property-get-removed", "cached-property-set-removed",
        "cached-event-add-removed", "cached-event-remove-removed", "cached-event-raise-removed", "cached-constructor-removed",
        "fresh-removed-members-absent", "old-own-fields-retained", "current-own-fields", "changed-body-rejects-old",
        "changed-body-accepts-current", "deleted-reference-survives-gc", "removed-marker-lookup-absent",
        "base-event-effects-stable", "old-field-restored", "unchanged-aot-sentinel"
    };
    private static readonly string[] Expected = {
        "assembly-lookup-absent", "assembly-ignore-case-absent", "assembly-throwing-lookup",
        "qualified-lookup-absent", "qualified-throwing-lookup", "all-types-exclude-deleted",
        "exported-types-exclude-deleted", "defined-types-exclude-deleted", "companion-type-absent",
        "current-parent", "root-assignability", "deleted-members-absent", "own-field-layout",
        "own-field-reflection", "construction-reflection", "root-virtual", "interface-virtual",
        "generic-dispatch", "root-method-definition", "reflection-virtual", "reference-survives-gc",
        "independent-objects"
    };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Observed(IEnumerable<string> lines) => lines.Where(line => line.StartsWith(Prefix))
        .Select(line => line[Prefix.Length..]).ToArray();

    internal static void ValidateCurrent(string model)
    {
        using var module = ModuleDefMD.Load(model);
        const string deleted = "HybridCLR.Lab.ParentEvolution.";
        if (module.GetTypes().Any(type => type.FullName.StartsWith(deleted, StringComparison.Ordinal)) ||
            module.GetTypeRefs().Any(type => type.FullName.StartsWith(deleted, StringComparison.Ordinal)))
            throw new InvalidDataException("Deleted definitions or references remain in Current.");
        if (module.Find("HybridCLR.Lab.VirtualSignatures.Processor", false)?.BaseType?.FullName !=
            "HybridCLR.Lab.VirtualSignatures.ProcessorRoot")
            throw new InvalidDataException("The retained child must directly inherit Root.");
    }

    internal static int Reference(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("type-deletion-reference <Current DLL root> <Base Native DLL> <new output> <Unity CoreModule DLL>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        ValidateCurrent(Path.Combine(args[0], "HybridCLR.ValueLayoutModel.dll"));
        // The complete assembly also contains the existing MonoBehaviour cases.
        // Full type enumeration needs their actual Unity reference definitions,
        // although this CLR oracle does not invoke Unity native functionality.
        string unityCore = Path.GetFullPath(args[3]);
        AssemblyLoadContext.Default.LoadFromAssemblyPath(unityCore);
        Directory.CreateDirectory(output); string previous = Path.Combine(output, "framework");
        int prior = FrameworkCallbackWorkflow.Reference(new[] { args[0], args[1], previous });
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(value => value.GetName().Name == "HybridCLR.ValueLayoutModel");
        string error = null; string[] checks = Array.Empty<string>();
        using var trace = new StringWriter(); var original = Console.Out;
        try
        {
            Console.SetOut(trace);
            checks = (string[])assembly.GetType("HybridCLR.Lab.TypeDeletion.Cases", true)!.GetMethod("Run")!.Invoke(null, null)!;
        }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "deletion.log"); File.WriteAllText(log, trace.ToString());
        string[] observed = Observed(trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')));
        bool passed = prior == 0 && error == null && checks.SequenceEqual(Expected) && observed.SequenceEqual(Expected);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, observed, expected = Expected, error, unityCore, unityCoreSha256 = Hash(unityCore),
            priorResultSha256 = Hash(Path.Combine(previous, "result.json")),
            logSha256 = Hash(log), hostSha256 = Hash(typeof(TypeDeletionWorkflow).Assembly.Location),
            scope = "CLR actual type deletion, retained child and complete framework/virtual/business sequences"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Type deletion reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error);
        return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        bool cached = args.Length == 6 && args[5] == "cached";
        if (args.Length != 5 && !cached) throw new ArgumentException("type-deletion-replay <lab> <tool.dll> <Base proof> <shared resource> <new output> [cached]");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        ValidateCurrent(Path.Combine(args[3], "current/HybridCLR.ValueLayoutModel.dll"));
        Directory.CreateDirectory(output); string previous = Path.Combine(output, "framework");
        int prior = -1; string error = null;
        try
        {
            var previousArgs = args.Take(4).Append(previous);
            prior = FrameworkCallbackWorkflow.Replay((cached ? previousArgs.Append("type-deletion-cached") : previousArgs).ToArray());
        }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(previous, "virtual-business/player.json.log"), result = Path.Combine(previous, "result.json");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        string[] observed = Observed(lines);
        bool passed = prior == 0 && error == null && observed.SequenceEqual(Expected) &&
            lines.Count(line => line == "DHE type deletion pass: " + Expected.Length) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, cached, checks = observed, expected = Expected, error,
            priorResultSha256 = File.Exists(result) ? Hash(result) : null,
            logSha256 = File.Exists(log) ? Hash(log) : null, hostSha256 = Hash(typeof(TypeDeletionWorkflow).Assembly.Location),
            scope = "Immutable Player actual type deletion and complete framework/virtual/business sequence; optional explicit pre-load receiver/member cache"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Type deletion replay: " + passed + "; checks=" + observed.Length);
        return passed ? 0 : 1;
    }

    internal static int Audit(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("type-deletion-audit <shared workflow> <original Current root> <new audit file>");
        if (File.Exists(args[2])) throw new IOException("Audit output must be new.");
        string commonAudit = Path.GetFullPath(args[2]) + ".resource.json";
        if (FrozenResourceAudit.Run(new[] { args[0], args[1], commonAudit }) != 0) return 1;
        string model = Path.Combine(args[1], "HybridCLR.ValueLayoutModel.dll");
        ValidateCurrent(model);
        var current = MetaVersionSnapshot.Create(model);
        var currentTypes = current.Types.Select(type => type.StableId).ToHashSet(StringComparer.Ordinal);
        var currentMethods = current.Methods.Select(method => method.StableId).ToHashSet(StringComparer.Ordinal);
        JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        var workflow = Read(Path.Combine(args[0], "result.json"));
        string validationPath = Path.Combine(args[0], "resource/dhe-resource-update-validation.json");
        var validation = Read(validationPath);
        var records = new List<object>(); bool existingParent = false, rootOnly = false;
        foreach (var player in workflow.GetProperty("players").EnumerateArray())
        {
            string proof = player.GetProperty("proof").GetString()!;
            string identityPath = Path.Combine(proof, "base/build-identity.json");
            string baseId = Read(identityPath).GetProperty("baseId").GetString()!;
            string baselinePath = Path.Combine(proof, "base/baseline/HybridCLR.ValueLayoutModel.dll");
            var baseline = MetaVersionSnapshot.Create(baselinePath);
            var removedTypes = baseline.Types.Where(type => !currentTypes.Contains(type.StableId)).ToArray();
            var removedMethods = baseline.Methods.Where(method => !currentMethods.Contains(method.StableId)).ToArray();
            var selected = validation.GetProperty("bases").EnumerateArray().Single(row => row.GetProperty("baseId").GetString() == baseId);
            var assembly = selected.GetProperty("assemblies").EnumerateArray()
                .Single(row => row.GetProperty("assemblyName").GetString() == current.AssemblyName);
            bool containsParent = baseline.Types.Any(type => type.Identity == "HybridCLR.Lab.ParentEvolution.ProcessorMiddle");
            bool hasDeletionCapability = selected.GetProperty("requiredRuntimeCapabilities").EnumerateArray()
                .Any(value => value.GetString() == "removed-types-v1");
            if (!assembly.GetProperty("compatible").GetBoolean() ||
                assembly.GetProperty("removedTypeCount").GetInt32() != removedTypes.Length ||
                assembly.GetProperty("removedMethodCount").GetInt32() != removedMethods.Length ||
                removedTypes.Any(type => !type.Identity.StartsWith("HybridCLR.Lab.ParentEvolution.", StringComparison.Ordinal)) ||
                (containsParent && (removedTypes.Length == 0 || !hasDeletionCapability)) ||
                (!containsParent && removedTypes.Length != 0))
                throw new InvalidDataException("The resource plan does not match actual Base type/method deletion: " + baseId);
            existingParent |= containsParent; rootOnly |= !containsParent;
            records.Add(new { baseId, containsParent, baselinePath, baselineSha256 = Hash(baselinePath),
                identitySha256 = Hash(identityPath), removedTypes = removedTypes.Select(type => type.Identity).ToArray(),
                removedMethodCount = removedMethods.Length, hasDeletionCapability });
        }
        bool passed = existingParent && rootOnly;
        File.WriteAllText(args[2], JsonSerializer.Serialize(new {
            passed, existingParent, rootOnly, records, commonAuditSha256 = Hash(commonAudit),
            validationSha256 = Hash(validationPath), currentSha256 = Hash(model),
            hostSha256 = Hash(typeof(TypeDeletionWorkflow).Assembly.Location),
            scope = "Independent resource identity audit plus actual deletion on existing-parent Base and absence control on root-only Base"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Type deletion plan audit: " + passed + "; bases=" + records.Count);
        return passed ? 0 : 1;
    }
}
