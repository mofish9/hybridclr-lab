using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

if (args.Length != 4) throw new ArgumentException("<tool DLL> <Lab root> <runtime manifest> <new output>");
string tool = Path.GetFullPath(args[0]), lab = Path.GetFullPath(args[1]);
string runtimePath = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
Directory.CreateDirectory(output);
var assembly = Assembly.LoadFrom(tool);
var program = assembly.GetType("HybridCLR.DheTool.Program", true)!;
var cliType = assembly.GetType("HybridCLR.DheTool.Cli", true)!;
object Cli(Dictionary<string, string> values) => Activator.CreateInstance(cliType,
    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
    new object[] { "resource-release-qualify", values }, null)!;
object Call(string method, params object[] values)
{
    try { return program.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, values)!; }
    catch (TargetInvocationException e) { throw e.InnerException!; }
}
var checks = new Dictionary<string, bool>();
void Check(string name, bool passed)
{
    checks[name] = passed;
    if (!passed) throw new Exception(name);
}
Dictionary<string, string> Gate(Dictionary<string, string> options, bool matrix, string[] roots) =>
    (Dictionary<string, string>)Call("CreateQualificationGateArguments", Cli(options),
        "tool-root", "package-id", "update-root", "snapshot", "snapshot-sha", "ledger-sha",
        new[] { "player-a", "player-b" }, "gate-output", matrix, roots);
var forwarded = Gate(new() { ["validationsourceroot"] = Path.GetRelativePath(Environment.CurrentDirectory, lab) },
    true, new[] { "historical-a", "historical-b" });
Check("explicit-relative-Lab-forwarded-as-absolute", forwarded["validationsourceroot"] == lab);
Check("matrix-and-evidence-roots-preserved", forwarded["requireenginematrix"] == "true" &&
    forwarded["evidencetoolchainroots"] == "historical-a,historical-b");
Check("gate-identities-and-player-coverage-preserved", forwarded["expectedtoolchainpackageid"] == "package-id" &&
    forwarded["channelsnapshot"] == "snapshot" && forwarded["expectedchannelsnapshotsha256"] == "snapshot-sha" &&
    forwarded["expectedreleaseledgersha256"] == "ledger-sha" && forwarded["changedplayers"] == "player-a,player-b" &&
    forwarded["output"] == "gate-output" && forwarded["resourceupdateroot"] == "update-root");
foreach (string? value in new string?[] { null, "", "  " })
{
    var options = new Dictionary<string, string>();
    if (value != null) options["validationsourceroot"] = value;
    var gate = Gate(options, false, Array.Empty<string>());
    Check("optional-input-" + (value == null ? "absent" : value.Length.ToString()),
        !gate.ContainsKey("validationsourceroot") && !gate.ContainsKey("requireenginematrix") &&
        !gate.ContainsKey("evidencetoolchainroots"));
}
JsonElement runtime = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(runtimePath));
Check("forwarded-Lab-resolves-matching-runtime-contract",
    (string)Call("ResolveManagedRuntimeContractRoot", runtime, Path.GetDirectoryName(tool)!,
        new[] { forwarded["validationsourceroot"] }, runtimePath) == lab);
string foreign = Path.Combine(output, "foreign-Lab");
Directory.CreateDirectory(Path.Combine(foreign, "manifests"));
File.WriteAllText(Path.Combine(foreign, "manifests/dhe-runtime-lock.json"), "{}");
bool rejected = false;
try { Call("ResolveManagedRuntimeContractRoot", runtime, Path.GetDirectoryName(tool)!, new[] { foreign }, runtimePath); }
catch (Exception e) { rejected = e.Message.Contains("does not match", StringComparison.Ordinal); }
Check("foreign-lock-remains-rejected", rejected);
File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new
{
    passed = true, scope = "compiled qualification argument builder and actual runtime contract resolver",
    toolSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(tool))), runtimePath, checks
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"PASS {checks.Count} qualification forwarding checks");
