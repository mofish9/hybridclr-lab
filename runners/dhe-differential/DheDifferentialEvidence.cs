using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HybridCLR.DheTool;

namespace HybridCLR.Lab;

internal sealed record DheDifferentialCase(string Id, string Category, string Layer,
    string[] Features, string DeclaringType, string MethodName, int MethodToken,
    bool MethodChanged, string? ReturnValue, string? SideEffect, string? ExceptionType,
    int InterpreterEntries, int AotEntries);

internal sealed record DheDifferentialResult(string AssemblyName, bool NativeDiagnostics,
    DheDifferentialCase[] Cases);

internal static class DheDifferentialEvidence
{
    public static DheDifferentialResult Read(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 4 * 1024 * 1024)
            throw new InvalidDataException("Missing or oversized differential result: " + path);
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream, new UTF8Encoding(false, true));
        if (!reader.ReadBytes(8).SequenceEqual(Encoding.ASCII.GetBytes("DHESUITE")) || reader.ReadInt32() != 1)
            throw new InvalidDataException("Differential record header mismatch.");
        string assembly = Text(reader);
        bool native = Boolean(reader);
        int count = Count(reader, 4096);
        var records = new List<DheDifferentialCase>();
        for (int index = 0; index < count; index++)
        {
            string id = Text(reader);
            try
            {
                string category = Text(reader), layer = Text(reader);
                string[] features = Enumerable.Range(0, Count(reader, 128)).Select(_ => Text(reader)).ToArray();
                string declaringType = Text(reader), methodName = Text(reader);
                int token = reader.ReadInt32();
                bool changed = Boolean(reader);
                records.Add(new(id, category, layer, features, declaringType, methodName, token,
                    changed, NullableText(reader), NullableText(reader), NullableText(reader),
                    reader.ReadInt32(), reader.ReadInt32()));
            }
            catch (Exception exception) when (exception is IOException or ArgumentException)
            {
                throw new InvalidDataException("Incomplete differential case: " + id, exception);
            }
        }
        if (reader.ReadInt32() != 0x454e4f44 || stream.Position != stream.Length)
            throw new InvalidDataException("Differential completion marker or trailing bytes mismatch.");
        if (records.Select(record => record.Id).Distinct(StringComparer.Ordinal).Count() != records.Count)
            throw new InvalidDataException("Duplicate differential case IDs.");
        return new(assembly, native, records.ToArray());
    }

    public static void ValidateObservations(DheDifferentialResult reference, DheDifferentialResult actual,
        JsonElement manifest, JsonElement golden)
    {
        Require(reference.AssemblyName == "HybridCLR.ManagedCases" && actual.AssemblyName == reference.AssemblyName,
            "Differential assembly mismatch.");
        Require(!reference.NativeDiagnostics, "CLR reference must not impersonate a native DHE run.");
        Require(manifest.GetProperty("schemaVersion").GetInt32() == 2 && golden.GetProperty("schemaVersion").GetInt32() == 1 &&
            manifest.GetProperty("suiteId").GetString() == golden.GetProperty("suiteId").GetString(), "Suite identity mismatch.");
        var definitions = manifest.GetProperty("cases").EnumerateArray().ToArray();
        var expected = golden.GetProperty("cases").EnumerateArray().ToArray();
        Require(definitions.Length > 0 && definitions.Length == expected.Length &&
            definitions.Length == reference.Cases.Length && definitions.Length == actual.Cases.Length,
            "Manifest, golden, CLR and Player case counts differ.");
        Require(definitions.Select(value => value.GetProperty("id").GetString()).Distinct(StringComparer.Ordinal).Count() == definitions.Length,
            "Duplicate manifest case IDs.");
        for (int index = 0; index < definitions.Length; index++)
        {
            var entry = definitions[index];
            var contract = expected[index];
            var clr = reference.Cases[index];
            var player = actual.Cases[index];
            foreach (var record in new[] { clr, player })
            {
                Require(record.Id == entry.GetProperty("id").GetString() && record.Id == contract.GetProperty("id").GetString(),
                    "Differential case identity/order mismatch at " + index);
                foreach (var metadata in new[] { entry, contract })
                    Require(record.Category == metadata.GetProperty("category").GetString() &&
                        record.Layer == metadata.GetProperty("layer").GetString() &&
                        record.Features.SequenceEqual(metadata.GetProperty("features").EnumerateArray().Select(value => value.GetString()), StringComparer.Ordinal),
                        record.Id + ": case metadata mismatch.");
                Require(record.ExceptionType == Optional(contract, "exceptionType"), record.Id + ": exception mismatch: " + record.ExceptionType);
                if (record.ExceptionType == null)
                    Require(record.ReturnValue == Optional(contract, "returnValue") && record.SideEffect == Optional(contract, "sideEffect"),
                        record.Id + ": return/side-effect mismatch: " + record.ReturnValue + " / " + record.SideEffect);
            }
            Require(clr.DeclaringType == player.DeclaringType && clr.MethodName == player.MethodName &&
                clr.ReturnValue == player.ReturnValue && clr.SideEffect == player.SideEffect && clr.ExceptionType == player.ExceptionType,
                clr.Id + ": CLR/Player difference.");
        }
    }

    public static object Validate(string referencePath, string actualPath, string manifestPath, string goldenPath,
        string currentAssembly, byte[] baseMvBytes, string traceRoot, bool requireInterpretedEntries)
    {
        DheDifferentialResult reference = Read(referencePath), actual = Read(actualPath);
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        using var golden = JsonDocument.Parse(File.ReadAllText(goldenPath));
        ValidateObservations(reference, actual, manifest.RootElement, golden.RootElement);
        Require(actual.NativeDiagnostics, "Player lacks native DHE method-state evidence.");
        string identityPath = referencePath + ".identity.json";
        using var identity = JsonDocument.Parse(File.ReadAllText(identityPath));
        Require(identity.RootElement.GetProperty("format").GetString() == "hybridclr.dhe-differential-reference.json" &&
            identity.RootElement.GetProperty("schemaVersion").GetInt32() == 1 &&
            identity.RootElement.GetProperty("passed").GetBoolean() &&
            identity.RootElement.GetProperty("caseCount").GetInt32() == reference.Cases.Length &&
            identity.RootElement.GetProperty("manifestSha256").GetString() == Hash(manifestPath) &&
            identity.RootElement.GetProperty("goldenSha256").GetString() == Hash(goldenPath) &&
            identity.RootElement.GetProperty("inputSha256").GetString() == Hash(currentAssembly) &&
            identity.RootElement.GetProperty("resultSha256").GetString() == Hash(referencePath),
            "CLR reference is not bound to the exact shipped current assembly and result.");
        var current = MetaVersionSnapshot.Create(currentAssembly);
        var methods = current.Methods.ToDictionary(method => method.Token);
        var baseline = DheFixtureMetaVersion.Read(baseMvBytes, current.AssemblyName);
        var traces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(traceRoot))
            foreach (string path in Directory.EnumerateFileSystemEntries(traceRoot))
            {
                string name = Path.GetFileName(path);
                Require(Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0 &&
                    name.Length == 64 && name.All(Uri.IsHexDigit) && !Directory.EnumerateFileSystemEntries(path).Any(),
                    "Malformed case-entry receipt: " + path);
                traces.Add(name);
            }
        var observed = new List<object>();
        for (int index = 0; index < reference.Cases.Length; index++)
        {
            var clr = reference.Cases[index];
            var player = actual.Cases[index];
            Require(methods.TryGetValue(unchecked((uint)clr.MethodToken), out var method), clr.Id + ": missing current delegate method token.");
            string prefix = clr.DeclaringType + "::" + clr.MethodName + "|";
            Require(method!.Identity.StartsWith(prefix, StringComparison.Ordinal), clr.Id + ": delegate method identity mismatch.");
            bool expectedChanged = !baseline.methods.TryGetValue(method.StableId, out string? baseVersion) ||
                !string.Equals(baseVersion, method.Version, StringComparison.OrdinalIgnoreCase);
            Require(player.MethodChanged == expectedChanged, clr.Id + ": native method state disagrees with Base/current MV.");
            bool receipt = traces.Contains(method.StableId);
            if (requireInterpretedEntries)
                Require(expectedChanged && receipt, clr.Id + ": interpreted entry was not actually executed.");
            observed.Add(new { id = clr.Id, methodStableId = method.StableId, expectedChanged,
                nativeMethodChanged = player.MethodChanged, entryReceipt = receipt,
                player.InterpreterEntries, player.AotEntries });
        }
        Require(traces.IsSubsetOf(current.Methods.Select(method => method.StableId)), "Unknown method-entry receipts.");
        return new { passed = true, caseCount = actual.Cases.Length, differentialCount = 0,
            referencePath, referenceSha256 = Hash(referencePath), actualPath, actualSha256 = Hash(actualPath),
            manifestSha256 = Hash(manifestPath), goldenSha256 = Hash(goldenPath),
            currentAssemblySha256 = Hash(currentAssembly), requireInterpretedEntries,
            entryReceiptCount = traces.Count, methods = observed };
    }

    public static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
    private static int Count(BinaryReader reader, int maximum)
    {
        int count = reader.ReadInt32();
        if (count < 0 || count > maximum) throw new InvalidDataException("Differential count is out of bounds.");
        return count;
    }
    private static bool Boolean(BinaryReader reader)
    {
        byte value = reader.ReadByte();
        if (value > 1) throw new InvalidDataException("Invalid differential Boolean.");
        return value != 0;
    }
    private static string Text(BinaryReader reader)
    {
        string value = reader.ReadString();
        if (value.Length > 16384) throw new InvalidDataException("Differential string is too long.");
        return value;
    }
    private static string? NullableText(BinaryReader reader) => Boolean(reader) ? Text(reader) : null;
    private static string? Optional(JsonElement value, string name) => value.TryGetProperty(name, out var item) ? item.GetString() : null;
    private static void Require(bool condition, string error)
    {
        if (!condition) throw new InvalidDataException(error);
    }
}
