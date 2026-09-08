using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

string[] names = { "HybridCLR.ManagedCases", "HybridCLR.CrossAssemblyDerived", "HybridCLR.ManagedCasesAot", "HybridCLR.MetadataStress" };
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string NewOutput(string path)
{
    path = Path.GetFullPath(path);
    if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Output must be new: " + path);
    Directory.CreateDirectory(path);
    return path;
}
void Run(string executable, string[] arguments, string root)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("Could not start " + executable);
    if (!process.WaitForExit(300000)) { process.Kill(true); throw new TimeoutException(executable); }
    if (process.ExitCode != 0) throw new InvalidOperationException(executable + " exited " + process.ExitCode);
}
string Git(string root, params string[] arguments)
{
    var start = new ProcessStartInfo("git") { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException("Could not read source identity.");
    string value = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException("Cannot authenticate fixture source.");
    return value.Trim();
}
if (args.Length == 5 && args[0] == "build" && args[1] is "base" or "current")
{
    string lab = Path.GetFullPath(args[2]), seed = Path.GetFullPath(args[3]);
    if (!Directory.Exists(lab) || !names.All(name => File.Exists(Path.Combine(seed, name + ".dll"))))
        throw new IOException("Lab and complete immutable seed are required.");
    string output = NewOutput(args[4]);
    string define = "HYBRIDCLR_TARGET_WINDOWS;DHE_CURRENT;DHE_STRUCTURE_CURRENT;DHE_EVOLUTION_CURRENT;" +
        "DHE_GENERIC_FIELDS_CURRENT;DHE_FIELD_ADDRESSES_CURRENT;DHE_INTERFACE_EVOLUTION_CURRENT;" +
        "DHE_CROSS_INTERFACE_CURRENT;DHE_GENERIC_INTERFACE_BASE" +
        (args[1] == "current" ? ";DHE_GENERIC_INTERFACE_CURRENT" : "");
    foreach (var project in new[] { ("HybridCLR.CrossAssemblyDerived", "dependencies", define),
        ("HybridCLR.ManagedCasesAot", "aot", define + ";HYBRIDCLR_AOT_BENCHMARK") })
    {
        Run("dotnet", new[] { "build", Path.Combine(lab, "managed-cases", project.Item1, project.Item1 + ".csproj"),
            "-c", "Release", "--output", Path.Combine(output, project.Item2), "--nologo", "--no-incremental", "-v:minimal",
            "-p:DefineConstants=" + project.Item3.Replace(";", "%3B", StringComparison.Ordinal) }, lab);
    }
    foreach (string name in names)
    {
        string root = name == "HybridCLR.MetadataStress" ? seed :
            Path.Combine(output, name == "HybridCLR.ManagedCasesAot" ? "aot" : "dependencies");
        File.Copy(Path.Combine(root, name + ".dll"), Path.Combine(output, name + ".dll"));
    }
    File.Copy(Path.Combine(output, "dependencies", "HybridCLR.BoundaryContracts.dll"),
        Path.Combine(output, "HybridCLR.BoundaryContracts.dll"));
    File.WriteAllText(Path.Combine(output, "build.json"), JsonSerializer.Serialize(new
    {
        variant = args[1], lab, seed, define,
        sourceHead = Git(lab, "rev-parse", "HEAD"), sourceTree = Git(lab, "rev-parse", "HEAD^{tree}"),
        sourceChanges = Git(lab, "status", "--porcelain"),
        assemblies = names.Select(name => new { name, sha256 = Hash(Path.Combine(output, name + ".dll")) }),
    }, jsonOptions));
    return 0;
}
if (args.Length != 4 || args[0] != "analyze")
    throw new ArgumentException("Use build base|current <lab> <immutable seed> <new output>, or analyze <Base DLLs> <Current DLLs> <new output>.");
string baselineRoot = Path.GetFullPath(args[1]), currentRoot = Path.GetFullPath(args[2]), analysisOutput = NewOutput(args[3]);
var baseline = names.ToDictionary(name => name, name => MetaVersionSnapshot.Create(Path.Combine(baselineRoot, name + ".dll")));
var current = names.ToDictionary(name => name, name => MetaVersionSnapshot.Create(Path.Combine(currentRoot, name + ".dll")));
const string owner = "HybridCLR.CrossAssemblyDerived";
const string contract = "HybridCLR.Lab.CrossAssemblyDerived.ICrossRevisionValue`1";
const string implementation = "HybridCLR.Lab.CrossAssemblyDerived.CrossRevisionValue`1";
const string child = "HybridCLR.Lab.CrossAssemblyDerived.CrossRevisionNativeChild";
const string caller = "HybridCLR.Lab.CrossAssemblyDerived.GenericInterfaceNativeCallers";
string[] Slots(string root)
{
    using var module = ModuleDefMD.Load(Path.Combine(root, owner + ".dll"));
    return module.GetTypes().Single(type => type.FullName == contract).Methods.Where(method => method.IsVirtual)
        .Select(method => method.Name.String).ToArray();
}
var analyses = names.ToDictionary(name => name, name => ResourceUpdateCompatibility.Analyze(baseline[name], current[name],
    currentAssemblySet: current.Values));
var checks = new Dictionary<string, bool>
{
    ["generic-interface-already-in-Base"] = baseline[owner].Types.Any(type => type.Identity == contract),
    ["Base-interface-slot-zero-RoundTrip"] = Slots(baselineRoot).SequenceEqual(new[] { "RoundTrip" }),
    ["Current-interface-slot-collision"] = Slots(currentRoot).SequenceEqual(new[] { "AddedValue", "EchoGeneric", "RoundTrip" }),
    ["native-child-has-closed-generic-parent"] = baseline[owner].TypeParents[child].DefinitionName == implementation &&
        baseline[owner].TypeParents[child].TypeName != implementation,
};
foreach (string name in new[] { "Integer", "Reference", "Value" })
{
    var before = baseline[owner].Methods.Single(method => method.DeclaringType == caller && method.Name == name);
    var after = current[owner].Methods.Single(method => method.StableId == before.StableId);
    checks["native-caller-unchanged-" + name] = before.Version == after.Version;
}
foreach (string name in names) checks[name + "-compatible"] = analyses[name].Compatible;
foreach (string capability in new[] { "existing-interface-method-slots-v1", "inherited-interface-dispatch-v1",
    "base-virtual-slots-on-current-descendants-v1" })
{
    checks[capability + "-required"] = analyses[owner].RequiredRuntimeCapabilities.Contains(capability);
    checks[capability + "-missing-Base-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
        ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
        ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability),
        analyses[owner].RequiredRuntimeCapabilities);
}
string routingPath = Path.Combine(analysisOutput, "caller-routing.txt");
var expectedRouting = new Dictionary<string, bool> { ["Integer"] = false, ["Reference"] = false, ["Value"] = false };
bool RoutingRejects()
{
    try { DheRetainedCallers.Validate(routingPath, expectedRouting); return false; }
    catch (InvalidDataException) { return true; }
}
checks["missing-caller-routing-rejected"] = RoutingRejects();
File.WriteAllLines(routingPath, new[] { "Integer\t0\t1\t0", "Reference\t0\t1\t0", "Value\t0\t1\t0" });
checks["retained-native-callers-accepted"] = DheRetainedCallers.Validate(routingPath, expectedRouting).Length == 3;
foreach (var invalid in new[] { "Integer\t1\t1\t1", "Integer\t0\t0\t1", "Integer\t0\t-1\t1", "Unknown\t0\t1\t1" })
{
    File.WriteAllLines(routingPath, new[] { invalid, "Reference\t0\t1\t0", "Value\t0\t1\t0" });
    checks["invalid-native-routing-rejected-" + invalid] = RoutingRejects();
}
File.WriteAllLines(routingPath, new[] { "Integer\t0\t1\t0", "Integer\t0\t1\t0", "Value\t0\t1\t0" });
checks["duplicate-native-routing-rejected"] = RoutingRejects();
File.WriteAllLines(routingPath, new[] { "Integer\t0\t1\t0", "Reference\t0\t1\t0" });
checks["incomplete-native-routing-rejected"] = RoutingRejects();
expectedRouting["Integer"] = expectedRouting["Reference"] = expectedRouting["Value"] = true;
File.WriteAllLines(routingPath, new[] { "Integer\t1\t1\t1", "Reference\t1\t1\t1", "Value\t1\t1\t1" });
checks["new-interpreted-callers-on-older-Base-accepted"] = DheRetainedCallers.Validate(routingPath, expectedRouting).Length == 3;
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(analysisOutput, "report.json"), JsonSerializer.Serialize(new
{
    passed, scope = "Actual generic-interface fixture identities and capability negotiation; not Player execution",
    inputs = new[] { baselineRoot, currentRoot }.SelectMany(root => names.Select(name =>
        new { path = Path.Combine(root, name + ".dll"), sha256 = Hash(Path.Combine(root, name + ".dll")) })),
    checks, analyses = analyses.Select(item => new { assembly = item.Key, item.Value.Compatible,
        item.Value.RequiredRuntimeCapabilities, item.Value.UnsupportedChanges }),
}, jsonOptions));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
