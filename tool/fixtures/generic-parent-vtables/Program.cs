using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

if (args.Length != 4) throw new ArgumentException("Pass original Base, evolved Base, Current DLL roots, and a new output directory.");
string output = Path.GetFullPath(args[3]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
string[] names = { "HybridCLR.ManagedCases", "HybridCLR.CrossAssemblyDerived", "HybridCLR.ManagedCasesAot", "HybridCLR.MetadataStress" };
var sets = args.Take(3).Select(root => names.ToDictionary(name => name,
    name => MetaVersionSnapshot.Create(Path.Combine(root, name + ".dll")))).ToArray();
const string assembly = "HybridCLR.ManagedCasesAot";
const string child = "HybridCLR.Lab.ManagedCasesAot.DheEvolutionGenericDerived";
const string parent = "HybridCLR.Lab.ManagedCasesAot.GenericVirtualOperation`1";
const string capability = "closed-current-parent-vtables-v1";
var original = ResourceUpdateCompatibility.Analyze(sets[0][assembly], sets[2][assembly], currentAssemblySet: sets[2].Values);
var evolved = ResourceUpdateCompatibility.Analyze(sets[1][assembly], sets[2][assembly], currentAssemblySet: sets[2].Values);
string[] oldCapabilities = ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability).ToArray();
bool CanRun(ResourceUpdateCompatibility result, IEnumerable<string> available) =>
    ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
        ResourceUpdateCompatibility.CurrentNativeRuntimeContract, available, result.RequiredRuntimeCapabilities);
var checks = new Dictionary<string, bool>
{
    ["original-descendant-is-new"] = !sets[0][assembly].Types.Any(type => type.Identity == child),
    ["evolved-descendant-is-native"] = sets[1][assembly].Types.Any(type => type.Identity == child),
    ["closed-parent-definition-from-metadata"] = sets[2][assembly].TypeParents[child].DefinitionName == parent &&
        sets[2][assembly].TypeParents[child].TypeName.Contains("IntOperationStruct"),
    ["original-requires-repaired-vtables"] = original.RequiredRuntimeCapabilities.Contains(capability),
    ["original-old-runtime-rejected"] = !CanRun(original, oldCapabilities),
    ["original-repaired-runtime-eligible"] = original.Compatible && CanRun(original, ResourceUpdateCompatibility.KnownRuntimeCapabilities),
    ["already-native-descendant-remains-eligible"] = evolved.Compatible &&
        !evolved.RequiredRuntimeCapabilities.Contains(capability) && CanRun(evolved, oldCapabilities),
    ["local-parent-without-assembly-set-still-gated"] = ResourceUpdateCompatibility.Analyze(
        sets[0][assembly], sets[2][assembly]).RequiredRuntimeCapabilities.Contains(capability),
};

// Build two analyzer-only copies with a nonsealed intermediate parent. The
// only extra child exists in Current, so detecting its generic grandparent
// requires following the declaration graph rather than its immediate base.
using (var before = ModuleDefMD.Load(Path.Combine(args[1], assembly + ".dll")))
using (var after = ModuleDefMD.Load(Path.Combine(args[2], assembly + ".dll")))
{
    before.GetTypes().Single(type => type.FullName == child).IsSealed = false;
    var intermediate = after.GetTypes().Single(type => type.FullName == child);
    intermediate.IsSealed = false;
    after.Types.Add(new TypeDefUser("HybridCLR.Lab.ManagedCasesAot", "DheChainedDescendant", intermediate)
    { Attributes = dnlib.DotNet.TypeAttributes.Public });
    string beforePath = Path.Combine(output, "intermediate-base.dll"), afterPath = Path.Combine(output, "intermediate-current.dll");
    before.Write(beforePath);
    after.Write(afterPath);
    var chainBase = MetaVersionSnapshot.Create(beforePath);
    var chainCurrent = MetaVersionSnapshot.Create(afterPath);
    var chain = ResourceUpdateCompatibility.Analyze(chainBase, chainCurrent,
        currentAssemblySet: sets[2].Values.Where(snapshot => snapshot.AssemblyName != assembly).Append(chainCurrent));
    checks["intermediate-descendant-requires-repair"] = chain.Compatible && chain.RequiredRuntimeCapabilities.Contains(capability);
    checks["intermediate-old-runtime-rejected"] = !CanRun(chain, oldCapabilities);
}
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, scope = "Capability analysis of immutable inputs and isolated declaration fixtures; not native execution",
    inputs = args.Take(3).SelectMany(root => names.Select(name =>
    {
        string path = Path.GetFullPath(Path.Combine(root, name + ".dll"));
        return new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) };
    })), checks,
    originalCapabilities = original.RequiredRuntimeCapabilities, evolvedCapabilities = evolved.RequiredRuntimeCapabilities,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
