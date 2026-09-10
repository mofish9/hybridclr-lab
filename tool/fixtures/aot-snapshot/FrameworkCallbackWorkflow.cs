using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

internal static class FrameworkCallbackWorkflow
{
    private static readonly string[] Expected = {
        "array-sort-value-comparison", "list-sort-value-comparison", "list-sort-value-comparer",
        "array-sort-reference-comparison", "list-find-value-predicate", "list-convert-value-reference",
        "array-convert-value-reference", "list-foreach-value-action", "binary-search-value-comparer",
        "dictionary-value-comparer", "hashset-reference-comparer", "struct-target-comparison",
        "array-convert-existing-virtual-value", "list-convert-existing-virtual-reference",
        "array-convert-existing-generic-value", "task-existing-virtual-exception",
        "comparison-exception", "predicate-exception"
    };
    private const string Prefix = "DHE framework callback check: ";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static string[] Observed(IEnumerable<string> lines) => lines.Where(line => line.StartsWith(Prefix))
        .Select(line => line[Prefix.Length..]).ToArray();

    internal static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("framework-callback-reference <Current DLL root> <Base Native DLL> <new output>");
        string output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Reference output must be new.");
        Directory.CreateDirectory(output);
        string previous = Path.Combine(output, "virtual-business");
        int prior = VirtualSignatureWorkflow.Reference(new[] { args[0], args[1], previous });
        var assembly = AssemblyLoadContext.Default.Assemblies.Single(value => value.GetName().Name == "HybridCLR.ValueLayoutModel");
        string error = null; string[] checks = Array.Empty<string>();
        using var trace = new StringWriter(); var original = Console.Out;
        try
        {
            Console.SetOut(trace);
            checks = (string[])assembly.GetType("HybridCLR.Lab.FrameworkCallbacks.Cases", true)!.GetMethod("Run")!.Invoke(null, null)!;
        }
        catch (Exception exception) { error = (exception is TargetInvocationException wrapper ? wrapper.InnerException : exception)?.ToString(); }
        finally { Console.SetOut(original); }
        string log = Path.Combine(output, "framework.log"); File.WriteAllText(log, trace.ToString());
        string[] observed = Observed(trace.ToString().Split('\n').Select(line => line.TrimEnd('\r')));
        bool passed = prior == 0 && error == null && checks.SequenceEqual(Expected) && observed.SequenceEqual(Expected);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks, observed, expected = Expected, error, priorResultSha256 = Hash(Path.Combine(previous, "result.json")),
            logSha256 = Hash(log), hostSha256 = Hash(typeof(FrameworkCallbackWorkflow).Assembly.Location),
            scope = "CLR framework callback reference plus 25 virtual-signature and 46 business checks"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Framework callback reference: " + passed + "; checks=" + observed.Length);
        if (error != null) Console.WriteLine(error);
        return passed ? 0 : 1;
    }

    internal static int Replay(string[] args)
    {
        bool parentCache = args.Length == 6 && args[5] == "parent-transition-cached";
        if (args.Length != 5 && !parentCache) throw new ArgumentException("framework-callback-replay <lab> <tool.dll> <Base proof> <shared resource> <new output> [parent-transition-cached]");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Replay output must be new.");
        Directory.CreateDirectory(output);
        string player = Path.Combine(output, "virtual-business");
        int prior = -1; string error = null;
        try { prior = UnitySerializationWorkflow.Replay(args.Take(4).Concat(new[] { player, parentCache ? "parent-transition-cached" : "virtual-signatures" }).ToArray()); }
        catch (Exception exception) { error = exception.ToString(); }
        string log = Path.Combine(player, "player.json.log"), previous = Path.Combine(player, "result.json");
        string[] lines = File.Exists(log) ? File.ReadAllLines(log) : Array.Empty<string>();
        string[] observed = Observed(lines);
        bool passed = prior == 0 && error == null && observed.SequenceEqual(Expected) &&
            lines.Count(line => line == "DHE framework callback pass: " + Expected.Length) == 1;
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, checks = observed, expected = Expected, error,
            priorResultSha256 = File.Exists(previous) ? Hash(previous) : null,
            logSha256 = File.Exists(log) ? Hash(log) : null,
            hostSha256 = Hash(typeof(FrameworkCallbackWorkflow).Assembly.Location),
            scope = "Immutable Player framework callbacks plus full 25 virtual-signature and 46 business sequence; no inferred framework AOT coverage"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Framework callback replay: " + passed + "; checks=" + observed.Length);
        return passed ? 0 : 1;
    }
}
