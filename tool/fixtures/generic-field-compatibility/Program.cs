using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

if (args.Length != 4) throw new ArgumentException("Pass original Base DLL, evolved Base DLL, prepared Current DLL and new output root.");
string[] inputs = args.Take(3).Select(Path.GetFullPath).ToArray();
string output = Path.GetFullPath(args[3]);
if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
var original = MetaVersionSnapshot.Create(inputs[0]);
var evolved = MetaVersionSnapshot.Create(inputs[1]);
var current = MetaVersionSnapshot.Create(inputs[2]);
const string capability = "supplemental-existing-generic-type-fields-v1";
var checks = new Dictionary<string, bool>();
var first = ResourceUpdateCompatibility.Analyze(original, current);
var later = ResourceUpdateCompatibility.Analyze(evolved, current);
checks["new-type-compatible-without-new-capability"] = first.Compatible && !first.RequiredRuntimeCapabilities.Contains(capability);
checks["existing-generic-fields-compatible"] = later.Compatible;
checks["existing-generic-fields-require-capability"] = later.RequiredRuntimeCapabilities.Contains(capability);
checks["old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    "dhe-runtime-v11", ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability),
    later.RequiredRuntimeCapabilities);
checks["new-runtime-accepted"] = ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    ResourceUpdateCompatibility.CurrentNativeRuntimeContract, ResourceUpdateCompatibility.KnownRuntimeCapabilities,
    later.RequiredRuntimeCapabilities);
var noOp = ResourceUpdateCompatibility.Analyze(evolved, evolved);
checks["no-op-does-not-require-field-capability"] = noOp.Compatible && !noOp.RequiredRuntimeCapabilities.Contains(capability);
var added = current.Fields.Where(field => field.DeclaringTypeIsGeneric &&
    field.Identity.Contains("DheAddedGenericType", StringComparison.Ordinal) && field.Name.StartsWith("Added", StringComparison.Ordinal)).ToArray();
checks["fixture-adds-two-instance-and-two-static-fields"] = added.Count(field => field.IsStatic) == 2 && added.Count(field => !field.IsStatic) == 2;
var address = ResourceUpdateCompatibility.Analyze(evolved, current, added.Where(field => !field.IsStatic).Select(field => field.Identity));
checks["field-address-requires-capability"] = address.Compatible &&
    address.RequiredRuntimeCapabilities.Contains("supplemental-instance-field-addresses-v1");
checks["field-address-old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
    ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v12",
    ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != "supplemental-instance-field-addresses-v1"),
    address.RequiredRuntimeCapabilities);
var removed = ResourceUpdateCompatibility.Analyze(current, evolved);
checks["generic-removal-requires-capability"] = removed.RequiredRuntimeCapabilities.Contains(capability);
string threadStaticPath = Path.Combine(output, "thread-static.dll");
using (var module = ModuleDefMD.Load(inputs[2]))
{
    var type = module.Types.Single(type => type.Name == "DheAddedGenericType`1");
    var field = type.Fields.Single(field => field.Name == "AddedCount");
    var attribute = new TypeRefUser(module, "System", "ThreadStaticAttribute", module.CorLibTypes.AssemblyRef);
    field.CustomAttributes.Add(new CustomAttribute(new MemberRefUser(module, ".ctor",
        MethodSig.CreateInstance(module.CorLibTypes.Void), attribute)));
    module.Write(threadStaticPath);
}
checks["thread-static-still-rejected"] = ResourceUpdateCompatibility.Analyze(evolved,
    MetaVersionSnapshot.Create(threadStaticPath)).UnsupportedChanges.Any(value =>
    value.StartsWith("added-threadstatic-or-rva-field-on-existing-type:", StringComparison.Ordinal));
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, inputs = inputs.Select(path => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
    checks, unsupported = later.UnsupportedChanges,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
