using System.Text.Json;
using HybridCLR.DheTool;

if (args.Length != 1 && args.Length != 3)
    throw new ArgumentException("Pass new output root, optionally followed by actual expected/installed runtime roots.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
var checks = new Dictionary<string, bool>();
string source = Path.Combine(output, "source");
string installed = Path.Combine(output, "installed");
void Put(string root, string relative, string value)
{
    string path = Path.Combine(root, relative);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, value);
}
foreach (string name in new[] { "hybridclr/DheRuntime.cpp", "hybridclr/DheRuntime.h", "vm/Class.cpp",
    "hybridclr/metadata/Assembly.cpp", "hybridclr/generated/AssemblyManifest.cpp",
    "hybridclr/generated/MethodBridge.cpp", "hybridclr/generated/UnityVersion.h" })
{
    Put(source, name, "source:" + name);
    Put(installed, name, "source:" + name);
}
var matching = RuntimeSourceBinding.Validate(source, installed);
checks["matching-runtime"] = matching.Passed && matching.SourceSha256 == matching.InstalledSourceSha256;
Put(installed, "hybridclr/generated/libil2cpp-version.txt", "installer-receipt");
checks["installer-receipt"] = RuntimeSourceBinding.Validate(source, installed).Passed;
foreach (string generated in new[] { "AssemblyManifest.cpp", "MethodBridge.cpp", "UnityVersion.h" })
    Put(installed, "hybridclr/generated/" + generated, "project-generated");
var generatedResult = RuntimeSourceBinding.Validate(source, installed);
checks["only-three-generated-files-may-change"] = generatedResult.Passed &&
    generatedResult.GeneratedFileHashes.Count == 3 && generatedResult.SourceSha256 == matching.SourceSha256 &&
    generatedResult.GeneratedFileHashes["hybridclr/generated/MethodBridge.cpp"] !=
        matching.GeneratedFileHashes["hybridclr/generated/MethodBridge.cpp"];
Put(installed, "hybridclr/metadata/Assembly.cpp", "old-runtime");
checks["stale-native-file-rejected"] = !RuntimeSourceBinding.Validate(source, installed).Passed;
Put(installed, "hybridclr/metadata/Assembly.cpp", "source:hybridclr/metadata/Assembly.cpp");
Put(source, "hybridclr/metadata/NewCapability.h", "new-capability");
checks["missing-native-file-rejected"] = !RuntimeSourceBinding.Validate(source, installed).Passed;
Put(installed, "hybridclr/metadata/NewCapability.h", "new-capability");
Put(installed, "hybridclr/generated/Unexpected.cpp", "unreviewed-code");
checks["extra-generated-source-rejected"] = !RuntimeSourceBinding.Validate(source, installed).Passed;
File.Delete(Path.Combine(installed, "hybridclr/generated/Unexpected.cpp"));
File.Delete(Path.Combine(installed, "hybridclr/generated/MethodBridge.cpp"));
checks["missing-generator-output-rejected"] = !RuntimeSourceBinding.Validate(source, installed).Passed;
Put(installed, "hybridclr/generated/MethodBridge.cpp", "project-generated");
checks["repaired-runtime"] = RuntimeSourceBinding.Validate(source, installed).Passed;
foreach (string platform in new[] { "WindowsEditor", "OSXEditor", "LinuxEditor" })
    checks["host-path-" + platform] = RuntimeSourceBinding.InstalledRoot(output, platform).Contains(
        "LocalIl2CppData-" + platform, StringComparison.Ordinal);
try { RuntimeSourceBinding.InstalledRoot(output, "../outside"); checks["invalid-host-rejected"] = false; }
catch (ArgumentException) { checks["invalid-host-rejected"] = true; }
RuntimeSourceBinding.Result? actual = args.Length == 3 ? RuntimeSourceBinding.Validate(args[1], args[2]) : null;
if (actual != null) checks["actual-stale-demo-rejected"] = !actual.Passed;
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { passed, checks, actual },
    new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
