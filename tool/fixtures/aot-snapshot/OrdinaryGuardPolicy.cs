using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;
using HybridCLR.Editor.Commands;

internal static class OrdinaryGuardPolicy
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("ordinary-guard-policy <proof-13 root> <new output>");
        string proof = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string Hash(string file) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        JsonElement Read(string file) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(file));
        var json = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true };
        string Serialize(object value) => JsonSerializer.Serialize(value, value.GetType(), json);
        string Git(string root, params string[] arguments)
        {
            var start = new ProcessStartInfo("git") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-C"); start.ArgumentList.Add(root);
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; string value = process.StandardOutput.ReadToEnd(); process.WaitForExit();
            if (process.ExitCode != 0) throw new IOException("Cannot bind source identity.");
            return value.Trim();
        }
        string lab = Path.GetFullPath("../../../../../..", AppContext.BaseDirectory);
        string package = typeof(OrdinaryGuardPolicy).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(attribute => attribute.Key == "DhePackageRoot").Value!;
        string labHead = Git(lab, "rev-parse", "HEAD"), packageHead = Git(package, "rev-parse", "HEAD");
        foreach (string root in new[] { lab, package }) if (Git(root, "status", "--porcelain").Length != 0)
            throw new InvalidDataException("Commit sources before guard policy verification.");
        string identityPath = Path.Combine(proof, "base/build-identity.json");
        var identity = Read(identityPath);
        string[] mutable = identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        string before = Path.Combine(proof, "base", identity.GetProperty("aotAnalysisSnapshot").GetString()!);
        before = Path.Combine(Path.GetDirectoryName(before)!, "assemblies");
        string after = Path.Combine(proof, "project/Library/Bee/artifacts/WinPlayerBuildProgram/ManagedStripped");
        const string identityType = "HybridCLR.Lab.Snapshot.DheBuildIdentity";
        string requests = Path.Combine(output, "requests");
        var first = DheOrdinaryGuardInventory.Generate(before, mutable, identityType, requests, Serialize);
        var firstManifest = Read(first.ManifestPath);
        string firstHash = Hash(first.ManifestPath);
        int initialCoreMethods = Read(Path.Combine(requests, "UnityEngine.CoreModule.mv.json")).GetProperty("methods").GetArrayLength();
        var final = DheOrdinaryGuardInventory.Generate(after, mutable, identityType, requests, Serialize);
        var manifest = Read(final.ManifestPath);
        var checks = new Dictionary<string, bool>();
        var sources = manifest.GetProperty("sources").EnumerateArray().ToArray();
        checks["real-final-strip-changes-core-module-method-table"] = initialCoreMethods !=
            Read(Path.Combine(requests, "UnityEngine.CoreModule.mv.json")).GetProperty("methods").GetArrayLength();
        checks["regeneration-replaces-inventory-identity"] = firstHash != Hash(final.ManifestPath);
        checks["complete-ordinary-assembly-set"] = sources.Select(row => row.GetProperty("assemblyName").GetString()!).ToHashSet()
            .SetEquals(Directory.GetFiles(after, "*.dll").Select(file => Path.GetFileNameWithoutExtension(file)).Except(mutable));
        checks["hotfix-set-unchanged"] = manifest.GetProperty("mutableAssemblyNames").EnumerateArray().Select(value => value.GetString()!).ToHashSet().SetEquals(mutable);
        checks["all-source-and-request-hashes-bound"] = sources.All(row =>
            Hash(row.GetProperty("source").GetString()!) == row.GetProperty("sourceSha256").GetString() &&
            Hash(Path.Combine(requests, row.GetProperty("guardMvJson").GetString()!)) == row.GetProperty("guardMvJsonSha256").GetString());
        // Compare independent package request serialization to the established
        // MV compiler, including framework generics and the generated identity owner.
        foreach (string name in new[] { "Assembly-CSharp", "HybridCLR.ValueLayoutNative", "UnityEngine.CoreModule", "mscorlib" })
        {
            Console.WriteLine("Compare package guards with MV compiler: " + name);
            var mv = MetaVersionSnapshot.Create(Path.Combine(after, name + ".dll"));
            var actual = Read(Path.Combine(requests, name + ".mv.json")).GetProperty("methods").EnumerateArray().ToDictionary(row => row.GetProperty("token").GetUInt32());
            var expected = mv.Methods.Where(method => method.DeclaringType != identityType).ToArray();
            checks["mv-protocol-equivalence-" + name] = actual.Count == expected.Length && expected.All(method =>
                actual.TryGetValue(method.Token, out var row) && row.GetProperty("identity").GetString() == method.Identity &&
                string.Equals(row.GetProperty("stableId").GetString(), method.StableId, StringComparison.OrdinalIgnoreCase) && row.GetProperty("flags").GetUInt32() == method.Flags &&
                row.GetProperty("returnType").GetString() == method.ReturnType &&
                row.GetProperty("parameterTypes").EnumerateArray().Select(value => value.GetString()).SequenceEqual(method.ParameterTypes) &&
                row.GetProperty("hasThis").GetBoolean() == method.HasThis &&
                row.GetProperty("declaringTypeIsValueType").GetBoolean() == method.DeclaringTypeIsValueType &&
                row.GetProperty("genericParameterCount").GetUInt32() == method.GenericParameterCount &&
                row.GetProperty("declaringTypeGenericParameterCount").GetUInt32() == method.DeclaringTypeGenericParameterCount);
        }
        checks["generated-identity-methods-excluded"] = sources.Sum(row => row.GetProperty("excludedIdentityMethodCount").GetInt32()) == 2;
        bool missingRejected = false;
        try { DheOrdinaryGuardInventory.Generate(after, mutable.Concat(new[] { "Absent.Assembly" }), identityType, Path.Combine(output, "missing"), Serialize); }
        catch (InvalidDataException) { missingRejected = true; }
        checks["missing-hotfix-inventory-rejected"] = missingRejected;
        bool ownerRejected = false;
        try { DheOrdinaryGuardInventory.Generate(after, mutable, "Absent.Identity", Path.Combine(output, "wrong-identity"), Serialize); }
        catch (InvalidDataException) { ownerRejected = true; }
        checks["missing-generated-identity-rejected"] = ownerRejected;
        checks["source-commits-stable"] = Git(lab, "rev-parse", "HEAD") == labHead && Git(package, "rev-parse", "HEAD") == packageHead &&
            Git(lab, "status", "--porcelain").Length == 0 && Git(package, "status", "--porcelain").Length == 0;
        File.WriteAllText(Path.Combine(output, "result.json"), Serialize(new { passed = checks.Values.All(value => value), checks,
            labHead, packageHead, hostSha256 = Hash(typeof(OrdinaryGuardPolicy).Assembly.Location), initialInventorySha256 = firstHash,
            finalInventorySha256 = Hash(final.ManifestPath), initialCoreMethods,
            scope = "Actual changing Unity stripped sources and package/MV compiler protocol equivalence; not Player startup qualification" }));
        foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
        return checks.Values.All(value => value) ? 0 : 1;
    }
}
