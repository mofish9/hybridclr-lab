using System.Text.Json;
using System.Runtime.Loader;
using HybridCLR.DheTool;
using HybridCLR.Lab;

if (args.Length != 3) throw new ArgumentException("Pass legacy Base DLL, evolved DLL, and a new output root.");
string output = Path.GetFullPath(args[2]);
if (File.Exists(output) || Directory.Exists(output)) throw new IOException("Test output already exists.");
var before = MetaVersionSnapshot.Create(Path.GetFullPath(args[0]));
var after = MetaVersionSnapshot.Create(Path.GetFullPath(args[1]));
DheFixtureMetaVersion Read(MetaVersionSnapshot snapshot) =>
    DheFixtureMetaVersion.Read(snapshot.ToBinary(), snapshot.AssemblyName);
var baseline = Read(before);
var current = Read(after);
var checks = new Dictionary<string, bool>();
checks["legacy-fixture-absent"] = !DheFixturePolicy.HasStructuralFixture(baseline);
checks["evolved-noop-fixture-present"] = DheFixturePolicy.HasStructuralFixture(current);
checks["mv-method-identities-match"] = after.Methods.All(method => current.methods[method.StableId]
    .Equals(method.Version, StringComparison.OrdinalIgnoreCase));
checks["mv-type-identities-match"] = after.Types.All(type => current.types[type.StableId]
    .Equals(type.Version, StringComparison.OrdinalIgnoreCase));
checks["legacy-probe-identities-match"] = DheFixturePolicy.LegacyProbes.All(probe =>
    before.Methods.Any(method => method.Identity == probe.Identity && method.StableId == probe.StableId) &&
    !after.Methods.Any(method => method.StableId == probe.StableId));
string stableIdentity = DheFixturePolicy.Calculator + "::Stable|System.Int32 (System.Int32)";
checks["old-base-stable-changed"] = DheFixturePolicy.MethodChanged(baseline, current, stableIdentity);
checks["evolved-base-stable-unchanged"] = !DheFixturePolicy.MethodChanged(current, current, stableIdentity);
var unrelatedChange = Read(after);
unrelatedChange.types[unrelatedChange.types.Keys.First()] = new string('0', 64);
checks["unrelated-type-does-not-force-stable-dispatch"] =
    !DheFixturePolicy.MethodChanged(current, unrelatedChange, stableIdentity);

bool Rejects(Action action)
{
    try { action(); return false; }
    catch (InvalidDataException) { return true; }
}
int calls = 0;
var callbacks = DheFixturePolicy.LegacyProbes.ToDictionary(probe => probe.Check,
    probe => (Action)(() => { calls++; throw new MissingMethodException(); }), StringComparer.Ordinal);
var removed = DheFixturePolicy.RunLegacyProbes(baseline, current, callbacks);
DheFixturePolicy.ValidateLegacyEvidence(baseline, current, removed);
checks["old-base-all-eight-native-probes-required"] = calls == 8 && removed.All(item => item.applicable && item.executed && item.passed);
calls = 0;
var retained = DheFixturePolicy.RunLegacyProbes(baseline, baseline, callbacks);
DheFixturePolicy.ValidateLegacyEvidence(baseline, baseline, retained);
checks["legacy-noop-not-counted-as-deletion"] = calls == 0 && retained.All(item =>
    !item.applicable && !item.executed && !item.passed && item.reason == "retained-in-current");
var absent = DheFixturePolicy.RunLegacyProbes(current, current, callbacks);
DheFixturePolicy.ValidateLegacyEvidence(current, current, absent);
checks["evolved-base-never-references-deleted-api"] = calls == 0 && absent.All(item =>
    !item.applicable && !item.executed && !item.passed && item.reason == "absent-from-base");
var missingCallbacks = DheFixturePolicy.RunLegacyProbes(baseline, current, new Dictionary<string, Action>());
checks["missing-compiled-probe-rejected"] = Rejects(() =>
    DheFixturePolicy.ValidateLegacyEvidence(baseline, current, missingCallbacks));
checks["false-inapplicability-rejected"] = Rejects(() =>
    DheFixturePolicy.ValidateLegacyEvidence(baseline, current, absent));
checks["duplicate-probe-rejected"] = Rejects(() =>
    DheFixturePolicy.ValidateLegacyEvidence(baseline, current, Enumerable.Repeat(removed[0], 8).ToArray()));
