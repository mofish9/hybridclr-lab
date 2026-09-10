using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ParentEvolutionWorkflow
{
    private static readonly string[] Expected = {
        "current-direct-parent", "root-ancestry", "runtime-middle-cast", "parent-constructor-once",
        "inherited-default-fields", "original-field-offsets", "inherited-field-owner", "inherited-reflection-read",
        "inherited-reflection-write", "inherited-method-invoke", "virtual-dispatch-through-root",
        "generic-dispatch-through-root", "root-method-definition", "parent-reference-survives-gc",
        "independent-instances", "parent-constructor-exception"
    };
    private const string Prefix = "DHE parent evolution check: ";
    private static readonly string[] MemberExpected = Expected.Concat(new[] {
        "inherited-field-reflected-type", "inherited-method-owner", "declared-only-inherited-fields-absent",
        "declared-only-inherited-methods-absent", "inherited-property-owner", "inherited-property-read-write",
        "declared-only-inherited-properties-absent", "inherited-event-owner", "inherited-event-add-remove",
        "declared-only-inherited-events-absent", "nonpublic-inherited-members-filtered", "inherited-static-flatten-hierarchy"
    }).ToArray();
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Observed(IEnumerable<string> lines) => lines.Where(line => line.StartsWith(Prefix))
        .Select(line => line[Prefix.Length..]).ToArray();

    internal static int Reference(string[] args)
    {
        bool members = args.Length == 4 && args[3] == "members";
        if (args.Length != 3 && !members) throw new ArgumentException("parent-evolution-reference <Current DLL root> <Base Native DLL> <new output> [members]");
        string[] expected = members ? MemberExpected : Expected;
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Reference output must be new.");
        Directory.CreateDirectory(output);
        string previous = Path.Combine(output, "framework");
        int prior = FrameworkCallbackWorkflow.Reference(new[] { args[0], args[1], previous });
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(value => value.GetName().Name == "HybridCLR.ValueLayoutModel");
        string error = null; string[] checks = Array.Empty<string>();
        using var trace = new StringWriter(); var original = Console.Out;
        try
        {
            Console.SetOut(trace);
            checks = (string[])assembly.GetType("HybridCLR.Lab.ParentEvolution.Cases", true)!.GetMethod("Run")!.Invoke(null, null)!;
        }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "parent.log"); File.WriteAllText(log, trace.ToString());
        string[] observed = Observed(trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')));
        bool passed = prior == 0 && error == null && checks.SequenceEqual(expected) && observed.SequenceEqual(expected);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, observed, expected, members, error, priorResultSha256 = Hash(Path.Combine(previous, "result.json")),
            logSha256 = Hash(log), hostSha256 = Hash(typeof(ParentEvolutionWorkflow).Assembly.Location),
            scope = "CLR parent evolution plus 18 framework, 25 virtual-signature and 46 business checks"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Parent evolution reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error);
        return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        bool members = args.Length == 6 && args[5] == "members";
        if (args.Length != 5 && !members) throw new ArgumentException("parent-evolution-replay <lab> <tool.dll> <Base proof> <shared resource> <new output> [members]");
        string[] expected = members ? MemberExpected : Expected;
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Replay output must be new.");
        Directory.CreateDirectory(output);
        string framework = Path.Combine(output, "framework");
        int prior = -1; string error = null;
        try { prior = FrameworkCallbackWorkflow.Replay(args.Take(4).Append(framework).ToArray()); }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(framework, "virtual-business/player.json.log"), previous = Path.Combine(framework, "result.json");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        string[] observed = Observed(lines);
        bool passed = prior == 0 && error == null && observed.SequenceEqual(expected) &&
            lines.Count(line => line == "DHE parent evolution pass: " + expected.Length) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks = observed, expected, members, error,
            priorResultSha256 = File.Exists(previous) ? Hash(previous) : null,
            logSha256 = File.Exists(log) ? Hash(log) : null,
            hostSha256 = Hash(typeof(ParentEvolutionWorkflow).Assembly.Location),
            scope = "Immutable Player parent evolution with complete framework/virtual/business sequence"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Parent evolution replay: " + passed + "; checks=" + observed.Length);
        return passed ? 0 : 1;
    }
}
