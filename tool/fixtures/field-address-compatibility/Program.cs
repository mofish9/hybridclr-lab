using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

if (args.Length != 4) throw new ArgumentException("Pass original Base DLL, evolved Base DLL, address Current DLL and new output root.");
string[] inputs = args.Take(3).Select(Path.GetFullPath).ToArray();
string output = Path.GetFullPath(args[3]);
if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
var original = MetaVersionSnapshot.Create(inputs[0]);
var evolved = MetaVersionSnapshot.Create(inputs[1]);
var current = MetaVersionSnapshot.Create(inputs[2]);
const string capability = "supplemental-instance-field-addresses-v1";
var checks = new Dictionary<string, bool>();
var addressScans = new Dictionary<string, string[]> { ["current"] = current.AddressTakenFieldIdentities };
var resolutions = new List<object>();
var first = ResourceUpdateCompatibility.Analyze(original, current);
var later = ResourceUpdateCompatibility.Analyze(evolved, current);
checks["new-type-needs-no-sidecar-address-capability"] = first.Compatible && !first.RequiredRuntimeCapabilities.Contains(capability);
checks["existing-generic-addresses-compatible"] = later.Compatible && later.RequiredRuntimeCapabilities.Contains(capability);
checks["old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    "dhe-runtime-v12", ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != capability), later.RequiredRuntimeCapabilities);
checks["current-runtime-accepted"] = ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    ResourceUpdateCompatibility.CurrentNativeRuntimeContract, ResourceUpdateCompatibility.KnownRuntimeCapabilities, later.RequiredRuntimeCapabilities);
var noOp = ResourceUpdateCompatibility.Analyze(current, current);
checks["native-fields-need-no-sidecar-address-capability"] = noOp.Compatible && !noOp.RequiredRuntimeCapabilities.Contains(capability);
string valueId = current.Fields.Single(field => field.DeclaringTypeIsGeneric && field.Identity.Contains("DheAddedGenericType", StringComparison.Ordinal) && field.Name == "AddedValue").Identity;
string itemsId = current.Fields.Single(field => field.DeclaringTypeIsGeneric && field.Identity.Contains("DheAddedGenericType", StringComparison.Ordinal) && field.Name == "AddedItems").Identity;
checks["same-assembly-closed-generic-definition-identity"] = current.AddressTakenFieldIdentities.Contains(valueId);
checks["original-field-token-collision-present"] = evolved.Fields.Any(before => current.Fields.Any(after =>
    before.Token == after.Token && before.Identity != after.Identity &&
    before.DeclaringTypeStableId == after.DeclaringTypeStableId &&
    after.Identity.Contains("DheAddedGenericType", StringComparison.Ordinal)));

File.Copy(inputs[2], Path.Combine(output, current.AssemblyName + ".dll"));
using (var source = ModuleDefMD.Load(inputs[2]))
{
    var owner = source.Types.Single(type => type.Name == "DheAddedGenericType`1");
    foreach (var field in source.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody)
        .SelectMany(method => method.Body.Instructions).Where(instruction => instruction.OpCode.Code == Code.Ldflda)
        .Select(instruction => instruction.Operand).OfType<IField>().Where(field => field.Name == "AddedValue").Take(3))
        resolutions.Add(new { field.FullName, definition = field.ResolveFieldDef()?.FullName,
            owner = field.DeclaringType.ResolveTypeDef()?.FullName,
            genericOwner = field.DeclaringType.TryGetGenericInstSig()?.GenericType.TypeDefOrRef.ResolveTypeDef()?.FullName });
    foreach (string shape in new[] { "scalar", "array", "nested-generic" })
    {
        var module = new ModuleDefUser("AddressConsumer." + shape + ".dll") { Kind = ModuleKind.Dll };
        new AssemblyDefUser("AddressConsumer." + shape, new Version(1, 0)).Modules.Add(module);
        var importer = new Importer(module);
        TypeSig argument = module.CorLibTypes.Int32;
        if (shape != "scalar") argument = new SZArraySig(argument);
        if (shape == "nested-generic")
            argument = new GenericInstSig(new ClassSig(new TypeRefUser(module, "System.Collections.Generic", "List`1", module.CorLibTypes.AssemblyRef)), argument);
        var closed = new TypeSpecUser(new GenericInstSig(new ClassSig(importer.Import(owner)), argument));
        var probe = new TypeDefUser("Probe", "Reader", module.CorLibTypes.Object.TypeDefOrRef);
        module.Types.Add(probe);
        foreach (string name in new[] { "AddedValue", "AddedItems" })
        {
            var definition = owner.Fields.Single(field => field.Name == name);
            var field = new MemberRefUser(module, name, new FieldSig(importer.Import(definition.FieldType)), closed);
            var method = new MethodDefUser(name, MethodSig.CreateStatic(module.CorLibTypes.Void, closed.ToTypeSig()),
                dnlib.DotNet.MethodAttributes.Public | dnlib.DotNet.MethodAttributes.Static);
            probe.Methods.Add(method);
            method.Body = new CilBody();
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldflda, field));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Pop));
            method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        }
        string path = Path.Combine(output, module.Name.String);
        module.Write(path);
        module.Dispose();
        var consumer = MetaVersionSnapshot.Create(path);
        addressScans[shape] = consumer.AddressTakenFieldIdentities;
        checks["cross-assembly-" + shape + "-definition-identities"] = consumer.AddressTakenFieldIdentities.OrderBy(value => value)
            .SequenceEqual(new[] { valueId, itemsId }.OrderBy(value => value));
        var cross = ResourceUpdateCompatibility.Analyze(evolved, current, consumer.AddressTakenFieldIdentities);
        checks["cross-assembly-" + shape + "-requires-capability"] = cross.Compatible && cross.RequiredRuntimeCapabilities.Contains(capability);
    }
}
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, inputs = inputs.Select(path => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
    checks, expected = new[] { valueId, itemsId }, addressScans, resolutions,
    fieldTokens = new[] { evolved, current }.Select(snapshot => snapshot.Fields.Where(field =>
        field.Identity.Contains("DheAddedGenericType", StringComparison.Ordinal)).Select(field => new { field.Token, field.Identity })),
    unsupported = later.UnsupportedChanges,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
