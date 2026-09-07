using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 3) throw new ArgumentException("Use <patcher DLL> <original compiler DLL> <new output root>.");
string patcher = Path.GetFullPath(args[0]), input = Path.GetFullPath(args[1]), root = Path.GetFullPath(args[2]);
if (Directory.Exists(root) || File.Exists(root)) throw new IOException("Test output must be new.");
Directory.CreateDirectory(root);
string inputHash = Hash(input);
var results = new List<object>();
bool passed = true;
string patched = Path.Combine(root, "patched", "Unity.IL2CPP.dll");
await Check("valid-input", input, patched, inputHash, true);
File.Copy(Path.Combine(Path.GetDirectoryName(input)!, "Unity.IL2CPP.DataModel.dll"),
    Path.Combine(Path.GetDirectoryName(patched)!, "Unity.IL2CPP.DataModel.dll"));
string patchedHash = Hash(patched);
await Check("wrong-hash", input, Path.Combine(root, "wrong-hash.dll"), new string('0', 64), false);
await Check("existing-output", input, patched, inputHash, false);
Require("existing-output-unchanged", Hash(patched) == patchedHash);
await Check("already-patched", patched, Path.Combine(root, "twice.dll"), patchedHash, false);
await Check("unrelated-assembly", patcher, Path.Combine(root, "unrelated.dll"), Hash(patcher), false);
string samePath = Path.Combine(root, "same-path.dll");
File.Copy(input, samePath);
await Check("same-input-output", samePath, samePath, inputHash, false);
Require("same-path-input-unchanged", Hash(samePath) == inputHash);
Require("original-compiler-unchanged", Hash(input) == inputHash);
File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new
{
    format = "hybridclr.dhe-aot-codegen-tests.json", schemaVersion = 1, passed,
    input, inputSha256 = inputHash, patcherSha256 = Hash(patcher),
    runnerSha256 = Hash(typeof(Program).Assembly.Location), results,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("Compiler patch checks: " + results.Count + "; passed=" + passed);
return passed ? 0 : 1;

async Task Check(string name, string source, string destination, string expectedHash, bool expectSuccess)
{
    bool existed = File.Exists(destination);
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in new[] { patcher, source, destination, expectedHash }) start.ArgumentList.Add(argument);
    using Process process = Process.Start(start) ?? throw new IOException("Could not launch patcher.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException)
    {
        if (!process.HasExited) process.Kill(true);
        await process.WaitForExitAsync();
        throw;
    }
    bool success = process.ExitCode == 0;
    bool outputState = expectSuccess
        ? File.Exists(destination) && File.Exists(destination + ".patch.json")
        : existed || (!File.Exists(destination) && !File.Exists(destination + ".patch.json"));
    bool valid = success == expectSuccess && outputState;
    results.Add(new { name, passed = valid, process.ExitCode, stdout = await stdout, stderr = await stderr });
    passed &= valid;
    if (expectSuccess && !valid) throw new InvalidDataException("Positive patch control failed.");
}
void Require(string name, bool valid)
{
    results.Add(new { name, passed = valid });
    passed &= valid;
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
