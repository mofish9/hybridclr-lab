using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;
using HybridCLR.Editor.Commands;
using HybridCLR.DheTool;
using System.Text.Json.Nodes;

if (args.Length > 0 && args[0] == "unity-workflow") return UnityWorkflow.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "evolution-workflow") return ResourceEvolutionWorkflow.Run(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "evolution-reference") return ResourceEvolutionWorkflow.Reference(args.Skip(1).ToArray());
if (args.Length > 0 && args[0] == "frozen-aot-policy") return FrozenAotPolicy.Run(args.Skip(1).ToArray());
if (args.Length == 4 && args[0] == "compare")
{
    if (Directory.Exists(args[3])) throw new IOException("Comparison output must be new.");
    Directory.CreateDirectory(args[3]);
    var normalized = new List<byte[]>();
    foreach (string file in args.Skip(1).Take(2))
    {
        using var module = ModuleDefMD.Load(file);
        var bytes = DheAotAnalysisSnapshot.Normalize(File.ReadAllBytes(file), "HybridCLR.Lab.Snapshot.DheBuildIdentity", out _, out _);
        normalized.Add(bytes); File.WriteAllBytes(Path.Combine(args[3], normalized.Count + ".dll"), bytes);
        using var stream = new MemoryStream(bytes); using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
        var summary = new
        {
            file, module.Name, module.Mvid, module.RuntimeVersion, module.Cor20HeaderFlags, length = bytes.Length,
            moduleAttributes = module.CustomAttributes.Select(attribute => new { attribute.TypeFullName, arguments = attribute.ConstructorArguments.Select(value => value.ToString()) }),
            assembly = module.Assembly.FullName,
            assemblyAttributes = module.Assembly.CustomAttributes.Select(attribute => new { attribute.TypeFullName, arguments = attribute.ConstructorArguments.Select(value => value.ToString()) }),
            types = module.GetTypes().Select(type => type.FullName), exports = module.ExportedTypes.Select(type => type.FullName),
            references = module.GetAssemblyRefs().Select(reference => reference.FullName),
            sections = pe.PEHeaders.SectionHeaders.Select(section => new { section.Name, section.PointerToRawData, section.SizeOfRawData }),
            debugEntries = pe.ReadDebugDirectory(),
        };
        File.WriteAllText(Path.Combine(args[3], normalized.Count + ".json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
    }
    Console.WriteLine(JsonSerializer.Serialize(new { sizes = normalized.Select(bytes => bytes.Length),
        differentOffsets = Enumerable.Range(0, Math.Min(normalized[0].Length, normalized[1].Length))
            .Where(index => normalized[0][index] != normalized[1][index]).Take(100).ToArray() }));
    return 0;
}
if (args.Length != 3) throw new ArgumentException("<real Unity stripped AOT root> <new output root> <tool DLL>");
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
JsonElement Identity(string hash, string relative) => JsonSerializer.SerializeToElement(new
    { aotAnalysisSnapshotSha256 = hash, aotAnalysisSnapshot = relative });
string relativeManifest = Path.GetRelativePath(output, capture.ManifestPath).Replace('\\', '/');
string identityPath = Path.Combine(output, "build-identity.json");
string[] aotNames = manifest.assemblies.Select(row => row.assemblyName).ToArray();
var boundIdentity = Identity(capture.ManifestSha256, relativeManifest);
var read = AotAnalysisSnapshot.Read(identityPath, boundIdentity, aotNames, dhe);
checks["resource-reader-complete-ordinary-subset"] = read.OrdinaryAssemblyPaths.Length == aotNames.Length - dhe.Length &&
    !read.OrdinaryAssemblyPaths.Select(Path.GetFileNameWithoutExtension).Intersect(dhe).Any();
checks["historical-no-snapshot-explicitly-absent"] = AotAnalysisSnapshot.Read(identityPath,
    JsonSerializer.SerializeToElement(new { }), aotNames, dhe) == null;
Reject("reader-partial-binding-rejected", () => AotAnalysisSnapshot.Read(identityPath,
    JsonSerializer.SerializeToElement(new { aotAnalysisSnapshotSha256 = capture.ManifestSha256 }), aotNames, dhe));
Reject("reader-path-traversal-rejected", () => AotAnalysisSnapshot.Read(identityPath,
    Identity(capture.ManifestSha256, "../manifest.json"), aotNames, dhe));
Reject("reader-wrong-aot-inventory-rejected", () => AotAnalysisSnapshot.Read(identityPath, boundIdentity, aotNames.Skip(1), dhe));
Reject("reader-wrong-dhe-classification-rejected", () => AotAnalysisSnapshot.Read(identityPath, boundIdentity, aotNames, dhe.Skip(1)));
void BadCapture(string name, Action<JsonNode> mutateManifest, Action<string> mutateFiles)
{
    string changed = Path.Combine(output, name); Directory.CreateDirectory(changed);
    JsonNode document = JsonNode.Parse(File.ReadAllText(capture.ManifestPath)); mutateManifest?.Invoke(document);
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(document.ToJsonString(json));
    string hash = Hash(bytes).ToLowerInvariant(), relative = "aot-analysis/" + hash + "/manifest.json";
    string newManifest = Path.Combine(changed, relative.Replace('/', Path.DirectorySeparatorChar));
    string dllRoot = Path.Combine(Path.GetDirectoryName(newManifest), "assemblies"); Directory.CreateDirectory(dllRoot);
    foreach (string file in Directory.GetFiles(captured, "*.dll")) File.Copy(file, Path.Combine(dllRoot, Path.GetFileName(file)));
    File.WriteAllBytes(newManifest, bytes); mutateFiles?.Invoke(dllRoot);
    Reject(name, () => AotAnalysisSnapshot.Read(Path.Combine(changed, "build-identity.json"), Identity(hash, relative), aotNames, dhe));
}
BadCapture("reader-tampered-dll-rejected", null, path => File.WriteAllBytes(Path.Combine(path, ownerName + ".dll"), new byte[] { 0 }));
BadCapture("reader-missing-dll-rejected", null, path => File.Delete(Path.Combine(path, ownerName + ".dll")));
BadCapture("reader-extra-dll-rejected", null, path => File.Copy(Path.Combine(path, ownerName + ".dll"), Path.Combine(path, "Extra.dll")));
BadCapture("reader-unsafe-dll-path-rejected", node => node["assemblies"][0]["file"] = "../escape.dll", null);
BadCapture("reader-wrong-owner-rejected", node => node["identityAssembly"] = "mscorlib", null);
BadCapture("reader-wrong-dll-name-rejected", node => node["assemblies"][0]["assemblyName"] = "Wrong", null);
string archive = Path.Combine(output, "archive");
string archiveManifest = Path.Combine(archive, relativeManifest.Replace('/', Path.DirectorySeparatorChar));
Directory.CreateDirectory(Path.GetDirectoryName(archiveManifest));
File.Copy(capture.ManifestPath, archiveManifest);
Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(archiveManifest), "assemblies"));
foreach (string file in Directory.GetFiles(captured, "*.dll"))
    File.Copy(file, Path.Combine(Path.GetDirectoryName(archiveManifest), "assemblies", Path.GetFileName(file)));
