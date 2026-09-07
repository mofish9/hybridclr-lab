using System.Text;
using System.Text.Json;
using HybridCLR.Lab;

if (args.Length != 1) throw new ArgumentException("Pass a new test output directory.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output exists.");
Directory.CreateDirectory(output);
var checks = new Dictionary<string, bool>();
var one = new DheDifferentialCase("case-a", "numeric", "managed-core", new[] { "integer" },
    "Fixture.Registry", "First", 0x06000001, false, "42", "side", null, 0, 1);
var two = one with { Id = "case-b", MethodName = "Second", MethodToken = 0x06000002,
    ReturnValue = null, SideEffect = null, ExceptionType = "System.InvalidOperationException" };
var reference = new DheDifferentialResult("HybridCLR.ManagedCases", false, new[] { one, two });
var actual = reference with { NativeDiagnostics = true };
var manifest = JsonSerializer.SerializeToElement(new { schemaVersion = 2, suiteId = "test", cases = new[] { one, two }
    .Select(item => new { id = item.Id, category = item.Category, layer = item.Layer, features = item.Features }) });
var golden = JsonSerializer.SerializeToElement(new { schemaVersion = 1, suiteId = "test", cases = new[] { one, two }
    .Select(item => new { id = item.Id, category = item.Category, layer = item.Layer, features = item.Features,
        returnValue = item.ReturnValue, sideEffect = item.SideEffect, exceptionType = item.ExceptionType }) });
bool Rejects(Action action)
{
    try { action(); return false; }
    catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or ArgumentException) { return true; }
}
void Validate(DheDifferentialResult value) => DheDifferentialEvidence.ValidateObservations(reference, value, manifest, golden);
Validate(actual);
checks["equal-complete-observations-pass"] = true;
checks["missing-case-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one } }));
checks["extra-case-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one, two, two } }));
checks["case-order-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { two, one } }));
checks["return-difference-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one with { ReturnValue = "43" }, two } }));
checks["side-effect-difference-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one with { SideEffect = "different" }, two } }));
checks["missing-exception-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one, two with { ExceptionType = null } } }));
checks["wrong-exception-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one, two with { ExceptionType = "System.Exception" } } }));
checks["category-difference-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one with { Category = "other" }, two } }));
checks["feature-difference-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one with { Features = Array.Empty<string>() }, two } }));
checks["delegate-identity-difference-rejected"] = Rejects(() => Validate(actual with { Cases = new[] { one with { MethodName = "Other" }, two } }));
checks["native-clr-reference-rejected"] = Rejects(() => DheDifferentialEvidence.ValidateObservations(actual, actual, manifest, golden));
checks["both-wrong-against-golden-rejected"] = Rejects(() => DheDifferentialEvidence.ValidateObservations(
    reference with { Cases = new[] { one with { ReturnValue = "bad" }, two } },
    actual with { Cases = new[] { one with { ReturnValue = "bad" }, two } }, manifest, golden));

byte[] Encode(DheDifferentialResult result)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
    writer.Write(Encoding.ASCII.GetBytes("DHESUITE")); writer.Write(1);
    writer.Write(result.AssemblyName); writer.Write(result.NativeDiagnostics); writer.Write(result.Cases.Length);
    void Nullable(string? value) { writer.Write(value != null); if (value != null) writer.Write(value); }
    foreach (var item in result.Cases)
    {
        writer.Write(item.Id); writer.Write(item.Category); writer.Write(item.Layer); writer.Write(item.Features.Length);
        foreach (string feature in item.Features) writer.Write(feature);
        writer.Write(item.DeclaringType); writer.Write(item.MethodName); writer.Write(item.MethodToken); writer.Write(item.MethodChanged);
        Nullable(item.ReturnValue); Nullable(item.SideEffect); Nullable(item.ExceptionType);
        writer.Write(item.InterpreterEntries); writer.Write(item.AotEntries);
    }
    writer.Write(0x454e4f44); writer.Flush();
    return stream.ToArray();
}
byte[] valid = Encode(actual);
string Write(string name, byte[] bytes)
{
    string path = Path.Combine(output, name + ".bin"); File.WriteAllBytes(path, bytes); return path;
}
Validate(DheDifferentialEvidence.Read(Write("valid", valid)));
checks["binary-roundtrip"] = true;
checks["every-truncation-rejected"] = Enumerable.Range(0, valid.Length).All(length =>
    Rejects(() => DheDifferentialEvidence.Read(Write("truncated-" + length, valid.Take(length).ToArray()))));
checks["trailing-bytes-rejected"] = Rejects(() => DheDifferentialEvidence.Read(Write("trailing", valid.Concat(new byte[] { 0 }).ToArray())));
checks["duplicate-binary-ids-rejected"] = Rejects(() => DheDifferentialEvidence.Read(Write("duplicate", Encode(actual with { Cases = new[] { one, one } }))));
byte[] wrongHeader = (byte[])valid.Clone(); wrongHeader[0] = 0;
checks["header-rejected"] = Rejects(() => DheDifferentialEvidence.Read(Write("header", wrongHeader)));
byte[] wrongVersion = (byte[])valid.Clone(); wrongVersion[8] = 2;
checks["version-rejected"] = Rejects(() => DheDifferentialEvidence.Read(Write("version", wrongVersion)));
byte[] wrongBoolean = (byte[])valid.Clone(); wrongBoolean[12 + 1 + Encoding.UTF8.GetByteCount(actual.AssemblyName)] = 2;
checks["boolean-rejected"] = Rejects(() => DheDifferentialEvidence.Read(Write("boolean", wrongBoolean)));
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(new { passed = checks.Values.All(value => value), checks },
    new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return checks.Values.All(value => value) ? 0 : 1;
