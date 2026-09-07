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
checks["new-type-declarations-capability-required"] = comparison.RequiredRuntimeCapabilities
    .Contains("supplemental-type-declarations-v1");
checks["new-type-declarations-old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v5",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-type-declarations-v1"),
    comparison.RequiredRuntimeCapabilities);
checks["supplemental-generic-invocation-capability-required"] = comparison.RequiredRuntimeCapabilities
    .Contains("supplemental-method-generic-invocation-v1");
checks["supplemental-generic-invocation-old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v4",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-method-generic-invocation-v1"),
    comparison.RequiredRuntimeCapabilities);
checks["unresolved-generic-stubs-capability-required"] = comparison.RequiredRuntimeCapabilities
    .Contains("supplemental-generic-unresolved-stubs-v1");
string[] v6Capabilities = ResourceUpdateCompatibility.KnownRuntimeCapabilities
    .Where(value => value != "supplemental-generic-unresolved-stubs-v1").ToArray();
checks["unresolved-generic-stubs-old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v6", v6Capabilities,
    comparison.RequiredRuntimeCapabilities);
ResourceUpdateCompatibility unity2021 = ResourceUpdateCompatibility.Analyze(before, after,
    usesUnresolvedCallStubs: false);
checks["unity2021-does-not-require-unresolved-stub-fix"] = unity2021.Compatible &&
    !unity2021.RequiredRuntimeCapabilities.Contains("supplemental-generic-unresolved-stubs-v1") &&
    ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
        ResourceUpdateCompatibility.CurrentNativeRuntimeContract, v6Capabilities, unity2021.RequiredRuntimeCapabilities);
checks["local-attribute-constructor-types-scanned"] = after.LocalAttributeConstructorTypeNames
    .Contains("HybridCLR.Lab.ManagedCasesAot.DheMetadataMarkerAttribute");
checks["external-attribute-constructors-not-local"] = !after.LocalAttributeConstructorTypeNames
    .Contains("System.Runtime.CompilerServices.AsyncStateMachineAttribute");
checks["compiler-generated-base-attributes-also-require-fix"] =
    after.LocalAttributeConstructorTypeNames.Contains("System.Runtime.CompilerServices.NullableAttribute") &&
    before.Types.Any(type => type.Identity == "System.Runtime.CompilerServices.NullableAttribute") &&
    comparison.RequiredRuntimeCapabilities.Contains("homologous-attribute-constructors-v1");
ResourceUpdateCompatibility evolvedNoOp = ResourceUpdateCompatibility.Analyze(after, after);
checks["evolved-base-attribute-capability-required"] = evolvedNoOp.Compatible &&
    evolvedNoOp.RequiredRuntimeCapabilities.Contains("homologous-attribute-constructors-v1");
checks["evolved-base-old-attribute-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v7",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "homologous-attribute-constructors-v1"),
    evolvedNoOp.RequiredRuntimeCapabilities);
checks["new-type-base-reference-old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v3",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-type-base-references-v1"),
    comparison.RequiredRuntimeCapabilities);
checks["old-runtime-capabilities-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v2",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-method-custom-attributes-v1" &&
        value != "assembly-reference-evolution-v1"), comparison.RequiredRuntimeCapabilities);
ResourceUpdateCompatibility noOp = ResourceUpdateCompatibility.Analyze(before, before);
checks["noop-does-not-require-new-method-capabilities"] = noOp.Compatible &&
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
