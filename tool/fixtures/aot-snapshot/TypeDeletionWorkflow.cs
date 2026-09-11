using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;

internal static class TypeDeletionWorkflow
{
    private const string Prefix = "DHE type deletion check: ";
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
        if (args.Length != 3) throw new ArgumentException("type-deletion-reference <Current DLL root> <Base Native DLL> <new output>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        ValidateCurrent(Path.Combine(args[0], "HybridCLR.ValueLayoutModel.dll"));
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
            passed, checks, observed, expected = Expected, error, priorResultSha256 = Hash(Path.Combine(previous, "result.json")),
            logSha256 = Hash(log), hostSha256 = Hash(typeof(TypeDeletionWorkflow).Assembly.Location),
            scope = "CLR actual type deletion, retained child and complete framework/virtual/business sequences"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Type deletion reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error);
        return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("type-deletion-replay <lab> <tool.dll> <Base proof> <shared resource> <new output>");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        ValidateCurrent(Path.Combine(args[3], "current/HybridCLR.ValueLayoutModel.dll"));
        Directory.CreateDirectory(output); string previous = Path.Combine(output, "framework");
        int prior = -1; string error = null;
        try { prior = FrameworkCallbackWorkflow.Replay(args.Take(4).Append(previous).ToArray()); }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(previous, "virtual-business/player.json.log"), result = Path.Combine(previous, "result.json");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        string[] observed = Observed(lines);
        bool passed = prior == 0 && error == null && observed.SequenceEqual(Expected) &&
            lines.Count(line => line == "DHE type deletion pass: " + Expected.Length) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks = observed, expected = Expected, error,
            priorResultSha256 = File.Exists(result) ? Hash(result) : null,
            logSha256 = File.Exists(log) ? Hash(log) : null, hostSha256 = Hash(typeof(TypeDeletionWorkflow).Assembly.Location),
            scope = "Immutable Player actual type deletion and complete framework/virtual/business sequence; cold objects only"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Type deletion replay: " + passed + "; checks=" + observed.Length);
        return passed ? 0 : 1;
    }
}
