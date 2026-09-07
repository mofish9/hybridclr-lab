using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.Editor.Il2CppDef;

if (args.Length != 3)
    throw new ArgumentException("Use <original Unity.IL2CPP.dll> <new output DLL> <expected original SHA-256>.");
string input = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
string report = output + ".patch.json";
if (input.Equals(output, StringComparison.OrdinalIgnoreCase) || File.Exists(output) || File.Exists(report))
    throw new IOException("Compiler patch requires new outputs distinct from its input.");
string inputHash = Hash(input);
if (!inputHash.Equals(args[2], StringComparison.OrdinalIgnoreCase))
    throw new InvalidDataException("Compiler input SHA-256 mismatch.");
string dataModelPath = Path.Combine(Path.GetDirectoryName(input), "Unity.IL2CPP.DataModel.dll");
DheAotCompilerPatchResult patch = DheAotCompilerPatch.Create(File.ReadAllBytes(input), File.ReadAllBytes(dataModelPath));
Directory.CreateDirectory(Path.GetDirectoryName(output));
File.WriteAllBytes(output, patch.Bytes);
if (Hash(input) != inputHash) throw new InvalidDataException("Original compiler changed while patching.");
File.WriteAllText(report, JsonSerializer.Serialize(new
{
    format = "hybridclr.dhe-aot-codegen-patch.json", schemaVersion = 1,
    scope = "Exploratory compiler control; not a formal runtime or toolchain release",
    input, inputSha256 = inputHash, output, outputSha256 = patch.PatchedSha256,
    dataModelPath, dataModelSha256 = patch.DataModelSha256,
    method = patch.Method, methodToken = patch.MethodToken,
    originalPopBranchOffset = patch.OriginalOffset, popCode = patch.PopCode,
    unchangedMethodCount = patch.UnchangedMethodCount,
    originalInputUnchanged = true, assemblyReferencesUnchanged = true,
    runnerSha256 = Hash(typeof(Program).Assembly.Location),
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Compiler Code.Pop emission patched; unchanged method bodies: " + patch.UnchangedMethodCount);

static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
