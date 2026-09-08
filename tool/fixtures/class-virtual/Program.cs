using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

string[] names = { "HybridCLR.ManagedCases", "HybridCLR.CrossAssemblyDerived", "HybridCLR.ManagedCasesAot", "HybridCLR.MetadataStress" };
const string owner = "HybridCLR.CrossAssemblyDerived";
const string prefix = "HybridCLR.Lab.CrossAssemblyDerived.";
var json = new JsonSerializerOptions { WriteIndented = true };
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
string Output(string path)
{
    path = Path.GetFullPath(path);
    if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Output must be new: " + path);
    Directory.CreateDirectory(path);
    return path;
}
string Run(string command, string root, params string[] arguments)
{
    var start = new ProcessStartInfo(command) { WorkingDirectory = root, UseShellExecute = false, RedirectStandardOutput = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException(command);
    string result = process.StandardOutput.ReadToEnd();
    process.WaitForExit();
    if (process.ExitCode != 0) throw new InvalidOperationException(command + " failed: " + result);
    return result.Trim();
}
if (args.Length == 5 && args[0] == "build" && args[1] is "base" or "current")
{
    string lab = Path.GetFullPath(args[2]), seed = Path.GetFullPath(args[3]), output = Output(args[4]);
    string define = "HYBRIDCLR_TARGET_WINDOWS;DHE_CURRENT;DHE_STRUCTURE_CURRENT;DHE_EVOLUTION_CURRENT;" +
        "DHE_GENERIC_FIELDS_CURRENT;DHE_FIELD_ADDRESSES_CURRENT;DHE_INTERFACE_EVOLUTION_CURRENT;" +
        "DHE_CROSS_INTERFACE_CURRENT;DHE_GENERIC_INTERFACE_BASE;DHE_GENERIC_INTERFACE_CURRENT;DHE_CLASS_VIRTUAL_BASE" +
        (args[1] == "current" ? ";DHE_CLASS_VIRTUAL_CURRENT" : "");
    foreach (string project in new[] { owner, "HybridCLR.ManagedCasesAot" })
        Console.WriteLine(Run("dotnet", lab, "build", Path.Combine(lab, "managed-cases", project, project + ".csproj"),
            "-c", "Release", "--output", Path.Combine(output, project), "--nologo", "--no-incremental", "-v:minimal",
            "-p:DefineConstants=" + (define + (project == "HybridCLR.ManagedCasesAot" ? ";HYBRIDCLR_AOT_BENCHMARK" : "")).Replace(";", "%3B")));
    foreach (string name in names.Append("HybridCLR.BoundaryContracts"))
        File.Copy(Path.Combine(name == "HybridCLR.MetadataStress" ? seed : Path.Combine(output,
            name == "HybridCLR.ManagedCasesAot" ? name : owner), name + ".dll"), Path.Combine(output, name + ".dll"));
    File.WriteAllText(Path.Combine(output, "build.json"), JsonSerializer.Serialize(new
    {
        variant = args[1], lab, seed, define, sourceHead = Run("git", lab, "rev-parse", "HEAD"),
        sourceChanges = Run("git", lab, "status", "--porcelain"),
        inputs = names.Select(name => new { name, sha256 = Hash(Path.Combine(output, name + ".dll")) }),
    }, json));
    return 0;
}
if (args.Length == 3 && args[0] == "reference")
{
    string input = Path.GetFullPath(args[1]), output = Output(args[2]);
    AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(input, name.Name + ".dll"))
        ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(input, name.Name + ".dll")) : null;
    var probe = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(input, owner + ".dll")).GetType(prefix + "ClassVirtualEvolutionProbe", true)!;
    var checks = new Dictionary<string, string>();
    foreach (var method in probe.GetMethods(BindingFlags.Public | BindingFlags.Static).OrderBy(method => method.Name == "ConcurrentFirstTouch" ? "0" : method.Name))
    {
        try { method.Invoke(null, null); checks[method.Name] = "passed"; }
        catch (Exception exception) { checks[method.Name] = exception.ToString(); }
    }
    File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
    {
        scope = "CLR reference only; not native DHE execution", passed = checks.Values.All(value => value == "passed"),
        inputSha256 = Hash(Path.Combine(input, owner + ".dll")), checks,
    }, json));
    foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
    return checks.Values.All(value => value == "passed") ? 0 : 1;
}
if (args.Length is not (4 or 5) || args[0] != "analyze")
    throw new ArgumentException("build base|current <lab> <seed> <new output>; reference <DLLs> <new output>; analyze <Base> <Current> <new output> [expect-rejection]");