File.WriteAllText(Path.Combine(archive, "build-identity.json"), JsonSerializer.Serialize(boundIdentity));
var tool = System.Reflection.Assembly.LoadFrom(Path.GetFullPath(args[2]));
var toolProgram = tool.GetType("HybridCLR.DheTool.Program", true);
var mapping = toolProgram.GetNestedType("ArchivePathMapping", System.Reflection.BindingFlags.NonPublic);
toolProgram.GetMethod("RewriteArchiveJsonDocuments", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
    .Invoke(null, new object[] { archive, output, Array.CreateInstance(mapping, 0) });
checks["actual-archive-rewriter-preserves-manifest-bytes"] = File.ReadAllBytes(archiveManifest).SequenceEqual(File.ReadAllBytes(capture.ManifestPath));
checks["archive-reader-portable-after-relocation"] = AotAnalysisSnapshot.Read(Path.Combine(archive, "build-identity.json"),
    boundIdentity, aotNames, dhe).OrdinaryAssemblyPaths.All(path => path.StartsWith(archive));
string GitHead(string root)
{
    var start = new System.Diagnostics.ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
    foreach (string argument in new[] { "-C", root, "rev-parse", "HEAD" }) start.ArgumentList.Add(argument);
    using var process = System.Diagnostics.Process.Start(start); string value = process.StandardOutput.ReadToEnd(); process.WaitForExit();
    if (process.ExitCode != 0) throw new IOException("Cannot bind fixture source.");
    return value.Trim();
}
var host = System.Reflection.Assembly.GetExecutingAssembly();
string packageRoot = host.GetCustomAttributes(typeof(System.Reflection.AssemblyMetadataAttribute), false)
    .Cast<System.Reflection.AssemblyMetadataAttribute>().Single(value => value.Key == "DhePackageRoot").Value;
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = checks.Values.All(value => value), checks, errors,
    labHead = GitHead(Path.GetFullPath("../../../../../..", AppContext.BaseDirectory)), packageHead = GitHead(packageRoot),
    hostSha256 = Hash(File.ReadAllBytes(host.Location)), toolSha256 = Hash(File.ReadAllBytes(args[2])),
    scope = "AOT snapshot normalization and validation using real stripped DLLs; not final Player qualification", input, capture.ManifestPath, capture.ManifestSha256 }, json));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value + (check.Value || !errors.ContainsKey(check.Key) ? "" : " " + errors[check.Key]));
return checks.Values.All(value => value) ? 0 : 1;
