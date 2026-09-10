using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HybridCLR.Editor.Commands;

internal static class InlineGuardTests
{
    internal static int Run(string[] args)
    {
        if (args.Length < 2 || args.Length > 3) throw new ArgumentException("inline-guard-policy <Base proof> <new report> [require-guards]");
        if (File.Exists(args[1])) throw new IOException("Inline report must be new.");
        string manifestPath = Path.Combine(args[0], "base/native/dhe-native-manifest.json");
        var manifest = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(manifestPath));
        var methods = manifest.GetProperty("methods").EnumerateArray().ToArray();
        var primary = methods.Where(method => !method.GetProperty("functionName").GetString()!.EndsWith("_inline", StringComparison.Ordinal))
            .GroupBy(method => method.GetProperty("functionName").GetString()!).ToDictionary(group => group.Key, group => group.First());
        var guarded = methods.Select(method => Path.GetFullPath(method.GetProperty("sourceFile").GetString()!) + "\n" + method.GetProperty("functionName").GetString()).ToHashSet();
        var checks = new Dictionary<string, bool>(); var copies = new List<object>(); var missing = new List<string>();
        string firstName = null, firstSignature = null; JsonElement firstPrimary = default;
        foreach (string file in Directory.GetFiles(manifest.GetProperty("generatedCppRoot").GetString()!, "*.cpp", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllLines(file);
            for (int line = 0; line + 1 < lines.Length; ++line)
            {
                if (!lines[line].Contains("IL2CPP_MANAGED_FORCE_INLINE") || lines[line].TrimEnd().EndsWith(";") || lines[line + 1].Trim() != "{") continue;
                var match = Regex.Match(lines[line], @"\b(?<name>[A-Za-z_][A-Za-z0-9_]*_inline)\s*\(");
                if (!match.Success) continue;
                string name = match.Groups["name"].Value, origin = name.Substring(0, name.Length - 7);
                if (!primary.TryGetValue(origin, out var indexed)) continue;
                DheInlineGuardPolicy.ValidateCopy(origin, indexed.GetProperty("nativeSignature").GetString()!, name, lines[line]);
                bool declared = guarded.Contains(Path.GetFullPath(file) + "\n" + name);
                bool marker = lines[line + 2].Contains("HYBRIDCLR_DHE_GUARD_BEGIN_V1:" + name + ":");
                if (!declared || !marker) missing.Add(file + ":" + (line + 1) + ":" + name);
                copies.Add(new { file, line = line + 1, function = name, declared, marker });
                if (firstName == null) { firstName = name; firstSignature = lines[line]; firstPrimary = indexed; }
            }
        }
        checks["real-inline-copies-found"] = copies.Count >= 2;
        bool Reject(string name, string signature)
        {
            try { DheInlineGuardPolicy.ValidateCopy(firstPrimary.GetProperty("functionName").GetString()!, firstPrimary.GetProperty("nativeSignature").GetString()!, name, signature); return false; }
            catch (InvalidDataException) { return true; }
        }
        checks["foreign-symbol-rejected"] = Reject(firstName + "_other", firstSignature);
        checks["ordinary-definition-not-an-alias"] = Reject(firstName, firstSignature.Replace("IL2CPP_MANAGED_FORCE_INLINE", "IL2CPP_EXTERN_C"));
        checks["changed-method-context-abi-rejected"] = Reject(firstName, firstSignature.Replace("const RuntimeMethod* method", "RuntimeMethod* method"));
        if (args.Length == 3) checks["every-indexed-inline-copy-has-a-guard"] = args[2] == "require-guards" && missing.Count == 0;
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(args[1], JsonSerializer.Serialize(new { passed, checks, inlineCopyCount = copies.Count, missing, copies,
            nativeManifestSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(manifestPath))),
            scope = "Read-only indexed native symbols and real inline definitions; optional complete marker/manifest coverage gate" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Inline guards: {passed}; copies={copies.Count}, missing={missing.Count}"); return passed ? 0 : 1;
    }
}
