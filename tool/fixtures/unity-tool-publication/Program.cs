using System.Diagnostics;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("<Lab publisher DLL> <clean Lab source> <new output>");
string publisher = Path.GetFullPath(args[0]), lab = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
if (Directory.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
string Run(string file, params string[] arguments)
{
    var start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true,
        RedirectStandardError = true, WorkingDirectory = lab };
    foreach (string arg in arguments) start.ArgumentList.Add(arg);
    using var process = Process.Start(start)!;
    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("Unexpected compilation/timeout in rejected publication"); }
    Task.WaitAll(stdout, stderr);
    return process.ExitCode + "\n" + stdout.Result + stderr.Result;
}
string head = Run("git", "rev-parse", "HEAD")[2..].Trim();
string tree = Run("git", "rev-parse", "HEAD^{tree}")[2..].Trim();
var checks = new Dictionary<string, bool>();
void Reject(string name, string expected, params string[] options)
{
    string destination = Path.Combine(output, name);
    string actual = Run("dotnet", new[] { publisher, "publish-unity-tool", "-LabRoot", lab, "-OutputRoot", destination }.Concat(options).ToArray());
    bool passed = actual.StartsWith("1\n") && actual.Contains(expected, StringComparison.OrdinalIgnoreCase) && !Directory.Exists(destination);
    checks[name] = passed;
    if (!passed) throw new Exception(name + ": " + actual);
}
string Evidence(string name, object value)
{
    string path = Path.Combine(output, name + ".json"); File.WriteAllText(path, JsonSerializer.Serialize(value)); return path;
}
Reject("unknown-mode", "Mode must be", "-Mode", "production");
Reject("missing-release-evidence", "releaseevidence", "-Mode", "Release");
string failed = Evidence("failed", new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-release-evidence.json", passed = false });
Reject("failed-evidence", "not a passing", "-Mode", "Release", "-ReleaseEvidence", failed);
string wrongSource = Evidence("wrong-source", new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-release-evidence.json",
    passed = true, sourceHead = new string('0', 40), sourceTree = tree, files = Array.Empty<object>() });
Reject("foreign-source-evidence", "does not match the source", "-Mode", "Release", "-ReleaseEvidence", wrongSource);
string incomplete = Evidence("incomplete", new { schemaVersion = 1, format = "hybridclr.dhe-toolchain-release-evidence.json",
    passed = true, sourceHead = head, sourceTree = tree, files = Array.Empty<object>() });
Reject("matching-header-cannot-bypass-matrix", "complete Player, resolver, and native matrix", "-Mode", "Release", "-ReleaseEvidence", incomplete);
Reject("evidence-not-silently-ignored", "requires -Mode Release", "-Mode", "Exploratory", "-ReleaseEvidence", incomplete);
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = true, sourceHead = head,
    scope = "Rejected binary publications exercise the same complete evidence validator used by source publication; no Release artifact is certified by this fixture", checks },
    new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} publication rejection checks");
