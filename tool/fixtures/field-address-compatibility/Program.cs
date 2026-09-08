using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

if (args.Length < 4 || args.Length > 5 || (args.Length == 5 && args[4] != "--token-collision"))
    throw new ArgumentException("Pass original Base DLL, evolved Base DLL, address Current DLL, new output root and optionally --token-collision.");
string[] inputs = args.Take(3).Select(Path.GetFullPath).ToArray();
string output = Path.GetFullPath(args[3]);
if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
var original = MetaVersionSnapshot.Create(inputs[0]);
var evolved = MetaVersionSnapshot.Create(inputs[1]);
string currentPath = args.Length == 5 ? CreateCollisionPayload(inputs[1], inputs[2], output) : inputs[2];
var current = MetaVersionSnapshot.Create(currentPath);
const string capability = "supplemental-instance-field-addresses-v1";
var checks = new Dictionary<string, bool>();
var inputSnapshot = MetaVersionSnapshot.Create(inputs[2]);
checks["payload-method-semantics-preserved"] = inputSnapshot.Methods.Select(method => (method.StableId, method.Version))
    .SequenceEqual(current.Methods.Select(method => (method.StableId, method.Version)));
checks["payload-type-and-field-semantics-preserved"] = inputSnapshot.Types.Select(type => (type.StableId, type.Version))
    .SequenceEqual(current.Types.Select(type => (type.StableId, type.Version))) &&
    inputSnapshot.Fields.Select(field => (field.StableId, field.Version)).SequenceEqual(current.Fields.Select(field => (field.StableId, field.Version)));
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

File.Copy(currentPath, Path.Combine(output, current.AssemblyName + ".dll"));
using (var source = ModuleDefMD.Load(currentPath))
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
    testedCurrent = new { path = currentPath, sha256 = current.AssemblySha256 },
    checks, expected = new[] { valueId, itemsId }, addressScans, resolutions,
    fieldTokens = new[] { evolved, current }.Select(snapshot => snapshot.Fields.Where(field =>
        field.Identity.Contains("DheAddedGenericType", StringComparison.Ordinal)).Select(field => new { field.Token, field.Identity })),
    unsupported = later.UnsupportedChanges,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;

static string CreateCollisionPayload(string baseline, string input, string output)
{
    using var baseModule = ModuleDefMD.Load(baseline);
    var baseOwner = baseModule.Types.Single(type => type.Name == "DheAddedGenericType`1");
    var baseField = baseOwner.Fields.Single(field => field.Name == "<Value>k__BackingField");
    using var module = ModuleDefMD.Load(input);
    var owner = module.Types.Single(type => type.FullName == baseOwner.FullName);
    module.Types.Remove(owner);
    // Reorder only TypeDefs, preserving every member and method body. Verify the
    // resulting FieldDef token instead of assuming a source-file ordering survives Unity.
    for (int index = 1; index <= module.Types.Count; index++)
    {
        module.Types.Insert(index, owner);
        using var stream = new MemoryStream();
        module.Write(stream);
        byte[] bytes = stream.ToArray();
        using var probe = ModuleDefMD.Load(bytes);
        bool collides = probe.Types.Single(type => type.FullName == owner.FullName).Fields.Any(field =>
            field.MDToken.Raw == baseField.MDToken.Raw && field.Name != baseField.Name);
        if (collides)
        {
            string root = Path.Combine(output, "payload-input");
            Directory.CreateDirectory(root);
            string result = Path.Combine(root, Path.GetFileName(input));
            File.WriteAllBytes(result, bytes);
            foreach (string path in Directory.GetFiles(Path.GetDirectoryName(input)!, "*.dll"))
                if (!Path.GetFileName(path).Equals(Path.GetFileName(input), StringComparison.OrdinalIgnoreCase))
                    File.Copy(path, Path.Combine(root, Path.GetFileName(path)));
            return result;
        }
        module.Types.Remove(owner);
    }
    throw new InvalidOperationException("Could not produce a same-type field token collision without changing member semantics.");
}
