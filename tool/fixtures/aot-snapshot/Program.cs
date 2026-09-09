using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using HybridCLR.Editor.Commands;

if (args.Length != 2) throw new ArgumentException("<real Unity stripped AOT root> <new output root>");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
const string identityName = "HybridCLR.Lab.SnapshotIdentity", ownerName = "Assembly-CSharp";
string[] dhe = { "HybridCLR.ValueLayoutModel", "HybridCLR.ValueLayoutOther", "HybridCLR.ValueLayoutConsumer" };
var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
string Serialize(DheAotAnalysisSnapshot.Manifest value) => JsonSerializer.Serialize(value, json);
DheAotAnalysisSnapshot.Manifest Deserialize(string value) => JsonSerializer.Deserialize<DheAotAnalysisSnapshot.Manifest>(value, json);
string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
var checks = new Dictionary<string, bool>();
var errors = new Dictionary<string, string>();
string Copy(string source, string name)
{
    string destination = Path.Combine(output, name); Directory.CreateDirectory(destination);
    foreach (string file in Directory.GetFiles(source, "*.dll")) File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
    return destination;
}
void Mutate(string file, Action<ModuleDefMD> change)
{
    using var module = ModuleDefMD.Load(File.ReadAllBytes(file));
    change(module); using var bytes = new MemoryStream();
    var options = new ModuleWriterOptions(module); options.PEHeadersOptions.TimeDateStamp = 123456789;
    module.Write(bytes, options); File.WriteAllBytes(file, bytes.ToArray());
}
string baseline = Copy(input, "base"), identityFile = Path.Combine(baseline, ownerName + ".dll");
Mutate(identityFile, module =>
{
    if (module.Find(identityName, false) != null) throw new InvalidDataException("Identity fixture already exists.");
    var identity = new TypeDefUser("HybridCLR.Lab", "SnapshotIdentity", module.CorLibTypes.Object.TypeDefOrRef)
    { Attributes = TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit };
    module.Types.Add(identity);
    identity.Fields.Add(new FieldDefUser("BaseId", new FieldSig(module.CorLibTypes.String),
        FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault) { Constant = new ConstantUser("placeholder") });
    identity.Fields.Add(new FieldDefUser("IdentityVersion", new FieldSig(module.CorLibTypes.Int32),
        FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal | FieldAttributes.HasDefault) { Constant = new ConstantUser(1) });
    identity.Fields.Add(new FieldDefUser("AssemblyNames", new FieldSig(new SZArraySig(module.CorLibTypes.String)),
        FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.InitOnly));
    var runtime = module.GetAssemblyRefs().Single(reference => reference.Name == "HybridCLR.Runtime");
    var returnType = new ClassSig(new TypeRefUser(module, "HybridCLR", "DheRuntimeIdentity", runtime));
    var create = new MethodDefUser("Create", MethodSig.CreateStatic(returnType), MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
    create.Body.Instructions.Add(Instruction.Create(OpCodes.Ldnull)); create.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
    identity.Methods.Add(create);
    var cctor = new MethodDefUser(".cctor", MethodSig.CreateStatic(module.CorLibTypes.Void), MethodImplAttributes.IL | MethodImplAttributes.Managed,
        MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName) { Body = new CilBody() };
    cctor.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); identity.Methods.Add(cctor);
});
var capture = DheAotAnalysisSnapshot.Capture(baseline, output, identityName, dhe, Serialize);
var repeated = DheAotAnalysisSnapshot.Capture(baseline, output, identityName, dhe, Serialize);
checks["content-addressed-capture-reused"] = repeated.ManifestPath == capture.ManifestPath && repeated.ManifestSha256 == capture.ManifestSha256;
var manifest = DheAotAnalysisSnapshot.Validate(capture.ManifestPath, capture.ManifestSha256, baseline, dhe, Deserialize);
checks["complete-real-unity-aot-inventory"] = manifest.assemblies.Length == Directory.GetFiles(input, "*.dll").Length;
checks["ordinary-aot-is-not-hotfix"] = manifest.assemblies.Where(image => image.dhe).Select(image => image.assemblyName).OrderBy(name => name)
    .SequenceEqual(dhe.OrderBy(name => name)) && manifest.assemblies.Any(image => !image.dhe && image.assemblyName == "mscorlib");
