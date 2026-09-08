using System.Security.Cryptography;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HybridCLR.DheTool;

if (args.Length is not (5 or 7))
    throw new ArgumentException("Pass Base DLL, Current DLL, real Player result, older Base DLL and a new report path, optionally followed by tool DLL and archived registry.");
var baseline = MetaVersionSnapshot.Create(args[0]);
var current = MetaVersionSnapshot.Create(args[1]);
var older = MetaVersionSnapshot.Create(args[3]);
using var player = JsonDocument.Parse(File.ReadAllText(args[2]));
var pair = new DhePlayerDispatch.AssemblyPair(baseline, current);
bool Accept(JsonElement result, params DhePlayerDispatch.AssemblyPair[] pairs)
{
    try { DhePlayerDispatch.Validate(result, pairs); return true; }
    catch (Exception e) when (e is InvalidDataException or KeyNotFoundException or InvalidOperationException)
    { return false; }
}
bool RejectMutation(string property, JsonNode? value, bool root = false)
{
    var copy = JsonNode.Parse(player.RootElement.GetRawText())!.AsObject();
    (root ? copy : copy["structuralEntryDispatch"]!.AsObject())[property] = value;
    using var document = JsonDocument.Parse(copy.ToJsonString());
    return !Accept(document.RootElement, pair);
}
var checks = new Dictionary<string, bool>
{
    ["real-Player-passed"] = player.RootElement.GetProperty("passed").GetBoolean(),
    ["real-entry-retained-AOT"] = !player.RootElement.GetProperty("changedProbeChanged").GetBoolean() &&
        !player.RootElement.GetProperty("structuralEntryDispatch").GetProperty("nativeChanged").GetBoolean(),
    ["retained-entry-interpreted-callee-accepted"] = Accept(player.RootElement, pair),
    ["missing-Current-method-rejected"] = !Accept(player.RootElement),
    ["ambiguous-Current-method-rejected"] = !Accept(player.RootElement, pair, pair),
    ["wrong-Base-generation-rejected"] = !Accept(player.RootElement,
        new DhePlayerDispatch.AssemblyPair(older, current)),
    ["missing-receipt-rejected"] = RejectMutation("structuralEntryDispatch", null, true),
    ["wrong-method-id-rejected"] = RejectMutation("methodStableId", JsonValue.Create(new string('0', 64))),
    ["wrong-method-name-rejected"] = RejectMutation("methodIdentity", JsonValue.Create("wrong")),
    ["wrong-Base-membership-rejected"] = RejectMutation("presentInBase", JsonValue.Create(false)),
    ["wrong-MV-change-flag-rejected"] = RejectMutation("expectedChanged", JsonValue.Create(true)),
    ["wrong-native-change-flag-rejected"] = RejectMutation("nativeChanged", JsonValue.Create(true)),
    ["unexecuted-receipt-rejected"] = RejectMutation("executed", JsonValue.Create(false)),
    ["zero-interpreter-count-rejected"] = RejectMutation("interpreterEntries", JsonValue.Create(0)),
    ["negative-interpreter-count-rejected"] = RejectMutation("interpreterEntries", JsonValue.Create(-1)),
    ["count-exceeding-total-rejected"] = RejectMutation("interpreterEntries",
        JsonValue.Create(player.RootElement.GetProperty("interpreterEntryCount").GetInt32() + 1)),
};
string output = Path.GetFullPath(args[4]);
if (File.Exists(output)) throw new IOException("Report already exists.");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
if (args.Length == 7)
{
    var tool = Assembly.LoadFrom(Path.GetFullPath(args[5]));
    var program = tool.GetType("HybridCLR.DheTool.Program", throwOnError: true)!;
    const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static;
    var read = program.GetMethod("ReadBaseRegistry", flags)!;
    checks["archived-Unity2021-registry-readable"] =
        read.Invoke(null, new object[] { Path.GetFullPath(args[6]), false }) != null;
    var required = (string[])program.GetField("RequiredPlayerEngineWorkflows", flags)!.GetValue(null)!;
    checks["only-two-active-engine-lanes-required"] =
        required.SequenceEqual(new[] { "Unity2022Fgs", "Tuanjie2022Fgs" });
    var unknown = JsonNode.Parse(File.ReadAllText(args[6]))!.AsObject();
    unknown["bases"]![0]!["engineWorkflow"] = "unknown-engine";
    string unknownPath = Path.ChangeExtension(output, "unknown-registry.json");
    if (File.Exists(unknownPath)) throw new IOException("Registry fixture already exists.");
    File.WriteAllText(unknownPath, unknown.ToJsonString());
    try { read.Invoke(null, new object[] { unknownPath, false });
        checks["unknown-registry-engine-rejected"] = false; }
    catch (TargetInvocationException exception)
    {
        checks["unknown-registry-engine-rejected"] =
            exception.InnerException?.Message.Contains("unsupported engineWorkflow",
                StringComparison.Ordinal) == true;
    }
}
File.WriteAllText(output, JsonSerializer.Serialize(new
{
    passed = checks.Values.All(value => value),
    inputs = args.Take(4).Concat(args.Skip(5)).Select(path => new { path = Path.GetFullPath(path),
        sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
    checks,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return checks.Values.All(value => value) ? 0 : 1;
