using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HybridCLR;
using HybridCLR.Lab.ManagedCasesAot;
using ManagedCasesSecondary = HybridCLR.Lab.ManagedCases;
using MetadataStressSecondary = HybridCLR.Lab.MetadataStress.DheSecondaryCases;
using CrossAssemblySecondary = HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe;
using UnityEngine;

namespace HybridCLR.Lab
{
    internal static class HybridCLRDhePlayerRunner
    {
        private const string RuntimePlanFile = "HybridCLRLab/DheDemo/DheRuntimePlan.json";
        private const string ResourceManifestFile = "HybridCLRLab/DheDemo/dhe-resource-update.json";
        private const string RuntimeAssetRoot = "HybridCLRLab/DheDemo/";
        private const string MainAssemblyName = "HybridCLR.ManagedCasesAot";
        private const string BuildIdentityFile = "HybridCLRLab/build-identity.json";
        private const string DefaultResultFile = "hybridclr-lab-dhe-result.json";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor || !string.Equals(GetArgument("-labMode"), "dhe", StringComparison.Ordinal))
            {
                return;
            }

            int exitCode = 0;
            DheRun result;
            try
            {
                result = Execute();
                exitCode = result.passed ? 0 : 1;
            }
            catch (Exception exception)
            {
                result = new DheRun
                {
                    passed = false,
                    error = exception.ToString(),
                };
                Debug.LogException(exception);
                exitCode = 1;
            }

