using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class FrozenAotMaterialize
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));

    public static int Run(string[] args)
    {
        if (args.Length != 5)
            throw new ArgumentException("materialize-frozen-aot <Base build identity> <latest hotfix DLL root> <asset output root> <runtime asset root> <Base MV asset root>");
        string identityPath = Path.GetFullPath(args[0]), currentRoot = Path.GetFullPath(args[1]);
        string outputRoot = Path.GetFullPath(args[2]);
        string runtimeAssetRoot = NormalizeAssetRoot(args[3]);
        string baseMetaAssetRoot = NormalizeAssetRoot(args[4]);
        if (Directory.Exists(outputRoot)) throw new IOException("Frozen AOT asset output must be new: " + outputRoot);
        Directory.CreateDirectory(outputRoot);
        JsonElement identity = Read(identityPath);
        string[] hotfixNames = identity.GetProperty("assemblies").EnumerateArray()
            .Select(row => row.GetProperty("assemblyName").GetString()!).ToArray();
        string[] aotNames = identity.GetProperty("aotAssemblyNames").EnumerateArray()
            .Select(row => row.GetString()!).ToArray();
        AotAnalysisSnapshot snapshot = AotAnalysisSnapshot.Read(identityPath, identity, aotNames, hotfixNames)
            ?? throw new InvalidDataException("Base identity does not contain an authenticated AOT snapshot.");
        string[] baselines = identity.GetProperty("assemblies").EnumerateArray()
            .Select(row => row.GetProperty("baselinePath").GetString()!).ToArray();
        string[] current = hotfixNames.Select(name => Path.Combine(currentRoot, name + ".dll")).ToArray();
        var compilation = FrozenAotAdaptation.Compile(snapshot, baselines, current);
        var records = new List<object>();
        foreach (var plan in compilation.Assemblies)
        {
            var source = snapshot.Assemblies.Single(item => item.AssemblyName == plan.AssemblyName);
            string sourceRelative = "FrozenAot/" + identity.GetProperty("baseId").GetString()!.ToLowerInvariant() + "/" + plan.AssemblyName + ".dll";
            string mvRelative = plan.AssemblyName + ".mv.bytes";
            string sourcePath = Path.Combine(outputRoot, sourceRelative.Replace('/', Path.DirectorySeparatorChar));
            string mvPath = Path.Combine(outputRoot, baseMetaAssetRoot.TrimEnd('/')
                .Substring(runtimeAssetRoot.TrimEnd('/').Length).TrimStart('/')
                .Replace('/', Path.DirectorySeparatorChar), mvRelative);
            Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(mvPath)!);
            File.Copy(source.Path, sourcePath);
            var mv = MetaVersionSnapshot.Create(source.Path);
            mv.WriteBinary(mvPath);
            records.Add(new
            {
                assemblyName = plan.AssemblyName,
                source = runtimeAssetRoot + sourceRelative,
                sourceSha256 = Hash(File.ReadAllBytes(sourcePath)),
                baseMetaVersion = baseMetaAssetRoot + mvRelative,
                baseMetaVersionSha256 = Hash(File.ReadAllBytes(mvPath)),
                currentStorageTypeTokens = plan.ExecutionPlan.CurrentStorageTypeTokens,
                currentExecutionMethodTokens = plan.ExecutionPlan.CurrentExecutionMethodTokens,
                excludedBaseTypeTokens = plan.ExcludedBaseTypeTokens,
                sourceKind = "frozen-base-aot"
            });
        }
        var document = new
        {
            schemaVersion = 1,
            format = "hybridclr.dhe-frozen-aot-source-plan.json",
            baseId = identity.GetProperty("baseId").GetString(),
            aotAnalysisSnapshotSha256 = snapshot.Sha256,
            runtimeAssetRoot,
            baseMetaVersionAssetRoot = baseMetaAssetRoot,
            sourceCount = records.Count,
            sources = records,
            obligations = compilation.Obligations,
            scope = "Materialized Base assets and source-bound execution records; Player integration remains a separate gate"
        };
        string planPath = Path.Combine(outputRoot, "frozen-aot-source-plan.json");
        File.WriteAllText(planPath, JsonSerializer.Serialize(document, Json));
        Console.WriteLine("Materialized frozen AOT sources: " + records.Count + "; plan=" + planPath);
        return 0;
    }

    private static string NormalizeAssetRoot(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal))
            throw new ArgumentException("Asset root must be below Assets/: " + value);
        return value.Replace('\\', '/').TrimEnd('/') + "/";
    }
}