var baseline = names.ToDictionary(name => name, name => MetaVersionSnapshot.Create(Path.Combine(args[1], name + ".dll")));
var current = names.ToDictionary(name => name, name => MetaVersionSnapshot.Create(Path.Combine(args[2], name + ".dll")));
var analyses = names.ToDictionary(name => name, name => ResourceUpdateCompatibility.Analyze(baseline[name], current[name], currentAssemblySet: current.Values));
var checksAnalysis = new Dictionary<string, bool>();
foreach (var method in baseline[owner].Methods.Where(method => method.DeclaringType == prefix + "ClassVirtualNativeCallers"))
    checksAnalysis["retained-caller-" + method.Name] = current[owner].Methods.Single(after => after.StableId == method.StableId).Version == method.Version;
checksAnalysis["all-eight-callers-present"] = checksAnalysis.Count == 8;
checksAnalysis["existing-root"] = baseline[owner].Types.Any(type => type.Identity == prefix + "RevisionVirtualRoot");
checksAnalysis["new-virtual-on-existing-root"] = current[owner].Methods.Any(method => method.DeclaringType == prefix + "RevisionVirtualRoot" && method.Name == "Inserted" && method.IsVirtual) &&
    !baseline[owner].Methods.Any(method => method.DeclaringType == prefix + "RevisionVirtualRoot" && method.Name == "Inserted");
bool expectedRejection = args.Length == 5 && args[4] == "expect-rejection";
checksAnalysis["compatibility-as-expected"] = expectedRejection
    ? analyses[owner].UnsupportedChanges.Any(change => change.StartsWith("added-virtual-abstract-or-pinvoke-method-on-existing-type:"))
    : analyses.Values.All(value => value.Compatible);
if (!expectedRejection)
{
    const string capability = "existing-class-virtual-methods-v1";
    checksAnalysis["class-virtual-capability-required"] = analyses[owner].RequiredRuntimeCapabilities.Contains(capability);
    checksAnalysis["older-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
        ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v26",
        ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), analyses[owner].RequiredRuntimeCapabilities);
    checksAnalysis["capable-runtime-accepted"] = ResourceUpdateCompatibility.CanExecuteUpdate(
        ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
        ResourceUpdateCompatibility.KnownRuntimeCapabilities, analyses[owner].RequiredRuntimeCapabilities);
    var noOp = ResourceUpdateCompatibility.Analyze(baseline[owner], baseline[owner], currentAssemblySet: baseline.Values);
    checksAnalysis["no-op-does-not-require-new-class-capability"] = !noOp.RequiredRuntimeCapabilities.Contains(capability);
}
string analysisOutput = Output(args[3]);
File.WriteAllText(Path.Combine(analysisOutput, "report.json"), JsonSerializer.Serialize(new
{
    passed = checksAnalysis.Values.All(value => value), expectedRejection, checks = checksAnalysis,
    analyses = analyses.Select(pair => new { assembly = pair.Key, pair.Value.Compatible, pair.Value.RequiredRuntimeCapabilities, pair.Value.UnsupportedChanges }),
    inputs = new[] { args[1], args[2] }.SelectMany(root => names.Select(name => new { path = Path.GetFullPath(Path.Combine(root, name + ".dll")), sha256 = Hash(Path.Combine(root, name + ".dll")) })),
}, json));
foreach (var check in checksAnalysis) Console.WriteLine(check.Key + ": " + check.Value);
return checksAnalysis.Values.All(value => value) ? 0 : 1;