            WriteResult(result);
            Debug.Log("[HybridCLR Lab] DHE demo: " + (result.passed ? "passed" : "failed"));
            Application.Quit(exitCode);
        }

        private static DheRun Execute()
        {
            StreamingAssetsProvider provider = new StreamingAssetsProvider();
            bool resourceUpdateManifestPresent = provider.Exists(ResourceManifestFile);
            string runtimePlanFile = RuntimePlanFile;
            if (resourceUpdateManifestPresent)
            {
                ResourceManifestData resourceManifest = JsonUtility.FromJson<ResourceManifestData>(
                    provider.LoadText(ResourceManifestFile));
                if (resourceManifest == null || string.IsNullOrWhiteSpace(resourceManifest.runtimePlan) ||
                    resourceManifest.runtimePlan.Contains("/") ||
                    resourceManifest.runtimePlan.Contains("\\") ||
                    resourceManifest.runtimePlan == "." || resourceManifest.runtimePlan == "..")
                {
                    throw new InvalidDataException("DHE resource manifest runtimePlan is invalid.");
                }
                runtimePlanFile = RuntimeAssetRoot + resourceManifest.runtimePlan;
            }
            DheRuntimeIdentity runtimeIdentity = HybridCLRDheBuildIdentity.Create();
            DheRuntime.Reset();
            string runtimeError;
            bool initialized = resourceUpdateManifestPresent
                ? DheRuntime.InitializeFromResourceUpdate(provider, runtimeIdentity,
                    ResourceManifestFile, out runtimeError, RuntimeAssetRoot, true)
                : DheRuntime.Initialize(provider, runtimeIdentity, out runtimeError,
                    runtimePlanFile, RuntimeAssetRoot, true);
            if (!initialized)
            {
                throw new InvalidDataException("DHE managed runtime initialization failed: " +
                    runtimeError);
            }
            DheRuntimePlanData runtimePlan = JsonUtility.FromJson<DheRuntimePlanData>(
                provider.LoadText(runtimePlanFile));
            if (runtimePlan == null || runtimePlan.schemaVersion != 1 ||
                !string.Equals(runtimePlan.format, "hybridclr.dhe-runtime-asset-plan.json", StringComparison.Ordinal) ||
                (runtimePlan.selection != "embedded-base-metaversion" &&
                 runtimePlan.selection != "embedded-base-metaversion-and-aot-metadata-set") ||
                ((runtimePlan.assemblies == null || runtimePlan.assemblies.Length == 0) &&
                 (runtimePlan.payloadVariants == null || runtimePlan.payloadVariants.Length == 0)))
            {
                throw new InvalidDataException("DHE runtime plan is empty or invalid.");
            }
            DheRuntimePayloadVariantData selectedPayloadVariant = SelectRuntimePayloadVariant(
                runtimePlan, runtimeIdentity.BaseId);
            DheAssemblyPlanData[] selectedPlanAssemblies = selectedPayloadVariant.assemblies;
            ApplyRuntimeAssemblyModes(runtimePlan, runtimeIdentity.BaseId, selectedPlanAssemblies);
            if (!DheRuntime.LoadAotMetadataImages(provider, HomologousImageMode.SuperSet,
                    out LoadImageErrorCode metadataError, out string metadataMessage))
            {
                throw new InvalidDataException("DHE AOT metadata load failed: " +
                    metadataError + "/" + metadataMessage);
            }
            Dictionary<string, LoadedDheAssembly> loadedAssemblies = new Dictionary<string, LoadedDheAssembly>(StringComparer.OrdinalIgnoreCase);
            int changedMethodCount = 0;
            bool retryValidated = false;
            string retryAssemblyName = string.Empty;
            LoadImageErrorCode retryFailureCode = LoadImageErrorCode.OK;
            var batchAssemblyNames = new List<string>();
            var batchCurrentAssemblies = new List<byte[]>();
            foreach (DheAssemblyPlanData assemblyPlan in selectedPlanAssemblies)
            {
                if (assemblyPlan == null || string.IsNullOrWhiteSpace(assemblyPlan.assemblyName) ||
                    string.IsNullOrWhiteSpace(assemblyPlan.current) ||
                    string.IsNullOrWhiteSpace(assemblyPlan.currentMetaVersion))
                {
                    throw new InvalidDataException("DHE runtime plan contains an incomplete assembly record.");
                }
                if (loadedAssemblies.ContainsKey(assemblyPlan.assemblyName))
                {
                    throw new InvalidDataException("DHE runtime plan contains duplicate assembly: " + assemblyPlan.assemblyName);
                }
                byte[] assemblyCurrent = DheStreamingAssetReader.Read(assemblyPlan.current);
                byte[] assemblyCurrentMv = DheStreamingAssetReader.Read(assemblyPlan.currentMetaVersion);
                bool interpreterOnly = string.Equals(assemblyPlan.executionMode,
                    "interpreter-only", StringComparison.Ordinal);
                if (!interpreterOnly && !string.Equals(assemblyPlan.executionMode,
                        "dhe-differential", StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(assemblyPlan.executionMode))
                {
                    throw new InvalidDataException("DHE runtime plan contains an unknown assembly execution mode: " +
                        assemblyPlan.executionMode);
                }
                MetaVersionInfo currentMv = ParseMetaVersion(assemblyCurrentMv,
                    assemblyPlan.assemblyName);
                MetaVersionInfo baseMv = null;
                if (!interpreterOnly)
                {
                    if (string.IsNullOrWhiteSpace(assemblyPlan.baseMetaVersion))
                        throw new InvalidDataException("DHE differential assembly has no Base MetaVersion: " +
                            assemblyPlan.assemblyName);
                    byte[] assemblyBaseMv = DheStreamingAssetReader.Read(assemblyPlan.baseMetaVersion);
                    baseMv = ParseMetaVersion(assemblyBaseMv, assemblyPlan.assemblyName);
                }
                byte[] assemblyCurrentHash = Sha256(assemblyCurrent);
                byte[] assemblyMvHash = Sha256(assemblyCurrentMv);
                byte[] assemblyBaselineHash = baseMv == null ? Array.Empty<byte>() : baseMv.assemblyHash;
                string actualCurrentHash = ToHex(assemblyCurrentHash);
                string actualBaselineHash = ToHex(assemblyBaselineHash);
                string actualMvHash = ToHex(assemblyMvHash);
                string mvCurrentHash = ToHex(currentMv.assemblyHash);
                string mvBaselineHash = ToHex(baseMv == null ? Array.Empty<byte>() : baseMv.assemblyHash);
                if (!string.Equals(assemblyPlan.currentSha256, actualCurrentHash, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(assemblyPlan.currentMetaVersionSha256, actualMvHash,
                        StringComparison.OrdinalIgnoreCase) ||
                    !ByteArraysEqual(assemblyCurrentHash, currentMv.assemblyHash))
                {
                    throw new InvalidDataException("DHE runtime plan hash binding failed for " +
                        assemblyPlan.assemblyName + "; current=" + actualCurrentHash +
                        "/plan=" + assemblyPlan.currentSha256 + "/mv=" + mvCurrentHash +
                        "; baseline=" + actualBaselineHash + "/mv=" + mvBaselineHash);
                }
                int assemblyChangedMethodCount = baseMv == null ? 0 : CountChangedMethods(baseMv, currentMv);
                changedMethodCount = checked(changedMethodCount + assemblyChangedMethodCount);
                if (!interpreterOnly)
                {
                    batchAssemblyNames.Add(assemblyPlan.assemblyName);
                    batchCurrentAssemblies.Add(assemblyCurrent);
                }
                loadedAssemblies.Add(assemblyPlan.assemblyName, new LoadedDheAssembly
                {
                    plan = assemblyPlan,
                    currentHash = assemblyCurrentHash,
                    baselineHash = assemblyBaselineHash,
                    mvCurrentHash = currentMv.assemblyHash,
                    mvBaselineHash = baseMv == null ? Array.Empty<byte>() : baseMv.assemblyHash,
                    currentMetaVersion = currentMv,
                    baseMetaVersion = baseMv,
                });
            }
            if (!DheRuntime.LoadAssemblyImages(batchAssemblyNames.ToArray(),
                    batchCurrentAssemblies.ToArray(), out LoadImageErrorCode batchLoadError,
                    out string batchLoadMessage))
            {
                throw new InvalidOperationException("DHE atomic batch load failed: " +
                    batchLoadError + "/" + batchLoadMessage);
            }
            foreach (LoadedDheAssembly loaded in loadedAssemblies.Values.Where(item =>
                         string.Equals(item.plan.executionMode, "interpreter-only",
                             StringComparison.Ordinal)))
            {
                if (!DheRuntime.LoadInterpreterAssemblyImage(loaded.plan.assemblyName,
                        DheStreamingAssetReader.Read(loaded.plan.current),
                        out Assembly interpreterAssembly, out LoadImageErrorCode interpreterLoadError,
                        out string interpreterLoadMessage))
                {
                    throw new InvalidOperationException("Interpreter-only assembly load failed: " +
                        loaded.plan.assemblyName + ": " + interpreterLoadError + "/" + interpreterLoadMessage);
                }
                loaded.assembly = interpreterAssembly;
            }
            foreach (LoadedDheAssembly loaded in loadedAssemblies.Values.Where(item =>
                         !string.Equals(item.plan.executionMode, "interpreter-only",
                             StringComparison.Ordinal)))
            {
                loaded.assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
                    string.Equals(candidate.GetName().Name, loaded.plan.assemblyName,
                        StringComparison.OrdinalIgnoreCase));
                if (loaded.assembly == null)
                    throw new InvalidOperationException("DHE Base AOT assembly was not found: " +
                        loaded.plan.assemblyName);
            }
            retryValidated = DheRuntime.TransactionRetryValidated;
            retryAssemblyName = DheRuntime.TransactionRetryAssemblyName;
            retryFailureCode = DheRuntime.TransactionRetryFailure;
            if (loadedAssemblies.Count != selectedPlanAssemblies.Length ||
                !loadedAssemblies.ContainsKey(MainAssemblyName))
            {
                throw new InvalidDataException("DHE runtime plan must load exactly its declared assemblies and include " + MainAssemblyName + ".");
            }

            LoadedDheAssembly mainLoaded = loadedAssemblies[MainAssemblyName];
            byte[] current = DheStreamingAssetReader.Read(mainLoaded.plan.current);
            byte[] mv = DheStreamingAssetReader.Read(mainLoaded.plan.currentMetaVersion);
            byte[] snapshot = mainLoaded.baselineHash;
            DheBuildIdentityData buildIdentity = JsonUtility.FromJson<DheBuildIdentityData>(
                System.Text.Encoding.UTF8.GetString(DheStreamingAssetReader.Read(BuildIdentityFile)));
            byte[] currentHash = Sha256(current);
            byte[] baselineHash = mainLoaded.baselineHash;
            byte[] expectedCurrentHash = mainLoaded.mvCurrentHash;
            byte[] expectedBaselineHash = mainLoaded.mvBaselineHash;
            LoadImageErrorCode loadError = LoadImageErrorCode.OK;

            MethodInfo addMethod = typeof(DheDemoCalculator).GetMethod("Add", BindingFlags.Public | BindingFlags.Static);
            MethodInfo stableMethod = typeof(DheDemoCalculator).GetMethod("Stable", BindingFlags.Public | BindingFlags.Static);
            // Keep the unchanged identity probe on a type that is not altered
            // by the structural fixture. DheDemoCalculator intentionally
            // evolves fields/properties/events, so its methods may be marked
            // changed through type dependencies even when their bodies stay
            // identical.
            MethodInfo identityUnchangedMethod = typeof(PerformanceWorkload).GetProperty(
                "All", BindingFlags.Public | BindingFlags.Static)?.GetMethod;
            MethodInfo addViaStableMethod = typeof(DheDemoCalculator).GetMethod("AddViaStable", BindingFlags.Public | BindingFlags.Static);
            MethodInfo addPairMethod = typeof(DheDemoCalculator).GetMethod("AddPair", BindingFlags.Public | BindingFlags.Static);
            MethodInfo wideMethod = typeof(DheDemoCalculator).GetMethod("Wide", BindingFlags.Public | BindingFlags.Static);
            MethodInfo touchMethod = typeof(DheDemoCalculator).GetMethod("Touch", BindingFlags.Public | BindingFlags.Static);
            MethodInfo instanceStableMethod = typeof(DheDemoCalculator).GetMethod("InstanceStable", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo instanceAddMethod = typeof(DheDemoCalculator).GetMethod("InstanceAdd", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo instanceAddViaStableMethod = typeof(DheDemoCalculator).GetMethod("InstanceAddViaStable", BindingFlags.Public | BindingFlags.Instance);
            if (addMethod == null || stableMethod == null || identityUnchangedMethod == null ||
                addViaStableMethod == null || addPairMethod == null ||
                wideMethod == null || touchMethod == null || instanceStableMethod == null ||
                instanceAddMethod == null || instanceAddViaStableMethod == null)
            {
                throw new MissingMethodException(typeof(DheDemoCalculator).FullName);
            }

            bool addChanged = RuntimeApi.IsDifferentialMethodChanged(addMethod);
            bool stableChanged = RuntimeApi.IsDifferentialMethodChanged(stableMethod);
            bool identityUnchangedChanged = RuntimeApi.IsDifferentialMethodChanged(identityUnchangedMethod);
            bool addViaStableChanged = RuntimeApi.IsDifferentialMethodChanged(addViaStableMethod);
            bool addPairChanged = RuntimeApi.IsDifferentialMethodChanged(addPairMethod);
            bool wideChanged = RuntimeApi.IsDifferentialMethodChanged(wideMethod);
            bool touchChanged = RuntimeApi.IsDifferentialMethodChanged(touchMethod);
            bool instanceStableChanged = RuntimeApi.IsDifferentialMethodChanged(instanceStableMethod);
            bool instanceAddChanged = RuntimeApi.IsDifferentialMethodChanged(instanceAddMethod);
            bool instanceAddViaStableChanged = RuntimeApi.IsDifferentialMethodChanged(instanceAddViaStableMethod);

            DheDemoCalculator calculator = new DheDemoCalculator();
            RuntimeApi.ResetDifferentialDispatchCounters();
            int addResult = DheDemoCalculator.Add(1);
            int stableResult = DheDemoCalculator.Stable(2);
            int identityUnchangedResult = PerformanceWorkload.All.Count;
            int addViaStableResult = DheDemoCalculator.AddViaStable(2);
            int addPairResult = DheDemoCalculator.AddPair(3, 4);
            long wideResult = DheDemoCalculator.Wide(5L);
            DheDemoCalculator.Touch(5);
            int touchValue = DheDemoCalculator.TouchValue;
            int instanceAddResult = calculator.InstanceAdd(1);
            int instanceStableResult = calculator.InstanceStable(2);
            int instanceAddViaStableResult = calculator.InstanceAddViaStable(2);
            MethodInfo managedSecondaryChangedMethod = typeof(ManagedCasesSecondary.DheSecondaryCases)
                .GetMethod("Changed", BindingFlags.Public | BindingFlags.Static);
            MethodInfo managedSecondaryUnchangedMethod = typeof(ManagedCasesSecondary.DheSecondaryCases)
                .GetMethod("Unchanged", BindingFlags.Public | BindingFlags.Static);
            MethodInfo metadataSecondaryChangedMethod = typeof(MetadataStressSecondary)
                .GetMethod("Changed", BindingFlags.Public | BindingFlags.Static);
            MethodInfo crossSecondaryChangedMethod = typeof(CrossAssemblySecondary)
                .GetMethod("Changed", BindingFlags.Public | BindingFlags.Static);
            if (managedSecondaryChangedMethod == null || managedSecondaryUnchangedMethod == null ||
                metadataSecondaryChangedMethod == null || crossSecondaryChangedMethod == null)
            {
                throw new MissingMethodException("DHE secondary changed-method fixture");
            }
            bool managedSecondaryChanged = RuntimeApi.IsDifferentialMethodChanged(managedSecondaryChangedMethod);
            bool managedSecondaryUnchanged = RuntimeApi.IsDifferentialMethodChanged(managedSecondaryUnchangedMethod);
            bool metadataSecondaryChanged = RuntimeApi.IsDifferentialMethodChanged(metadataSecondaryChangedMethod);
            bool crossSecondaryChanged = RuntimeApi.IsDifferentialMethodChanged(crossSecondaryChangedMethod);
            int managedSecondaryDirectResult = ManagedCasesSecondary.DheSecondaryCases.Changed(3);
            int managedSecondaryUnchangedDirectResult = ManagedCasesSecondary.DheSecondaryCases.Unchanged(3);
            int metadataSecondaryDirectResult = MetadataStressSecondary.Changed(3);
            int crossSecondaryDirectResult = CrossAssemblySecondary.Changed(3);
            int mainInterpreterEntryCount = RuntimeApi.GetDifferentialInterpreterEntryCount();
            int mainAotBridgeCallCount = RuntimeApi.GetDifferentialAotBridgeCallCount();
            int mainAotEntryCount = RuntimeApi.GetDifferentialAotEntryCount();
            // Keep the full additions fixture gate separate from dispatch
            // classification. A current payload may remove members or evolve a
            // type without containing the optional additions fixture; that
            // still changes Stable dispatch, but must not require reflection
            // assertions for types that are absent from the payload.
            bool structuralExpected = HasStructuralAdditions(mainLoaded.baseMetaVersion,
                mainLoaded.currentMetaVersion);
            bool structuralDispatchExpected = HasStructuralChanges(mainLoaded.baseMetaVersion,
                mainLoaded.currentMetaVersion);
            CapabilityDirectRun directCapability = ExecuteCapabilityDirect(structuralExpected);
            CapabilityRun capability = ExecuteCapabilityReflection(structuralExpected);
            StructuralRun structural = ExecuteStructural(mainLoaded.assembly, structuralExpected,
                directCapability.genericContainerResult);
            long metadataStressResult = ExecuteMetadataStress(loadedAssemblies);
            int metadataSecondaryReflectionResult = ExecuteSecondaryChanged(
                loadedAssemblies, "HybridCLR.MetadataStress", "HybridCLR.Lab.MetadataStress.DheSecondaryCases");
            string crossAssemblyResult = ExecuteCrossAssembly(loadedAssemblies);
            int crossSecondaryReflectionResult = ExecuteSecondaryChanged(
                loadedAssemblies, "HybridCLR.CrossAssemblyDerived", "HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe");
            bool currentHashValidated = ByteArraysEqual(currentHash, expectedCurrentHash);
            bool baselineHashValidated = ByteArraysEqual(baselineHash, expectedBaselineHash);
            string embeddedSnapshotHash = HybridCLRDheBuildIdentity.AotSnapshotSha256;
            bool embeddedSnapshotHashValidated = buildIdentity != null &&
                !string.IsNullOrWhiteSpace(embeddedSnapshotHash) &&
                string.Equals(buildIdentity.aotSnapshotSha256, embeddedSnapshotHash,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.managedAssemblySetSha256,
                    HybridCLRDheBuildIdentity.ManagedAssemblySetSha256,
                    StringComparison.OrdinalIgnoreCase);
            bool snapshotHashValidated = ByteArraysEqual(snapshot, expectedBaselineHash) && embeddedSnapshotHashValidated;
            string expectedTarget = GetArgument("-labTarget");
            DheBuildIdentityAssemblyData mainIdentity = buildIdentity?.assemblies?.FirstOrDefault(item =>
                string.Equals(item?.assemblyName, MainAssemblyName, StringComparison.OrdinalIgnoreCase));
            bool buildIdentityValidated = buildIdentity != null && buildIdentity.identityVersion == 1 &&
                string.Equals(buildIdentity.aotSnapshotKind, "managed-assembly-plus-generated-cpp-v1", StringComparison.Ordinal) &&
                string.Equals(buildIdentity.target, expectedTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.baseId, HybridCLRDheBuildIdentity.BaseId,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.runtimeProtocol,
                    HybridCLRDheBuildIdentity.RuntimeProtocol, StringComparison.Ordinal) &&
                string.Equals(buildIdentity.runtimeContract,
                    HybridCLRDheBuildIdentity.RuntimeContract, StringComparison.Ordinal) &&
                string.Equals(buildIdentity.runtimeAssetRoot,
                    HybridCLRDheBuildIdentity.RuntimeAssetRoot, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.baseMetaVersionAssetRoot,
                    HybridCLRDheBuildIdentity.BaseMetaVersionAssetRoot,
                    StringComparison.OrdinalIgnoreCase) &&
                new HashSet<string>(buildIdentity.runtimeCapabilities ?? Array.Empty<string>(),
                    StringComparer.Ordinal).SetEquals(
                    HybridCLRDheBuildIdentity.RuntimeCapabilities ?? Array.Empty<string>()) &&
                mainIdentity != null &&
                string.Equals(mainIdentity.baselineSha256, ToHex(baselineHash), StringComparison.OrdinalIgnoreCase) &&
                IsSha256(buildIdentity.baseMetaVersionSetSha256) &&
                IsSha256(buildIdentity.aotMetadataSetId) &&
                string.Equals(buildIdentity.aotMetadataSetId,
                    HybridCLRDheBuildIdentity.AotMetadataSetId,
                    StringComparison.OrdinalIgnoreCase) &&
                IsSha256(buildIdentity.nativeGuardSourceSha256) &&
                IsSha256(buildIdentity.nativeManifestSha256) &&
                !string.Equals(buildIdentity.nativeManifestSha256, new string('0', 64), StringComparison.Ordinal) &&
                string.Equals(buildIdentity.nativeGuardSourceSha256, HybridCLRDheBuildIdentity.NativeGuardSourceSha256, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.nativeManifestSha256, HybridCLRDheBuildIdentity.NativeManifestSha256, StringComparison.OrdinalIgnoreCase);
            bool noOpCapabilityDirectValidated = ValidateNoOpCapabilityDirect(directCapability);
            bool noOpCapabilityReflectionValidated = ValidateNoOpCapabilityReflection(capability);
            bool noOpMainBehaviorValidated = addResult == 2 && stableResult == 4 &&
                addViaStableResult == 5 && addPairResult == 8 && wideResult == 6L && touchValue == 12 &&
                instanceAddResult == 3 && instanceStableResult == 6 && instanceAddViaStableResult == 8 &&
                identityUnchangedResult == 21 && !identityUnchangedChanged &&
                !addChanged && !stableChanged && !addViaStableChanged && !addPairChanged && !wideChanged &&
                !touchChanged && !instanceStableChanged && !instanceAddChanged && !instanceAddViaStableChanged;
            bool noOpMultiAssemblyValidated = loadedAssemblies.Count >= 4 && metadataStressResult > 0 &&
                !managedSecondaryChanged && !managedSecondaryUnchanged && !metadataSecondaryChanged &&
                !crossSecondaryChanged && managedSecondaryDirectResult == 13 &&
                managedSecondaryUnchangedDirectResult == 6 && metadataSecondaryDirectResult == 13 &&
                crossSecondaryDirectResult == 13 && metadataSecondaryReflectionResult == 13 &&
                crossSecondaryReflectionResult == 13 &&
                string.Equals(crossAssemblyResult, "derived:26:34", StringComparison.Ordinal);
            bool noOpAotBehaviorValidated = changedMethodCount == 0 && noOpMainBehaviorValidated &&
                noOpMultiAssemblyValidated && noOpCapabilityDirectValidated &&
                noOpCapabilityReflectionValidated && mainInterpreterEntryCount == 0;
            bool stableDispatchValidated = structuralDispatchExpected
				? stableChanged && instanceStableChanged
				: !stableChanged && !instanceStableChanged;
            bool changedBehaviorValidated = changedMethodCount == 0
                ? noOpAotBehaviorValidated
                : (addResult == 101 && stableResult == 4 &&
                    addViaStableResult == 104 && addPairResult == 107 && wideResult == 1005L &&
                    touchValue == 705 && instanceAddResult == 201 && instanceStableResult == 6 &&
					instanceAddViaStableResult == 206 && addChanged && stableDispatchValidated &&
					addViaStableChanged && addPairChanged && wideChanged && touchChanged &&
					instanceAddChanged && instanceAddViaStableChanged &&
                    mainInterpreterEntryCount >= 7 && mainAotEntryCount >= 3 && capability.passed &&
                    directCapability.passed && structural.passed && managedSecondaryChanged && !managedSecondaryUnchanged &&
                    metadataSecondaryChanged && crossSecondaryChanged &&
                    managedSecondaryDirectResult == 103 && managedSecondaryUnchangedDirectResult == 6 &&
                    metadataSecondaryDirectResult == 103 && crossSecondaryDirectResult == 103 &&
                    metadataSecondaryReflectionResult == 103 && crossSecondaryReflectionResult == 103 &&
                    loadedAssemblies.Count >= 4 && metadataStressResult > 0 &&
                    string.Equals(crossAssemblyResult, "derived:26:34", StringComparison.Ordinal));
            bool transactionEvidenceValid = changedMethodCount == 0 || retryValidated;
            string transactionStatus = changedMethodCount == 0
                ? "notApplicable"
                : (retryValidated ? "validated" : "failed");
            bool dispatchProbeValidated = loadError == LoadImageErrorCode.OK && changedBehaviorValidated;

            return new DheRun
            {
                passed = loadError == LoadImageErrorCode.OK && changedBehaviorValidated &&
                    currentHashValidated && baselineHashValidated && snapshotHashValidated &&
                    buildIdentityValidated && transactionEvidenceValid,
                loadError = loadError.ToString(),
                assemblyName = typeof(DheDemoCalculator).Assembly.GetName().Name,
                currentAssemblySha256 = ToHex(currentHash),
                baselineAssemblySha256 = ToHex(baselineHash),
                mvCurrentSha256 = ToHex(expectedCurrentHash),
                mvBaselineSha256 = ToHex(expectedBaselineHash),
                addResult = addResult,
                stableResult = stableResult,
                addViaStableResult = addViaStableResult,
                addPairResult = addPairResult,
                wideResult = wideResult,
                touchValue = touchValue,
                instanceAddResult = instanceAddResult,
                instanceStableResult = instanceStableResult,
                instanceAddViaStableResult = instanceAddViaStableResult,
                capabilityDirectPassed = changedMethodCount == 0
                    ? noOpCapabilityDirectValidated
                    : directCapability.passed,
                capabilityDirectError = directCapability.error,
                capabilityDirectInterpreterEntryCount = directCapability.interpreterEntryCount,
                capabilityDirectAotEntryCount = directCapability.aotEntryCount,
                capabilityDirectGenericSelectResult = directCapability.genericSelectResult,
                capabilityDirectGenericSelectNullPassed = directCapability.genericSelectNullPassed,
                capabilityDirectGenericConstrainedResult = directCapability.genericConstrainedResult,
                capabilityDirectGenericVirtualResult = directCapability.genericVirtualResult,
                managedSecondaryChanged = managedSecondaryChanged,
                managedSecondaryUnchanged = managedSecondaryUnchanged,
                metadataSecondaryChanged = metadataSecondaryChanged,
                crossSecondaryChanged = crossSecondaryChanged,
                managedSecondaryDirectResult = managedSecondaryDirectResult,
                managedSecondaryUnchangedDirectResult = managedSecondaryUnchangedDirectResult,
                metadataSecondaryDirectResult = metadataSecondaryDirectResult,
                crossSecondaryDirectResult = crossSecondaryDirectResult,
                metadataSecondaryReflectionResult = metadataSecondaryReflectionResult,
                crossSecondaryReflectionResult = crossSecondaryReflectionResult,
                secondaryAssemblyDirectValidated = changedMethodCount == 0
                    ? noOpMultiAssemblyValidated
                    : managedSecondaryDirectResult == 103 && managedSecondaryUnchangedDirectResult == 6 &&
                        metadataSecondaryDirectResult == 103 && crossSecondaryDirectResult == 103,
                secondaryAssemblyChangedValidated = changedMethodCount == 0
                    ? noOpMultiAssemblyValidated
                    : managedSecondaryChanged && !managedSecondaryUnchanged && metadataSecondaryChanged &&
                        crossSecondaryChanged && metadataSecondaryReflectionResult == 103 &&
                        crossSecondaryReflectionResult == 103,
                capabilityPassed = changedMethodCount == 0
                    ? noOpCapabilityReflectionValidated
                    : capability.passed,
                capabilityError = capability.error,
                capabilityInterfaceChanged = capability.interfaceChanged,
                capabilityDelegateChanged = capability.delegateChanged,
                capabilityGenericSelectChanged = capability.genericSelectChanged,
                capabilityGenericConstrainedChanged = capability.genericConstrainedChanged,
                capabilityMutateValueChanged = capability.mutateValueChanged,
                capabilityBoxValueChanged = capability.boxValueChanged,
                capabilityRefOutValueChanged = capability.refOutValueChanged,
                capabilityAsyncValueChanged = capability.asyncValueChanged,
                capabilityIteratorChanged = capability.iterateValueChanged,
                capabilityAsyncStateMachineChanged = capability.asyncStateMachineChanged,
                capabilityIteratorStateMachineChanged = capability.iteratorStateMachineChanged,
                capabilityGenericContainerChanged = capability.genericContainerChanged,
                capabilityNullableChanged = capability.nullableChanged,
                capabilityDelegateClosedInstanceChanged = capability.delegateClosedInstanceChanged,
                capabilityDelegateOpenInstanceChanged = capability.delegateOpenInstanceChanged,
                capabilityDelegateMulticastChanged = capability.delegateMulticastChanged,
                capabilityExceptionFinallyChanged = capability.exceptionFinallyChanged,
                 capabilityVirtualChanged = capability.virtualChanged,
                  capabilityGenericVirtualChanged = capability.genericVirtualChanged,
                  capabilityGenericVirtualPassed = capability.genericVirtualPassed,
                 capabilityGenericBehaviorValidated = capability.genericBehaviorValidated,
                capabilityGenericSelectNullPassed = capability.genericSelectNullPassed,
                capabilityInterfaceResult = capability.interfaceResult,
                capabilityDelegateResult = capability.delegateResult,
                capabilityGenericSelectResult = capability.genericSelectResult,
                capabilityGenericConstrainedResult = capability.genericConstrainedResult,
                capabilityGenericContainerResult = capability.genericContainerResult,
                capabilityNullableResult = capability.nullableResult,
                capabilityDelegateClosedInstanceResult = capability.delegateClosedInstanceResult,
                capabilityDelegateOpenInstanceResult = capability.delegateOpenInstanceResult,
                capabilityDelegateMulticastResult = capability.delegateMulticastResult,
                capabilityExceptionFinallyResult = capability.exceptionFinallyResult,
                capabilityValueNumber = capability.valueNumber,
                capabilityValueWide = capability.valueWide,
                capabilityBoxNumber = capability.boxNumber,
                capabilityBoxWide = capability.boxWide,
                capabilityRefOutResult = capability.refOutResult,
                capabilityAsyncResult = capability.asyncResult,
                capabilityIteratorResult = capability.iteratorResult,
                capabilityVirtualResult = capability.virtualResult,
                capabilityGenericVirtualResult = capability.genericVirtualResult,
                structuralExpected = structural.expected,
                structuralDispatchExpected = structuralDispatchExpected,
                structuralPassed = structural.passed,
                structuralError = structural.error,
                structuralExistingEntryResult = structural.existingEntryResult,
                structuralAddedReferenceTypeFound = structural.addedReferenceTypeFound,
                structuralAddedGenericTypeFound = structural.addedGenericTypeFound,
                structuralAddedNestedTypeFound = structural.addedNestedTypeFound,
                structuralNestedDeclaringTypeValidated = structural.nestedDeclaringTypeValidated,
                structuralAddedStaticMethodFound = structural.addedStaticMethodFound,
                structuralAddedInstanceMethodFound = structural.addedInstanceMethodFound,
                structuralAddedStaticFieldsFound = structural.addedStaticFieldsFound,
                structuralAddedStaticFieldDeclaringTypeValidated =
                    structural.addedStaticFieldDeclaringTypeValidated,
                structuralAddedStaticFieldReflectionValueValidated =
                    structural.addedStaticFieldReflectionValueValidated,
				structuralAddedInstanceFieldsFound = structural.addedInstanceFieldsFound,
				structuralAddedInstanceFieldDeclaringTypeValidated =
					structural.addedInstanceFieldDeclaringTypeValidated,
				structuralAddedInstanceFieldDefaultValueValidated =
					structural.addedInstanceFieldDefaultValueValidated,
				structuralAddedInstanceFieldReflectionValueValidated =
					structural.addedInstanceFieldReflectionValueValidated,
				structuralAddedInstanceFieldGcValidated =
					structural.addedInstanceFieldGcValidated,
				structuralRemovedMethodHidden = structural.removedMethodHidden,
				structuralRemovedMethodGuardValidated = structural.removedMethodGuardValidated,
				structuralRemovedFieldsHidden = structural.removedFieldsHidden,
				structuralRemovedFieldGuardValidated = structural.removedFieldGuardValidated,
				structuralFieldSignatureReplacementVisible =
					structural.fieldSignatureReplacementVisible,
				structuralFieldSignatureReplacementRoundTripValidated =
					structural.fieldSignatureReplacementRoundTripValidated,
				structuralLogicalPropertiesValidated = structural.logicalPropertiesValidated,
				structuralLogicalPropertyRoundTripValidated =
					structural.logicalPropertyRoundTripValidated,
				structuralLogicalEventsValidated = structural.logicalEventsValidated,
				structuralLogicalEventRoundTripValidated =
					structural.logicalEventRoundTripValidated,
				structuralLogicalEventAccessorsValidated =
					structural.logicalEventAccessorsValidated,
				structuralLogicalAddedEventAccessors = structural.logicalAddedEventAccessors,
				structuralLogicalEvolvedEventAccessors = structural.logicalEvolvedEventAccessors,
				structuralLogicalAddedEventAccessorTouchValue =
					structural.logicalAddedEventAccessorTouchValue,
				structuralLogicalEvolvedEventAccessorTouchValue =
					structural.logicalEvolvedEventAccessorTouchValue,
				structuralLogicalAddedEventTouchValue =
					structural.logicalAddedEventTouchValue,
				structuralLogicalEvolvedEventTouchValue =
					structural.logicalEvolvedEventTouchValue,
				structuralRemovedPropertyGuardValidated =
					structural.removedPropertyGuardValidated,
				structuralReplacedPropertyGuardValidated =
					structural.replacedPropertyGuardValidated,
				structuralRemovedEventGuardValidated = structural.removedEventGuardValidated,
				structuralReplacedEventGuardValidated = structural.replacedEventGuardValidated,
				structuralRemovedTypeHidden = structural.removedTypeHidden,
				structuralRemovedTypeEnumerationHidden = structural.removedTypeEnumerationHidden,
				structuralRemovedTypeGuardValidated = structural.removedTypeGuardValidated,
				structuralOldSignatureHidden = structural.oldSignatureHidden,
				structuralOldSignatureGuardValidated = structural.oldSignatureGuardValidated,
				structuralNewSignatureFound = structural.newSignatureFound,
                structuralAssemblyEnumerationValidated = structural.assemblyEnumerationValidated,
                structuralTypeAssemblyMatchesBase = structural.typeAssemblyMatchesBase,
                structuralAddedReferenceResult = structural.addedReferenceResult,
                structuralAddedGenericResult = structural.addedGenericResult,
                structuralAddedNestedResult = structural.addedNestedResult,
                structuralAddedStaticResult = structural.addedStaticResult,
                structuralAddedInstanceResult = structural.addedInstanceResult,
                structuralAddedStaticFieldDirectResult = structural.addedStaticFieldDirectResult,
                structuralAddedStaticFieldReflectionResult =
                    structural.addedStaticFieldReflectionResult,
				structuralAddedInstanceFieldDirectResult =
					structural.addedInstanceFieldDirectResult,
				structuralAddedInstanceFieldReflectionResult =
					structural.addedInstanceFieldReflectionResult,
				structuralCurrentMemberDirectResult = structural.currentMemberDirectResult,
				structuralNewSignatureResult = structural.newSignatureResult,
                plannedDheAssemblies = GetAssemblyNames(runtimePlan, runtimeIdentity.BaseId),
                loadedDheAssemblies = GetAssemblyNames(loadedAssemblies),
                plannedDifferentialAssemblies = selectedPlanAssemblies.Where(item =>
                        !string.Equals(item.executionMode, "interpreter-only", StringComparison.Ordinal))
                    .Select(item => item.assemblyName).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                plannedInterpreterOnlyAssemblies = selectedPlanAssemblies.Where(item =>
                        string.Equals(item.executionMode, "interpreter-only", StringComparison.Ordinal))
                    .Select(item => item.assemblyName).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                loadedInterpreterOnlyAssemblies = loadedAssemblies.Values.Where(item =>
                        string.Equals(item.plan.executionMode, "interpreter-only", StringComparison.Ordinal))
                    .Select(item => item.plan.assemblyName).OrderBy(name => name, StringComparer.Ordinal).ToArray(),
                target = GetArgument("-labTarget"),
                resourceUpdateManifestPresent = resourceUpdateManifestPresent,
                resourceUpdateValidated = initialized,
                selectedBaseId = runtimeIdentity.BaseId,
                selectedAotMetadataSetId = runtimeIdentity.AotMetadataSetId,
                selectedBaseMetaVersionSetSha256 = runtimeIdentity.BaseMetaVersionSetSha256,
                selectedPayloadVariantId = selectedPayloadVariant.variantId,
                selectedPayloadCurrentAssemblySetSha256 =
                    selectedPayloadVariant.currentAssemblySetSha256,
                changedMethodCount = changedMethodCount,
                expectedChangedMethodCount = changedMethodCount,
                dispatchProbeValidated = dispatchProbeValidated,
                noOpAotBehaviorValidated = noOpAotBehaviorValidated,
                changedProbeChanged = addChanged,
                unchangedProbeChanged = identityUnchangedChanged,
                dispatchProbeError = dispatchProbeValidated ? null : "DHE changed/unchanged dispatch assertions failed.",
                transactionStatus = transactionStatus,
                retryValidated = retryValidated,
                retryAssemblyName = changedMethodCount == 0 ? null : retryAssemblyName,
                retryFailure = retryValidated ? retryFailureCode.ToString() : null,
                assemblyValidations = loadedAssemblies.Values.OrderBy(item => item.plan.assemblyName, StringComparer.Ordinal)
                    .Select(item => new DheAssemblyValidation
                    {
                        assemblyName = item.plan.assemblyName,
                        executionMode = string.Equals(item.plan.executionMode, "interpreter-only",
                            StringComparison.Ordinal) ? "interpreter-only" : "dhe-differential",
                        currentSha256 = ToHex(item.currentHash),
                        baselineSha256 = ToHex(item.baselineHash),
                        mvCurrentSha256 = ToHex(item.mvCurrentHash),
                        mvBaselineSha256 = ToHex(item.mvBaselineHash),
                        hashValidated = true,
                        loadError = LoadImageErrorCode.OK.ToString(),
                    }).ToArray(),
                multiAssemblyValidated = changedMethodCount == 0
                    ? noOpMultiAssemblyValidated
                    : loadedAssemblies.Count >= 4 && metadataStressResult > 0 &&
                        metadataSecondaryReflectionResult == 103 && crossSecondaryReflectionResult == 103 &&
                        string.Equals(crossAssemblyResult, "derived:26:34", StringComparison.Ordinal),
                metadataStressResult = metadataStressResult,
                crossAssemblyResult = crossAssemblyResult,
                changedMethod = addChanged ? "interpreter" : "aot",
                unchangedMethod = identityUnchangedChanged ? "interpreter" : "aot",
				changedCallingUnchangedMethod = addViaStableChanged
					? stableChanged ? "interpreter + interpreter callee" : "interpreter + AOT callee"
					: "aot",
                changedMultiArgumentMethod = addPairChanged ? "interpreter" : "aot",
                changedInt64Method = wideChanged ? "interpreter" : "aot",
                changedVoidMethod = touchChanged ? "interpreter" : "aot",
                changedInstanceMethod = instanceAddChanged ? "interpreter" : "aot",
                unchangedInstanceMethod = instanceStableChanged ? "interpreter" : "aot",
				changedInstanceCallingUnchangedMethod = instanceAddViaStableChanged
					? instanceStableChanged ? "interpreter + interpreter callee" : "interpreter + AOT callee"
					: "aot",
                interpreterEntryCount = mainInterpreterEntryCount,
                aotBridgeCallCount = mainAotBridgeCallCount,
                aotEntryCount = mainAotEntryCount,
                mvValidated = true,
                currentHashValidated = currentHashValidated,
                 baselineHashValidated = baselineHashValidated,
                 snapshotHashValidated = snapshotHashValidated,
                 embeddedSnapshotHash = embeddedSnapshotHash,
                embeddedSnapshotHashValidated = embeddedSnapshotHashValidated,
                buildIdentityValidated = buildIdentityValidated,
                identityVersion = buildIdentity == null ? 0 : buildIdentity.identityVersion,
                aotSnapshotKind = buildIdentity == null ? string.Empty : buildIdentity.aotSnapshotKind,
                nativeGuardSourceSha256 = buildIdentity == null ? string.Empty : buildIdentity.nativeGuardSourceSha256,
                nativeManifestSha256 = buildIdentity == null ? string.Empty : buildIdentity.nativeManifestSha256,
                changedToken = addMethod.MetadataToken,
                unchangedToken = identityUnchangedMethod.MetadataToken,
                changedCallingUnchangedToken = addViaStableMethod.MetadataToken,
                changedMultiArgumentToken = addPairMethod.MetadataToken,
                changedInt64Token = wideMethod.MetadataToken,
                changedVoidToken = touchMethod.MetadataToken,
                changedInstanceToken = instanceAddMethod.MetadataToken,
                unchangedInstanceToken = instanceStableMethod.MetadataToken,
                changedInstanceCallingUnchangedToken = instanceAddViaStableMethod.MetadataToken,
            };
        }

        private static bool ValidateNoOpCapabilityDirect(CapabilityDirectRun result)
        {
            return string.IsNullOrEmpty(result.error) && result.interfaceResult == 7 &&
                result.delegateResult == 7 && result.genericSelectResult == 9 &&
                !result.genericSelectNullPassed && result.genericConstrainedResult == 7 &&
                result.genericVirtualResult == 7 && result.mutateValueResult.Number == 4 &&
                result.mutateValueResult.Wide == 5L && result.boxValueResult.Number == 3 &&
                result.boxValueResult.Wide == 4L && result.refOutResult == 5 && result.asyncResult == 4 &&
                result.iteratorResult == "4,5" && result.genericContainerResult == 8 &&
                result.nullableResult == 4 && result.delegateClosedInstanceResult == 9 &&
                result.delegateOpenInstanceResult == 9 && result.delegateMulticastResult == 19 &&
                result.exceptionFinallyResult == 4 && result.virtualResult == 4 &&
                result.unchangedVirtualResult == 9 && result.interpreterEntryCount == 0;
        }

        private static bool ValidateNoOpCapabilityReflection(CapabilityRun result)
        {
            bool noChangedMethods = !result.interfaceChanged && !result.delegateChanged &&
                !result.genericSelectChanged && !result.genericConstrainedChanged &&
                !result.mutateValueChanged && !result.boxValueChanged && !result.refOutValueChanged &&
                !result.asyncValueChanged && !result.iterateValueChanged &&
                !result.asyncStateMachineChanged && !result.iteratorStateMachineChanged &&
                !result.genericContainerChanged && !result.nullableChanged &&
                !result.delegateClosedInstanceChanged && !result.delegateOpenInstanceChanged &&
                !result.delegateMulticastChanged && !result.exceptionFinallyChanged &&
                !result.virtualChanged && !result.genericVirtualChanged;
            return string.IsNullOrEmpty(result.error) && noChangedMethods && result.interfaceResult == 7 &&
                result.delegateResult == 7 && result.genericSelectResult == 9 &&
                !result.genericSelectNullPassed && result.genericConstrainedResult == 7 &&
                result.genericVirtualResult == 7 && result.valueNumber == 4 && result.valueWide == 5L &&
                result.boxNumber == 3 && result.boxWide == 4L && result.refOutResult == 5 &&
                result.asyncResult == 4 && result.iteratorResult == "4,5" &&
                result.genericContainerResult == 8 && result.nullableResult == 4 &&
                result.delegateClosedInstanceResult == 9 && result.delegateOpenInstanceResult == 9 &&
                result.delegateMulticastResult == 19 && result.exceptionFinallyResult == 4 &&
                result.virtualResult == 4;
        }

        private static CapabilityDirectRun ExecuteCapabilityDirect(bool structuralExpected)
        {
            CapabilityDirectRun result = new CapabilityDirectRun();
            try
            {
                SmallValue value = new SmallValue(3, 4);
                result.interfaceResult = DheCapabilityCases.InterfaceCall(new IntOperationStruct(), 3);
                result.delegateResult = DheCapabilityCases.DelegateCall(new IntOperation(input => input * 2), 3);
                result.genericSelectResult = DheCapabilityCases.GenericSelect(7, 9);
                result.genericSelectNullPassed = DheCapabilityCases.GenericSelect<string>(null, "fallback") == null;
                result.genericConstrainedResult = DheCapabilityCases.GenericConstrained(new IntOperationStruct(), 3);
                result.genericVirtualResult = new GenericVirtualOperation<IntOperationStruct>()
                    .Apply(new IntOperationStruct(), 3);
                result.mutateValueResult = DheCapabilityCases.MutateValue(value);
                result.boxValueResult = (SmallValue)DheCapabilityCases.BoxValue(value);
                DheCapabilityCases.RefOutValue(ref value, out result.refOutResult);
                result.asyncResult = DheCapabilityCases.AsyncValue(3).GetAwaiter().GetResult();
                result.iteratorResult = string.Join(",", DheCapabilityCases.IterateValue(3));
                result.genericContainerResult = DheCapabilityCases.GenericContainerValue(3);
                result.nullableResult = DheCapabilityCases.NullableValue(new SmallValue(3, 4));
                result.delegateClosedInstanceResult = DheCapabilityCases.DelegateClosedInstance(3);
                result.delegateOpenInstanceResult = DheCapabilityCases.DelegateOpenInstance(3);
                result.delegateMulticastResult = DheCapabilityCases.DelegateMulticast(3);
                result.exceptionFinallyResult = DheCapabilityCases.ExceptionFinally(3);
                result.virtualResult = new VirtualOperationBase().Apply(3);
                // This unchanged override is an explicit AOT control call.
                result.unchangedVirtualResult = new VirtualOperationDerived().Apply(3);
                result.interpreterEntryCount = RuntimeApi.GetDifferentialInterpreterEntryCount();
                result.aotEntryCount = RuntimeApi.GetDifferentialAotEntryCount();
                result.passed = result.interfaceResult == 106 && result.delegateResult == 106 &&
                    result.genericSelectResult == 7 && result.genericSelectNullPassed &&
                    result.genericConstrainedResult == 106 &&
                    result.genericVirtualResult == 106 &&
                    result.mutateValueResult.Number == 103 && result.mutateValueResult.Wide == 1004L &&
                    result.boxValueResult.Number == 103 && result.boxValueResult.Wide == 4L &&
                    result.refOutResult == 203 && result.asyncResult == 103 && result.iteratorResult == "103,5" &&
                    result.genericContainerResult == (structuralExpected ? 1812 : 107) && result.nullableResult == 103 &&
                    result.delegateClosedInstanceResult == 108 && result.delegateOpenInstanceResult == 108 &&
                    result.delegateMulticastResult == 118 && result.exceptionFinallyResult == 103 &&
                    result.virtualResult == 103 && result.unchangedVirtualResult == 9 &&
                    result.interpreterEntryCount > 0 && result.aotEntryCount > 0;
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
            }
            return result;
        }

        private static int ExecuteSecondaryChanged(
            Dictionary<string, LoadedDheAssembly> loadedAssemblies,
            string assemblyName,
            string declaringTypeName)
        {
            if (!loadedAssemblies.TryGetValue(assemblyName, out LoadedDheAssembly loaded))
            {
                throw new InvalidDataException("DHE runtime plan omitted " + assemblyName + ".");
            }
            Type declaringType = loaded.assembly.GetType(declaringTypeName, true);
            MethodInfo method = declaringType.GetMethod("Changed", BindingFlags.Public | BindingFlags.Static);
            if (method == null) throw new MissingMethodException(declaringTypeName, "Changed");
            return Convert.ToInt32(method.Invoke(null, new object[] { 3 }));
        }

        private static StructuralRun ExecuteStructural(Assembly assembly, bool expected,
            int existingEntryResult)
        {
            StructuralRun result = new StructuralRun
            {
                expected = expected,
                existingEntryResult = existingEntryResult,
            };
            if (!expected)
            {
                result.passed = true;
                return result;
            }

            try
            {
                const string addedReferenceName =
                    "HybridCLR.Lab.ManagedCasesAot.DheAddedReferenceType";
                const string addedGenericName =
                    "HybridCLR.Lab.ManagedCasesAot.DheAddedGenericType`1";
                const string addedNestedName =
                    "HybridCLR.Lab.ManagedCasesAot.DheCapabilityCases+DheAddedNestedType";
				const string removedTypeName =
					"HybridCLR.Lab.ManagedCasesAot.DheRemovedReferenceType";
                Type addedReference = assembly.GetType(addedReferenceName, false);
                Type addedGeneric = assembly.GetType(addedGenericName, false);
                Type capability = assembly.GetType(typeof(DheCapabilityCases).FullName, true);
                Type addedNested = assembly.GetType(addedNestedName, false) ?? capability.GetNestedType(
                    "DheAddedNestedType", BindingFlags.Public | BindingFlags.NonPublic);
                Type calculator = assembly.GetType(typeof(DheDemoCalculator).FullName, true);
                MethodInfo addedStatic = capability.GetMethod("AddedStaticMethod",
                    BindingFlags.Public | BindingFlags.Static);
                MethodInfo addedInstance = calculator.GetMethod("AddedInstanceMethod",
                    BindingFlags.Public | BindingFlags.Instance);
                MethodInfo staticFieldRoundTrip = calculator.GetMethod("AddedStaticFieldRoundTrip",
                    BindingFlags.Public | BindingFlags.Static);
                MethodInfo readStaticFields = calculator.GetMethod("ReadAddedStaticFields",
                    BindingFlags.Public | BindingFlags.Static);
                FieldInfo addedStaticCounter = calculator.GetField("AddedStaticCounter",
                    BindingFlags.Public | BindingFlags.Static);
                FieldInfo addedStaticText = calculator.GetField("AddedStaticText",
                    BindingFlags.Public | BindingFlags.Static);
				MethodInfo instanceFieldRoundTrip = calculator.GetMethod("AddedInstanceFieldRoundTrip",
					BindingFlags.Public | BindingFlags.Instance);
				MethodInfo readInstanceFields = calculator.GetMethod("ReadAddedInstanceFields",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo addedInstanceCounter = calculator.GetField("AddedInstanceCounter",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo addedInstanceText = calculator.GetField("AddedInstanceText",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo addedInstancePayload = calculator.GetField("AddedInstancePayload",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo addedInstanceStruct = calculator.GetField("AddedInstanceStruct",
					BindingFlags.Public | BindingFlags.Instance);
				MethodInfo removedLegacy = calculator.GetMethod("RemovedLegacyMethod",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo removedInstanceField = calculator.GetField("RemovedInstanceCounter",
					BindingFlags.Public | BindingFlags.Instance);
				FieldInfo removedStaticField = calculator.GetField("RemovedStaticText",
					BindingFlags.Public | BindingFlags.Static);
				MethodInfo removedFieldReader = calculator.GetMethod("ReadRemovedFields",
					BindingFlags.Public | BindingFlags.Instance);
				MethodInfo evolvedFieldRoundTrip = calculator.GetMethod("EvolvedInstanceFieldRoundTrip",
					BindingFlags.Public | BindingFlags.Instance);
				MethodInfo readEvolvedField = calculator.GetMethod("ReadEvolvedInstanceField",
					BindingFlags.Public | BindingFlags.Instance);
				PropertyInfo removedProperty = calculator.GetProperty("RemovedProperty",
					BindingFlags.Public | BindingFlags.Instance);
				PropertyInfo addedProperty = calculator.GetProperty("AddedProperty",
					BindingFlags.Public | BindingFlags.Instance);
				PropertyInfo evolvedProperty = calculator.GetProperty("EvolvedProperty",
					BindingFlags.Public | BindingFlags.Instance);
				EventInfo removedEvent = calculator.GetEvent("RemovedEvent",
					BindingFlags.Public | BindingFlags.Instance);
				EventInfo addedEvent = calculator.GetEvent("AddedEvent",
					BindingFlags.Public | BindingFlags.Instance);
				EventInfo evolvedEvent = calculator.GetEvent("EvolvedEvent",
					BindingFlags.Public | BindingFlags.Instance);
				MethodInfo exerciseCurrentMembers = calculator.GetMethod("ExerciseCurrentMembers",
					BindingFlags.Public | BindingFlags.Instance);
				Type removedType = assembly.GetType(removedTypeName, false);
				MethodInfo oldSignature = calculator.GetMethod("SignatureMigrated",
					BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
				MethodInfo newSignature = calculator.GetMethod("SignatureMigrated",
					BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(long) }, null);

                result.addedReferenceTypeFound = addedReference != null;
                result.addedGenericTypeFound = addedGeneric != null;
                result.addedNestedTypeFound = addedNested != null;
                result.addedStaticMethodFound = addedStatic != null;
                result.addedInstanceMethodFound = addedInstance != null;
                result.addedStaticFieldsFound = addedStaticCounter != null && addedStaticText != null;
				result.addedInstanceFieldsFound = instanceFieldRoundTrip != null &&
					readInstanceFields != null && addedInstanceCounter != null &&
					addedInstanceText != null && addedInstancePayload != null &&
					addedInstanceStruct != null;
				result.removedMethodHidden = removedLegacy == null;
				result.removedFieldsHidden = removedStaticField == null && removedFieldReader == null &&
					!calculator.GetFields(BindingFlags.Public | BindingFlags.Instance).Any(field =>
						string.Equals(field.Name, "RemovedInstanceCounter", StringComparison.Ordinal) &&
						field.FieldType == typeof(int));
				result.fieldSignatureReplacementVisible = removedInstanceField != null &&
					removedInstanceField.FieldType == typeof(string) && evolvedFieldRoundTrip != null &&
					readEvolvedField != null;
				result.logicalPropertiesValidated = removedProperty == null && addedProperty != null &&
					addedProperty.PropertyType == typeof(int) && evolvedProperty != null &&
					evolvedProperty.PropertyType == typeof(string) &&
					ReferenceEquals(addedProperty.DeclaringType, calculator) &&
					ReferenceEquals(evolvedProperty.DeclaringType, calculator) &&
					!calculator.GetProperties(BindingFlags.Public | BindingFlags.Instance)
						.Any(property => string.Equals(property.Name, "RemovedProperty",
							StringComparison.Ordinal));
				result.logicalEventsValidated = removedEvent == null && addedEvent != null &&
					addedEvent.EventHandlerType == typeof(Action<int>) && evolvedEvent != null &&
					evolvedEvent.EventHandlerType == typeof(Action<string>) &&
					ReferenceEquals(addedEvent.DeclaringType, calculator) &&
					ReferenceEquals(evolvedEvent.DeclaringType, calculator) &&
					!calculator.GetEvents(BindingFlags.Public | BindingFlags.Instance)
						.Any(@event => string.Equals(@event.Name, "RemovedEvent",
							StringComparison.Ordinal));
				result.removedTypeHidden = removedType == null;
				result.oldSignatureHidden = oldSignature == null ||
					oldSignature.GetParameters().Length != 1 ||
					oldSignature.GetParameters()[0].ParameterType != typeof(int);
				result.newSignatureFound = newSignature != null;
                Type[] visibleTypes = assembly.GetTypes();
                result.assemblyEnumerationValidated = visibleTypes.Any(type =>
                        string.Equals(type.FullName, addedReferenceName, StringComparison.Ordinal)) &&
                    visibleTypes.Any(type =>
                        string.Equals(type.FullName, addedGenericName, StringComparison.Ordinal)) &&
                    visibleTypes.Any(type =>
                        string.Equals(type.FullName, addedNestedName, StringComparison.Ordinal));
				result.removedTypeEnumerationHidden = !visibleTypes.Any(type =>
					string.Equals(type.FullName, removedTypeName, StringComparison.Ordinal));

                if (addedReference == null)
                    addedReference = visibleTypes.FirstOrDefault(type =>
                        string.Equals(type.FullName, addedReferenceName, StringComparison.Ordinal));
                if (addedGeneric == null)
                    addedGeneric = visibleTypes.FirstOrDefault(type =>
                        string.Equals(type.FullName, addedGenericName, StringComparison.Ordinal));
                if (addedNested == null)
                    addedNested = visibleTypes.FirstOrDefault(type =>
                        string.Equals(type.FullName, addedNestedName, StringComparison.Ordinal));

                if (addedReference == null || addedGeneric == null || addedNested == null ||
                    addedStatic == null || addedInstance == null || staticFieldRoundTrip == null ||
					readStaticFields == null || addedStaticCounter == null || addedStaticText == null ||
					!result.addedInstanceFieldsFound || newSignature == null)
                {
                    result.error = "Current metadata additions are not visible through Base Assembly/Type reflection.";
                    return result;
                }

                result.typeAssemblyMatchesBase = ReferenceEquals(addedReference.Assembly, assembly) &&
                    ReferenceEquals(addedGeneric.Assembly, assembly) &&
                    ReferenceEquals(addedNested.Assembly, assembly);
                result.nestedDeclaringTypeValidated = ReferenceEquals(addedNested.DeclaringType, capability) &&
                    ReferenceEquals(capability.GetNestedType("DheAddedNestedType",
                        BindingFlags.Public | BindingFlags.NonPublic), addedNested);
                result.addedStaticFieldDeclaringTypeValidated =
                    ReferenceEquals(addedStaticCounter.DeclaringType, calculator) &&
                    ReferenceEquals(addedStaticText.DeclaringType, calculator);
				result.addedInstanceFieldDeclaringTypeValidated =
					ReferenceEquals(addedInstanceCounter.DeclaringType, calculator) &&
					ReferenceEquals(addedInstanceText.DeclaringType, calculator) &&
					ReferenceEquals(addedInstancePayload.DeclaringType, calculator) &&
					ReferenceEquals(addedInstanceStruct.DeclaringType, calculator);
                object referenceObject = Activator.CreateInstance(addedReference,
                    new object[] { 300 });
                MethodInfo apply = addedReference.GetMethod("Apply",
                    BindingFlags.Public | BindingFlags.Instance);
                if (apply == null)
                    throw new MissingMethodException(addedReferenceName, "Apply");
                result.addedReferenceResult = Convert.ToInt32(apply.Invoke(referenceObject,
                    new object[] { 3 }));

                Type closedGeneric = addedGeneric.MakeGenericType(typeof(int));
                object genericObject = Activator.CreateInstance(closedGeneric,
                    new object[] { result.addedReferenceResult });
                PropertyInfo valueProperty = closedGeneric.GetProperty("Value",
                    BindingFlags.Public | BindingFlags.Instance);
                if (valueProperty == null)
                    throw new MissingMemberException(addedGenericName, "Value");
                result.addedGenericResult = Convert.ToInt32(valueProperty.GetValue(genericObject));
                object nestedObject = Activator.CreateInstance(addedNested,
                    new object[] { 600 });
                MethodInfo nestedApply = addedNested.GetMethod("Apply",
                    BindingFlags.Public | BindingFlags.Instance);
                if (nestedApply == null)
                    throw new MissingMethodException(addedNestedName, "Apply");
                result.addedNestedResult = Convert.ToInt32(nestedApply.Invoke(nestedObject,
                    new object[] { 3 }));
                result.addedStaticResult = Convert.ToInt32(addedStatic.Invoke(null,
                    new object[] { 3 }));
				result.addedInstanceResult = Convert.ToInt32(addedInstance.Invoke(
					Activator.CreateInstance(calculator), new object[] { 3 }));
				result.newSignatureResult = Convert.ToInt32(newSignature.Invoke(
					Activator.CreateInstance(calculator), new object[] { 3L }));
				var legacyObject = new DheDemoCalculator();
#if HYBRIDCLR_DHE_BASE_PLAYER
				try
				{
					legacyObject.RemovedLegacyMethod(3);
				}
				catch (MissingMethodException)
				{
					result.removedMethodGuardValidated = true;
				}
				try
				{
					legacyObject.ReadRemovedFields();
				}
				catch (MissingMethodException)
				{
					result.removedFieldGuardValidated = true;
				}
#endif
				if (result.fieldSignatureReplacementVisible)
				{
					object evolvedObject = Activator.CreateInstance(calculator);
					int directLength = Convert.ToInt32(evolvedFieldRoundTrip.Invoke(evolvedObject,
						new object[] { 3 }));
					bool directValue = string.Equals(Convert.ToString(
						removedInstanceField.GetValue(evolvedObject)), "field-3", StringComparison.Ordinal);
					removedInstanceField.SetValue(evolvedObject, "dhe");
					int reflectedLength = Convert.ToInt32(readEvolvedField.Invoke(evolvedObject, null));
					result.fieldSignatureReplacementRoundTripValidated = directLength == 7 &&
						directValue && reflectedLength == 3;
				}
				if (result.logicalPropertiesValidated && exerciseCurrentMembers != null)
				{
					object memberObject = Activator.CreateInstance(calculator);
					addedProperty.SetValue(memberObject, 3);
					result.logicalPropertyRoundTripValidated =
						Convert.ToInt32(addedProperty.GetValue(memberObject)) == 1403 &&
						string.Equals(Convert.ToString(evolvedProperty.GetValue(memberObject)),
							"property-1500", StringComparison.Ordinal);
					result.currentMemberDirectResult = Convert.ToInt32(
						exerciseCurrentMembers.Invoke(memberObject, new object[] { 3 }));
				}
				if (result.logicalEventsValidated)
				{
					object memberObject = Activator.CreateInstance(calculator);
					MethodInfo addedAddMethod = addedEvent.GetAddMethod();
					MethodInfo addedRemoveMethod = addedEvent.GetRemoveMethod();
					MethodInfo evolvedAddMethod = evolvedEvent.GetAddMethod();
					MethodInfo evolvedRemoveMethod = evolvedEvent.GetRemoveMethod();
					result.logicalEventAccessorsValidated = addedAddMethod != null &&
						addedRemoveMethod != null && evolvedAddMethod != null &&
						evolvedRemoveMethod != null &&
						string.Equals(addedAddMethod.Name, "add_AddedEvent", StringComparison.Ordinal) &&
						string.Equals(addedRemoveMethod.Name, "remove_AddedEvent", StringComparison.Ordinal) &&
						string.Equals(evolvedAddMethod.Name, "add_EvolvedEvent", StringComparison.Ordinal) &&
						string.Equals(evolvedRemoveMethod.Name, "remove_EvolvedEvent", StringComparison.Ordinal);
					result.logicalAddedEventAccessors = (addedAddMethod?.Name ?? "null") + "/" +
						(addedRemoveMethod?.Name ?? "null");
					result.logicalEvolvedEventAccessors = (evolvedAddMethod?.Name ?? "null") + "/" +
						(evolvedRemoveMethod?.Name ?? "null");
					DheDemoCalculator.TouchValue = 0;
					Action<int> addedHandler = _ => { };
					if (result.logicalEventAccessorsValidated)
					{
						addedAddMethod.Invoke(memberObject, new object[] { addedHandler });
						addedRemoveMethod.Invoke(memberObject, new object[] { addedHandler });
						result.logicalAddedEventAccessorTouchValue = DheDemoCalculator.TouchValue;
					}
					DheDemoCalculator.TouchValue = 0;
					addedEvent.AddEventHandler(memberObject, addedHandler);
					addedEvent.RemoveEventHandler(memberObject, addedHandler);
					result.logicalAddedEventTouchValue = DheDemoCalculator.TouchValue;
					bool addedRoundTrip = result.logicalAddedEventTouchValue == 1760;
					DheDemoCalculator.TouchValue = 0;
					Action<string> evolvedHandler = _ => { };
					if (result.logicalEventAccessorsValidated)
					{
						evolvedAddMethod.Invoke(memberObject, new object[] { evolvedHandler });
						evolvedRemoveMethod.Invoke(memberObject, new object[] { evolvedHandler });
						result.logicalEvolvedEventAccessorTouchValue = DheDemoCalculator.TouchValue;
					}
					DheDemoCalculator.TouchValue = 0;
					evolvedEvent.AddEventHandler(memberObject, evolvedHandler);
					evolvedEvent.RemoveEventHandler(memberObject, evolvedHandler);
					result.logicalEvolvedEventTouchValue = DheDemoCalculator.TouchValue;
					result.logicalEventRoundTripValidated = addedRoundTrip &&
						result.logicalEvolvedEventTouchValue == 1870;
				}
#if HYBRIDCLR_DHE_BASE_PLAYER
				try
				{
					_ = legacyObject.RemovedProperty;
				}
				catch (MissingMethodException)
				{
					result.removedPropertyGuardValidated = true;
				}
				try
				{
					_ = legacyObject.EvolvedProperty;
				}
				catch (MissingMethodException)
				{
					result.replacedPropertyGuardValidated = true;
				}
				try
				{
					Action<int> removedHandler = _ => { };
					legacyObject.RemovedEvent += removedHandler;
				}
				catch (MissingMethodException)
				{
					result.removedEventGuardValidated = true;
				}
				try
				{
					Action<int> replacedHandler = _ => { };
					legacyObject.EvolvedEvent += replacedHandler;
				}
				catch (MissingMethodException)
				{
					result.replacedEventGuardValidated = true;
				}
				try
				{
					_ = new DheRemovedReferenceType(3);
				}
				catch (MissingMethodException)
				{
					result.removedTypeGuardValidated = true;
				}
				try
				{
					legacyObject.SignatureMigrated(3);
				}
				catch (MissingMethodException)
				{
					result.oldSignatureGuardValidated = true;
				}
#endif
				object instanceFieldObject = Activator.CreateInstance(calculator);
				object defaultStruct = addedInstanceStruct.GetValue(instanceFieldObject);
				result.addedInstanceFieldDefaultValueValidated =
					Convert.ToInt32(addedInstanceCounter.GetValue(instanceFieldObject)) == 0 &&
					addedInstanceText.GetValue(instanceFieldObject) == null &&
					addedInstancePayload.GetValue(instanceFieldObject) == null &&
					defaultStruct is SmallValue initialValue && initialValue.Number == 0 &&
					initialValue.Wide == 0;
				result.addedInstanceFieldDirectResult = Convert.ToInt32(
					instanceFieldRoundTrip.Invoke(instanceFieldObject, new object[] { 3 }));
				object directPayload = addedInstancePayload.GetValue(instanceFieldObject);
				var directPayloadWeak = new WeakReference(directPayload);
				directPayload = null;
				GC.Collect();
				GC.WaitForPendingFinalizers();
				GC.Collect();
				result.addedInstanceFieldGcValidated = directPayloadWeak.IsAlive &&
					addedInstancePayload.GetValue(instanceFieldObject) != null;
				object reflectedPayload = new object();
				addedInstanceCounter.SetValue(instanceFieldObject, 1200);
				addedInstanceText.SetValue(instanceFieldObject, "dhe");
				addedInstancePayload.SetValue(instanceFieldObject, reflectedPayload);
				addedInstanceStruct.SetValue(instanceFieldObject, new SmallValue(4, 5));
				result.addedInstanceFieldReflectionResult = Convert.ToInt32(
					readInstanceFields.Invoke(instanceFieldObject, null));
				result.addedInstanceFieldReflectionValueValidated =
					Convert.ToInt32(addedInstanceCounter.GetValue(instanceFieldObject)) == 1200 &&
					string.Equals(Convert.ToString(addedInstanceText.GetValue(instanceFieldObject)),
						"dhe", StringComparison.Ordinal) &&
					ReferenceEquals(addedInstancePayload.GetValue(instanceFieldObject), reflectedPayload) &&
					addedInstanceStruct.GetValue(instanceFieldObject) is SmallValue reflectedValue &&
					reflectedValue.Number == 4 && reflectedValue.Wide == 5;
                result.addedStaticFieldDirectResult = Convert.ToInt32(
                    staticFieldRoundTrip.Invoke(null, new object[] { 3 }));
                addedStaticCounter.SetValue(null, 1200);
                addedStaticText.SetValue(null, "dhe");
                result.addedStaticFieldReflectionResult = Convert.ToInt32(
                    readStaticFields.Invoke(null, null));
                result.addedStaticFieldReflectionValueValidated =
                    Convert.ToInt32(addedStaticCounter.GetValue(null)) == 1200 &&
                    string.Equals(Convert.ToString(addedStaticText.GetValue(null)), "dhe",
                        StringComparison.Ordinal);
                result.passed = result.existingEntryResult == 1812 &&
                    result.addedReferenceResult == 303 && result.addedGenericResult == 303 &&
                    result.addedNestedResult == 603 && result.addedStaticResult == 403 &&
                    result.addedInstanceResult == 503 && result.assemblyEnumerationValidated &&
                    result.typeAssemblyMatchesBase && result.nestedDeclaringTypeValidated &&
                    result.addedStaticFieldsFound && result.addedStaticFieldDeclaringTypeValidated &&
                    result.addedStaticFieldDirectResult == 906 &&
                    result.addedStaticFieldReflectionResult == 1203 &&
					result.addedStaticFieldReflectionValueValidated &&
					result.addedInstanceFieldsFound &&
					result.addedInstanceFieldDeclaringTypeValidated &&
					result.addedInstanceFieldDefaultValueValidated &&
					result.addedInstanceFieldDirectResult == 1046 &&
					result.addedInstanceFieldReflectionResult == 1212 &&
					result.addedInstanceFieldReflectionValueValidated &&
					result.addedInstanceFieldGcValidated && result.removedMethodHidden &&
					result.removedMethodGuardValidated && result.removedFieldsHidden &&
					result.removedFieldGuardValidated && result.removedTypeHidden &&
					result.fieldSignatureReplacementVisible &&
					result.fieldSignatureReplacementRoundTripValidated &&
					result.logicalPropertiesValidated &&
					result.logicalPropertyRoundTripValidated &&
					result.logicalEventsValidated && result.logicalEventRoundTripValidated &&
					result.removedPropertyGuardValidated &&
					result.replacedPropertyGuardValidated &&
					result.removedEventGuardValidated && result.replacedEventGuardValidated &&
					result.currentMemberDirectResult == 3163 &&
					result.removedTypeEnumerationHidden && result.removedTypeGuardValidated &&
					result.oldSignatureHidden &&
					result.oldSignatureGuardValidated && result.newSignatureFound &&
					result.newSignatureResult == 1303;
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
            }
            return result;
        }

        private static CapabilityRun ExecuteCapabilityReflection(bool structuralExpected)
        {
            CapabilityRun result = new CapabilityRun();
            try
            {
                Type capabilityType = typeof(DheCapabilityCases);
                MethodInfo interfaceCall = capabilityType.GetMethod("InterfaceCall", BindingFlags.Public | BindingFlags.Static);
                MethodInfo delegateCall = capabilityType.GetMethod("DelegateCall", BindingFlags.Public | BindingFlags.Static);
                MethodInfo genericSelect = capabilityType.GetMethod("GenericSelect", BindingFlags.Public | BindingFlags.Static);
                MethodInfo genericConstrained = capabilityType.GetMethod("GenericConstrained", BindingFlags.Public | BindingFlags.Static);
                MethodInfo mutateValue = capabilityType.GetMethod("MutateValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo boxValue = capabilityType.GetMethod("BoxValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo refOutValue = capabilityType.GetMethod("RefOutValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo asyncValue = capabilityType.GetMethod("AsyncValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo iterateValue = capabilityType.GetMethod("IterateValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo genericContainerValue = capabilityType.GetMethod("GenericContainerValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo nullableValue = capabilityType.GetMethod("NullableValue", BindingFlags.Public | BindingFlags.Static);
                MethodInfo delegateClosedInstance = capabilityType.GetMethod("DelegateClosedInstance", BindingFlags.Public | BindingFlags.Static);
                MethodInfo delegateOpenInstance = capabilityType.GetMethod("DelegateOpenInstance", BindingFlags.Public | BindingFlags.Static);
                MethodInfo delegateMulticast = capabilityType.GetMethod("DelegateMulticast", BindingFlags.Public | BindingFlags.Static);
                MethodInfo exceptionFinally = capabilityType.GetMethod("ExceptionFinally", BindingFlags.Public | BindingFlags.Static);
                MethodInfo virtualApply = typeof(VirtualOperationBase).GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
                MethodInfo genericVirtualApply = typeof(GenericVirtualOperation<IntOperationStruct>).GetMethod("Apply", BindingFlags.Public | BindingFlags.Instance);
                Type asyncStateMachineType = capabilityType.GetNestedType("<AsyncValue>d__7", BindingFlags.NonPublic);
                Type iteratorStateMachineType = capabilityType.GetNestedType("<IterateValue>d__8", BindingFlags.NonPublic);
                MethodInfo asyncMoveNext = asyncStateMachineType?.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo iteratorMoveNext = iteratorStateMachineType?.GetMethod("MoveNext", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (interfaceCall == null || delegateCall == null || genericSelect == null || genericConstrained == null ||
                    mutateValue == null || boxValue == null || refOutValue == null || asyncValue == null || iterateValue == null ||
                    genericContainerValue == null || nullableValue == null || delegateClosedInstance == null ||
                    delegateOpenInstance == null ||
                    delegateMulticast == null || exceptionFinally == null || asyncMoveNext == null || iteratorMoveNext == null ||
                    virtualApply == null || genericVirtualApply == null)
                {
                    throw new MissingMethodException(capabilityType.FullName);
                }

                result.interfaceChanged = RuntimeApi.IsDifferentialMethodChanged(interfaceCall);
                result.delegateChanged = RuntimeApi.IsDifferentialMethodChanged(delegateCall);
                result.genericSelectChanged = RuntimeApi.IsDifferentialMethodChanged(genericSelect);
                result.genericConstrainedChanged = RuntimeApi.IsDifferentialMethodChanged(genericConstrained);
                result.mutateValueChanged = RuntimeApi.IsDifferentialMethodChanged(mutateValue);
                result.boxValueChanged = RuntimeApi.IsDifferentialMethodChanged(boxValue);
                result.refOutValueChanged = RuntimeApi.IsDifferentialMethodChanged(refOutValue);
                result.asyncValueChanged = RuntimeApi.IsDifferentialMethodChanged(asyncValue);
                result.iterateValueChanged = RuntimeApi.IsDifferentialMethodChanged(iterateValue);
                result.asyncStateMachineChanged = RuntimeApi.IsDifferentialMethodChanged(asyncMoveNext);
                result.iteratorStateMachineChanged = RuntimeApi.IsDifferentialMethodChanged(iteratorMoveNext);
                result.genericContainerChanged = RuntimeApi.IsDifferentialMethodChanged(genericContainerValue);
                result.nullableChanged = RuntimeApi.IsDifferentialMethodChanged(nullableValue);
                result.delegateClosedInstanceChanged = RuntimeApi.IsDifferentialMethodChanged(delegateClosedInstance);
                result.delegateOpenInstanceChanged = RuntimeApi.IsDifferentialMethodChanged(delegateOpenInstance);
                result.delegateMulticastChanged = RuntimeApi.IsDifferentialMethodChanged(delegateMulticast);
                result.exceptionFinallyChanged = RuntimeApi.IsDifferentialMethodChanged(exceptionFinally);
                result.virtualChanged = RuntimeApi.IsDifferentialMethodChanged(virtualApply);
                result.genericVirtualChanged = RuntimeApi.IsDifferentialMethodChanged(genericVirtualApply);

                void Capture(string name, Action action)
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        if (!string.IsNullOrEmpty(result.error))
                        {
                            result.error += "\n";
                        }
                        result.error += name + ": " + exception;
                    }
                }

                Capture("InterfaceCall", () =>
                    result.interfaceResult = (int)interfaceCall.Invoke(null, new object[] { new IntOperationStruct(), 3 }));
                Capture("DelegateCall", () =>
                    result.delegateResult = (int)delegateCall.Invoke(null, new object[] { new IntOperation(value => value * 2), 3 }));
                Capture("GenericSelect", () =>
                    result.genericSelectResult = (int)genericSelect.MakeGenericMethod(typeof(int))
                        .Invoke(null, new object[] { 7, 9 }));
                Capture("GenericSelectNull", () =>
                    result.genericSelectNullPassed = genericSelect.MakeGenericMethod(typeof(string))
                        .Invoke(null, new object[] { null, "fallback" }) == null);
                Capture("GenericConstrained", () =>
                    result.genericConstrainedResult = (int)genericConstrained.MakeGenericMethod(typeof(IntOperationStruct))
                        .Invoke(null, new object[] { new IntOperationStruct(), 3 }));
                Capture("GenericContainerValue", () =>
                    result.genericContainerResult = (int)genericContainerValue.Invoke(null, new object[] { 3 }));
                Capture("NullableValue", () =>
                    result.nullableResult = (int)nullableValue.Invoke(null, new object[] { new SmallValue(3, 4) }));
                Capture("DelegateClosedInstance", () =>
                    result.delegateClosedInstanceResult = (int)delegateClosedInstance.Invoke(null, new object[] { 3 }));
                Capture("DelegateOpenInstance", () =>
                    result.delegateOpenInstanceResult = (int)delegateOpenInstance.Invoke(null, new object[] { 3 }));
                Capture("DelegateMulticast", () =>
                    result.delegateMulticastResult = (int)delegateMulticast.Invoke(null, new object[] { 3 }));
                Capture("ExceptionFinally", () =>
                    result.exceptionFinallyResult = (int)exceptionFinally.Invoke(null, new object[] { 3 }));

                SmallValue value = new SmallValue(3, 4);
                Capture("MutateValue", () =>
                {
                    SmallValue mutated = (SmallValue)mutateValue.Invoke(null, new object[] { value });
                    result.valueNumber = mutated.Number;
                    result.valueWide = mutated.Wide;
                });
                Capture("BoxValue", () =>
                {
                    SmallValue boxed = (SmallValue)boxValue.Invoke(null, new object[] { value });
                    result.boxNumber = boxed.Number;
                    result.boxWide = boxed.Wide;
                });

                Capture("RefOutValue", () =>
                {
                    object[] refOutArgs = { value, null };
                    refOutValue.Invoke(null, refOutArgs);
                    result.refOutResult = (int)refOutArgs[1];
                });

                Capture("AsyncValue", () =>
                {
                    Task<int> task = (Task<int>)asyncValue.Invoke(null, new object[] { 3 });
                    result.asyncResult = task.GetAwaiter().GetResult();
                });
                Capture("IterateValue", () =>
                {
                    IEnumerable<int> sequence = (IEnumerable<int>)iterateValue.Invoke(null, new object[] { 3 });
                    List<int> values = new List<int>(sequence);
                    result.iteratorResult = string.Join(",", values);
                });

                Capture("VirtualApply", () =>
                    result.virtualResult = (int)virtualApply.Invoke(new VirtualOperationBase(), new object[] { 3 }));
                Capture("GenericVirtualApply", () =>
                    result.genericVirtualResult = (int)genericVirtualApply.Invoke(
                        new GenericVirtualOperation<IntOperationStruct>(), new object[] { new IntOperationStruct(), 3 }));
                result.genericVirtualPassed = result.genericVirtualChanged && result.genericVirtualResult == 106;
                result.genericBehaviorValidated = result.genericSelectResult == 7 &&
                    result.genericSelectNullPassed &&
                    result.genericConstrainedResult == 106 && result.genericVirtualResult == 106;
                result.passed = result.interfaceResult == 106 && result.delegateResult == 106 &&
                    result.valueNumber == 103 && result.valueWide == 1004L && result.refOutResult == 203 &&
                    result.boxNumber == 103 && result.boxWide == 4L && result.boxValueChanged &&
                    result.asyncResult == 103 && result.iteratorResult == "103,5" &&
                    result.genericContainerResult == (structuralExpected ? 1812 : 107) && result.nullableResult == 103 &&
                    result.delegateClosedInstanceResult == 108 && result.delegateOpenInstanceResult == 108 &&
                    result.delegateMulticastResult == 118 &&
                    result.exceptionFinallyResult == 103 &&
                    result.interfaceChanged && result.delegateChanged && result.genericSelectChanged &&
                    result.genericConstrainedChanged && result.mutateValueChanged && result.boxValueChanged &&
                    result.refOutValueChanged &&
                    result.genericContainerChanged && result.nullableChanged &&
                    result.delegateClosedInstanceChanged && result.delegateOpenInstanceChanged &&
                    result.delegateMulticastChanged && result.exceptionFinallyChanged && result.virtualChanged &&
                    result.asyncStateMachineChanged && result.iteratorStateMachineChanged &&
                    result.virtualResult == 103 && result.genericVirtualPassed && result.genericBehaviorValidated;
            }
            catch (Exception exception)
            {
                result.error = exception.ToString();
            }
            return result;
        }

        private static long ExecuteMetadataStress(Dictionary<string, LoadedDheAssembly> loadedAssemblies)
        {
            if (!loadedAssemblies.TryGetValue("HybridCLR.MetadataStress", out LoadedDheAssembly loaded))
            {
                throw new InvalidDataException("DHE runtime plan omitted HybridCLR.MetadataStress.");
            }
            Type entryType = loaded.assembly.GetType("HybridCLR.Lab.MetadataStress.MetadataStressEntry", true);
            MethodInfo touch = entryType.GetMethod("Touch", BindingFlags.Public | BindingFlags.Static);
            if (touch == null) throw new MissingMethodException(entryType.FullName, "Touch");
            return Convert.ToInt64(touch.Invoke(null, null));
        }

        private static string ExecuteCrossAssembly(Dictionary<string, LoadedDheAssembly> loadedAssemblies)
        {
            if (!loadedAssemblies.TryGetValue("HybridCLR.CrossAssemblyDerived", out LoadedDheAssembly loaded))
            {
                throw new InvalidDataException("DHE runtime plan omitted HybridCLR.CrossAssemblyDerived.");
            }
            Type probeType = loaded.assembly.GetType("HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe", true);
            MethodInfo run = probeType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static);
            if (run == null) throw new MissingMethodException(probeType.FullName, "Run");
            return (string)run.Invoke(null, null);
        }

        private static string[] GetAssemblyNames(DheRuntimePlanData plan, string baseId)
        {
            return SelectRuntimeAssemblies(plan, baseId)
                .Select(item => item.assemblyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        private static DheAssemblyPlanData[] SelectRuntimeAssemblies(DheRuntimePlanData plan,
            string baseId)
        {
            return SelectRuntimePayloadVariant(plan, baseId).assemblies;
        }

        private static DheRuntimePayloadVariantData SelectRuntimePayloadVariant(
            DheRuntimePlanData plan, string baseId)
        {
            if (plan.payloadVariants == null || plan.payloadVariants.Length == 0)
            {
                if (plan.assemblies == null || plan.assemblies.Length == 0 ||
                    string.IsNullOrWhiteSpace(plan.currentAssemblySetSha256))
                    throw new InvalidDataException("DHE runtime plan has no default payload variant.");
                return new DheRuntimePayloadVariantData
                {
                    variantId = "default",
                    currentAssemblySetSha256 = plan.currentAssemblySetSha256,
                    assemblies = plan.assemblies,
                };
            }
            DheRuntimeBaseSelectionData[] selections = plan.baseSelections ??
                Array.Empty<DheRuntimeBaseSelectionData>();
            DheRuntimeBaseSelectionData[] matches = selections.Where(item => item != null &&
                string.Equals(item.baseId, baseId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("DHE runtime plan has no unique payload variant selection for this Base.");
            string variantId = string.IsNullOrWhiteSpace(matches[0].payloadVariantId)
                ? "default" : matches[0].payloadVariantId;
            DheRuntimePayloadVariantData[] variants = plan.payloadVariants.Where(item => item != null &&
                string.Equals(item.variantId, variantId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (variants.Length != 1 || variants[0].assemblies == null ||
                variants[0].assemblies.Length == 0 ||
                !string.Equals(matches[0].currentAssemblySetSha256,
                    variants[0].currentAssemblySetSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DHE runtime plan payload variant is not bound to this Base.");
            return variants[0];
        }

        private static void ApplyRuntimeAssemblyModes(DheRuntimePlanData plan, string baseId,
            DheAssemblyPlanData[] assemblies)
        {
            DheRuntimeBaseSelectionData[] selections = plan.baseSelections ??
                Array.Empty<DheRuntimeBaseSelectionData>();
            if (selections.Length == 0)
            {
                return;
            }
            DheRuntimeBaseSelectionData[] matches = selections.Where(item => item != null &&
                string.Equals(item.baseId, baseId, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length != 1)
                throw new InvalidDataException("DHE runtime plan has no unique assembly mode selection for this Base.");
            DheRuntimeAssemblyModeData[] modes = matches[0].assemblyModes ??
                Array.Empty<DheRuntimeAssemblyModeData>();
            if (modes.Length == 0)
                return;
            var modesByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DheRuntimeAssemblyModeData mode in modes)
            {
                if (mode == null || string.IsNullOrWhiteSpace(mode.assemblyName) ||
                    string.IsNullOrWhiteSpace(mode.executionMode) ||
                    !modesByName.TryAdd(mode.assemblyName, mode.executionMode))
                    throw new InvalidDataException("DHE runtime plan contains an invalid assembly mode.");
            }
            if (modesByName.Count != assemblies.Length)
                throw new InvalidDataException("DHE runtime plan assembly modes do not match the payload.");
            foreach (DheAssemblyPlanData assembly in assemblies)
            {
                if (assembly == null || !modesByName.TryGetValue(assembly.assemblyName,
                        out string mode) || (mode != "dhe-differential" && mode != "interpreter-only"))
                    throw new InvalidDataException("DHE runtime plan assembly mode is invalid: " +
                        assembly?.assemblyName);
                assembly.executionMode = mode;
            }
        }

        private static string[] GetAssemblyNames(Dictionary<string, LoadedDheAssembly> assemblies)
        {
            return assemblies.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        private sealed class StreamingAssetsProvider : IDheRuntimeAssetProvider
        {
            public bool Exists(string assetPath)
            {
                try
                {
                    return DheStreamingAssetReader.Exists(assetPath);
                }
                catch
                {
                    return false;
                }
            }

            public string LoadText(string assetPath)
            {
                return System.Text.Encoding.UTF8.GetString(LoadBytes(assetPath))
                    .TrimStart('\uFEFF');
            }

            public byte[] LoadBytes(string assetPath)
            {
                return DheStreamingAssetReader.Read(assetPath);
            }
        }

        private static byte[] Sha256(byte[] bytes)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return sha.ComputeHash(bytes);
            }
        }

        private static bool IsSha256(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length != 64)
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!Uri.IsHexDigit(character))
                {
                    return false;
                }
            }
            return true;
        }

        private static byte[] Slice(byte[] bytes, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length)
            {
                throw new InvalidDataException("Invalid DHE mv digest offsets.");
            }
            byte[] result = new byte[length];
            Buffer.BlockCopy(bytes, offset, result, 0, length);
            return result;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
        }

        private static MetaVersionInfo ParseMetaVersion(byte[] bytes, string expectedAssemblyName)
        {
            if (bytes == null || bytes.Length < 60 ||
                !string.Equals(System.Text.Encoding.ASCII.GetString(bytes, 0, 8),
                    "DHEMETA1", StringComparison.Ordinal) || BitConverter.ToUInt32(bytes, 8) != 1)
                throw new InvalidDataException("DHE MetaVersion header is invalid for " +
                    expectedAssemblyName + ".");
            int nameSize = checked((int)BitConverter.ToUInt32(bytes, 16));
            int typeCount = checked((int)BitConverter.ToUInt32(bytes, 20));
            int methodCount = checked((int)BitConverter.ToUInt32(bytes, 24));
            long expectedSize = 60L + nameSize + 72L * typeCount + 104L * methodCount;
            if (nameSize <= 0 || expectedSize != bytes.Length)
                throw new InvalidDataException("DHE MetaVersion size is invalid for " +
                    expectedAssemblyName + ".");
            string assemblyName = System.Text.Encoding.UTF8.GetString(bytes, 60, nameSize);
            if (!string.Equals(assemblyName, expectedAssemblyName, StringComparison.Ordinal))
                throw new InvalidDataException("DHE MetaVersion assembly identity mismatch: " +
                    assemblyName + "/" + expectedAssemblyName + ".");
            var types = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int typeOffset = checked(60 + nameSize);
            for (int index = 0; index < typeCount; index++)
            {
                int offset = checked(typeOffset + index * 72);
                string stableId = ToHex(Slice(bytes, offset, 32));
                string version = ToHex(Slice(bytes, offset + 32, 32));
                if (types.ContainsKey(stableId))
                    throw new InvalidDataException("DHE MetaVersion has a duplicate type stable ID.");
                types.Add(stableId, version);
            }
            var methods = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            int methodOffset = checked(60 + nameSize + 72 * typeCount);
            for (int index = 0; index < methodCount; index++)
            {
                int offset = checked(methodOffset + index * 104);
                string stableId = ToHex(Slice(bytes, offset, 32));
                string version = ToHex(Slice(bytes, offset + 32, 32));
                if (methods.ContainsKey(stableId))
                    throw new InvalidDataException("DHE MetaVersion has a duplicate method stable ID.");
                methods.Add(stableId, version);
            }
            return new MetaVersionInfo
            {
                assemblyHash = Slice(bytes, 28, 32),
                types = types,
                methods = methods,
            };
        }

        private static int CountAddedTypes(MetaVersionInfo baseline,
            MetaVersionInfo current) => current.types.Keys.Count(id =>
                !baseline.types.ContainsKey(id));

        private static int CountAddedMethods(MetaVersionInfo baseline,
            MetaVersionInfo current) => current.methods.Keys.Count(id =>
                !baseline.methods.ContainsKey(id));

        private static int CountRemovedTypes(MetaVersionInfo baseline,
            MetaVersionInfo current) => baseline.types.Keys.Count(id =>
                !current.types.ContainsKey(id));

        private static int CountRemovedMethods(MetaVersionInfo baseline,
            MetaVersionInfo current) => baseline.methods.Keys.Count(id =>
                !current.methods.ContainsKey(id));

        private static int CountChangedTypes(MetaVersionInfo baseline,
            MetaVersionInfo current) => baseline.types.Count(item =>
                current.types.TryGetValue(item.Key, out string version) &&
                !string.Equals(item.Value, version, StringComparison.OrdinalIgnoreCase));

        private static bool HasStructuralChanges(MetaVersionInfo baseline,
            MetaVersionInfo current) =>
            CountAddedTypes(baseline, current) > 0 ||
            CountRemovedTypes(baseline, current) > 0 ||
            CountChangedTypes(baseline, current) > 0 ||
            CountAddedMethods(baseline, current) > 0 ||
            CountRemovedMethods(baseline, current) > 0;

        private static bool HasStructuralAdditions(MetaVersionInfo baseline,
            MetaVersionInfo current) =>
            CountAddedTypes(baseline, current) > 0 ||
            CountAddedMethods(baseline, current) > 0;

        private static int CountChangedMethods(MetaVersionInfo baseline,
            MetaVersionInfo current)
        {
            int changed = baseline.methods.Count(item => !current.methods.TryGetValue(item.Key,
                out string version) || !string.Equals(item.Value, version,
                StringComparison.OrdinalIgnoreCase));
            return checked(changed + current.methods.Keys.Count(id => !baseline.methods.ContainsKey(id)));
        }

        private static string ToHex(byte[] bytes)
        {
            return BitConverter.ToString(bytes).Replace("-", string.Empty);
        }

        [Serializable]
        private sealed class DheRuntimePlanData
        {
            public int schemaVersion;
            public string format;
            public string selection;
            public string currentAssemblySetSha256;
            public DheAssemblyPlanData[] assemblies;
            public DheRuntimePayloadVariantData[] payloadVariants;
            public DheRuntimeBaseSelectionData[] baseSelections;
        }

        [Serializable]
        private sealed class DheRuntimePayloadVariantData
        {
            public string variantId;
            public string currentAssemblySetSha256;
            public DheAssemblyPlanData[] assemblies;
        }

        [Serializable]
        private sealed class DheRuntimeBaseSelectionData
        {
            public string baseId;
            public string payloadVariantId;
            public string currentAssemblySetSha256;
            public DheRuntimeAssemblyModeData[] assemblyModes;
        }

        [Serializable]
        private sealed class DheRuntimeAssemblyModeData
        {
            public string assemblyName;
            public string executionMode;
        }

        [Serializable]
        private sealed class StringArrayWrapper
        {
            public string[] items;
        }

        [Serializable]
        private sealed class ResourceManifestData
        {
            public string runtimePlan;
        }

        [Serializable]
        private sealed class DheAssemblyPlanData
        {
            public string assemblyName;
            public string executionMode;
            public string target;
            public string current;
            public string mv;
            public string snapshot;
            public string currentSha256;
            public string baselineSha256;
            public string mvSha256;
            public string snapshotSha256;
            public string currentMetaVersion;
            public string baseMetaVersion;
            public string currentMetaVersionSha256;
        }

        private sealed class MetaVersionInfo
        {
            public byte[] assemblyHash;
            public Dictionary<string, string> types;
            public Dictionary<string, string> methods;
        }

        private sealed class LoadedDheAssembly
        {
            public DheAssemblyPlanData plan;
            public Assembly assembly;
            public byte[] currentHash;
            public byte[] baselineHash;
            public byte[] mvCurrentHash;
            public byte[] mvBaselineHash;
            public MetaVersionInfo currentMetaVersion;
            public MetaVersionInfo baseMetaVersion;
        }

        [Serializable]
        private sealed class DheAssemblyValidation
        {
            public string assemblyName;
            public string executionMode;
            public string currentSha256;
            public string baselineSha256;
            public string mvCurrentSha256;
            public string mvBaselineSha256;
            public bool hashValidated;
            public string loadError;
        }

        private static void WriteResult(DheRun result)
        {
            string path = GetArgument("-labResult");
            if (string.IsNullOrWhiteSpace(path)) path = DefaultResultFile;
            if (!Path.IsPathRooted(path)) path = Path.Combine(Environment.CurrentDirectory, path);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            }
            return string.Empty;
        }

        private sealed class CapabilityRun
        {
            public bool passed;
            public string error;
            public bool interfaceChanged;
            public bool delegateChanged;
            public bool genericSelectChanged;
            public bool genericConstrainedChanged;
            public bool mutateValueChanged;
            public bool boxValueChanged;
            public bool refOutValueChanged;
            public bool asyncValueChanged;
            public bool iterateValueChanged;
            public bool asyncStateMachineChanged;
            public bool iteratorStateMachineChanged;
            public bool genericContainerChanged;
            public bool nullableChanged;
            public bool delegateClosedInstanceChanged;
            public bool delegateOpenInstanceChanged;
            public bool delegateMulticastChanged;
            public bool exceptionFinallyChanged;
            public bool virtualChanged;
            public bool genericVirtualChanged;
            public bool genericVirtualPassed;
            public bool genericBehaviorValidated;
            public int interfaceResult;
            public int delegateResult;
            public int genericSelectResult;
            public bool genericSelectNullPassed;
            public int genericConstrainedResult;
            public int genericContainerResult;
            public int nullableResult;
            public int delegateClosedInstanceResult;
            public int delegateOpenInstanceResult;
            public int delegateMulticastResult;
            public int exceptionFinallyResult;
            public int valueNumber;
            public long valueWide;
            public int boxNumber;
            public long boxWide;
            public int refOutResult;
            public int asyncResult;
            public string iteratorResult;
            public int virtualResult;
            public int genericVirtualResult;
        }

        private sealed class CapabilityDirectRun
        {
            public bool passed;
            public string error;
            public int interfaceResult;
            public int delegateResult;
            public int genericSelectResult;
            public bool genericSelectNullPassed;
            public int genericConstrainedResult;
            public int genericVirtualResult;
            public SmallValue mutateValueResult;
            public SmallValue boxValueResult;
            public int refOutResult;
            public int asyncResult;
            public string iteratorResult;
            public int genericContainerResult;
            public int nullableResult;
            public int delegateClosedInstanceResult;
            public int delegateOpenInstanceResult;
            public int delegateMulticastResult;
            public int exceptionFinallyResult;
            public int virtualResult;
            public int unchangedVirtualResult;
            public int interpreterEntryCount;
            public int aotEntryCount;
        }

        private sealed class StructuralRun
        {
            public bool expected;
            public bool passed;
            public string error;
            public int existingEntryResult;
            public bool addedReferenceTypeFound;
            public bool addedGenericTypeFound;
            public bool addedNestedTypeFound;
            public bool nestedDeclaringTypeValidated;
            public bool addedStaticMethodFound;
            public bool addedInstanceMethodFound;
            public bool addedStaticFieldsFound;
            public bool addedStaticFieldDeclaringTypeValidated;
            public bool addedStaticFieldReflectionValueValidated;
			public bool addedInstanceFieldsFound;
			public bool addedInstanceFieldDeclaringTypeValidated;
			public bool addedInstanceFieldDefaultValueValidated;
			public bool addedInstanceFieldReflectionValueValidated;
			public bool addedInstanceFieldGcValidated;
			public bool removedMethodHidden;
			public bool removedMethodGuardValidated;
			public bool removedFieldsHidden;
			public bool removedFieldGuardValidated;
			public bool fieldSignatureReplacementVisible;
			public bool fieldSignatureReplacementRoundTripValidated;
			public bool logicalPropertiesValidated;
			public bool logicalPropertyRoundTripValidated;
			public bool logicalEventsValidated;
			public bool logicalEventRoundTripValidated;
			public bool logicalEventAccessorsValidated;
			public string logicalAddedEventAccessors;
			public string logicalEvolvedEventAccessors;
			public int logicalAddedEventAccessorTouchValue;
			public int logicalEvolvedEventAccessorTouchValue;
			public int logicalAddedEventTouchValue;
			public int logicalEvolvedEventTouchValue;
			public bool removedPropertyGuardValidated;
			public bool replacedPropertyGuardValidated;
			public bool removedEventGuardValidated;
			public bool replacedEventGuardValidated;
			public bool removedTypeHidden;
			public bool removedTypeEnumerationHidden;
			public bool removedTypeGuardValidated;
			public bool oldSignatureHidden;
			public bool oldSignatureGuardValidated;
			public bool newSignatureFound;
            public bool assemblyEnumerationValidated;
            public bool typeAssemblyMatchesBase;
            public int addedReferenceResult;
            public int addedGenericResult;
            public int addedNestedResult;
            public int addedStaticResult;
            public int addedInstanceResult;
            public int addedStaticFieldDirectResult;
            public int addedStaticFieldReflectionResult;
			public int addedInstanceFieldDirectResult;
			public int addedInstanceFieldReflectionResult;
			public int currentMemberDirectResult;
			public int newSignatureResult;
        }

        [Serializable]
        private sealed class DheBuildIdentityData
        {
            public int identityVersion;
            public string target;
            public string baseId;
            public string managedAssemblySetSha256;
            public string aotSnapshotSha256;
            public string aotSnapshotKind;
            public string nativeGuardSourceSha256;
            public string nativeManifestSha256;
            public string baseMetaVersionSetSha256;
            public string aotMetadataSetId;
            public string runtimeProtocol;
            public string runtimeContract;
            public string[] runtimeCapabilities;
            public string runtimeAssetRoot;
            public string baseMetaVersionAssetRoot;
            public DheBuildIdentityAssemblyData[] assemblies;
        }

        [Serializable]
        private sealed class DheBuildIdentityAssemblyData
        {
            public string assemblyName;
            public string baselineSha256;
            public string baseMetaVersionSha256;
        }

        [Serializable]
        private sealed class DheRun
        {
            public int schemaVersion = 1;
            public string format = "hybridclr.dhe-player-result.json";
            public string target;
            public bool resourceUpdateManifestPresent;
            public bool resourceUpdateValidated;
            public string selectedBaseId;
            public string selectedAotMetadataSetId;
            public string selectedBaseMetaVersionSetSha256;
            public string selectedPayloadVariantId;
            public string selectedPayloadCurrentAssemblySetSha256;
            public bool passed;
            public string error;
            public string loadError;
            public string assemblyName;
            public string[] plannedDheAssemblies;
            public string[] loadedDheAssemblies;
            public string[] plannedDifferentialAssemblies;
            public string[] plannedInterpreterOnlyAssemblies;
            public string[] loadedInterpreterOnlyAssemblies;
            public int changedMethodCount;
            public int expectedChangedMethodCount;
            public bool dispatchProbeValidated;
            public bool noOpAotBehaviorValidated;
            public bool changedProbeChanged;
            public bool unchangedProbeChanged;
            public string dispatchProbeError;
            public string transactionStatus;
            public bool retryValidated;
            public string retryAssemblyName;
            public string retryFailure;
            public DheAssemblyValidation[] assemblyValidations;
            public bool multiAssemblyValidated;
            public long metadataStressResult;
            public string crossAssemblyResult;
            public string currentAssemblySha256;
            public string baselineAssemblySha256;
            public string mvCurrentSha256;
            public string mvBaselineSha256;
            public int addResult;
            public int stableResult;
            public int addViaStableResult;
            public int addPairResult;
            public long wideResult;
            public int touchValue;
            public int instanceAddResult;
            public int instanceStableResult;
            public int instanceAddViaStableResult;
            public bool capabilityDirectPassed;
            public string capabilityDirectError;
            public int capabilityDirectInterpreterEntryCount;
            public int capabilityDirectAotEntryCount;
            public bool managedSecondaryChanged;
            public bool managedSecondaryUnchanged;
            public bool metadataSecondaryChanged;
            public bool crossSecondaryChanged;
            public int managedSecondaryDirectResult;
            public int managedSecondaryUnchangedDirectResult;
            public int metadataSecondaryDirectResult;
            public int crossSecondaryDirectResult;
            public int metadataSecondaryReflectionResult;
            public int crossSecondaryReflectionResult;
            public bool secondaryAssemblyDirectValidated;
            public bool secondaryAssemblyChangedValidated;
            public bool capabilityPassed;
            public string capabilityError;
            public bool capabilityInterfaceChanged;
            public bool capabilityDelegateChanged;
            public bool capabilityGenericSelectChanged;
            public bool capabilityGenericConstrainedChanged;
            public bool capabilityMutateValueChanged;
            public bool capabilityBoxValueChanged;
            public bool capabilityRefOutValueChanged;
            public bool capabilityAsyncValueChanged;
            public bool capabilityIteratorChanged;
            public bool capabilityAsyncStateMachineChanged;
            public bool capabilityIteratorStateMachineChanged;
            public bool capabilityGenericContainerChanged;
            public bool capabilityNullableChanged;
            public bool capabilityDelegateClosedInstanceChanged;
            public bool capabilityDelegateOpenInstanceChanged;
            public bool capabilityDelegateMulticastChanged;
            public bool capabilityExceptionFinallyChanged;
            public bool capabilityVirtualChanged;
            public bool capabilityGenericVirtualChanged;
            public bool capabilityGenericVirtualPassed;
            public bool capabilityGenericBehaviorValidated;
            public bool capabilityGenericSelectNullPassed;
            public int capabilityDirectGenericSelectResult;
            public bool capabilityDirectGenericSelectNullPassed;
            public int capabilityDirectGenericConstrainedResult;
            public int capabilityDirectGenericVirtualResult;
            public int capabilityInterfaceResult;
            public int capabilityDelegateResult;
            public int capabilityGenericSelectResult;
            public int capabilityGenericConstrainedResult;
            public int capabilityGenericContainerResult;
            public int capabilityNullableResult;
            public int capabilityDelegateClosedInstanceResult;
            public int capabilityDelegateOpenInstanceResult;
            public int capabilityDelegateMulticastResult;
            public int capabilityExceptionFinallyResult;
            public int capabilityValueNumber;
            public long capabilityValueWide;
            public int capabilityBoxNumber;
            public long capabilityBoxWide;
            public int capabilityRefOutResult;
            public int capabilityAsyncResult;
            public string capabilityIteratorResult;
            public int capabilityVirtualResult;
            public int capabilityGenericVirtualResult;
            public bool structuralExpected;
            public bool structuralDispatchExpected;
            public bool structuralPassed;
            public string structuralError;
            public int structuralExistingEntryResult;
            public bool structuralAddedReferenceTypeFound;
            public bool structuralAddedGenericTypeFound;
            public bool structuralAddedNestedTypeFound;
            public bool structuralNestedDeclaringTypeValidated;
            public bool structuralAddedStaticMethodFound;
            public bool structuralAddedInstanceMethodFound;
            public bool structuralAddedStaticFieldsFound;
            public bool structuralAddedStaticFieldDeclaringTypeValidated;
            public bool structuralAddedStaticFieldReflectionValueValidated;
			public bool structuralAddedInstanceFieldsFound;
			public bool structuralAddedInstanceFieldDeclaringTypeValidated;
			public bool structuralAddedInstanceFieldDefaultValueValidated;
			public bool structuralAddedInstanceFieldReflectionValueValidated;
			public bool structuralAddedInstanceFieldGcValidated;
			public bool structuralRemovedMethodHidden;
			public bool structuralRemovedMethodGuardValidated;
			public bool structuralRemovedFieldsHidden;
			public bool structuralRemovedFieldGuardValidated;
			public bool structuralFieldSignatureReplacementVisible;
			public bool structuralFieldSignatureReplacementRoundTripValidated;
			public bool structuralLogicalPropertiesValidated;
			public bool structuralLogicalPropertyRoundTripValidated;
			public bool structuralLogicalEventsValidated;
			public bool structuralLogicalEventRoundTripValidated;
			public bool structuralLogicalEventAccessorsValidated;
			public string structuralLogicalAddedEventAccessors;
			public string structuralLogicalEvolvedEventAccessors;
			public int structuralLogicalAddedEventAccessorTouchValue;
			public int structuralLogicalEvolvedEventAccessorTouchValue;
			public int structuralLogicalAddedEventTouchValue;
			public int structuralLogicalEvolvedEventTouchValue;
			public bool structuralRemovedPropertyGuardValidated;
			public bool structuralReplacedPropertyGuardValidated;
			public bool structuralRemovedEventGuardValidated;
			public bool structuralReplacedEventGuardValidated;
			public bool structuralRemovedTypeHidden;
			public bool structuralRemovedTypeEnumerationHidden;
			public bool structuralRemovedTypeGuardValidated;
			public bool structuralOldSignatureHidden;
			public bool structuralOldSignatureGuardValidated;
			public bool structuralNewSignatureFound;
            public bool structuralAssemblyEnumerationValidated;
            public bool structuralTypeAssemblyMatchesBase;
            public int structuralAddedReferenceResult;
            public int structuralAddedGenericResult;
            public int structuralAddedNestedResult;
            public int structuralAddedStaticResult;
            public int structuralAddedInstanceResult;
            public int structuralAddedStaticFieldDirectResult;
            public int structuralAddedStaticFieldReflectionResult;
			public int structuralAddedInstanceFieldDirectResult;
			public int structuralAddedInstanceFieldReflectionResult;
			public int structuralCurrentMemberDirectResult;
			public int structuralNewSignatureResult;
            public string changedMethod;
            public string unchangedMethod;
            public string changedCallingUnchangedMethod;
            public string changedMultiArgumentMethod;
            public string changedInt64Method;
            public string changedVoidMethod;
            public string changedInstanceMethod;
            public string unchangedInstanceMethod;
            public string changedInstanceCallingUnchangedMethod;
            public int interpreterEntryCount;
            public int aotBridgeCallCount;
            public int aotEntryCount;
            public bool mvValidated;
            public bool currentHashValidated;
            public bool baselineHashValidated;
            public bool snapshotHashValidated;
            public string embeddedSnapshotHash;
            public bool embeddedSnapshotHashValidated;
            public bool buildIdentityValidated;
            public int identityVersion;
            public string aotSnapshotKind;
            public string nativeGuardSourceSha256;
            public string nativeManifestSha256;
            public int changedToken;
            public int unchangedToken;
            public int changedCallingUnchangedToken;
            public int changedMultiArgumentToken;
            public int changedInt64Token;
            public int changedVoidToken;
            public int changedInstanceToken;
            public int unchangedInstanceToken;
            public int changedInstanceCallingUnchangedToken;
        }
    }
}
