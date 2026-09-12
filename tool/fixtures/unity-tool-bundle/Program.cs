using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.PackageManager;

if (args.Length > 0 && args[0] == "echo") { Console.Write(JsonSerializer.Serialize(args.Skip(1))); return 0; }
if (args.Length != 4) throw new ArgumentException("<bundle> <new output> <Unity Editor contents> <assembly>");
string bundle = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
if (Directory.Exists(output)) throw new IOException("Output must be new.");
string package = Path.Combine(output, "含空格 Package", "com.code-philosophy.hybridclr@8.13.0");
string copied = Path.Combine(package, "Tools~", "DHE");
foreach (string file in Directory.GetFiles(bundle, "*", SearchOption.AllDirectories))
{
    string destination = Path.Combine(copied, Path.GetRelativePath(bundle, file));
    Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(file, destination);
}
SettingsUtil.ProjectDir = output;
PackageInfo.TestRoot = package;
EditorApplication.applicationContentsPath = args[2];
var checks = new Dictionary<string, bool>();
void Require(string name, bool value) { checks[name] = value; if (!value) throw new Exception(name); }
void Reject(string name, Action action, string message)
{
    try { action(); } catch (Exception error) when (error.Message.Contains(message, StringComparison.OrdinalIgnoreCase)) { checks[name] = true; return; }
    throw new Exception("Expected rejection: " + name);
}
Require("unity-runtime-without-sdk", !Directory.Exists(Path.Combine(args[2], "NetCoreRuntime", "sdk")));
Require("copied-package-with-spaces-verifies", DheToolCommand.Run("verify-package").Contains("passed"));
Require("tool-root-independent-of-project-cwd", DheToolCommand.Run("version").Contains("packageId="));
Reject("release-remains-fail-closed", () => DheToolCommand.Run("verify-package", "-RequireRelease"), "release-ready");
Reject("nonzero-exit-is-build-failure", () => DheToolCommand.Run("mv"), "Missing required argument");
Reject("nested-unity-workflow-rejected", () => DheToolCommand.Run("workflow"), "outside Unity");
string assembly = Path.Combine(output, "带 空格.dll"); File.Copy(args[3], assembly);
DheToolCommand.Run("mv", "-Assembly", assembly, "-Output", Path.Combine(output, "meta version.json"), "-Binary", Path.Combine(output, "meta version.mv"));
Require("real-mv-with-unicode-spaces", new FileInfo(Path.Combine(output, "meta version.mv")).Length > 0);
string[] special = { "", "with spaces", "a\"b", "trailing\\", "a\\\"b", "中文", "$HOME", "`literal`", "line\nbreak" };
var quote = typeof(DheToolCommand).GetMethod("QuoteArgument", BindingFlags.NonPublic | BindingFlags.Static);
string Quote(string value) => (string)quote.Invoke(null, new object[] { value });
var start = new ProcessStartInfo(DheToolCommand.ResolveDotnetHost()) { UseShellExecute = false, RedirectStandardOutput = true,
    Arguments = string.Join(" ", new[] { Assembly.GetExecutingAssembly().Location, "echo" }.Concat(special).Select(Quote)) };
using (var child = Process.Start(start))
{
    string returned = child.StandardOutput.ReadToEnd(); child.WaitForExit();
    Require("actual-process-argument-roundtrip", child.ExitCode == 0 && JsonSerializer.Deserialize<string[]>(returned).SequenceEqual(special));
}
string extra = Path.Combine(copied, "unexpected.cs"); File.WriteAllText(extra, "extra");
Reject("extra-source-file-rejected", () => DheToolCommand.Run("version"), "Unexpected files"); File.Delete(extra);
string target = Path.Combine(copied, "dnlib.dll"); byte[] original = File.ReadAllBytes(target);
byte[] changed = (byte[])original.Clone(); changed[changed.Length / 2] ^= 1; File.WriteAllBytes(target, changed);
Reject("binary-tamper-before-launch", () => DheToolCommand.Run("version"), "hash mismatch"); File.WriteAllBytes(target, original);
File.Move(target, target + ".saved");
Reject("missing-dependency-before-launch", () => DheToolCommand.Run("version"), "Missing or damaged"); File.Move(target + ".saved", target);
// Exercise CLI policy too; bypassing the Editor wrapper must not expose Lab commands.
var policy = new ProcessStartInfo(DheToolCommand.ResolveDotnetHost()) { UseShellExecute = false, RedirectStandardError = true };
policy.ArgumentList.Add(Path.Combine(copied, "HybridCLR.DheTool.dll")); policy.ArgumentList.Add("publish");
using (var child = Process.Start(policy)) { string error = child.StandardError.ReadToEnd(); child.WaitForExit(); Require("lab-command-unavailable-in-binary", child.ExitCode != 0 && error.Contains("belongs to the Lab")); }
Require("restored-bundle-verifies", DheToolCommand.Run("verify-package").Contains("passed"));
string template = Path.Combine(copied, "templates", "dhe-workflow-config.json");
string bundleParent = Path.GetDirectoryName(copied);
byte[] templateBefore = File.ReadAllBytes(template);
Reject("mv-cannot-overwrite-bundle-template", () => DheToolCommand.Run("mv", "-Assembly", assembly,
    "-Output", template, "-Binary", Path.Combine(output, "forbidden.mv")), "overlap the executing tool");
