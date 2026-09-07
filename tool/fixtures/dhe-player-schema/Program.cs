using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length != 6)
    throw new ArgumentException("Pass tool, schema, original no-op report, evolved no-op report, legacy update report, and output directory.");
string tool = Path.GetFullPath(args[0]);
string schema = Path.GetFullPath(args[1]);
string output = Path.GetFullPath(args[5]);
if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
    throw new InvalidOperationException("Test output must be new or empty.");
Directory.CreateDirectory(output);
JsonObject original = Read(args[2]);
JsonObject evolved = Read(args[3]);
JsonObject legacy = Read(args[4]);
var checks = new List<object>();
int failures = 0;
await Check("original-noop-empty-probes", original, true);
await Check("evolved-noop-eight-probes", evolved, true);
await Check("legacy-structural-update", legacy, true);
await Check("original-null-probes", Change(original, value => value["structuralLegacyProbes"] = null), true);
await Check("original-absent-probes", Change(original, value => value.Remove("structuralLegacyProbes")), true);
await Check("evolved-empty-probes-rejected", Change(evolved, value => value["structuralLegacyProbes"] = new JsonArray()), false);
await Check("evolved-seven-probes-rejected", Change(evolved, value => Probes(value).RemoveAt(0)), false);
await Check("original-partial-probes-rejected", Change(original, value =>
    value["structuralLegacyProbes"] = JsonNode.Parse("[" + Probes(evolved)[0]!.ToJsonString() + "]")), false);
await Check("evolved-null-probes-rejected", Change(evolved, value => value["structuralLegacyProbes"] = null), false);
await Check("evolved-absent-probes-rejected", Change(evolved, value => value.Remove("structuralLegacyProbes")), false);
await Check("skipped-probe-passed-rejected", Change(evolved, value => Probes(value)[0]!["passed"] = true), false);
await Check("skipped-probe-executed-rejected", Change(evolved, value => Probes(value)[0]!["executed"] = true), false);
await Check("applicable-unexecuted-probe-rejected", Change(evolved, value =>
{
    Probes(value)[0]!["applicable"] = true;
    Probes(value)[0]!["reason"] = "removed-from-base";
}), false);
await Check("structural-failure-rejected", Change(evolved, value => value["structuralPassed"] = false), false);
await Check("evolved-nine-probes-rejected", Change(evolved, value =>
    Probes(value).Add(JsonNode.Parse(Probes(value)[0]!.ToJsonString()))), false);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed = failures == 0,
    schemaSha256 = Hash(schema),
    toolSha256 = Hash(tool),
    inputs = args.Skip(2).Take(3).Select(path => new { path = Path.GetFullPath(path), sha256 = Hash(path) }),
    checks,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Player schema checks: {checks.Count - failures}/{checks.Count} passed");
return failures == 0 ? 0 : 1;

async Task Check(string name, JsonObject document, bool expected)
{
    string path = Path.Combine(output, name + ".json");
    File.WriteAllText(path, document.ToJsonString());
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardOutput = true, RedirectStandardError = true,
    };
    foreach (string argument in new[] { tool, "schema-validate", "-Schema", schema, "-Document", path })
        start.ArgumentList.Add(argument);
    using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start schema validator.");
    Task<string> stdout = process.StandardOutput.ReadToEndAsync();
    Task<string> stderr = process.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
    try { await process.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException) { process.Kill(true); await process.WaitForExitAsync(); throw; }
    string log = await stdout + await stderr;
    File.WriteAllText(Path.Combine(output, name + ".log"), log);
    bool passed = (process.ExitCode == 0) == expected;
    if (!passed) failures++;
    checks.Add(new { name, expectedAccepted = expected, exitCode = process.ExitCode, passed });
}

static JsonObject Read(string path) => JsonNode.Parse(File.ReadAllText(path))!.AsObject();
static JsonArray Probes(JsonObject value) => value["structuralLegacyProbes"]!.AsArray();
static JsonObject Change(JsonObject input, Action<JsonObject> mutate)
{
    var copy = JsonNode.Parse(input.ToJsonString())!.AsObject();
    mutate(copy);
    return copy;
}
static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
