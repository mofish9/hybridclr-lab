using System.Security.Cryptography;
using System.Text.Json;

namespace HybridCLR.DheTool;

internal static class OrdinaryGuardInventory
{
    // Generated from the actual stripped Base inputs before native finalization.
    // This does not add ordinary assemblies to the mutable hotfix set.
    public static void Generate(string aotRoot, IEnumerable<string> mutableAssemblies,
        string identityType, string outputRoot)
    {
        var mutable = mutableAssemblies.ToHashSet(StringComparer.Ordinal);
        string[] files = Directory.GetFiles(aotRoot, "*.dll").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        if (files.Length == 0 || !mutable.IsSubsetOf(files.Select(path => Path.GetFileNameWithoutExtension(path))))
            throw new InvalidDataException("Ordinary guard inventory requires the complete stripped Base input set.");
        if (string.IsNullOrWhiteSpace(identityType) || Directory.Exists(outputRoot))
            throw new InvalidDataException("Ordinary guard inventory requires an identity type and a new output directory.");
        Directory.CreateDirectory(outputRoot);
        var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var sources = new List<object>();
        int identityOwners = 0;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (string file in files)
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (mutable.Contains(name)) continue;
            var mv = MetaVersionSnapshot.Create(file);
            if (mv.AssemblyName != name || !names.Add(name))
                throw new InvalidDataException("Ordinary guard source file identity mismatch: " + file);
            var excluded = mv.Types.Where(type => type.Identity == identityType).Select(type => type.StableId).ToHashSet(StringComparer.Ordinal);
            // Type identities in MV include the namespace but not the assembly.
            if (excluded.Count != 0) identityOwners++;
            var methods = mv.Methods.Where(method => !excluded.Contains(method.DeclaringTypeStableId)).ToArray();
            string output = Path.Combine(outputRoot, name + ".mv.json");
            File.WriteAllText(output, JsonSerializer.Serialize(new { mv.AssemblyName, methods, mv.AssemblySha256 }, json));
            sources.Add(new { assemblyName = name, source = Path.GetFullPath(file), sourceSha256 = mv.AssemblySha256,
                guardMvJson = Path.GetFileName(output), guardMvJsonSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))),
                methodCount = methods.Length, excludedIdentityMethodCount = mv.Methods.Length - methods.Length });
        }
        if (identityOwners != 1) throw new InvalidDataException("Ordinary guard inventory requires exactly one generated identity owner.");
        File.WriteAllText(Path.Combine(outputRoot, "ordinary-guard-inventory.json"), JsonSerializer.Serialize(new {
            schemaVersion = 1, format = "hybridclr.dhe-ordinary-guard-inventory.json", sourceRoot = Path.GetFullPath(aotRoot),
            mutableAssemblyNames = mutable.OrderBy(name => name, StringComparer.Ordinal).ToArray(), identityType,
            ordinaryAssemblyCount = sources.Count, sources,
            scope = "Complete original ordinary AOT method requests; final native coverage must be independently validated" }, json));
        Console.WriteLine("DHE ordinary guard inventory: " + sources.Count + " assemblies");
    }
}
