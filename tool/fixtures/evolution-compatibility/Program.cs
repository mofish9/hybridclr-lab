using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

if (args.Length != 3) throw new ArgumentException("Pass baseline DLL, evolution DLL, and new output root.");
string baseline = Path.GetFullPath(args[0]);
string current = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Test output already exists.");
Directory.CreateDirectory(output);
var before = MetaVersionSnapshot.Create(baseline);
var after = MetaVersionSnapshot.Create(current);
var checks = new Dictionary<string, bool>();
ResourceUpdateCompatibility comparison = ResourceUpdateCompatibility.Analyze(before, after);
checks["evolution-compatible"] = comparison.Compatible;
checks["method-attributes-capability-required"] = comparison.RequiredRuntimeCapabilities
    .Contains("supplemental-method-custom-attributes-v1");
checks["reference-evolution-capability-required"] = comparison.RequiredRuntimeCapabilities
    .Contains("assembly-reference-evolution-v1");
checks["new-type-base-reference-capability-required"] = comparison.RequiredRuntimeCapabilities
    .Contains("supplemental-type-base-references-v1");
checks["new-type-base-reference-old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v3",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-type-base-references-v1"),
    comparison.RequiredRuntimeCapabilities);
checks["old-runtime-capabilities-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v2",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-method-custom-attributes-v1" &&
        value != "assembly-reference-evolution-v1"), comparison.RequiredRuntimeCapabilities);
ResourceUpdateCompatibility noOp = ResourceUpdateCompatibility.Analyze(before, before);
checks["noop-retains-old-runtime-support"] = noOp.Compatible &&
    ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v2",
        ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-method-custom-attributes-v1" &&
            value != "assembly-reference-evolution-v1"), noOp.RequiredRuntimeCapabilities);

string Mutate(string label, Action<ModuleDefMD> change)
{
    string path = Path.Combine(output, label + ".dll");
    using var module = ModuleDefMD.Load(current);
    change(module);
    module.Write(path);
    return path;
}
string identityChange = Mutate("reference-identity", module =>
{
    AssemblyRef reference = module.GetAssemblyRefs().First(item => before.AssemblyReferences.ContainsKey(item.Name.String));
    reference.Version = new Version(reference.Version.Major + 1, 0, 0, 0);
});
checks["retained-reference-identity-rejected"] = ResourceUpdateCompatibility.Analyze(before,
    MetaVersionSnapshot.Create(identityChange)).UnsupportedChanges.Any(value =>
        value.StartsWith("existing-assembly-reference-identity-change:", StringComparison.Ordinal));
string scopeChange = Mutate("reference-scope", module =>
{
    TypeRef type = module.GetTypeRefs().First(item => before.TypeReferenceScopes.ContainsKey(item.FullName) &&
        item.ResolutionScope is AssemblyRef);
    type.ResolutionScope = new AssemblyRefUser("Dhe.Rebound", new Version(1, 0, 0, 0));
});
checks["retained-type-scope-rejected"] = ResourceUpdateCompatibility.Analyze(before,
    MetaVersionSnapshot.Create(scopeChange)).UnsupportedChanges.Any(value =>
        value.StartsWith("existing-type-reference-scope-change:", StringComparison.Ordinal));
string assemblyChange = Mutate("assembly-identity", module => module.Assembly.Version = new Version(2, 0, 0, 0));
checks["assembly-identity-still-rejected"] = ResourceUpdateCompatibility.Analyze(before,
    MetaVersionSnapshot.Create(assemblyChange)).UnsupportedChanges.Any(value =>
        value.StartsWith("assembly-or-module-metadata-change:", StringComparison.Ordinal));
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, baseline, current, checks, unsupported = comparison.UnsupportedChanges,
    requiredCapabilities = comparison.RequiredRuntimeCapabilities,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
