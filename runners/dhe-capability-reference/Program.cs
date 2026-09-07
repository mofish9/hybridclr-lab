using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length < 2 || args.Length > 3 || (args.Length == 3 && args[2] != "--main-only"))
    throw new ArgumentException("Pass the fixture DLL, a new result JSON path, and optionally --main-only.");
string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (File.Exists(output)) throw new IOException("Reference output already exists.");
string root = Path.GetDirectoryName(input)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    string dependency = Path.Combine(root, name.Name + ".dll");
    return File.Exists(dependency) ? context.LoadFromAssemblyPath(dependency) : null;
};
string? error = null;
var observations = new Dictionary<string, int>();
try
{
    Assembly fixture = AssemblyLoadContext.Default.LoadFromAssemblyPath(input);
    Type calculator = fixture.GetType("HybridCLR.Lab.ManagedCasesAot.DheDemoCalculator", true)!;
    if (args.Length == 2)
    {
        Type assertions = fixture.GetType("HybridCLR.Lab.ManagedCasesAot.DheEvolutionAssertions", true)!;
        MethodInfo validate = assertions.GetMethod("Validate") ?? throw new MissingMethodException(assertions.FullName, "Validate");
        validate.Invoke(null, new[] { Activator.CreateInstance(calculator) });
    }
    foreach (var probe in new[] { ("addResult", "Add", 1), ("stableResult", "Stable", 2),
                 ("addViaStableResult", "AddViaStable", 2) })
        observations[probe.Item1] = Convert.ToInt32(calculator.GetMethod(probe.Item2)!.Invoke(null, new object[] { probe.Item3 }));
}
catch (Exception exception)
{
    error = exception.ToString();
}
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    format = "hybridclr.dhe-capability-reference.json", schemaVersion = 1,
    generatedAtUtc = DateTimeOffset.UtcNow, passed = error == null,
    scope = args.Length == 3 ? "main-observations-only" : "evolution-capabilities-and-main-observations",
    observations,
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    input, inputSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))),
    runnerSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))),
    assemblies = Directory.GetFiles(root, "*.dll").OrderBy(path => path, StringComparer.Ordinal)
        .Select(path => new { name = Path.GetFileName(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
    error,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(error == null ? "DHE capability reference passed." : error);
return error == null ? 0 : 1;
