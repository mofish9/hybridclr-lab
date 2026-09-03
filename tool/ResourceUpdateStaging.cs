using System.Text;
using System.Text.Json;

namespace HybridCLR.DheTool;

internal static partial class Program
{
    private static int StageResourceUpdate(Cli cli)
    {
        var updateRoot = RequireDirectory(cli.Require("updateroot"), "DHE resource update root");
        var assetRoot = RequireDirectory(cli.Require("assetroot"), "DHE runtime asset destination");
        var baseBuildIdentityPath = RequireFile(cli.Require("basebuildidentity"),
            "Base Player build identity");
        var baseBuildIdentity = ReadJson<JsonElement>(baseBuildIdentityPath);
        var manifestPath = RequireFile(Path.Combine(updateRoot, "dhe-resource-update.json"),
            "DHE resource update manifest");
        var manifest = ReadJson<JsonElement>(manifestPath);
        if (GetInt(manifest, "schemaVersion") != 1 ||
            !string.Equals(GetString(manifest, "format"), "hybridclr.dhe-resource-update.json",
                StringComparison.Ordinal) ||
            !string.Equals(GetString(manifest, "payloadModel"), "single-current-payload",
                StringComparison.Ordinal) ||
            !string.Equals(GetString(manifest, "compatibilityPolicy"),
                ResourceUpdateCompatibility.Policy, StringComparison.Ordinal) ||
            !string.Equals(GetString(manifest, "runtimeProtocol"),
                ResourceUpdateCompatibility.RuntimeProtocol, StringComparison.Ordinal) ||
            !GetBool(manifest, "compatibilityValidated") ||
            GetBool(manifest, "playerUpdateRequired"))
            throw new DheException("Resource update must be a schema v1 single-current-payload release.");

        var validationSource = ValidateResourceUpdateCompatibility(updateRoot, manifest);
        var selectedBase = ValidateStagingBuildIdentity(baseBuildIdentityPath,
            baseBuildIdentity, manifest);

        var runtimeAssetRoot = RequirePortableAssetRoot(GetString(manifest, "runtimeAssetRoot"),
            "runtimeAssetRoot");
        var baseMetaVersionAssetRoot = RequirePortableAssetRoot(
            GetString(manifest, "baseMetaVersionAssetRoot"), "baseMetaVersionAssetRoot");
        if (!baseMetaVersionAssetRoot.StartsWith(runtimeAssetRoot, StringComparison.OrdinalIgnoreCase))
            throw new DheException("BaseMetaVersionAssetRoot must be contained by RuntimeAssetRoot for staging.");
        var baseRelative = baseMetaVersionAssetRoot[runtimeAssetRoot.Length..].TrimEnd('/');
        if (!IsPortableRelativePath(baseRelative))
            throw new DheException("Base MetaVersion destination is not a portable relative path.");
        var embeddedBaseRoot = RequireDirectory(ResolveContainedPath(assetRoot, baseRelative,
            "Embedded Base MetaVersion root"), "Embedded Base MetaVersion root");
        var embeddedBase = ValidateEmbeddedBaseMetaVersionSet(embeddedBaseRoot, manifest,
            baseBuildIdentity, selectedBase);
        var baseTreeBefore = TreeHashForRelease(embeddedBaseRoot, Array.Empty<string>());

        var runtimePlanRelative = GetString(manifest, "runtimePlan") ?? string.Empty;
        var runtimePlanSource = RequireFile(ResolveContainedPath(updateRoot, runtimePlanRelative,
            "DHE runtime plan"), "DHE runtime plan");
        string runtimePlanSha256 = GetString(manifest, "runtimePlanSha256") ?? string.Empty;
        if (!IsHex(runtimePlanSha256, 64, 64) ||
            !string.Equals(Sha256File(runtimePlanSource), runtimePlanSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE runtime plan hash does not match the resource manifest.");
        var runtimePlan = ReadJson<JsonElement>(runtimePlanSource);
        if (GetInt(runtimePlan, "schemaVersion") != 1 ||
            !string.Equals(GetString(runtimePlan, "format"),
                "hybridclr.dhe-runtime-asset-plan.json", StringComparison.Ordinal) ||
            !string.Equals(GetString(runtimePlan, "selection"),
                "embedded-base-metaversion", StringComparison.Ordinal) ||
            !string.Equals(GetString(runtimePlan, "currentAssemblySetSha256"),
                GetString(manifest, "currentAssemblySetSha256"), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RequirePortableAssetRoot(GetString(runtimePlan, "baseMetaVersionAssetRoot"),
                    "runtime plan baseMetaVersionAssetRoot"), baseMetaVersionAssetRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE runtime plan is not bound to the resource update manifest.");

        var payloads = ValidateResourceUpdatePayload(updateRoot, manifest, runtimePlan,
            runtimeAssetRoot, baseMetaVersionAssetRoot);
        var immutableFiles = cli.GetList("immutablefiles").Select(path =>
            RequireFile(path, "Immutable Player file")).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var immutableBefore = immutableFiles.ToDictionary(path => path, Sha256File,
            StringComparer.OrdinalIgnoreCase);

        var stagedFiles = new List<object>();
        foreach (var payload in payloads)
        {
            var target = ResolveContainedPath(assetRoot, payload.RelativePath,
                "DHE staged payload");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(payload.SourcePath, target, true);
            var targetHash = Sha256File(target);
            if (!targetHash.Equals(payload.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new DheException("Staged DHE payload hash mismatch: " + payload.RelativePath);
            stagedFiles.Add(new { path = payload.RelativePath, sha256 = targetHash });
        }

        var planFileName = Path.GetFileName(runtimePlanRelative);
        var requestedPlanFileName = cli.Optional("planfilename");
        if (!string.IsNullOrWhiteSpace(requestedPlanFileName) &&
            !string.Equals(requestedPlanFileName, planFileName, StringComparison.Ordinal))
            throw new DheException(
                "PlanFileName cannot differ from the immutable resource manifest runtimePlan path.");
        if (!IsPortableRelativePath(planFileName) || planFileName.Contains('/'))
            throw new DheException("PlanFileName must be a simple portable file name.");
        var stagedPlanPath = ResolveContainedPath(assetRoot, planFileName, "Staged runtime plan");
        File.Copy(runtimePlanSource, stagedPlanPath, true);
        if (!string.Equals(Sha256File(stagedPlanPath), runtimePlanSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("Staged DHE runtime plan hash mismatch.");
        var manifestFileName = cli.Optional("manifestfilename") ?? "dhe-resource-update.json";
        if (!IsPortableRelativePath(manifestFileName) || manifestFileName.Contains('/'))
            throw new DheException("ManifestFileName must be a simple portable file name.");
        var stagedManifestPath = ResolveContainedPath(assetRoot, manifestFileName,
            "Staged resource update manifest");
        File.Copy(manifestPath, stagedManifestPath, true);
        var validationFileName = Path.GetFileName(validationSource);
        var stagedValidationPath = ResolveContainedPath(assetRoot, validationFileName,
            "Staged resource compatibility validation");
        File.Copy(validationSource, stagedValidationPath, true);
        if (!string.Equals(Sha256File(stagedValidationPath),
                GetString(manifest, "validationSha256"), StringComparison.OrdinalIgnoreCase))
            throw new DheException("Staged DHE resource compatibility validation hash mismatch.");

        var baseTreeAfter = TreeHashForRelease(embeddedBaseRoot, Array.Empty<string>());
        if (!baseTreeAfter.Equals(baseTreeBefore, StringComparison.OrdinalIgnoreCase))
            throw new DheException("Resource staging modified the embedded Base MetaVersion tree.");
        var immutableRecords = immutableBefore.Select(pair =>
        {
            var after = Sha256File(pair.Key);
            if (!after.Equals(pair.Value, StringComparison.OrdinalIgnoreCase))
                throw new DheException("Resource staging modified immutable Player file: " + pair.Key);
            return new { path = pair.Key, sha256Before = pair.Value, sha256After = after };
        }).ToArray();

        var output = SafeReportPath(cli.Require("output"), new[] { manifestPath, runtimePlanSource });
        WriteJson(output, new
        {
            schemaVersion = 1,
            format = "hybridclr.dhe-resource-stage.json",
            generatedAtUtc = DateTimeOffset.UtcNow,
            passed = true,
            updateRoot,
            assetRoot,
            payloadModel = "single-current-payload",
            currentAssemblySetSha256 = GetString(manifest, "currentAssemblySetSha256"),
            stagedPlanPath,
            stagedPlanSha256 = runtimePlanSha256,
            stagedManifestPath,
            stagedValidationPath,
            embeddedBaseRoot,
            selectedBaseId = embeddedBase.BaseId,
            baseBuildIdentityPath,
            baseBuildIdentitySha256 = Sha256File(baseBuildIdentityPath),
            baseMetaVersionSetSha256 = embeddedBase.SetSha256,
            baseMetaVersionTreeSha256Before = baseTreeBefore,
            baseMetaVersionTreeSha256After = baseTreeAfter,
            baseMetaVersionUnchanged = true,
            stagedFiles = stagedFiles.ToArray(),
            immutableFiles = immutableRecords,
        });
        Console.WriteLine("DHE resource update staged without modifying the Base: " + output);
        return 0;
    }

    private static JsonElement ValidateStagingBuildIdentity(string identityPath,
        JsonElement identity, JsonElement manifest)
    {
        if (GetInt(identity, "schemaVersion") != 1 ||
            !string.Equals(GetString(identity, "format"),
                "hybridclr.dhe-build-identity.json", StringComparison.Ordinal) ||
            GetInt(identity, "identityVersion") != 1 ||
            !string.Equals(GetString(identity, "state"), "staged-for-final-player",
                StringComparison.Ordinal) ||
            !string.Equals(GetString(identity, "aotSnapshotKind"),
                "managed-assembly-plus-generated-cpp-v1", StringComparison.Ordinal))
            throw new DheException("Base Player build identity contract is invalid.");

        string baseId = GetString(identity, "baseId") ?? string.Empty;
        string target = GetString(identity, "target") ?? string.Empty;
        string managedSet = GetString(identity, "managedAssemblySetSha256") ?? string.Empty;
        string snapshot = GetString(identity, "aotSnapshotSha256") ?? string.Empty;
        string baseMetaVersionSet = GetString(identity, "baseMetaVersionSetSha256") ?? string.Empty;
        string guard = GetString(identity, "nativeGuardSourceSha256") ?? string.Empty;
        string nativeManifest = GetString(identity, "nativeManifestSha256") ?? string.Empty;
        string runtimeProtocol = GetString(identity, "runtimeProtocol") ?? string.Empty;
        string runtimeContract = GetString(identity, "runtimeContract") ?? string.Empty;
        string runtimeAssetRoot = RequirePortableAssetRoot(
            GetString(identity, "runtimeAssetRoot"), "identity runtimeAssetRoot");
        string baseMetaVersionAssetRoot = RequirePortableAssetRoot(
            GetString(identity, "baseMetaVersionAssetRoot"),
            "identity baseMetaVersionAssetRoot");
        string[] runtimeCapabilities = ReadRuntimeCapabilities(identity,
            "runtimeCapabilities");
        if (target.Length == 0 || target.Any(character => !(char.IsLetterOrDigit(character) ||
                character is '.' or '_' or '-')) ||
            !IsHex(baseId, 64, 64) || !IsHex(managedSet, 64, 64) ||
            !IsHex(snapshot, 64, 64) || !IsHex(baseMetaVersionSet, 64, 64) ||
            !IsHex(guard, 64, 64) || !IsHex(nativeManifest, 64, 64) ||
            !string.Equals(runtimeProtocol, ResourceUpdateCompatibility.RuntimeProtocol,
                StringComparison.Ordinal) || string.IsNullOrWhiteSpace(runtimeContract) ||
            !IsValidCapabilitySet(runtimeCapabilities))
            throw new DheException("Base Player build identity fields are invalid.");

        string computedBaseId = ComputeBaseId(target, managedSet, snapshot,
            baseMetaVersionSet, guard, nativeManifest, runtimeProtocol, runtimeContract,
            runtimeCapabilities, runtimeAssetRoot, baseMetaVersionAssetRoot);
        if (!string.Equals(baseId, computedBaseId, StringComparison.OrdinalIgnoreCase))
            throw new DheException("Base Player build identity composite baseId is invalid.");

        if (!manifest.TryGetProperty("supportedBases", out JsonElement supportedBases) ||
            supportedBases.ValueKind != JsonValueKind.Array)
            throw new DheException("Resource update supported Base records are missing.");
        JsonElement[] matches = supportedBases.EnumerateArray().Where(candidate =>
            string.Equals(GetString(candidate, "baseId"), baseId,
                StringComparison.OrdinalIgnoreCase)).ToArray();
        if (matches.Length != 1)
            throw new DheException(
                "Base Player build identity does not uniquely match a supported Base ID.");

        JsonElement selected = matches[0];
        string identitySha256 = Sha256File(identityPath);
        string[] selectedCapabilities = ReadRuntimeCapabilities(selected,
            "runtimeCapabilities");
        if (!string.Equals(GetString(selected, "buildIdentitySha256"), identitySha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selected, "target"), target,
                StringComparison.Ordinal) ||
            !string.Equals(GetString(selected, "managedAssemblySetSha256"), managedSet,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selected, "aotSnapshotSha256"), snapshot,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selected, "baseMetaVersionSetSha256"),
                baseMetaVersionSet, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selected, "nativeGuardSourceSha256"), guard,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selected, "nativeManifestSha256"), nativeManifest,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selected, "runtimeProtocol"), runtimeProtocol,
                StringComparison.Ordinal) ||
            !string.Equals(GetString(selected, "nativeRuntimeContract"), runtimeContract,
                StringComparison.Ordinal) ||
            !new HashSet<string>(selectedCapabilities, StringComparer.Ordinal)
                .SetEquals(runtimeCapabilities) ||
            !string.Equals(RequirePortableAssetRoot(GetString(selected, "runtimeAssetRoot"),
                    "supported Base runtimeAssetRoot"), runtimeAssetRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RequirePortableAssetRoot(
                    GetString(selected, "baseMetaVersionAssetRoot"),
                    "supported Base baseMetaVersionAssetRoot"), baseMetaVersionAssetRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RequirePortableAssetRoot(GetString(manifest, "runtimeAssetRoot"),
                    "manifest runtimeAssetRoot"), runtimeAssetRoot,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(RequirePortableAssetRoot(
                    GetString(manifest, "baseMetaVersionAssetRoot"),
                    "manifest baseMetaVersionAssetRoot"), baseMetaVersionAssetRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException(
                "Base Player build identity does not match its supported Base record.");
        return selected;
    }

    private static (string BaseId, string SetSha256) ValidateEmbeddedBaseMetaVersionSet(
        string embeddedBaseRoot, JsonElement manifest, JsonElement identity,
        JsonElement selectedBase)
    {
        if (!manifest.TryGetProperty("assemblies", out JsonElement assemblies) ||
            assemblies.ValueKind != JsonValueKind.Array || assemblies.GetArrayLength() == 0)
            throw new DheException("Resource update assembly records are missing.");
        string[] names = assemblies.EnumerateArray().Select(assembly =>
                NormalizeName(GetString(assembly, "assemblyName") ?? string.Empty))
            .ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw new DheException("Resource update assembly records are duplicated.");
        if (!identity.TryGetProperty("assemblies", out JsonElement identityAssemblies) ||
            identityAssemblies.ValueKind != JsonValueKind.Array ||
            !selectedBase.TryGetProperty("assemblies", out JsonElement selectedAssemblies) ||
            selectedAssemblies.ValueKind != JsonValueKind.Array)
            throw new DheException("Base Player assembly identity records are missing.");
        var identityByName = identityAssemblies.EnumerateArray().ToDictionary(record =>
                NormalizeName(GetString(record, "assemblyName") ?? string.Empty),
            record => record, StringComparer.OrdinalIgnoreCase);
        var selectedByName = selectedAssemblies.EnumerateArray().ToDictionary(record =>
                NormalizeName(GetString(record, "assemblyName") ?? string.Empty),
            record => record, StringComparer.OrdinalIgnoreCase);
        if (identityByName.Count != names.Length || selectedByName.Count != names.Length ||
            !new HashSet<string>(names, StringComparer.OrdinalIgnoreCase)
                .SetEquals(identityByName.Keys) ||
            !new HashSet<string>(names, StringComparer.OrdinalIgnoreCase)
                .SetEquals(selectedByName.Keys))
            throw new DheException(
                "Base Player assembly identity does not match the resource update set.");
        if (Directory.GetFiles(embeddedBaseRoot, "*.mv2.bytes",
                SearchOption.TopDirectoryOnly).Length != 0)
            throw new DheException(
                "Embedded Base MetaVersion root contains retired .mv2.bytes artifacts.");

        string[] actualFiles = Directory.GetFiles(embeddedBaseRoot, "*.mv.bytes",
            SearchOption.TopDirectoryOnly);
        string[] actualNames = actualFiles.Select(path =>
                Path.GetFileName(path)[..^".mv.bytes".Length])
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!new HashSet<string>(names, StringComparer.OrdinalIgnoreCase).SetEquals(actualNames) ||
            actualNames.Length != names.Length)
            throw new DheException(
                "Embedded Base MetaVersion assembly set does not match the resource update.");

        var records = new List<(string name, byte[] bytes)>();
        foreach (string name in names)
        {
            string path = RequireFile(Path.Combine(embeddedBaseRoot, name + ".mv.bytes"),
                name + " embedded Base MetaVersion");
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < 60 ||
                !Encoding.ASCII.GetString(bytes, 0, 8).Equals(MetaVersionSnapshot.Magic,
                    StringComparison.Ordinal) ||
                BitConverter.ToUInt32(bytes, 8) != MetaVersionSnapshot.SchemaVersion ||
                BitConverter.ToUInt32(bytes, 12) != MetaVersionSnapshot.StrictFlag)
                throw new DheException("Embedded Base MetaVersion header is invalid: " + name);
            int nameLength = checked((int)BitConverter.ToUInt32(bytes, 16));
            if (nameLength <= 0 || nameLength > bytes.Length - 60 ||
                !Encoding.UTF8.GetString(bytes, 60, nameLength).Equals(name,
                    StringComparison.Ordinal))
                throw new DheException(
                    "Embedded Base MetaVersion assembly identity is invalid: " + name);
            string sha256 = Sha256Bytes(bytes);
            JsonElement identityAssembly = identityByName[name];
            JsonElement selectedAssembly = selectedByName[name];
            if (!string.Equals(GetString(identityAssembly, "baseMetaVersionSha256"), sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(identityAssembly, "embeddedBaseMetaVersionSha256"),
                    sha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(selectedAssembly, "baseMetaVersionSha256"), sha256,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(identityAssembly, "baselineSha256"),
                    GetString(selectedAssembly, "baselineAssemblySha256"),
                    StringComparison.OrdinalIgnoreCase))
                throw new DheException(
                    "Embedded Base MetaVersion is not bound to the Player identity: " + name);
            records.Add((name, bytes));
        }

        string setSha256 = NamedByteSetHash(records);
        if (!string.Equals(GetString(identity, "baseMetaVersionSetSha256"), setSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selectedBase, "baseMetaVersionSetSha256"), setSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException(
                "Embedded Base MetaVersion set does not match the selected Player identity.");
        string baseId = GetString(identity, "baseId") ?? string.Empty;
        return (baseId, setSha256);
    }

    private static ResourcePayload[] ValidateResourceUpdatePayload(string updateRoot, JsonElement manifest,
        JsonElement runtimePlan, string runtimeAssetRoot, string baseMetaVersionAssetRoot)
    {
        if (!manifest.TryGetProperty("assemblies", out var assemblies) ||
            assemblies.ValueKind != JsonValueKind.Array || assemblies.GetArrayLength() == 0 ||
            !runtimePlan.TryGetProperty("assemblies", out var planAssemblies) ||
            planAssemblies.ValueKind != JsonValueKind.Array ||
            planAssemblies.GetArrayLength() != assemblies.GetArrayLength())
            throw new DheException("Resource update assembly records are missing or inconsistent.");

        var planByName = planAssemblies.EnumerateArray().ToDictionary(
            item => NormalizeName(GetString(item, "assemblyName") ?? string.Empty),
            item => item, StringComparer.OrdinalIgnoreCase);
        var payloads = new List<ResourcePayload>();
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var assembly in assemblies.EnumerateArray())
        {
            var name = NormalizeName(GetString(assembly, "assemblyName") ?? string.Empty);
            if (!planByName.TryGetValue(name, out var plan))
                throw new DheException("Runtime plan has no record for payload assembly: " + name);
            AddResourcePayload(updateRoot, GetString(assembly, "dll"), GetString(assembly, "dllSha256"),
                GetString(plan, "current"), runtimeAssetRoot, payloads, paths);
            AddResourcePayload(updateRoot, GetString(assembly, "currentMetaVersion"),
                GetString(assembly, "currentMetaVersionSha256"), GetString(plan, "currentMetaVersion"),
                runtimeAssetRoot, payloads, paths);
            var expectedBasePath = baseMetaVersionAssetRoot + name + ".mv.bytes";
            if (!string.Equals(GetString(plan, "baseMetaVersion"), expectedBasePath,
                    StringComparison.OrdinalIgnoreCase))
                throw new DheException("Runtime plan Base MetaVersion path is invalid for " + name + ".");
        }

        if (!manifest.TryGetProperty("aotMetadata", out JsonElement manifestAotMetadata) ||
            manifestAotMetadata.ValueKind != JsonValueKind.Array ||
            !runtimePlan.TryGetProperty("aotMetadata", out JsonElement planAotMetadata) ||
            planAotMetadata.ValueKind != JsonValueKind.Array ||
            manifestAotMetadata.GetArrayLength() != planAotMetadata.GetArrayLength())
            throw new DheException(
                "Resource update AOT metadata records are missing or inconsistent.");
        var planAotByName = planAotMetadata.EnumerateArray().ToDictionary(item =>
                NormalizeName(GetString(item, "assemblyName") ?? string.Empty),
            item => item, StringComparer.OrdinalIgnoreCase);
        if (planAotByName.Count != planAotMetadata.GetArrayLength())
            throw new DheException("Runtime plan AOT metadata records are duplicated.");
        var manifestAotNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement metadata in manifestAotMetadata.EnumerateArray())
        {
            string name = NormalizeName(GetString(metadata, "assemblyName") ?? string.Empty);
            if (name.Length == 0 || !manifestAotNames.Add(name) ||
                !planAotByName.TryGetValue(name, out JsonElement planMetadata))
                throw new DheException("Resource update AOT metadata record is invalid: " + name);
            string assetPath = GetString(metadata, "path") ?? string.Empty;
            if (!assetPath.StartsWith(runtimeAssetRoot + "payload/",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(planMetadata, "path"), assetPath,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(planMetadata, "sha256"),
                    GetString(metadata, "sha256"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(planMetadata, "sourceKind"),
                    GetString(metadata, "sourceKind"), StringComparison.Ordinal) ||
                !string.Equals(GetString(planMetadata, "manifestSha256"),
                    GetString(metadata, "manifestSha256"), StringComparison.OrdinalIgnoreCase))
                throw new DheException(
                    "Resource update AOT metadata is not bound to the runtime plan: " + name);
            string relativePath = assetPath[runtimeAssetRoot.Length..];
            AddResourcePayload(updateRoot, relativePath, GetString(metadata, "sha256"),
                GetString(planMetadata, "path"), runtimeAssetRoot, payloads, paths);
        }
        return payloads.ToArray();
    }

    private static string ValidateResourceUpdateCompatibility(string updateRoot, JsonElement manifest)
    {
        var validationRelative = GetString(manifest, "validation") ?? string.Empty;
        var expectedValidationHash = GetString(manifest, "validationSha256");
        var validationPath = RequireFile(ResolveContainedPath(updateRoot, validationRelative,
            "DHE resource compatibility validation"), "DHE resource compatibility validation");
        if (!IsHex(expectedValidationHash, 64, 64) ||
            !string.Equals(Sha256File(validationPath), expectedValidationHash,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE resource compatibility validation hash mismatch.");
        var validation = ReadJson<JsonElement>(validationPath);
        if (GetInt(validation, "schemaVersion") != 1 ||
            !string.Equals(GetString(validation, "format"),
                "hybridclr.dhe-resource-update-validation.json", StringComparison.Ordinal) ||
            !GetBool(validation, "passed") ||
            !string.Equals(GetString(validation, "compatibilityPolicy"),
                ResourceUpdateCompatibility.Policy, StringComparison.Ordinal) ||
            !string.Equals(GetString(validation, "runtimeProtocol"),
                ResourceUpdateCompatibility.RuntimeProtocol, StringComparison.Ordinal) ||
            !string.Equals(GetString(validation, "currentAssemblySetSha256"),
                GetString(manifest, "currentAssemblySetSha256"), StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE resource compatibility validation did not pass.");

        if (!manifest.TryGetProperty("supportedBases", out JsonElement supportedBases) ||
            supportedBases.ValueKind != JsonValueKind.Array || supportedBases.GetArrayLength() == 0 ||
            !validation.TryGetProperty("bases", out JsonElement validatedBases) ||
            validatedBases.ValueKind != JsonValueKind.Array ||
            validatedBases.GetArrayLength() != supportedBases.GetArrayLength())
            throw new DheException("DHE resource compatibility Base records are missing or inconsistent.");

        var validatedById = validatedBases.EnumerateArray().ToDictionary(
            ResourceBaseIdentityKey, item => item,
            StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement supportedBase in supportedBases.EnumerateArray())
        {
            string baseId = GetString(supportedBase, "baseId") ?? string.Empty;
            string identityKey = ResourceBaseIdentityKey(supportedBase);
            string[] runtimeCapabilities = ReadRuntimeCapabilities(supportedBase,
                "runtimeCapabilities");
            string[] requiredRuntimeCapabilities = ReadRuntimeCapabilities(supportedBase,
                "requiredRuntimeCapabilities");
            if (!IsHex(baseId, 64, 64) || !GetBool(supportedBase, "compatible") ||
                !GetBool(supportedBase, "guardCoverageValidated") ||
                GetInt(supportedBase, "unsupportedChangeCount") != 0 ||
                !string.Equals(GetString(supportedBase, "runtimeProtocol"),
                    ResourceUpdateCompatibility.RuntimeProtocol, StringComparison.Ordinal) ||
                string.IsNullOrWhiteSpace(GetString(supportedBase, "nativeRuntimeContract")) ||
                !IsValidCapabilitySet(runtimeCapabilities) ||
                !IsValidCapabilitySet(requiredRuntimeCapabilities) ||
                !new HashSet<string>(runtimeCapabilities, StringComparer.Ordinal)
                    .IsSupersetOf(requiredRuntimeCapabilities) ||
                !IsHex(GetString(supportedBase, "buildIdentitySha256"), 64, 64) ||
                !validatedById.TryGetValue(identityKey, out JsonElement validatedBase) ||
                !GetBool(validatedBase, "compatible") ||
                !GetBool(validatedBase, "guardCoverageValidated") ||
                GetInt(validatedBase, "unsupportedChangeCount") != 0 ||
                !string.Equals(GetString(validatedBase, "nativeRuntimeContract"),
                    GetString(supportedBase, "nativeRuntimeContract"), StringComparison.Ordinal) ||
                !new HashSet<string>(ReadRuntimeCapabilities(validatedBase,
                        "runtimeCapabilities"), StringComparer.Ordinal)
                    .SetEquals(runtimeCapabilities) ||
                !new HashSet<string>(ReadRuntimeCapabilities(validatedBase,
                        "requiredRuntimeCapabilities"), StringComparer.Ordinal)
                    .SetEquals(requiredRuntimeCapabilities))
                throw new DheException("DHE resource update contains an unvalidated Base: " + baseId);
        }
        return validationPath;
    }

    private static string[] ReadRuntimeCapabilities(JsonElement value, string property)
    {
        return value.TryGetProperty(property, out JsonElement capabilities) &&
               capabilities.ValueKind == JsonValueKind.Array
            ? capabilities.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString() ?? string.Empty).ToArray()
            : Array.Empty<string>();
    }

    private static bool IsValidCapabilitySet(string[] values) =>
        values.Length != 0 && !values.Any(string.IsNullOrWhiteSpace) &&
        values.Distinct(StringComparer.Ordinal).Count() == values.Length;

    private static string ResourceBaseIdentityKey(JsonElement value)
    {
        var target = GetString(value, "target") ?? string.Empty;
        var baseId = GetString(value, "baseId") ?? string.Empty;
        var managed = GetString(value, "managedAssemblySetSha256") ?? string.Empty;
        var snapshot = GetString(value, "aotSnapshotSha256") ?? string.Empty;
        var baseMetaVersion = GetString(value, "baseMetaVersionSetSha256") ?? string.Empty;
        var guard = GetString(value, "nativeGuardSourceSha256") ?? string.Empty;
        var nativeManifest = GetString(value, "nativeManifestSha256") ?? string.Empty;
        if (target.Length == 0 || !IsHex(baseId, 64, 64) || !IsHex(managed, 64, 64) ||
            !IsHex(snapshot, 64, 64) ||
            !IsHex(baseMetaVersion, 64, 64) || !IsHex(guard, 64, 64) ||
            !IsHex(nativeManifest, 64, 64))
            throw new DheException("DHE resource update contains an incomplete Player Base identity.");
        return string.Join("|", target, baseId, managed, snapshot, baseMetaVersion, guard,
            nativeManifest);
    }

    private static void AddResourcePayload(string updateRoot, string? relativePath, string? expectedHash,
        string? planAssetPath, string runtimeAssetRoot, List<ResourcePayload> payloads,
        HashSet<string> paths)
    {
        var relative = relativePath ?? string.Empty;
        if (!relative.StartsWith("payload/", StringComparison.OrdinalIgnoreCase) ||
            !paths.Add(relative) || !IsHex(expectedHash, 64, 64) ||
            !string.Equals(planAssetPath, runtimeAssetRoot + relative, StringComparison.OrdinalIgnoreCase))
            throw new DheException("Resource payload path/hash binding is invalid: " + relative);
        var source = RequireFile(ResolveContainedPath(updateRoot, relative, "DHE resource payload"),
            "DHE resource payload");
        if (!Sha256File(source).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE resource payload hash mismatch: " + relative);
        payloads.Add(new ResourcePayload(relative, source, expectedHash!.ToLowerInvariant()));
    }

    private static string RequirePortableAssetRoot(string? value, string description)
    {
        var normalized = (value ?? string.Empty).Replace('\\', '/');
        if (!normalized.EndsWith("/", StringComparison.Ordinal) ||
            !IsPortableRelativePath(normalized.TrimEnd('/')))
            throw new DheException(description + " must be a portable relative directory path.");
        return normalized;
    }

    private sealed record ResourcePayload(string RelativePath, string SourcePath, string Sha256);
}
