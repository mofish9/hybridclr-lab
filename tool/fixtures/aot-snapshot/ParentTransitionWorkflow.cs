using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

internal static class ParentTransitionWorkflow
{
    private const string Prefix = "DHE parent transition check: ";
    private static readonly string[] Common = {
        "current-direct-parent", "root-contract-retained", "former-parent-not-assignable", "former-parent-cast-rejected",
        "former-fields-not-inherited", "former-method-not-inherited", "former-property-not-inherited", "former-event-not-inherited",
        "former-constructor-not-run", "own-fields-retained", "own-field-reflection", "root-method-definition", "root-virtual-value",
        "interface-value", "generic-root-call", "reflection-construction", "former-field-rejects-current",
        "former-method-rejects-current", "former-property-rejects-current", "former-event-rejects-current", "rejection-preserves-own-data"
    };
    internal static readonly string[] CacheExpected = {
        "logical-type-stable", "parent-selection-changed", "current-own-fields", "cached-field-retains-old-storage",
        "cached-method-retains-old-storage", "cached-property-retains-old-storage", "cached-event-retains-old-storage",
        "cached-field-rejects-current", "cached-method-rejects-current", "cached-property-rejects-current", "cached-event-rejects-current",
        "selected-body-rejects-old-receiver", "selected-body-accepts-current", "rejection-preserves-both-objects"
    };
    private static string[] Expected(string mode) => mode == "removal" ? Common : mode == "replacement" ? Common.Concat(new[] {
        "replacement-constructor-count", "replacement-fields", "replacement-field-owner", "replacement-field-roundtrip",
        "replacement-method-invoke", "replacement-property-roundtrip", "replacement-event-roundtrip", "replacement-reference-survives-gc",
        "replacement-independent-instances", "replacement-constructor-exception"
    }).ToArray() : throw new ArgumentException("Mode must be removal or replacement.");
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Observed(IEnumerable<string> lines) => lines.Where(line => line.StartsWith(Prefix)).Select(line => line[Prefix.Length..]).ToArray();

    internal static int Reference(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("parent-transition-reference <Current root> <Base Native DLL> <new output> <removal|replacement>");
        string output = Path.GetFullPath(args[2]), mode = args[3]; var expected = Expected(mode);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output); string previous = Path.Combine(output, "framework");
        int prior = FrameworkCallbackWorkflow.Reference(new[] { args[0], args[1], previous });
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(value => value.GetName().Name == "HybridCLR.ValueLayoutModel");
        string error = null; string[] checks = Array.Empty<string>();
        using var trace = new StringWriter(); var original = Console.Out;
        try { Console.SetOut(trace); checks = (string[])assembly.GetType("HybridCLR.Lab.ParentTransitions.Cases", true)!.GetMethod("Run")!.Invoke(null, null)!; }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "parent.log"); File.WriteAllText(log, trace.ToString());
        string[] lines = trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')).ToArray(), observed = Observed(lines);
        bool passed = prior == 0 && error == null && checks.SequenceEqual(expected) && observed.SequenceEqual(expected) &&
            lines.Count(line => line == "DHE parent transition mode: " + mode) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, mode, checks, observed, expected, error, priorResultSha256 = Hash(Path.Combine(previous, "result.json")),
            logSha256 = Hash(log), hostSha256 = Hash(typeof(ParentTransitionWorkflow).Assembly.Location),
            scope = "CLR parent transition plus 18 framework,25 virtual-signature and46 business checks"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Parent transition reference: " + passed + "; " + mode + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error); return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        bool cached = args.Length == 7 && args[6] == "cached";
        if (args.Length != 6 && !cached) throw new ArgumentException("parent-transition-replay <lab> <tool.dll> <Base proof> <shared resource> <new output> <removal|replacement> [cached]");
        string output = Path.GetFullPath(args[4]), mode = args[5]; var expected = Expected(mode);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output); string framework = Path.Combine(output, "framework");
        int prior = -1; string error = null;
        try
        {
            var previousArgs = args.Take(4).Append(framework);
            prior = FrameworkCallbackWorkflow.Replay((cached ? previousArgs.Append("parent-transition-cached") : previousArgs).ToArray());
        }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(framework, "virtual-business/player.json.log"), previous = Path.Combine(framework, "result.json");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>(), observed = Observed(lines);
        bool passed = prior == 0 && error == null && observed.SequenceEqual(expected) &&
            lines.Count(line => line == "DHE parent transition mode: " + mode) == 1 &&
            lines.Count(line => line == "DHE parent transition pass: " + expected.Length) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, mode, cached, checks = observed, expected, error,
            priorResultSha256 = File.Exists(previous) ? Hash(previous) : null, logSha256 = File.Exists(log) ? Hash(log) : null,
            hostSha256 = Hash(typeof(ParentTransitionWorkflow).Assembly.Location),
            scope = "Immutable Player parent transition and complete framework/virtual/business sequence, with optional original receiver cache"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Parent transition replay: " + passed + "; " + mode + "; checks=" + observed.Length); return passed ? 0 : 1;
    }
}
