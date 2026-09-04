using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

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
            !new[] { "single-current-payload", "variant-current-payload" }.Contains(
                GetString(manifest, "payloadModel"), StringComparer.Ordinal) ||
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
        string selectedVariantId = GetString(selectedBase, "payloadVariantId") ?? "default";
        JsonElement manifestVariant = SelectPayloadVariant(manifest, selectedVariantId,
            "Resource update manifest");
        string selectedCurrentSetHash = GetString(manifestVariant, "currentAssemblySetSha256") ??
            GetString(manifest, "currentAssemblySetSha256") ?? string.Empty;

        var runtimePlanRelative = GetString(manifest, "runtimePlan") ?? string.Empty;
        var runtimePlanSource = RequireFile(ResolveContainedPath(updateRoot, runtimePlanRelative,
            "DHE runtime plan"), "DHE runtime plan");
        string runtimePlanSha256 = GetString(manifest, "runtimePlanSha256") ?? string.Empty;
        if (!IsHex(runtimePlanSha256, 64, 64) ||
            !string.Equals(Sha256File(runtimePlanSource), runtimePlanSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE runtime plan hash does not match the resource manifest.");
        var runtimePlan = ReadJson<JsonElement>(runtimePlanSource);
        ValidatePayloadVariantSetHash(runtimePlan, "DHE runtime plan");
        JsonElement runtimePlanVariant = SelectPayloadVariant(runtimePlan, selectedVariantId,
            "DHE runtime plan");
        if (GetInt(runtimePlan, "schemaVersion") != 1 ||
            !string.Equals(GetString(runtimePlan, "format"),
                "hybridclr.dhe-runtime-asset-plan.json", StringComparison.Ordinal) ||
            !string.Equals(GetString(runtimePlan, "selection"),
                "embedded-base-metaversion-and-aot-metadata-set", StringComparison.Ordinal) ||
            !string.Equals(GetString(runtimePlanVariant, "currentAssemblySetSha256"),
                selectedCurrentSetHash, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(runtimePlan, "payloadVariantSetSha256"),
                GetString(manifest, "payloadVariantSetSha256"), StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(GetString(selectedBase, "currentAssemblySetSha256")) &&
             !string.Equals(GetString(selectedBase, "currentAssemblySetSha256"),
                 selectedCurrentSetHash, StringComparison.OrdinalIgnoreCase)) ||
            !string.Equals(RequirePortableAssetRoot(GetString(runtimePlan, "baseMetaVersionAssetRoot"),
                    "runtime plan baseMetaVersionAssetRoot"), baseMetaVersionAssetRoot,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE runtime plan is not bound to the resource update manifest.");

        var payloads = ValidateResourceUpdatePayload(updateRoot, manifest, runtimePlan,
            selectedBase, runtimeAssetRoot, baseMetaVersionAssetRoot);
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
            payloadModel = GetString(manifest, "payloadModel") ?? "single-current-payload",
            payloadVariantId = selectedVariantId,
            payloadVariantSetSha256 = GetString(manifest, "payloadVariantSetSha256"),
            currentAssemblySetSha256 = selectedCurrentSetHash,
            stagedPlanPath,
            stagedPlanSha256 = runtimePlanSha256,
            stagedManifestPath,
            stagedValidationPath,
            embeddedBaseRoot,
            selectedBaseId = embeddedBase.BaseId,
            selectedAotMetadataSetId = GetString(selectedBase, "aotMetadataSetId"),
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

    /// <summary>
    /// Binds a resource-only update and its real Player smoke back to the
    /// immutable Base workflow. This replaces the obsolete changed-Player
    /// rebuild as the changed lane consumed by toolchain release evidence.
    /// </summary>
    private static int ResourcePlayerEvidence(Cli cli)
    {
        string updateRoot = RequireDirectory(cli.Require("resourceupdateroot"),
            "DHE resource update root");
        string manifestPath = RequireFile(Path.Combine(updateRoot, "dhe-resource-update.json"),
            "DHE resource update manifest");
        JsonElement manifest = ReadJson<JsonElement>(manifestPath);
        RequireEvidenceFormat(manifest, "hybridclr.dhe-resource-update.json",
            "Resource update manifest");
        string validationPath = ValidateResourceUpdateCompatibility(updateRoot, manifest);
        JsonElement validation = ReadJson<JsonElement>(validationPath);
        string runtimePlanPath = RequireFile(ResolveContainedPath(updateRoot,
            GetString(manifest, "runtimePlan") ?? string.Empty, "DHE resource runtime plan"),
            "DHE resource runtime plan");

        string stagePath = RequireFile(cli.Require("stagereport"), "DHE resource stage report");
        JsonElement stage = ReadJson<JsonElement>(stagePath);
        RequireEvidenceFormat(stage, "hybridclr.dhe-resource-stage.json", "Resource stage");
        string playerPath = RequireFile(cli.Require("playerresult"), "DHE resource Player result");
        JsonElement player = ReadJson<JsonElement>(playerPath);
        RequireEvidenceFormat(player, "hybridclr.dhe-player-result.json", "Resource Player");
        string baseWorkflowPath = RequireFile(cli.Require("baseworkflowreport"),
            "DHE Base workflow report");
        JsonElement baseWorkflow = ReadJson<JsonElement>(baseWorkflowPath);
        RequireEvidenceFormat(baseWorkflow, "hybridclr.dhe-project-player-workflow.json",
            "Base workflow");

        var errors = new List<string>();
        if (!GetBool(validation, "passed") || !GetBool(stage, "passed") ||
            !GetBool(baseWorkflow, "passed") || !GetBool(player, "passed"))
            errors.Add("Resource update, stage, Base workflow, and Player must all pass.");
        try { ValidateNoOpPlayerEvidence(baseWorkflow.GetProperty("player")); }
        catch (Exception ex) { errors.Add("Base workflow is not a complete no-op proof: " + ex.Message); }

        string selectedBaseId = GetString(stage, "selectedBaseId") ?? string.Empty;
        string selectedAotMetadataSetId = GetString(stage,
            "selectedAotMetadataSetId") ?? string.Empty;
        string selectedVariantId = GetString(stage, "payloadVariantId") ?? "default";
        JsonElement selectedBase = manifest.GetProperty("supportedBases").EnumerateArray()
            .SingleOrDefault(item => string.Equals(GetString(item, "baseId"), selectedBaseId,
                StringComparison.OrdinalIgnoreCase));
        if (selectedBase.ValueKind == JsonValueKind.Undefined || !GetBool(selectedBase, "compatible"))
            errors.Add("Resource stage did not select one compatible Base record.");

        JsonElement selectedManifestVariant = SelectPayloadVariant(manifest, selectedVariantId,
            "Resource update manifest");
        JsonElement selectedValidationVariant = SelectPayloadVariant(validation, selectedVariantId,
            "Resource update validation");
        string currentSet = GetString(selectedManifestVariant, "currentAssemblySetSha256") ??
            GetString(manifest, "currentAssemblySetSha256") ?? string.Empty;
        string target = GetString(player, "target") ?? string.Empty;
        if (!string.Equals(Path.GetFullPath(GetString(stage, "updateRoot") ?? string.Empty),
                Path.GetFullPath(updateRoot), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(stage, "currentAssemblySetSha256"), currentSet,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selectedValidationVariant, "currentAssemblySetSha256") ??
                GetString(validation, "currentAssemblySetSha256"), currentSet,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selectedBase, "target"), target, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selectedBase, "aotMetadataSetId"),
                selectedAotMetadataSetId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selectedBase, "payloadVariantId") ?? "default",
                selectedVariantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(selectedBase, "currentAssemblySetSha256"), currentSet,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(player, "selectedBaseId"), selectedBaseId,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(player, "selectedPayloadVariantId") ?? "default",
                selectedVariantId, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(GetString(player, "selectedPayloadCurrentAssemblySetSha256"),
                currentSet, StringComparison.OrdinalIgnoreCase))
            errors.Add("Resource, stage, Base, and Player selection identities do not agree.");

        string stagedManifest = RequireFile(GetString(stage, "stagedManifestPath") ?? string.Empty,
            "Staged resource manifest");
        string stagedValidation = RequireFile(GetString(stage, "stagedValidationPath") ?? string.Empty,
            "Staged resource validation");
        string stagedPlan = RequireFile(GetString(stage, "stagedPlanPath") ?? string.Empty,
            "Staged runtime plan");
        if (!Sha256File(stagedManifest).Equals(Sha256File(manifestPath),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(stagedValidation).Equals(Sha256File(validationPath),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(stagedPlan).Equals(Sha256File(runtimePlanPath),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(stagedPlan).Equals(GetString(stage, "stagedPlanSha256"),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(runtimePlanPath).Equals(GetString(manifest, "runtimePlanSha256"),
                StringComparison.OrdinalIgnoreCase))
            errors.Add("Staged resource manifest, validation, or runtime plan bytes drifted.");

        string buildIdentityPath = RequireFile(GetString(stage, "baseBuildIdentityPath") ?? string.Empty,
            "Staged Base build identity");
        string workflowIdentityPath = ResolveEvidencePath(GetString(baseWorkflow, "buildIdentity"),
            Path.GetDirectoryName(baseWorkflowPath)!, "Base workflow build identity");
        string nativeManifestPath = ResolveEvidencePath(GetString(baseWorkflow, "nativeManifest"),
            Path.GetDirectoryName(baseWorkflowPath)!, "Base workflow native manifest");
        JsonElement nativeManifest = ReadJson<JsonElement>(nativeManifestPath);
        if (!Sha256File(buildIdentityPath).Equals(GetString(stage, "baseBuildIdentitySha256"),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(buildIdentityPath).Equals(Sha256File(workflowIdentityPath),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(buildIdentityPath).Equals(GetString(selectedBase, "buildIdentitySha256"),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(nativeManifestPath).Equals(GetString(selectedBase, "nativeManifestSha256"),
                StringComparison.OrdinalIgnoreCase) ||
            !Sha256File(nativeManifestPath).Equals(GetString(player, "nativeManifestSha256"),
                StringComparison.OrdinalIgnoreCase))
            errors.Add("Base build identity or native manifest is not bound to the selected resource record.");

        string[] assemblyNames = selectedManifestVariant.GetProperty("assemblies").EnumerateArray()
            .Select(item => GetString(item, "assemblyName") ?? string.Empty)
            .Where(name => name.Length > 0).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] plannedNames = player.GetProperty("plannedDheAssemblies").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        string[] loadedNames = player.GetProperty("loadedDheAssemblies").EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!assemblyNames.SequenceEqual(plannedNames, StringComparer.OrdinalIgnoreCase) ||
            !assemblyNames.SequenceEqual(loadedNames, StringComparer.OrdinalIgnoreCase))
            errors.Add("Resource manifest and Player assembly scopes do not agree.");
        ValidatePlayerAssemblies(player, assemblyNames, errors);

        int expectedChanged = selectedBase.ValueKind == JsonValueKind.Undefined ? 0 :
            selectedBase.GetProperty("assemblies").EnumerateArray().Sum(item =>
                GetInt(item, "guardRequiredMethodCount") + GetInt(item, "addedMethodCount"));
        if (expectedChanged <= 0 || GetInt(player, "changedMethodCount") != expectedChanged ||
            GetInt(player, "expectedChangedMethodCount") != expectedChanged ||
            GetInt(player, "interpreterEntryCount") <= 0 || GetInt(player, "aotEntryCount") <= 0 ||
            !GetBool(player, "resourceUpdateManifestPresent") ||
            !GetBool(player, "resourceUpdateValidated") || !GetBool(player, "dispatchProbeValidated") ||
            !GetBool(player, "changedProbeChanged") ||
            !GetBool(player, "multiAssemblyValidated") || !GetBool(player, "capabilityPassed") ||
            !GetBool(player, "secondaryAssemblyChangedValidated") ||
            !GetBool(player, "structuralPassed") || !GetBool(player, "retryValidated") ||
            GetString(player, "transactionStatus") != "validated" ||
            GetString(player, "retryFailure") != "DHE_MV_REGISTRATION_FAILED")
            errors.Add("Resource Player did not prove changed interpreter/AOT dispatch, structure, and rollback.");

        if (!GetBool(stage, "baseMetaVersionUnchanged") ||
            stage.GetProperty("immutableFiles").EnumerateArray().Any(item =>
                !string.Equals(GetString(item, "sha256Before"), GetString(item, "sha256After"),
                    StringComparison.OrdinalIgnoreCase)))
            errors.Add("Resource stage did not preserve immutable Base files.");
        if (errors.Count > 0) throw new DheException(string.Join(" ", errors));

        int methodCount = selectedBase.GetProperty("assemblies").EnumerateArray().Sum(item =>
            GetInt(item, "unchangedMethodCount") + GetInt(item, "changedMethodCount") +
            GetInt(item, "addedMethodCount"));
        int typeChangeCount = selectedBase.GetProperty("assemblies").EnumerateArray().Sum(item =>
            GetInt(item, "changedExistingTypeCount") + GetInt(item, "addedTypeCount") +
            GetInt(item, "removedTypeCount"));
        int guardedMethodCount = selectedBase.GetProperty("assemblies").EnumerateArray()
            .Sum(item => GetInt(item, "guardCoveredMethodCount"));
        var output = SafeReportPath(cli.Require("output"), new[]
        {
            manifestPath, validationPath, runtimePlanPath, stagePath, playerPath, baseWorkflowPath,
            buildIdentityPath, nativeManifestPath,
        });
        WriteJson(output, new
        {
            schemaVersion = 1,
            format = "hybridclr.dhe-resource-player-workflow.json",
            generatedAtUtc = DateTimeOffset.UtcNow,
            passed = true,
            validationPassed = true,
            target,
            mode = GetString(baseWorkflow, "mode"),
            coverageRequired = true,
            coverageGatePassed = true,
            releaseReady = ResourcePlayerReleaseReady(baseWorkflow),
            artifactValidationPassed = true,
            buildIdentityReady = true,
            identityVersion = 1,
            aotSnapshotKind = GetString(player, "aotSnapshotKind"),
            nativeGuardSourceSha256 = GetString(player, "nativeGuardSourceSha256"),
            nativeManifestSha256 = GetString(player, "nativeManifestSha256"),
            pathSemantics = "workspace-absolute-v1",
            projectPlan = manifestPath,
            projectPlanValidation = validationPath,
            batchReport = validationPath,
            runtimePlan = runtimePlanPath,
            runtimePlanProjectPath = stagedPlan,
            sourcePreflight = GetString(baseWorkflow, "sourcePreflight"),
            cleanCheckoutGate = GetString(baseWorkflow, "cleanCheckoutGate"),
            toolchainGate = GetString(baseWorkflow, "toolchainGate"),
            expectedToolchainPackageId = GetString(baseWorkflow, "expectedToolchainPackageId"),
            transaction = new
            {
                status = GetString(player, "transactionStatus"),
                retryValidated = GetBool(player, "retryValidated"),
                retryAssemblyName = GetString(player, "retryAssemblyName"),
                retryFailure = GetString(player, "retryFailure"),
            },
            assemblyScope = new
            {
                strategy = "single-current-multibase-resource",
                aotAssemblies = assemblyNames,
                loadedDheAssemblies = loadedNames,
                stagedDependencies = Array.Empty<string>(),
                stagedDependenciesLoadedAsDhe = false,
                secondaryAssemblyChangedValidated = GetBool(player,
                    "secondaryAssemblyChangedValidated"),
                secondaryAssemblyDirectValidated = GetBool(player,
                    "secondaryAssemblyDirectValidated"),
            },
            capability = new
            {
                methodCount,
                changedMethodCount = expectedChanged,
                typeChangeCount,
                compatibility = "compatible",
            },
            nativeGuardCoverage = new
            {
                manifestAvailable = true,
                changedMethodCount = expectedChanged,
                supportedChangedMethodCount = expectedChanged,
                unsupportedChangedMethodCount = 0,
                nativeEntryCount = GetInt(nativeManifest, "nativeEntryCount"),
                guardedMethodCount,
                complete = true,
            },
            player,
            playerResult = playerPath,
            nativeManifest = nativeManifestPath,
            buildIdentity = buildIdentityPath,
            resourceEvidence = stagePath,
            resourceBuildPolicy = "required",
            resourceUpdateManifest = manifestPath,
            resourceUpdateManifestSha256 = Sha256File(manifestPath),
            resourceUpdateValidation = validationPath,
            resourceUpdateValidationSha256 = Sha256File(validationPath),
            resourceStage = stagePath,
            resourceStageSha256 = Sha256File(stagePath),
            baseWorkflowReport = baseWorkflowPath,
            baseWorkflowReportSha256 = Sha256File(baseWorkflowPath),
            playerResultSha256 = Sha256File(playerPath),
            buildIdentitySha256 = Sha256File(buildIdentityPath),
            runtimePlanSha256 = Sha256File(runtimePlanPath),
            selectedBaseId,
            selectedAotMetadataSetId,
            selectedPayloadVariantId = selectedVariantId,
            selectedPayloadCurrentAssemblySetSha256 = currentSet,
            payloadVariantSetSha256 = GetString(manifest, "payloadVariantSetSha256"),
            currentAssemblySetSha256 = currentSet,
            artifactValidation = validationPath,
            archiveManifest = (string?)null,
            archiveGate = (string?)null,
            runtimeSource = GetString(baseWorkflow, "runtimeSource"),
        });
        Console.WriteLine("DHE resource Player workflow evidence: " + output);
        return 0;
    }

    private static bool ResourcePlayerReleaseReady(JsonElement baseWorkflow) =>
        string.Equals(GetString(baseWorkflow, "mode"), "Release", StringComparison.Ordinal) &&
        GetBool(baseWorkflow, "releaseReady");

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
        string aotMetadataSetId = GetString(identity, "aotMetadataSetId") ?? string.Empty;
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
            !IsHex(aotMetadataSetId, 64, 64) ||
            !IsHex(guard, 64, 64) || !IsHex(nativeManifest, 64, 64) ||
            !string.Equals(runtimeProtocol, ResourceUpdateCompatibility.RuntimeProtocol,
                StringComparison.Ordinal) || string.IsNullOrWhiteSpace(runtimeContract) ||
            !IsValidCapabilitySet(runtimeCapabilities))
            throw new DheException("Base Player build identity fields are invalid.");

        string computedBaseId = ComputeBaseId(target, managedSet, snapshot,
            baseMetaVersionSet, aotMetadataSetId, guard, nativeManifest, runtimeProtocol, runtimeContract,
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
            !string.Equals(GetString(selected, "aotMetadataSetId"),
                aotMetadataSetId, StringComparison.OrdinalIgnoreCase) ||
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
        JsonElement runtimePlan, JsonElement selectedBase, string runtimeAssetRoot,
        string baseMetaVersionAssetRoot)
    {
        string variantId = GetString(selectedBase, "payloadVariantId") ?? "default";
        JsonElement manifestVariant = SelectPayloadVariant(manifest, variantId,
            "Resource update manifest");
        JsonElement planVariant = SelectPayloadVariant(runtimePlan, variantId,
            "DHE runtime plan");
        JsonElement assemblies = manifestVariant.TryGetProperty("assemblies", out var selectedAssemblies)
            ? selectedAssemblies
            : default;
        JsonElement planAssemblies = planVariant.TryGetProperty("assemblies", out var selectedPlanAssemblies)
            ? selectedPlanAssemblies
            : default;
        if (assemblies.ValueKind != JsonValueKind.Array || assemblies.GetArrayLength() == 0 ||
            planAssemblies.ValueKind != JsonValueKind.Array ||
            planAssemblies.GetArrayLength() != assemblies.GetArrayLength())
            throw new DheException("Resource update assembly records are missing or inconsistent.");

        string variantHash = GetString(manifestVariant, "currentAssemblySetSha256") ?? string.Empty;
        if (!IsHex(variantHash, 64, 64) ||
            !string.Equals(GetString(planVariant, "currentAssemblySetSha256"), variantHash,
                StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrWhiteSpace(GetString(selectedBase, "currentAssemblySetSha256")) &&
             !string.Equals(GetString(selectedBase, "currentAssemblySetSha256"), variantHash,
                 StringComparison.OrdinalIgnoreCase)))
            throw new DheException("Resource update payload variant hash is not bound to the selected Base.");

        var planByName = planAssemblies.EnumerateArray().ToDictionary(
            item => NormalizeName(GetString(item, "assemblyName") ?? string.Empty),
            item => item, StringComparer.OrdinalIgnoreCase);
        var payloads = new List<ResourcePayload>();
        var paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        if (!runtimePlan.TryGetProperty("aotMetadata", out JsonElement legacyMetadata) ||
            legacyMetadata.ValueKind != JsonValueKind.Array || legacyMetadata.GetArrayLength() != 0 ||
            !manifest.TryGetProperty("aotMetadataSets", out JsonElement manifestSets) ||
            manifestSets.ValueKind != JsonValueKind.Array || manifestSets.GetArrayLength() == 0 ||
            !runtimePlan.TryGetProperty("aotMetadataSets", out JsonElement planSets) ||
            planSets.ValueKind != JsonValueKind.Array ||
            planSets.GetArrayLength() != manifestSets.GetArrayLength())
            throw new DheException("Resource update AOT metadata sets are missing or inconsistent.");
        var planSetsById = planSets.EnumerateArray().ToDictionary(item =>
            GetString(item, "aotMetadataSetId") ?? string.Empty, item => item,
            StringComparer.OrdinalIgnoreCase);
        if (planSetsById.Count != planSets.GetArrayLength())
            throw new DheException("Runtime plan AOT metadata sets are duplicated.");
        var validSetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement manifestSet in manifestSets.EnumerateArray())
        {
            string setId = GetString(manifestSet, "aotMetadataSetId") ?? string.Empty;
            if (!IsHex(setId, 64, 64) || !validSetIds.Add(setId) ||
                !planSetsById.TryGetValue(setId, out JsonElement planSet) ||
                !manifestSet.TryGetProperty("assemblies", out JsonElement manifestMetadata) ||
                manifestMetadata.ValueKind != JsonValueKind.Array ||
                !planSet.TryGetProperty("assemblies", out JsonElement planMetadata) ||
                planMetadata.ValueKind != JsonValueKind.Array ||
                manifestMetadata.GetArrayLength() != planMetadata.GetArrayLength())
                throw new DheException("Resource update AOT metadata set is invalid: " + setId);
            var metadataPlanByName = planMetadata.EnumerateArray().ToDictionary(item =>
                    NormalizeName(GetString(item, "assemblyName") ?? string.Empty), item => item,
                StringComparer.OrdinalIgnoreCase);
            if (metadataPlanByName.Count != planMetadata.GetArrayLength())
                throw new DheException("Runtime plan AOT metadata records are duplicated: " + setId);
            var manifestNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var setBytes = new List<(string name, byte[] bytes)>();
            foreach (JsonElement metadata in manifestMetadata.EnumerateArray())
            {
                string name = NormalizeName(GetString(metadata, "assemblyName") ?? string.Empty);
                if (name.Length == 0 || !manifestNames.Add(name) ||
                    !metadataPlanByName.TryGetValue(name, out JsonElement planRecord))
                    throw new DheException("Resource update AOT metadata record is invalid: " + name);
                string assetPath = GetString(metadata, "path") ?? string.Empty;
                if (!assetPath.StartsWith(runtimeAssetRoot + "payload/aot-metadata/",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(GetString(planRecord, "path"), assetPath,
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(GetString(planRecord, "sha256"),
                        GetString(metadata, "sha256"), StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(GetString(planRecord, "sourceKind"),
                        GetString(metadata, "sourceKind"), StringComparison.Ordinal) ||
                    !string.Equals(GetString(planRecord, "manifestSha256"),
                        GetString(metadata, "manifestSha256"), StringComparison.OrdinalIgnoreCase))
                    throw new DheException(
                        "Resource update AOT metadata is not bound to the runtime plan: " + name);
                string relativePath = assetPath[runtimeAssetRoot.Length..];
                AddResourcePayload(updateRoot, relativePath, GetString(metadata, "sha256"),
                    GetString(planRecord, "path"), runtimeAssetRoot, payloads, paths);
                setBytes.Add((name, File.ReadAllBytes(ResolveContainedPath(updateRoot, relativePath,
                    "DHE resource AOT metadata"))));
            }
            if (!string.Equals(NamedByteSetHash(setBytes), setId,
                    StringComparison.OrdinalIgnoreCase))
                throw new DheException("Resource update AOT metadata set hash mismatch: " + setId);
        }

        if (!runtimePlan.TryGetProperty("baseSelections", out JsonElement selections) ||
            selections.ValueKind != JsonValueKind.Array ||
            !manifest.TryGetProperty("supportedBases", out JsonElement supportedBases) ||
            supportedBases.ValueKind != JsonValueKind.Array ||
            selections.GetArrayLength() != supportedBases.GetArrayLength())
            throw new DheException("Resource update Base metadata selections are missing.");
        var selectionsByBase = selections.EnumerateArray().ToDictionary(item =>
            GetString(item, "baseId") ?? string.Empty, item => item,
            StringComparer.OrdinalIgnoreCase);
        if (selectionsByBase.Count != selections.GetArrayLength())
            throw new DheException("Resource update Base metadata selections are duplicated.");
        foreach (JsonElement supportedBase in supportedBases.EnumerateArray())
        {
            string baseId = GetString(supportedBase, "baseId") ?? string.Empty;
            string setId = GetString(supportedBase, "aotMetadataSetId") ?? string.Empty;
            if (!IsHex(baseId, 64, 64) || !IsHex(setId, 64, 64) ||
                !validSetIds.Contains(setId) ||
                !selectionsByBase.TryGetValue(baseId, out JsonElement selection) ||
                !string.Equals(GetString(selection, "aotMetadataSetId"), setId,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(selection, "payloadVariantId") ?? "default",
                    GetString(supportedBase, "payloadVariantId") ?? "default",
                    StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrWhiteSpace(GetString(selection, "currentAssemblySetSha256")) &&
                 !string.Equals(GetString(selection, "currentAssemblySetSha256"),
                     GetString(supportedBase, "currentAssemblySetSha256"),
                     StringComparison.OrdinalIgnoreCase)))
                throw new DheException("Resource update Base metadata selection is invalid: " + baseId);
        }
        if (!string.Equals(GetString(selectedBase, "aotMetadataSetId"),
                GetString(selectionsByBase[GetString(selectedBase, "baseId") ?? string.Empty],
                    "aotMetadataSetId"), StringComparison.OrdinalIgnoreCase))
            throw new DheException("Selected Base AOT metadata set does not match the runtime plan.");
        return payloads.ToArray();
    }

    private static JsonElement SelectPayloadVariant(JsonElement document, string variantId,
        string description)
    {
        if (document.TryGetProperty("payloadVariants", out JsonElement variants) &&
            variants.ValueKind == JsonValueKind.Array)
        {
            JsonElement[] matches = variants.EnumerateArray().Where(item =>
                string.Equals(GetString(item, "variantId"), variantId,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new DheException(description + " does not contain exactly one payload variant: " +
                    variantId);
            return matches[0];
        }
        if (!string.Equals(variantId, "default", StringComparison.OrdinalIgnoreCase))
            throw new DheException(description + " has no payload variant: " + variantId);
        return document;
    }

    private static string ValidateResourceUpdateCompatibility(string updateRoot, JsonElement manifest)
    {
        ValidateBaseRegistryAudit(updateRoot, manifest);
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

        foreach (string property in new[]
        {
            "baseRegistrySha256", "baseRegistryEntryCount", "baseRegistryAuditPath",
            "baseRegistryAuditSha256"
        })
        {
            if (!OptionalJsonPropertiesEqual(validation, manifest, property))
                throw new DheException(
                    "DHE resource registry binding differs between manifest and validation: " +
                    property);
        }

        if (!manifest.TryGetProperty("supportedBases", out JsonElement supportedBases) ||
            supportedBases.ValueKind != JsonValueKind.Array || supportedBases.GetArrayLength() == 0 ||
            !validation.TryGetProperty("bases", out JsonElement validatedBases) ||
            validatedBases.ValueKind != JsonValueKind.Array ||
            validatedBases.GetArrayLength() != supportedBases.GetArrayLength())
            throw new DheException("DHE resource compatibility Base records are missing or inconsistent.");

        ValidatePayloadVariantSet(manifest, validation);

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
                !string.Equals(GetString(validatedBase, "aotMetadataSetId"),
                    GetString(supportedBase, "aotMetadataSetId"), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(validatedBase, "payloadVariantId") ?? "default",
                    GetString(supportedBase, "payloadVariantId") ?? "default",
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(GetString(validatedBase, "currentAssemblySetSha256"),
                    GetString(supportedBase, "currentAssemblySetSha256"),
                    StringComparison.OrdinalIgnoreCase) ||
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

    private static void ValidatePayloadVariantSet(JsonElement manifest, JsonElement validation)
    {
        ValidatePayloadVariantSetHash(manifest, "DHE resource manifest");
        ValidatePayloadVariantSetHash(validation, "DHE resource validation");
        bool manifestHasVariants = manifest.TryGetProperty("payloadVariants", out JsonElement manifestVariants);
        bool validationHasVariants = validation.TryGetProperty("payloadVariants", out JsonElement validationVariants);
        if (manifestHasVariants != validationHasVariants)
            throw new DheException("DHE resource payload variant records are not bound.");
        if (!manifestHasVariants)
            return;
        if (manifestVariants.ValueKind != JsonValueKind.Array || validationVariants.ValueKind != JsonValueKind.Array ||
            manifestVariants.GetArrayLength() == 0 ||
            manifestVariants.GetArrayLength() != validationVariants.GetArrayLength())
            throw new DheException("DHE resource payload variant records are invalid.");
        var validationById = validationVariants.EnumerateArray().ToDictionary(item =>
            GetString(item, "variantId") ?? string.Empty, item => item,
            StringComparer.OrdinalIgnoreCase);
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement variant in manifestVariants.EnumerateArray())
        {
            string id = GetString(variant, "variantId") ?? string.Empty;
            string hash = GetString(variant, "currentAssemblySetSha256") ?? string.Empty;
            if (!IsPayloadVariantId(id) || !ids.Add(id) || !IsHex(hash, 64, 64) ||
                !validationById.TryGetValue(id, out JsonElement validated) ||
                !string.Equals(GetString(validated, "currentAssemblySetSha256"), hash,
                    StringComparison.OrdinalIgnoreCase))
                throw new DheException("DHE resource payload variant record is invalid: " + id);
        }
    }

    private static void ValidatePayloadVariantSetHash(JsonElement document, string description)
    {
        if (!document.TryGetProperty("payloadVariants", out JsonElement variants))
            return;
        if (variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() == 0)
            throw new DheException(description + " payload variant records are invalid.");
        string setHash = GetString(document, "payloadVariantSetSha256") ?? string.Empty;
        if (!IsHex(setHash, 64, 64) ||
            !string.Equals(setHash, ComputePayloadVariantSetHash(variants),
                StringComparison.OrdinalIgnoreCase))
            throw new DheException(description + " payload variant set hash is invalid.");
    }

    private static string ComputePayloadVariantSetHash(JsonElement variants)
    {
        using var sha = SHA256.Create();
        foreach (JsonElement variant in variants.EnumerateArray().OrderBy(item =>
                     GetString(item, "variantId") ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            byte[] bytes = Encoding.UTF8.GetBytes((GetString(variant, "variantId") ?? string.Empty) +
                "\n" + (GetString(variant, "currentAssemblySetSha256") ?? string.Empty).ToLowerInvariant() +
                "\n");
            sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!).ToLowerInvariant();
    }

    private static void ValidateBaseRegistryAudit(string updateRoot, JsonElement manifest)
    {
        string? registrySha256 = GetString(manifest, "baseRegistrySha256");
        string? auditPathValue = GetString(manifest, "baseRegistryAuditPath");
        string? auditSha256 = GetString(manifest, "baseRegistryAuditSha256");
        if (string.IsNullOrWhiteSpace(registrySha256))
        {
            if (!manifest.TryGetProperty("baseRegistryEntryCount", out JsonElement entryCount) ||
                entryCount.ValueKind != JsonValueKind.Null ||
                !string.IsNullOrWhiteSpace(auditPathValue) ||
                !string.IsNullOrWhiteSpace(auditSha256))
                throw new DheException("DHE resource update has Base registry audit fields without a registry.");
            return;
        }

        if (!IsHex(registrySha256, 64, 64) ||
            !string.Equals(auditPathValue, "audit/dhe-base-registry.json",
                StringComparison.Ordinal) ||
            !IsHex(auditSha256, 64, 64))
            throw new DheException("DHE Base registry audit binding is invalid.");
        string auditPath = RequireFile(ResolveContainedPath(updateRoot, auditPathValue!,
            "DHE Base registry audit copy"), "DHE Base registry audit copy");
        if (!string.Equals(Sha256File(auditPath), registrySha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Sha256File(auditPath), auditSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE Base registry audit copy hash does not match the manifest.");

        // Do not resolve paths from the archived document: those paths are
        // relative to the original registry location. Validate its identity
        // and entry set here, while ReadBaseRegistry validates paths at build
        // time before the copy is made.
        JsonElement archived = ReadJson<JsonElement>(auditPath);
        if (GetInt(archived, "schemaVersion") != 1 ||
            !string.Equals(GetString(archived, "format"),
                "hybridclr.dhe-base-registry.json", StringComparison.Ordinal) ||
            (GetString(archived, "pathSemantics") is not ("registry-relative-v1" or
                "workspace-absolute-v1")) ||
            !archived.TryGetProperty("bases", out JsonElement bases) ||
            bases.ValueKind != JsonValueKind.Array || bases.GetArrayLength() == 0 ||
            GetInt(manifest, "baseRegistryEntryCount") != bases.GetArrayLength())
            throw new DheException("DHE archived Base registry identity or entry count is invalid.");

        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (JsonElement entry in bases.EnumerateArray())
        {
            string baseId = GetString(entry, "baseId") ?? string.Empty;
            string workflow = GetString(entry, "engineWorkflow") ?? string.Empty;
            if (!IsHex(baseId, 64, 64) || !ids.Add(baseId) ||
                !RequiredPlayerEngineWorkflows.Contains(workflow,
                    StringComparer.Ordinal))
                throw new DheException("DHE archived Base registry contains an invalid entry.");
        }
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

    private static bool OptionalJsonPropertiesEqual(JsonElement left, JsonElement right,
        string property)
    {
        bool leftPresent = left.TryGetProperty(property, out JsonElement leftValue);
        bool rightPresent = right.TryGetProperty(property, out JsonElement rightValue);
        return leftPresent == rightPresent &&
            (!leftPresent || JsonEquivalent(leftValue, rightValue));
    }

    private static string ResourceBaseIdentityKey(JsonElement value)
    {
        var target = GetString(value, "target") ?? string.Empty;
        var baseId = GetString(value, "baseId") ?? string.Empty;
        var managed = GetString(value, "managedAssemblySetSha256") ?? string.Empty;
        var snapshot = GetString(value, "aotSnapshotSha256") ?? string.Empty;
        var baseMetaVersion = GetString(value, "baseMetaVersionSetSha256") ?? string.Empty;
        var aotMetadataSetId = GetString(value, "aotMetadataSetId") ?? string.Empty;
        var guard = GetString(value, "nativeGuardSourceSha256") ?? string.Empty;
        var nativeManifest = GetString(value, "nativeManifestSha256") ?? string.Empty;
        if (target.Length == 0 || !IsHex(baseId, 64, 64) || !IsHex(managed, 64, 64) ||
            !IsHex(snapshot, 64, 64) ||
            !IsHex(baseMetaVersion, 64, 64) || !IsHex(aotMetadataSetId, 64, 64) ||
            !IsHex(guard, 64, 64) ||
            !IsHex(nativeManifest, 64, 64))
            throw new DheException("DHE resource update contains an incomplete Player Base identity.");
        return string.Join("|", target, baseId, managed, snapshot, baseMetaVersion,
            aotMetadataSetId, guard,
            nativeManifest);
    }

    private static void AddResourcePayload(string updateRoot, string? relativePath, string? expectedHash,
        string? planAssetPath, string runtimeAssetRoot, List<ResourcePayload> payloads,
        Dictionary<string, string> paths)
    {
        var relative = relativePath ?? string.Empty;
        if (!relative.StartsWith("payload/", StringComparison.OrdinalIgnoreCase) ||
            !IsHex(expectedHash, 64, 64) ||
            !string.Equals(planAssetPath, runtimeAssetRoot + relative, StringComparison.OrdinalIgnoreCase))
            throw new DheException("Resource payload path/hash binding is invalid: " + relative);
        if (paths.TryGetValue(relative, out string? priorHash))
        {
            if (!string.Equals(priorHash, expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new DheException("Resource payload path has conflicting hashes: " + relative);
            return;
        }
        var source = RequireFile(ResolveContainedPath(updateRoot, relative, "DHE resource payload"),
            "DHE resource payload");
        if (!Sha256File(source).Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            throw new DheException("DHE resource payload hash mismatch: " + relative);
        paths.Add(relative, expectedHash!);
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
