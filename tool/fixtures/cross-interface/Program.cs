using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

if (args.Length != 3) throw new ArgumentException("Pass Base DLL root, prepared Current DLL root, and a new output directory.");
string baselineRoot = Path.GetFullPath(args[0]), currentRoot = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
string[] names = { "HybridCLR.ManagedCases", "HybridCLR.CrossAssemblyDerived", "HybridCLR.ManagedCasesAot", "HybridCLR.MetadataStress" };
var baseline = names.ToDictionary(name => name, name => MetaVersionSnapshot.Create(Path.Combine(baselineRoot, name + ".dll")));
var current = names.ToDictionary(name => name, name => MetaVersionSnapshot.Create(Path.Combine(currentRoot, name + ".dll")));
using var baseModule = ModuleDefMD.Load(Path.Combine(baselineRoot, names[0] + ".dll"));
using var currentModule = ModuleDefMD.Load(Path.Combine(currentRoot, names[0] + ".dll"));
using var derivedModule = ModuleDefMD.Load(Path.Combine(currentRoot, names[1] + ".dll"));
const string contractName = "HybridCLR.Lab.ManagedCases.ICrossAssemblyLazyVTableContract";
TypeDef Type(ModuleDef module, string name) => module.GetTypes().Single(type => type.FullName == name);
string[] Slots(ModuleDef module) => Type(module, contractName).Methods.Where(method => method.IsVirtual)
    .Select(method => method.Name.String).ToArray();
var checks = new Dictionary<string, bool>
{
    ["base-interface-original-slot"] = Slots(baseModule).SequenceEqual(new[] { "Compute" }),
    ["current-interface-slot-collision"] = Slots(currentModule).SequenceEqual(new[] { "Added", "EchoAdded", "Compute" }),
    ["generic-interface-method-declaration"] = Type(currentModule, contractName).Methods.Single(method => method.Name == "EchoAdded").GenericParameters.Count == 1,
};
foreach (var item in new[]
{
    (names[0], "HybridCLR.Lab.ManagedCases.CrossAssemblyLazyVTableBase"),
    (names[1], "HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableDerived"),
})
{
    var previous = baseline[item.Item1];
    var next = current[item.Item1];
    var oldType = previous.Types.Single(type => type.Identity == item.Item2);
    var newType = next.Types.Single(type => type.StableId == oldType.StableId);
    checks[item.Item2 + "-existing-layout"] = oldType.LayoutVersion == newType.LayoutVersion;
    foreach (string name in new[] { "Compute", "Describe" })
    {
        var method = previous.Methods.Single(method => method.DeclaringType == item.Item2 && method.Name == name);
        checks[item.Item2 + "-" + name + "-unchanged"] = next.Methods.Single(value => value.StableId == method.StableId).Version == method.Version;
    }
}
var oldCaller = baseline[names[1]].Methods.Single(method => method.DeclaringType == "HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe" && method.Name == "Run");
checks["cross-assembly-AOT-caller-unchanged"] = current[names[1]].Methods.Single(method => method.StableId == oldCaller.StableId).Version == oldCaller.Version;
var originalCases = baseline[names[0]].Methods.Where(method => method.ReturnType == "HybridCLR.Lab.ManagedCases.CaseObservation" &&
    method.ParameterTypes.Length == 0).ToArray();
var fingerprints = JsonSerializer.SerializeToElement(FingerprintDiagnostics.Analyze(
    Path.Combine(baselineRoot, names[0] + ".dll"), Path.Combine(currentRoot, names[0] + ".dll"),
    baseline[names[0]], current[names[0]]));
