using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using HybridCLR.Lab;
using HybridCLR.Lab.ManagedCasesAot;

if (args.Length == 2 && args[0] == "inspect")
{
    Console.WriteLine(JsonSerializer.Serialize(DheDifferentialEvidence.Read(args[1]), new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
if (args.Length == 3 && args[0] == "instrument")
{
    DheDifferentialInstrumentation.Generate(args[1], args[2]);
    return 0;
}
if (args.Length != 6 || args[0] != "reference")
    throw new ArgumentException("Use instrument <prepared root> <new root>, or reference <case DLL> <support root> <new result binary> <manifest> <golden>.");
string input = Path.GetFullPath(args[1]), support = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
if (File.Exists(output) || File.Exists(output + ".identity.json")) throw new IOException("Reference output already exists.");
string entryRoot = output + ".entries";
if (Directory.Exists(entryRoot) || File.Exists(entryRoot)) throw new IOException("Reference entry receipts already exist.");
Environment.SetEnvironmentVariable(DheDifferentialWorkload.EntriesVariable, entryRoot);
string current = Path.GetDirectoryName(input)!;
AssemblyLoadContext.Default.Resolving += (context, name) =>
{
    foreach (string root in new[] { current, support })
    {
        string candidate = Path.Combine(root, name.Name + ".dll");
        if (File.Exists(candidate)) return context.LoadFromAssemblyPath(candidate);
    }
    return null;
};
AssemblyLoadContext.Default.LoadFromAssemblyPath(input);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
DheDifferentialWorkload.Run(output);
var result = DheDifferentialEvidence.Read(output);
using var manifest = JsonDocument.Parse(File.ReadAllText(args[4]));
using var golden = JsonDocument.Parse(File.ReadAllText(args[5]));
DheDifferentialEvidence.ValidateObservations(result, result, manifest.RootElement, golden.RootElement);
File.WriteAllText(output + ".identity.json", JsonSerializer.Serialize(new
{
    format = "hybridclr.dhe-differential-reference.json", schemaVersion = 1, passed = true,
    input, inputSha256 = DheDifferentialEvidence.Hash(input), resultSha256 = DheDifferentialEvidence.Hash(output),
    manifestSha256 = DheDifferentialEvidence.Hash(args[4]), goldenSha256 = DheDifferentialEvidence.Hash(args[5]),
    runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    runnerSha256 = DheDifferentialEvidence.Hash(Assembly.GetExecutingAssembly().Location),
    caseCount = result.Cases.Length,
    supportAssemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic &&
        !string.IsNullOrEmpty(assembly.Location) && (Path.GetDirectoryName(assembly.Location) == current ||
            Path.GetDirectoryName(assembly.Location) == support))
        .Select(assembly => new { path = assembly.Location, sha256 = DheDifferentialEvidence.Hash(assembly.Location) }),
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("CLR differential observations match golden: " + result.Cases.Length);
return 0;
