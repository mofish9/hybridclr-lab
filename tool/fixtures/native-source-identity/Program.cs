using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HybridCLR.DheTool;
using HybridCLR.Editor.Il2CppDef;

if (args.Length != 3) throw new ArgumentException("Pass new output root, archived stale runtime ZIP and actual installed runtime.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
string unpacked = Path.Combine(output, "stale");
ZipFile.ExtractToDirectory(args[1], unpacked);
string stale = Path.Combine(unpacked, "libil2cpp");
var serializer = new JsonSerializerOptions { IncludeFields = true };
string Hash(object value) => Convert.ToHexString(SHA256.HashData(
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, serializer))));
var before = DheNativeSourceIdentity.Capture(stale);
var after = DheNativeSourceIdentity.Capture(args[2]);
var independent = RuntimeSourceBinding.Validate(args[2], args[2]);
var checks = new Dictionary<string, bool>
{
    ["stale-source-distinguished"] = before.sourceSha256 != after.sourceSha256,
    ["native-manifest-source-identity-distinguished"] = Hash(new { runtimeContract = "dhe-runtime-v12", runtimeSourceIdentity = before }) !=
        Hash(new { runtimeContract = "dhe-runtime-v12", runtimeSourceIdentity = after }),
    ["repeatable"] = Hash(after) == Hash(DheNativeSourceIdentity.Capture(args[2])),
    ["independent-source-hash-agrees"] = independent.SourceSha256 == after.sourceSha256 &&
        independent.SourceFileCount == after.sourceFileCount,
    ["independent-generated-hashes-agree"] = after.generatedFiles.Length == 3 &&
        after.generatedFiles.All(file => independent.GeneratedFileHashes[file.path] == file.sha256),
};
string originalPath = Path.Combine(stale, "hybridclr", "generated", "UnityVersion.h");
File.AppendAllText(originalPath, "\n// changed generated source\n");
var generatedChange = DheNativeSourceIdentity.Capture(stale);
try { DheNativeSourceIdentity.RequireUnchanged(after, after); checks["unchanged-finalization"] = true; }
catch (InvalidOperationException) { checks["unchanged-finalization"] = false; }
try { DheNativeSourceIdentity.RequireUnchanged(before, after); checks["runtime-drift-during-finalization-rejected"] = false; }
catch (InvalidOperationException) { checks["runtime-drift-during-finalization-rejected"] = true; }
try { DheNativeSourceIdentity.RequireUnchanged(before, generatedChange); checks["generated-drift-during-finalization-rejected"] = false; }
catch (InvalidOperationException) { checks["generated-drift-during-finalization-rejected"] = true; }
checks["generated-change-preserves-source-hash"] = before.sourceSha256 == generatedChange.sourceSha256;
checks["generated-change-alters-manifest-identity"] = Hash(before) != Hash(generatedChange);
string receipt = Path.Combine(stale, "hybridclr", "generated", "libil2cpp-version.txt");
File.WriteAllText(receipt, "different installer receipt");
checks["receipt-is-not-native-identity"] = Hash(generatedChange) == Hash(DheNativeSourceIdentity.Capture(stale));
File.WriteAllText(Path.Combine(stale, "hybridclr", "generated", "Unexpected.cpp"), "extra-native-source");
checks["extra-generated-code-is-source-identity"] = generatedChange.sourceSha256 != DheNativeSourceIdentity.Capture(stale).sourceSha256;
File.Delete(originalPath);
try { DheNativeSourceIdentity.Capture(stale); checks["missing-generator-output-rejected"] = false; }
catch (IOException) { checks["missing-generator-output-rejected"] = true; }
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, checks, staleArchive = Path.GetFullPath(args[1]), installed = Path.GetFullPath(args[2]), before, after,
}, new JsonSerializerOptions { IncludeFields = true, WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
