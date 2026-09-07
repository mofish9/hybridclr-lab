using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

if (args.Length != 4) throw new ArgumentException("Pass original Base DLL, evolved Base DLL, attribute current DLL, and new output root.");
string output = Path.GetFullPath(args[3]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output already exists.");
Directory.CreateDirectory(output);
var original = MetaVersionSnapshot.Create(args[0]);
var evolved = MetaVersionSnapshot.Create(args[1]);
var current = MetaVersionSnapshot.Create(args[2]);
const string Capability = "logical-attribute-members-v1";
const string Marker = "HybridCLR.Lab.ManagedCasesAot.DheMetadataMarkerAttribute";
bool Needs(MetaVersionSnapshot baseline, MetaVersionSnapshot after, params MetaVersionSnapshot[] all) =>
    ResourceUpdateCompatibility.Analyze(baseline, after, currentAssemblySet: all.Length == 0 ? new[] { after } : all)
        .RequiredRuntimeCapabilities.Contains(Capability);
var checks = new Dictionary<string, bool>();
checks["new-attribute-type-does-not-require-alias-fix"] = !Needs(original, current);
checks["existing-attribute-type-requires-fix"] = Needs(evolved, current);
checks["named-properties-require-fix-even-for-noop"] = Needs(current, current);
checks["named-property-use-scanned"] = current.AttributeUses.Any(use => use.TypeName == Marker && use.HasNamedProperties);
checks["unused-new-members-remain-allowed"] = !Needs(evolved, WithoutUses(true, true));
checks["constructor-overload-requires-fix-without-named-properties"] = Needs(evolved, WithoutUses(true, false));
checks["named-property-requires-fix-with-retained-constructor"] = Needs(evolved, WithoutUses(false, true));
checks["old-runtime-rejected"] = !ResourceUpdateCompatibility.CanExecuteUpdate(ResourceUpdateCompatibility.RuntimeProtocol,
    "dhe-runtime-v8", ResourceUpdateCompatibility.KnownRuntimeCapabilities.Where(value => value != Capability),
    ResourceUpdateCompatibility.Analyze(evolved, current).RequiredRuntimeCapabilities);

using ModuleDefUser owner = CreateModule("Dhe.Attribute.Owner");
TypeDef ownerType = new TypeDefUser("Dhe", "OwnerAttribute",
    new TypeRefUser(owner, "System", "Attribute", owner.CorLibTypes.AssemblyRef));
owner.Types.Add(ownerType);
ownerType.Attributes = TypeAttributes.Public;
MethodDef ownerCtor = Constructor(owner, ownerType, ownerType.BaseType);
var getter = new MethodDefUser("get_Note", MethodSig.CreateInstance(owner.CorLibTypes.String),
    MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig)
{ Body = new CilBody() };
getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ldstr, "note"));
getter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
var setter = new MethodDefUser("set_Note", MethodSig.CreateInstance(owner.CorLibTypes.Void, owner.CorLibTypes.String),
    MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig)
{ Body = new CilBody() };
setter.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
ownerType.Methods.Add(getter);
ownerType.Methods.Add(setter);
ownerType.Properties.Add(new PropertyDefUser("Note", PropertySig.CreateInstance(owner.CorLibTypes.String))
{ GetMethod = getter, SetMethod = setter });
string ownerPath = Path.Combine(output, "Dhe.Attribute.Owner.dll");
owner.Write(ownerPath);
MetaVersionSnapshot ownerSnapshot = MetaVersionSnapshot.Create(ownerPath);

using ModuleDefUser consumer = CreateModule("Dhe.Attribute.Consumer");
var ownerRef = new TypeRefUser(consumer, "Dhe", "OwnerAttribute",
    new AssemblyRefUser(owner.Assembly.Name, owner.Assembly.Version));
var derived = new TypeDefUser("Dhe", "DerivedAttribute", ownerRef);
consumer.Types.Add(derived);
derived.Attributes = TypeAttributes.Public;
MethodDef derivedCtor = Constructor(consumer, derived, ownerRef);
var target = new TypeDefUser("Dhe", "Target", consumer.CorLibTypes.Object.TypeDefOrRef);
consumer.Types.Add(target);
target.Attributes = TypeAttributes.Public;
var attribute = new CustomAttribute(derivedCtor);
attribute.NamedArguments.Add(new CANamedArgument(false, consumer.CorLibTypes.String, "Note",
    new CAArgument(consumer.CorLibTypes.String, "inherited-note")));
target.CustomAttributes.Add(attribute);
string consumerPath = Path.Combine(output, "Dhe.Attribute.Consumer.dll");
consumer.Write(consumerPath);
MetaVersionSnapshot consumerSnapshot = MetaVersionSnapshot.Create(consumerPath);
checks["owner-alone-has-no-attribute-use"] = !Needs(ownerSnapshot, ownerSnapshot);
checks["inherited-cross-assembly-property-requires-fix"] =
    Needs(ownerSnapshot, ownerSnapshot, ownerSnapshot, consumerSnapshot);
attribute.Constructor = new MemberRefUser(consumer, ".ctor", MethodSig.CreateInstance(consumer.CorLibTypes.Void), ownerRef);
string directPath = Path.Combine(output, "Dhe.Attribute.DirectConsumer.dll");
consumer.Write(directPath);
checks["direct-cross-assembly-property-requires-fix"] =
    Needs(ownerSnapshot, ownerSnapshot, ownerSnapshot, MetaVersionSnapshot.Create(directPath));

bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, inputs = args.Take(3).Select(Path.GetFullPath), checks,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;

MetaVersionSnapshot WithoutUses(bool removeProperties, bool removeOverload)
{
    using var module = ModuleDefMD.Load(args[2]);
    if (removeProperties)
        foreach (CustomAttribute item in module.GetTypes().SelectMany(type => type.CustomAttributes))
        {
            for (int index = item.NamedArguments.Count - 1; index >= 0; index--)
                if (!item.NamedArguments[index].IsField) item.NamedArguments.RemoveAt(index);
        }
    if (removeOverload)
    {
        TypeDef marker = module.GetTypes().Single(type => type.FullName == Marker);
        CustomAttribute item = module.GetTypes().Single(type => type.Name == "DheOverloadedAttributeTarget")
            .CustomAttributes.Single(value => value.AttributeType.FullName == Marker);
        item.Constructor = marker.Methods.Single(method => method.IsInstanceConstructor && method.MethodSig.Params.Count == 2);
        item.ConstructorArguments.Add(new CAArgument(module.CorLibTypes.String, "retained-constructor"));
    }
    string path = Path.Combine(output, $"without-{removeProperties}-{removeOverload}.dll");
    module.Write(path);
    return MetaVersionSnapshot.Create(path);
}

static ModuleDefUser CreateModule(string name)
{
    var module = new ModuleDefUser(name + ".dll") { Kind = ModuleKind.Dll };
    new AssemblyDefUser(name, new Version(1, 0, 0, 0)).Modules.Add(module);
    return module;
}

static MethodDef Constructor(ModuleDef module, TypeDef type, ITypeDefOrRef parent)
{
    var method = new MethodDefUser(".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void),
        MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName)
    { Body = new CilBody() };
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Call,
        new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void), parent)));
    method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    type.Methods.Add(method);
    return method;
}
