using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

if (args.Length != 3)
    throw new ArgumentException("Use <original Unity.IL2CPP.dll> <new output DLL> <expected original SHA-256>.");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
string report = output + ".patch.json";
if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(report))
    throw new IOException("Compiler patch requires new outputs distinct from its input.");
string inputHash = Hash(input);
if (!inputHash.Equals(args[2], StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Compiler input SHA-256 mismatch.");
using var module = ModuleDefMD.Load(input);
TypeDef writer = module.Find("Unity.IL2CPP.MethodBodyWriter", false)
    ?? throw new InvalidDataException("Unsupported compiler: no MethodBodyWriter.");
MethodDef dispatch = writer.Methods.Single(method => method.Name == "ProcessInstruction");
TypeDef stackInfo = module.Find("Unity.IL2CPP.StackInfo", false);
FieldDef expression = stackInfo.Fields.Single(field => field.Name == "Expression" && field.FieldType.FullName == "System.String");
FieldDef codeWriter = writer.Fields.Single(field => field.Name == "_writer");
MethodDef writeStatement = module.Find("Unity.IL2CPP.CodeWriters.ICodeWriter", false).Methods.Single(method =>
    method.Name == "WriteStatement" && method.MethodSig.Params.Count == 1 && method.MethodSig.Params[0].FullName == "System.String");
var instructions = dispatch.Body.Instructions;
var candidates = Enumerable.Range(0, instructions.Count - 4).Where(index =>
    instructions[index].OpCode.Code == Code.Ldarg_0 &&
    instructions[index + 1].OpCode.Code == Code.Ldfld &&
    instructions[index + 1].Operand is IField field && field.Name == "_valueStack" &&
    instructions[index + 2].OpCode.Code == Code.Callvirt &&
    instructions[index + 2].Operand is IMethod pop && pop.Name == "Pop" &&
    pop.MethodSig.RetType is GenericVar returnType && returnType.Number == 0 &&
    pop.DeclaringType is TypeSpec declaringType && declaringType.TypeSig is GenericInstSig stack &&
    stack.GenericType.FullName == "System.Collections.Generic.Stack`1" &&
    stack.GenericArguments.Count == 1 && stack.GenericArguments[0].FullName == stackInfo.FullName &&
    instructions[index + 3].OpCode.Code == Code.Pop &&
    instructions[index + 4].OpCode.Code == Code.Ret).ToArray();
if (candidates.Length != 1)
    throw new InvalidDataException("Unsupported or already patched compiler: expected one discarded StackInfo branch.");
int start = candidates[0];
TypeRef codeReference = module.GetTypeRefs().Single(type => type.FullName == "Unity.IL2CPP.DataModel.Code");
string dataModelPath = Path.Combine(Path.GetDirectoryName(input)!, codeReference.DefinitionAssembly.Name + ".dll");
using var dataModel = ModuleDefMD.Load(dataModelPath);
int popCode = Convert.ToInt32(dataModel.Find(codeReference.FullName, false).Fields
    .Single(field => field.Name == "Pop").Constant.Value);
if (!instructions.Any(instruction => instruction.OpCode.Code == Code.Switch &&
    instruction.Operand is IList<Instruction> targets && targets.Count > popCode &&
    ReferenceEquals(targets[popCode], instructions[start])))
    throw new InvalidDataException("The candidate block is not the compiler's Code.Pop dispatch target.");
uint originalOffset = instructions[start].Offset;
var unchangedBodies = module.GetTypes().SelectMany(type => type.Methods)
    .Where(method => method.HasBody && method != dispatch)
    .ToDictionary(method => method.FullName, method => BodyText(method));
string[] originalReferences = module.GetAssemblyRefs().Select(reference => reference.FullName).ToArray();

// A discarded result still has observable exception/volatile semantics. The
// native compiler may eliminate pure evaluation after C++ has expressed it.
var discarded = new Local(stackInfo.ToTypeSig());
dispatch.Body.Variables.Add(discarded);
dispatch.Body.SimplifyBranches();
Instruction oldPop = instructions[start + 3];
oldPop.OpCode = OpCodes.Stloc;
oldPop.Operand = discarded;
var concat = new MemberRefUser(module, "Concat", MethodSig.CreateStatic(module.CorLibTypes.String,
    module.CorLibTypes.String, module.CorLibTypes.String, module.CorLibTypes.String),
    module.CorLibTypes.String.TypeDefOrRef);
Instruction[] emit =
{
    Instruction.Create(OpCodes.Ldarg_0),
    Instruction.Create(OpCodes.Ldfld, codeWriter),
    Instruction.Create(OpCodes.Ldstr, "(void)("),
    Instruction.Create(stackInfo.IsValueType ? OpCodes.Ldloca : OpCodes.Ldloc, discarded),
    Instruction.Create(OpCodes.Ldfld, expression),
    Instruction.Create(OpCodes.Ldstr, ")"),
    Instruction.Create(OpCodes.Call, concat),
    Instruction.Create(OpCodes.Callvirt, writeStatement),
};
for (int index = 0; index < emit.Length; index++) instructions.Insert(start + 4 + index, emit[index]);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
var options = new ModuleWriterOptions(module);
options.MetadataOptions.Flags |= MetadataFlags.PreserveAll;
module.Write(output, options);
using (var verified = ModuleDefMD.Load(output))
{
    if (!originalReferences.SequenceEqual(verified.GetAssemblyRefs().Select(reference => reference.FullName)))
        throw new InvalidDataException("Compiler patch changed assembly dependencies.");
    foreach (MethodDef method in verified.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
        if (unchangedBodies.TryGetValue(method.FullName, out string? before) && before != BodyText(method))
            throw new InvalidDataException("Compiler patch changed another method: " + method.FullName);
    MethodDef changed = verified.Find(writer.FullName, false).Methods.Single(method => method.Name == dispatch.Name);
    if (changed.Body.Instructions.Count(instruction => instruction.OpCode.Code == Code.Ldstr &&
            instruction.Operand is string value && value == "(void)(") != 1)
        throw new InvalidDataException("Compiler patch emission marker is missing or ambiguous.");
}
if (Hash(input) != inputHash) throw new InvalidDataException("Original compiler changed while patching.");
File.WriteAllText(report, JsonSerializer.Serialize(new
{
    format = "hybridclr.dhe-aot-codegen-patch.json", schemaVersion = 1,
    scope = "Exploratory compiler control; not a formal runtime or toolchain release",
    input, inputSha256 = inputHash, output, outputSha256 = Hash(output),
    dataModelPath, dataModelSha256 = Hash(dataModelPath),
    method = dispatch.FullName, methodToken = dispatch.MDToken.Raw,
    originalPopBranchOffset = originalOffset, popCode, unchangedMethodCount = unchangedBodies.Count,
    originalInputUnchanged = true, assemblyReferencesUnchanged = true,
    runnerSha256 = Hash(typeof(Program).Assembly.Location),
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Compiler Code.Pop emission patched; unchanged method bodies: " + unchangedBodies.Count);

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static string BodyText(MethodDef method) => string.Join("\n", method.Body.Instructions.Select(instruction => instruction.ToString()));
