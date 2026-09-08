using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;
using HybridCLR.Lab;

if (args.Length != 4) throw new ArgumentException("Pass Base DLL, Current DLL, failed Player result, and a new output directory.");
string output = Path.GetFullPath(args[3]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
var baselineSnapshot = MetaVersionSnapshot.Create(Path.GetFullPath(args[0]));
var currentSnapshot = MetaVersionSnapshot.Create(Path.GetFullPath(args[1]));
var baseline = DheFixtureMetaVersion.Read(baselineSnapshot.ToBinary(), baselineSnapshot.AssemblyName);
var current = DheFixtureMetaVersion.Read(currentSnapshot.ToBinary(), currentSnapshot.AssemblyName);
var options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
using var player = JsonDocument.Parse(File.ReadAllText(args[2]));
var evidence = player.RootElement.GetProperty("structuralEntryDispatch")
    .Deserialize<DheFixturePolicy.EntryDispatchEvidence>(options)!;
DheFixturePolicy.ValidateEntryDispatch(baseline, current, evidence);
bool Accept(DheFixturePolicy.EntryDispatchEvidence entry)
{
    try
    {
        DheFixturePolicy.ValidateEntryDispatch(baseline, current, entry);
        return entry.InterpretedCallExecuted;
    }
    catch (InvalidDataException) { return false; }
}
DheFixturePolicy.EntryDispatchEvidence Changed(Action<DheFixturePolicy.EntryDispatchEvidence> mutate)
{
    var copy = JsonSerializer.Deserialize<DheFixturePolicy.EntryDispatchEvidence>(JsonSerializer.Serialize(evidence, options), options)!;
    mutate(copy);
    return copy;
}
var checks = new Dictionary<string, bool>
{
    ["recorded-workflow-failed"] = !player.RootElement.GetProperty("passed").GetBoolean(),
    ["recorded-resource-has-changes"] = player.RootElement.GetProperty("changedMethodCount").GetInt32() > 0,
    ["recorded-structural-assertions-pass"] = player.RootElement.GetProperty("structuralPassed").GetBoolean(),
    ["recorded-entry-unchanged-in-real-MV"] = evidence.presentInBase && !evidence.expectedChanged && !evidence.nativeChanged,
    ["recorded-entry-executes-interpreted-callees"] = evidence.executed && evidence.interpreterEntries > 0,
    ["unchanged-entry-is-not-relabeled-as-changed"] = !evidence.ChangedBaseEntryExecuted,
    ["mixed-call-qualifies-interpreter-execution"] = Accept(evidence),
    ["zero-interpreted-execution-rejected"] = !Accept(Changed(value => value.interpreterEntries = 0)),
    ["negative-counter-rejected"] = !Accept(Changed(value => value.interpreterEntries = -1)),
    ["unexecuted-entry-rejected"] = !Accept(Changed(value => value.executed = false)),
    ["wrong-native-routing-rejected"] = !Accept(Changed(value => value.nativeChanged = true)),
    ["wrong-MV-routing-rejected"] = !Accept(Changed(value => value.expectedChanged = true)),
    ["wrong-Base-membership-rejected"] = !Accept(Changed(value => value.presentInBase = false)),
    ["wrong-method-identity-rejected"] = !Accept(Changed(value => value.methodStableId = new string('0', 64))),
};
Directory.CreateDirectory(output);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed = checks.Values.All(value => value),
    inputs = args.Take(3).Select(path => new { path = Path.GetFullPath(path), sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }),
    evidence, checks,
}, options));
foreach (var item in checks) Console.WriteLine(item.Key + ": " + item.Value);
return checks.Values.All(value => value) ? 0 : 1;