Require("rejected-write-keeps-template", File.ReadAllBytes(template).SequenceEqual(templateBefore));
Reject("mv-binary-cannot-overwrite-dependency", () => DheToolCommand.Run("mv", "-Assembly", assembly,
    "-Output", Path.Combine(output, "no-partial.json"), "-Binary", Path.Combine(copied, "dnlib.dll")), "overlap the executing tool");
Require("both-mv-outputs-validated-before-write", !File.Exists(Path.Combine(output, "no-partial.json")));
Reject("ancestor-output-root-rejected", () => DheToolCommand.Run("batch", "-BaselineRoot", output,
    "-CurrentRoot", output, "-OutputRoot", bundleParent), "overlap the executing tool");
Reject("root-override-cannot-disable-output-protection", () => DheToolCommand.Run("new-config",
    "-Root", output, "-Output", template), "overlap the executing tool");
Reject("normalized-parent-traversal-rejected", () => DheToolCommand.Run("new-config", "-Output",
    Path.Combine(copied, "templates", "..", "templates", "dhe-workflow-config.json")), "overlap the executing tool");
Reject("stage-destination-rejected-before-input-processing", () => DheToolCommand.Run("stage-resource-update",
    "-AssetRoot", copied), "overlap the executing tool");
Require("all-rejected-writes-keep-bundle-valid", DheToolCommand.Run("verify-package").Contains("passed"));

string configPath = Path.Combine(output, "precedence.json");
string configOutput = Path.Combine(output, "config-run");
string explicitRoot = Path.Combine(output, "intended-missing-root");
File.WriteAllText(configPath, JsonSerializer.Serialize(new {
    projectPath = output, settingsFile = assembly, outputRoot = configOutput,
    target = "StandaloneWindows64", adapterMethod = "MustNotLaunchUnity", unity = DheToolCommand.ResolveDotnetHost(),
    mode = "Release", runPlayer = true, bootstrap = true, toolchainRoot = explicitRoot,
    expectedToolchainPackageId = new string('0', 64)
}));
(int Exit, string Error) External(params string[] arguments)
{
    var info = new ProcessStartInfo(DheToolCommand.ResolveDotnetHost()) { UseShellExecute = false,
        RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = output };
    info.ArgumentList.Add(Path.Combine(copied, "HybridCLR.DheTool.dll"));
    foreach (string argument in arguments) info.ArgumentList.Add(argument);
    using var child = Process.Start(info);
    var stdout = child.StandardOutput.ReadToEndAsync(); var stderr = child.StandardError.ReadToEndAsync();
    if (!child.WaitForExit(10000)) { child.Kill(true); throw new Exception("Diagnostic command timed out"); }
    Task.WaitAll(stdout, stderr); return (child.ExitCode, stderr.Result + stdout.Result);
}
var fromConfig = External("workflow", "-Config", configPath);
Require("config-toolchain-root-beats-package-default", fromConfig.Exit != 0 && fromConfig.Error.Contains("installed release-ready") &&
    !File.Exists(Path.Combine(configOutput, "toolchain-gate.json")));
var fromCli = External("workflow", "-Config", configPath, "-ToolchainRoot", copied);
Require("explicit-cli-beats-config", fromCli.Exit != 0 && File.Exists(Path.Combine(configOutput, "toolchain-gate.json")) &&
    JsonDocument.Parse(File.ReadAllText(Path.Combine(configOutput, "toolchain-gate.json"))).RootElement.GetProperty("packageRoot").GetString()
        .TrimEnd('\\', '/').Equals(copied, StringComparison.OrdinalIgnoreCase));
var badConfig = new { outputRoot = copied, mode = "Release" };
File.WriteAllText(configPath, JsonSerializer.Serialize(badConfig));
var unsafeConfig = External("workflow", "-Config", configPath);
Require("config-output-protected-before-workflow", unsafeConfig.Exit != 0 && unsafeConfig.Error.Contains("overlap the executing tool"));
Require("configuration-rejections-keep-bundle-valid", DheToolCommand.Run("verify-package").Contains("passed"));
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} package-tool checks");
return 0;
