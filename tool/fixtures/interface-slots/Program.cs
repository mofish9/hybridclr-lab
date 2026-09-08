using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

if (args.Length != 4)
    throw new ArgumentException("Pass original Base DLL, evolved Base DLL, prepared Current DLL and a new output directory.");
string[] paths = args.Take(3).Select(Path.GetFullPath).ToArray();
string output = Path.GetFullPath(args[3]);
if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output must be new.");
using var original = ModuleDefMD.Load(paths[0]);
using var evolved = ModuleDefMD.Load(paths[1]);
using var current = ModuleDefMD.Load(paths[2]);
const string prefix = "HybridCLR.Lab.ManagedCasesAot.";
TypeDef Type(ModuleDef module, string name) => module.GetTypes().Single(type => type.FullName == prefix + name);
string[] Slots(ModuleDef module) => Type(module, "IIntOperation").Methods
    .Where(method => method.IsVirtual).Select(method => method.Name.String).ToArray();
var checks = new Dictionary<string, bool>
{
    ["original-interface-slot"] = Slots(original).SequenceEqual(new[] { "Apply" }),
    ["evolved-interface-slot"] = Slots(evolved).SequenceEqual(new[] { "Apply" }),
    ["current-interface-slot-collision"] = Slots(current).SequenceEqual(new[] { "Added", "Apply" }),
    ["class-is-new-only-to-original-base"] = !original.GetTypes().Any(type => type.FullName == prefix + "DheEvolutionOperation") &&
        Type(evolved, "DheEvolutionOperation") != null,
};
var before = MetaVersionSnapshot.Create(paths[1]);
var after = MetaVersionSnapshot.Create(paths[2]);
foreach (string name in new[] { "IIntOperation", "IntOperationStruct", "DheEvolutionOperation" })
{
    TypeDef baseType = Type(evolved, name);
    TypeDef currentType = Type(current, name);
    checks[name + "-physical-fields-unchanged"] = baseType.Fields.Select(field => field.FullName)
        .SequenceEqual(currentType.Fields.Select(field => field.FullName));
    MethodDef method = baseType.Methods.Single(method => method.Name == "Apply");
    MetaVersionMethod baseMethod = before.Methods.Single(candidate => candidate.Token == method.MDToken.Raw);
    MetaVersionMethod currentMethod = after.Methods.Single(candidate => candidate.StableId == baseMethod.StableId);
    checks[name + "-original-method-unchanged"] = baseMethod.Version == currentMethod.Version;
}
MethodDef implementation = Type(current, "DheEvolutionOperation").Methods.Single(method => method.HasOverrides);
checks["explicit-class-implementation"] = implementation.IsVirtual && implementation.IsFinal && implementation.IsPrivate &&
    implementation.Overrides.Count == 1 && implementation.Overrides[0].MethodDeclaration.Name == "Added";
MethodDef structMethod = Type(current, "IntOperationStruct").Methods.Single(method => method.Name == "Added");
checks["implicit-struct-implementation"] = structMethod.IsVirtual && structMethod.IsFinal && structMethod.IsPublic;
const string capability = "existing-interface-method-slots-v1";
var originalUpdate = ResourceUpdateCompatibility.Analyze(MetaVersionSnapshot.Create(paths[0]), after);
var evolvedUpdate = ResourceUpdateCompatibility.Analyze(before, after);
checks["original-interface-update-requires-capability"] = originalUpdate.Compatible && originalUpdate.RequiredRuntimeCapabilities.Contains(capability);
checks["evolved-interface-update-requires-capability"] = evolvedUpdate.Compatible && evolvedUpdate.RequiredRuntimeCapabilities.Contains(capability);
checks["old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    "dhe-runtime-v13", ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability),
    evolvedUpdate.RequiredRuntimeCapabilities);
checks["candidate-runtime-eligible"] = ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    ResourceUpdateCompatibility.CurrentNativeRuntimeContract, ResourceUpdateCompatibility.KnownRuntimeCapabilities,
    evolvedUpdate.RequiredRuntimeCapabilities);
checks["unchanged-interface-needs-no-capability"] = !ResourceUpdateCompatibility.Analyze(after, after)
    .RequiredRuntimeCapabilities.Contains(capability);
Directory.CreateDirectory(output);
foreach (bool final in new[] { false, true })
{
    using var negative = ModuleDefMD.Load(paths[2]);
    var method = new MethodDefUser("UnrelatedVirtual", MethodSig.CreateInstance(negative.CorLibTypes.Int32),
        dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Virtual | dnlib.DotNet.MethodAttributes.NewSlot |
        (final ? dnlib.DotNet.MethodAttributes.Final : 0));
    method.Body = new CilBody();
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 123));
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    Type(negative, "DheEvolutionOperation").Methods.Add(method);
    string path = Path.Combine(output, final ? "unrelated-final.dll" : "unrelated-virtual.dll");
    negative.Write(path);
    var analysis = ResourceUpdateCompatibility.Analyze(before, MetaVersionSnapshot.Create(path));
    checks[final ? "unrelated-final-virtual-rejected" : "unrelated-virtual-rejected"] = !analysis.Compatible &&
        analysis.UnsupportedChanges.Any(reason => reason.Contains("UnrelatedVirtual", StringComparison.Ordinal));
}
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, scope = "Current interface slot collision and unchanged Base declarations; not runtime support",
    inputs = paths.Select(path => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
    slots = new { original = Slots(original), evolved = Slots(evolved), current = Slots(current) }, checks,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