string final = Copy(baseline, "final");
Mutate(Path.Combine(final, ownerName + ".dll"), module =>
{
    module.Mvid = Guid.NewGuid(); module.EncId = Guid.NewGuid(); module.EncBaseId = Guid.NewGuid();
    TypeDef identity = module.Find(identityName, false);
    identity.Fields.Single(field => field.Name == "BaseId").Constant = new ConstantUser(new string('b', 64));
    MethodDef create = identity.Methods.Single(method => method.Name == "Create");
    create.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldc_I4_1));
    create.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Pop));
});
checks["final-identity-bytes-differ"] = Hash(File.ReadAllBytes(Path.Combine(final, ownerName + ".dll"))) != Hash(File.ReadAllBytes(identityFile));
try { DheAotAnalysisSnapshot.Validate(capture.ManifestPath, capture.ManifestSha256, final, dhe, Deserialize); checks["identity-self-reference-normalized"] = true; }
catch (Exception error) { checks["identity-self-reference-normalized"] = false; errors["identity-self-reference-normalized"] = error.ToString(); }
foreach (string file in Directory.GetFiles(baseline, "*.dll"))
{
    byte[] first = DheAotAnalysisSnapshot.Normalize(File.ReadAllBytes(file), identityName, out _, out _);
    byte[] second = DheAotAnalysisSnapshot.Normalize(first, identityName, out _, out _);
    checks["rewrite-idempotent-" + Path.GetFileName(file)] = first.SequenceEqual(second);
}
void Reject(string name, Action action)
{
    try { action(); checks[name] = false; }
    catch (Exception error) when (error is IOException || error is InvalidDataException || error is ArgumentException)
    { checks[name] = true; errors[name] = error.Message; }
}
void Changed(string name, string assembly, Action<ModuleDefMD> mutation)
{
    string root = Copy(final, name); Mutate(Path.Combine(root, assembly + ".dll"), mutation);
    Reject(name, () => DheAotAnalysisSnapshot.Validate(capture.ManifestPath, capture.ManifestSha256, root, dhe, Deserialize));
}
Changed("ordinary-method-change-rejected", "HybridCLR.ValueLayoutNative", module =>
{
    MethodDef method = module.GetTypes().SelectMany(type => type.Methods).Single(method => method.Name == "Echo");
    method.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Ldc_I4_1)); method.Body.Instructions.Insert(1, Instruction.Create(OpCodes.Pop));
});
Changed("ordinary-field-layout-change-rejected", "HybridCLR.ValueLayoutNative", module =>
    module.Find("HybridCLR.Lab.ValueLayoutNative.NativeInlineOwner", false).Fields.Add(new FieldDefUser("Extra",
        new FieldSig(module.CorLibTypes.Int64), FieldAttributes.Public)));
Changed("identity-signature-change-rejected", ownerName, module =>
    module.Find(identityName, false).Fields.Single(field => field.Name == "BaseId").Name = "DifferentId");
Changed("unexpected-identity-shape-rejected", ownerName, module =>
    module.Find(identityName, false).Fields.Add(new FieldDefUser("Instance", new FieldSig(module.CorLibTypes.Int32), FieldAttributes.Public)));
Changed("assembly-reference-change-rejected", ownerName, module =>
    module.GetAssemblyRefs().Single(reference => reference.Name == "HybridCLR.Runtime").Version = new Version(9, 9, 9, 9));
string missing = Copy(final, "missing"); File.Delete(Path.Combine(missing, "HybridCLR.ValueLayoutOther.dll"));
Reject("missing-final-assembly-rejected", () => DheAotAnalysisSnapshot.Validate(capture.ManifestPath, capture.ManifestSha256, missing, dhe, Deserialize));
string extra = Copy(final, "extra"); File.Copy(Path.Combine(extra, "HybridCLR.ValueLayoutOther.dll"), Path.Combine(extra, "Extra.dll"));
Reject("extra-final-assembly-rejected", () => DheAotAnalysisSnapshot.Validate(capture.ManifestPath, capture.ManifestSha256, extra, dhe, Deserialize));
Reject("wrong-manifest-hash-rejected", () => DheAotAnalysisSnapshot.Validate(capture.ManifestPath, new string('0', 64), final, dhe, Deserialize));
Reject("wrong-dhe-set-rejected", () => DheAotAnalysisSnapshot.Validate(capture.ManifestPath, capture.ManifestSha256, final, dhe.Take(1), Deserialize));
Reject("identity-in-hotfix-rejected", () => DheAotAnalysisSnapshot.Capture(baseline, Path.Combine(output, "bad-identity-owner"), identityName, dhe.Append(ownerName), Serialize));
string tampered = Path.Combine(output, "tampered-capture"); Directory.CreateDirectory(tampered);
File.Copy(capture.ManifestPath, Path.Combine(tampered, "manifest.json"));
string captured = Path.Combine(Path.GetDirectoryName(capture.ManifestPath), "assemblies");
Directory.CreateDirectory(Path.Combine(tampered, "assemblies"));
foreach (string file in Directory.GetFiles(captured, "*.dll")) File.Copy(file, Path.Combine(tampered, "assemblies", Path.GetFileName(file)));
File.WriteAllBytes(Path.Combine(tampered, "assemblies", ownerName + ".dll"), new byte[] { 0 });
Reject("captured-dll-tamper-rejected", () => DheAotAnalysisSnapshot.Validate(Path.Combine(tampered, "manifest.json"), capture.ManifestSha256, final, dhe, Deserialize));
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = checks.Values.All(value => value), checks, errors,
    scope = "AOT snapshot normalization and validation using real stripped DLLs; not final Player qualification", input, capture.ManifestPath, capture.ManifestSha256 }, json));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value + (check.Value || !errors.ContainsKey(check.Key) ? "" : " " + errors[check.Key]));
return checks.Values.All(value => value) ? 0 : 1;
