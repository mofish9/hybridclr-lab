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
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, checks }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} package-tool checks");
return 0;