checks["missing-probe-rejected"] = Rejects(() =>
    DheFixturePolicy.ValidateLegacyEvidence(baseline, current, removed.Take(7).ToArray()));
callbacks[DheFixturePolicy.LegacyProbes[0].Check] = () => { };
checks["missing-tombstone-rejected"] = Rejects(() => DheFixturePolicy.ValidateLegacyEvidence(baseline,
    current, DheFixturePolicy.RunLegacyProbes(baseline, current, callbacks)));
callbacks[DheFixturePolicy.LegacyProbes[0].Check] = () => throw new InvalidOperationException("wrong exception");
checks["wrong-exception-rejected"] = Rejects(() => DheFixturePolicy.ValidateLegacyEvidence(baseline,
    current, DheFixturePolicy.RunLegacyProbes(baseline, current, callbacks)));
absent[0].passed = true;
checks["skipped-probe-cannot-pass"] = Rejects(() => DheFixturePolicy.ValidateLegacyEvidence(current, current, absent));
checks["mv-truncation-rejected"] = Rejects(() => DheFixtureMetaVersion.Read(after.ToBinary().SkipLast(1).ToArray(), after.AssemblyName));
checks["mv-identity-rejected"] = Rejects(() => DheFixtureMetaVersion.Read(after.ToBinary(), "WrongAssembly"));
checks["missing-current-method-rejected"] = Rejects(() => DheFixturePolicy.MethodChanged(current, current, "Missing"));
Directory.CreateDirectory(output);
string nextPath = Path.Combine(output, "next.dll");
string latestPath = Path.Combine(output, "latest.dll");
string defaultPath = Path.Combine(output, "default.dll");
ManagedCaseVariants.WriteNextCurrentAssembly(Path.GetFullPath(args[1]), nextPath, true);
ManagedCaseVariants.WriteNextCurrentAssembly(nextPath, latestPath, true);
ManagedCaseVariants.WriteNextCurrentAssembly(Path.GetFullPath(args[1]), defaultPath);
int ReadAdd(string path)
{
    var context = new AssemblyLoadContext(Guid.NewGuid().ToString(), isCollectible: true);
    try
    {
        var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
        return Convert.ToInt32(assembly.GetType(DheFixturePolicy.Calculator, true)!
            .GetMethod("Add")!.Invoke(null, new object[] { 1 }));
    }
    finally { context.Unload(); }
}
int seedValue = ReadAdd(args[1]);
checks["observable-next-advances-clr-result"] = ReadAdd(nextPath) == seedValue + 1;
checks["observable-latest-advances-clr-result"] = ReadAdd(latestPath) == seedValue + 2;
checks["default-generator-preserves-observed-result"] = ReadAdd(defaultPath) == seedValue;
var latestMv = MetaVersionSnapshot.Create(latestPath);
checks["observable-variant-preserves-declarations"] = after.Types.Select(type => type.StableId)
    .SequenceEqual(latestMv.Types.Select(type => type.StableId)) &&
    after.Fields.Select(field => field.StableId).SequenceEqual(latestMv.Fields.Select(field => field.StableId)) &&
    after.Methods.Select(method => method.StableId).SequenceEqual(latestMv.Methods.Select(method => method.StableId));
JsonElement expectedObservation = JsonSerializer.Deserialize<JsonElement>("{\"addResult\":102,\"stableResult\":4,\"addViaStableResult\":104}");
DheObservedResults.Validate(expectedObservation, expectedObservation);
checks["equal-observations-accepted"] = true;
foreach (string field in DheObservedResults.Fields)
{
    var wrong = JsonSerializer.Deserialize<Dictionary<string, int>>(expectedObservation.GetRawText())!;
    wrong[field]--;
    checks["stale-observation-rejected-" + field] = Rejects(() => DheObservedResults.Validate(expectedObservation,
        JsonSerializer.SerializeToElement(wrong)));
    wrong.Remove(field);
    checks["missing-observation-rejected-" + field] = Rejects(() => DheObservedResults.Validate(expectedObservation,
        JsonSerializer.SerializeToElement(wrong)));
}
checks["non-numeric-observation-rejected"] = Rejects(() => DheObservedResults.Validate(expectedObservation,
    JsonSerializer.Deserialize<JsonElement>("{\"addResult\":\"102\",\"stableResult\":4,\"addViaStableResult\":104}")));
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new
{
    passed, baseline = Path.GetFullPath(args[0]), current = Path.GetFullPath(args[1]), checks,
}, new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
