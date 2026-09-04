using System.Text.Json;
using System.Linq;

namespace HybridCLR.DheTool;

internal static partial class Program
{
    private sealed record BaseRegistryEntry(
        string BaseId,
        string EngineWorkflow,
        string PayloadVariantId,
        string BaselineRoot,
        string NativeManifest,
        string BuildIdentity,
        string? AotMetadataRoot);

    private sealed record BaseRegistryDocument(
        string SourcePath,
        string PathSemantics,
        string Sha256,
        BaseRegistryEntry[] Entries);

    private static BaseRegistryDocument ReadBaseRegistry(string path)
    {
        string sourcePath = RequireFile(path, "DHE Base registry");
        JsonElement document = ReadJson<JsonElement>(sourcePath);
        if (GetInt(document, "schemaVersion") != 1 ||
            !string.Equals(GetString(document, "format"),
                "hybridclr.dhe-base-registry.json", StringComparison.Ordinal))
            throw new DheException("DHE Base registry must use schema v1.");

        string pathSemantics = GetString(document, "pathSemantics") ?? string.Empty;
        if (pathSemantics is not ("registry-relative-v1" or "workspace-absolute-v1"))
            throw new DheException("DHE Base registry pathSemantics is invalid.");
        if (!document.TryGetProperty("bases", out JsonElement bases) ||
            bases.ValueKind != JsonValueKind.Array || bases.GetArrayLength() == 0)
            throw new DheException("DHE Base registry must contain at least one Base.");
        if (bases.GetArrayLength() > 1024)
            throw new DheException("DHE Base registry contains too many Base entries.");

        string registryDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath))!;
        var entries = new List<BaseRegistryEntry>(bases.GetArrayLength());
        var baseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var buildIdentityPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement item in bases.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new DheException("DHE Base registry contains a non-object Base entry.");
            string baseId = GetString(item, "baseId") ?? string.Empty;
            if (!IsHex(baseId, 64, 64) || !baseIds.Add(baseId))
                throw new DheException("DHE Base registry contains an invalid or duplicate baseId.");
            string engineWorkflow = GetString(item, "engineWorkflow") ?? string.Empty;
            if (!RequiredPlayerEngineWorkflows.Contains(engineWorkflow,
                    StringComparer.Ordinal))
                throw new DheException("DHE Base registry contains an unsupported engineWorkflow: " +
                    engineWorkflow);
            string payloadVariantId = GetString(item, "payloadVariantId") ?? "default";
            if (!IsPayloadVariantId(payloadVariantId))
                throw new DheException("DHE Base registry contains an invalid payloadVariantId.");

            string baselineRoot = ResolveBaseRegistryPath(item, "baselineRoot", registryDirectory,
                pathSemantics, requireDirectory: true);
            string nativeManifest = ResolveBaseRegistryPath(item, "nativeManifest", registryDirectory,
                pathSemantics, requireDirectory: false);
            string buildIdentity = ResolveBaseRegistryPath(item, "buildIdentity", registryDirectory,
                pathSemantics, requireDirectory: false);
            if (!buildIdentityPaths.Add(buildIdentity))
                throw new DheException("DHE Base registry reuses one build identity path for multiple entries.");

            string? aotMetadataRoot = null;
            if (item.TryGetProperty("aotMetadataRoot", out JsonElement metadataValue) &&
                metadataValue.ValueKind != JsonValueKind.Null)
            {
                if (metadataValue.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(metadataValue.GetString()))
                    throw new DheException("DHE Base registry aotMetadataRoot must be a path or null.");
                aotMetadataRoot = ResolveBaseRegistryPath(item, "aotMetadataRoot",
                    registryDirectory, pathSemantics, requireDirectory: true);
            }

            entries.Add(new BaseRegistryEntry(baseId, engineWorkflow, payloadVariantId, baselineRoot,
                nativeManifest, buildIdentity, aotMetadataRoot));
        }

        return new BaseRegistryDocument(sourcePath, pathSemantics, Sha256File(sourcePath),
            entries.ToArray());
    }

    private static bool IsPayloadVariantId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            return false;
        return value.All(character => char.IsLetterOrDigit(character) ||
            character is '-' or '_' or '.');
    }

    private static string ResolveBaseRegistryPath(JsonElement entry, string property,
        string registryDirectory, string pathSemantics, bool requireDirectory)
    {
        string raw = GetString(entry, property) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(raw) || raw.IndexOf('\0') >= 0)
            throw new DheException("DHE Base registry " + property + " is missing or invalid.");
        bool rooted = Path.IsPathRooted(raw);
        if (pathSemantics == "registry-relative-v1" && rooted)
            throw new DheException("DHE Base registry " + property +
                " must be relative to the registry file.");
        if (pathSemantics == "workspace-absolute-v1" && !rooted)
            throw new DheException("DHE Base registry " + property +
                " must be an absolute workspace path.");
        string resolved = Path.GetFullPath(rooted ? raw : Path.Combine(registryDirectory, raw));
        if (requireDirectory)
            RequireDirectory(resolved, "DHE Base registry " + property);
        else
            RequireFile(resolved, "DHE Base registry " + property);
        return resolved;
    }
}