checks["all-220-case-entry-bodies-unchanged"] = originalCases.Length == 220 && fingerprints.GetProperty("allBodiesEqual").GetBoolean();
checks["all-220-case-entry-metadata-unchanged"] = fingerprints.GetProperty("allMetadataEqual").GetBoolean();
checks["no-unexplained-case-fingerprint-changes"] = fingerprints.GetProperty("allVersionsEqualAfterRvaOnlyNormalization").GetBoolean();
var explicitType = Type(derivedModule, "HybridCLR.Lab.CrossAssemblyDerived.CrossRevisionExplicit");
checks["explicit-declarations-cross-assembly-memberrefs"] = explicitType.Methods.Where(method => method.HasOverrides)
    .SelectMany(method => method.Overrides).Count(item => item.MethodDeclaration is MemberRef &&
        item.MethodDeclaration.DeclaringType.FullName == contractName &&
        item.MethodDeclaration.DeclaringType.DefinitionAssembly.Name == names[0]) == 2;
checks["existing-derived-inherits-current-implementation"] = Type(derivedModule, "HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableDerived")
    .Methods.All(method => method.Name != "Added" && method.Name != "EchoAdded");
checks["generic-interface-type-is-new-to-base"] = !baseline[names[1]].Types.Any(type =>
    type.Identity == "HybridCLR.Lab.CrossAssemblyDerived.ICrossRevisionValue`1") &&
    Type(derivedModule, "HybridCLR.Lab.CrossAssemblyDerived.ICrossRevisionValue`1").GenericParameters.Count == 1;
var analyses = names.ToDictionary(name => name, name => ResourceUpdateCompatibility.Analyze(baseline[name], current[name],
    currentAssemblySet: current.Values));
foreach (string name in names)
    checks[name + "-compatible"] = analyses[name].Compatible;
checks["cross-interface-native-capability-required"] = analyses[names[0]].RequiredRuntimeCapabilities.Contains("existing-interface-method-slots-v1");
const string declarationCapability = "cross-assembly-interface-declarations-v1";
checks["cross-assembly-declaration-capability-required"] = analyses[names[0]].RequiredRuntimeCapabilities.Contains(declarationCapability);
checks["slot-only-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v16",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != declarationCapability),
    analyses[names[0]].RequiredRuntimeCapabilities);
checks["candidate-capabilities-satisfy-declarations"] = ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, ResourceUpdateCompatibility.CurrentNativeRuntimeContract,
    ResourceUpdateCompatibility.KnownRuntimeCapabilities, analyses[names[0]].RequiredRuntimeCapabilities);
checks["missing-assembly-set-requires-declaration-capability"] = ResourceUpdateCompatibility.Analyze(
    baseline[names[0]], current[names[0]]).RequiredRuntimeCapabilities.Contains(declarationCapability);
const string inheritedCapability = "inherited-interface-dispatch-v1";
checks["inherited-interface-capability-required"] = analyses[names[0]].RequiredRuntimeCapabilities.Contains(inheritedCapability);
checks["declaration-only-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v17",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != inheritedCapability),
    analyses[names[0]].RequiredRuntimeCapabilities);
const string descendantCapability = "base-virtual-slots-on-current-descendants-v1";
checks["descendant-virtual-slot-capability-required"] = analyses[names[0]].RequiredRuntimeCapabilities.Contains(descendantCapability);
checks["interface-only-inheritance-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v18",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != descendantCapability),
    analyses[names[0]].RequiredRuntimeCapabilities);
bool passed = checks.Values.All(value => value);
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed,
    scope = "Cross-assembly declaration identity, unchanged AOT callers, and exact diagnosis of conservative RVA invalidation; not Player execution or all-AOT case retention",
    inputs = new[] { baselineRoot, currentRoot }.SelectMany(root => names.Select(name =>
    {
        string path = Path.Combine(root, name + ".dll");
        return new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    })),
    slots = new { baseline = Slots(baseModule), current = Slots(currentModule) },
    fingerprints,
    checks,
    analyses = analyses.Select(item => new { assembly = item.Key, item.Value.Compatible,
        item.Value.RequiredRuntimeCapabilities, item.Value.UnsupportedChanges }),
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
