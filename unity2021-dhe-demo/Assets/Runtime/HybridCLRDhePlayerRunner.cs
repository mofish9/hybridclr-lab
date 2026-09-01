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
            DheRuntimePlanData runtimePlan = JsonUtility.FromJson<DheRuntimePlanData>(
                System.Text.Encoding.UTF8.GetString(ReadStreamingAssetBytes(RuntimePlanFile)));
            if (runtimePlan == null || runtimePlan.schemaVersion != 1 ||
                !string.Equals(runtimePlan.format, "hybridclr.dhe-runtime-asset-plan.json", StringComparison.Ordinal) ||
                runtimePlan.assemblies == null || runtimePlan.assemblies.Length == 0)
            {
                throw new InvalidDataException("DHE runtime plan is empty or invalid.");
            }
            Dictionary<string, LoadedDheAssembly> loadedAssemblies = new Dictionary<string, LoadedDheAssembly>(StringComparer.OrdinalIgnoreCase);
            int changedMethodCount = 0;
            bool retryValidated = false;
            string retryAssemblyName = string.Empty;
            LoadImageErrorCode retryFailureCode = LoadImageErrorCode.OK;
            foreach (DheAssemblyPlanData assemblyPlan in runtimePlan.assemblies)
            {
                if (assemblyPlan == null || string.IsNullOrWhiteSpace(assemblyPlan.assemblyName) ||
                    string.IsNullOrWhiteSpace(assemblyPlan.current) ||
                    string.IsNullOrWhiteSpace(assemblyPlan.mv) || string.IsNullOrWhiteSpace(assemblyPlan.snapshot))
                {
                    throw new InvalidDataException("DHE runtime plan contains an incomplete assembly record.");
                }
                if (loadedAssemblies.ContainsKey(assemblyPlan.assemblyName))
                {
                    throw new InvalidDataException("DHE runtime plan contains duplicate assembly: " + assemblyPlan.assemblyName);
                }
                byte[] assemblyCurrent = ReadStreamingAssetBytes(assemblyPlan.current);
                byte[] assemblyMv = ReadStreamingAssetBytes(assemblyPlan.mv);
                byte[] assemblySnapshot = ReadStreamingAssetBytes(assemblyPlan.snapshot);
                if (assemblyMv.Length < 88 || !string.Equals(System.Text.Encoding.ASCII.GetString(assemblyMv, 0, 8), "DHEMVLT1", StringComparison.Ordinal) ||
                    BitConverter.ToUInt32(assemblyMv, 8) != 1)
                {
                    throw new InvalidDataException("DHE MV binary header is invalid for " + assemblyPlan.assemblyName);
                }
                byte[] assemblyExpectedBaselineHash = Slice(assemblyMv, 24, 32);
                byte[] assemblyExpectedCurrentHash = Slice(assemblyMv, 56, 32);
                int assemblyNameSize = checked((int)BitConverter.ToUInt32(assemblyMv, 12));
                if (assemblyNameSize <= 0 || 88 + assemblyNameSize > assemblyMv.Length ||
                    !string.Equals(System.Text.Encoding.UTF8.GetString(assemblyMv, 88, assemblyNameSize), assemblyPlan.assemblyName, StringComparison.Ordinal))
                {
                    throw new InvalidDataException("DHE MV assembly identity does not match runtime plan for " + assemblyPlan.assemblyName);
                }
                byte[] assemblyCurrentHash = Sha256(assemblyCurrent);
                byte[] assemblyMvHash = Sha256(assemblyMv);
                byte[] assemblySnapshotHash = Sha256(assemblySnapshot);
                // The baseline image is compiled into the Player. Its identity
                // is carried by the MV header and the 32-byte snapshot asset;
                // no baseline DLL is shipped as a runtime asset.
                byte[] assemblyBaselineHash = assemblyExpectedBaselineHash;
                string actualCurrentHash = ToHex(assemblyCurrentHash);
                string actualBaselineHash = ToHex(assemblyBaselineHash);
                string actualMvHash = ToHex(assemblyMvHash);
                string actualSnapshotHash = ToHex(assemblySnapshotHash);
                string mvCurrentHash = ToHex(assemblyExpectedCurrentHash);
                string mvBaselineHash = ToHex(assemblyExpectedBaselineHash);
                string snapshotHash = ToHex(assemblySnapshot);
                if (!string.Equals(assemblyPlan.currentSha256, actualCurrentHash, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrWhiteSpace(assemblyPlan.baselineSha256) &&
                        !string.Equals(assemblyPlan.baselineSha256, actualBaselineHash, StringComparison.OrdinalIgnoreCase)) ||
                    !string.Equals(assemblyPlan.mvSha256, actualMvHash, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(assemblyPlan.snapshotSha256, actualSnapshotHash, StringComparison.OrdinalIgnoreCase) ||
                    !ByteArraysEqual(assemblyCurrentHash, assemblyExpectedCurrentHash) ||
                    !ByteArraysEqual(assemblySnapshot, assemblyExpectedBaselineHash))
                {
                    throw new InvalidDataException("DHE runtime plan hash binding failed for " +
                        assemblyPlan.assemblyName + "; current=" + actualCurrentHash +
                        "/plan=" + assemblyPlan.currentSha256 + "/mv=" + mvCurrentHash +
                        "; baseline=" + actualBaselineHash + "/plan=" + assemblyPlan.baselineSha256 +
                        "/mv=" + mvBaselineHash + "; snapshot=" + snapshotHash +
                        "/plan=" + assemblyPlan.snapshotSha256);
                }
                int assemblyChangedMethodCount = checked((int)BitConverter.ToUInt32(assemblyMv, 16));
                changedMethodCount = checked(changedMethodCount + assemblyChangedMethodCount);

                // Exercise the runtime transaction boundary before the first
                // valid load: an invalid method token must retire the
                // homologous image, allowing the same assembly to be retried
                // without an already-loaded false positive.
                if (!retryValidated && assemblyChangedMethodCount > 0)
                {
                    byte[] invalidMv = CreateInvalidMetaVersion(assemblyMv);
                    retryFailureCode = RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(
                        assemblyCurrent, invalidMv, assemblySnapshot);
                    if (retryFailureCode != LoadImageErrorCode.DHE_MV_REGISTRATION_FAILED)
                    {
                        throw new InvalidOperationException(
                            "DHE invalid-MV transaction probe returned " + retryFailureCode +
                            " for " + assemblyPlan.assemblyName + ".");
                    }
                    retryAssemblyName = assemblyPlan.assemblyName;
                }
                LoadImageErrorCode assemblyLoadError = RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(
                    assemblyCurrent, assemblyMv, assemblySnapshot);
                if (assemblyLoadError != LoadImageErrorCode.OK)
                {
                    throw new InvalidOperationException("DHE load failed for " + assemblyPlan.assemblyName + ": " + assemblyLoadError);
                }
                Assembly assembly = Assembly.Load(assemblyCurrent);
                loadedAssemblies.Add(assemblyPlan.assemblyName, new LoadedDheAssembly
                {
                    plan = assemblyPlan,
                    assembly = assembly,
                    currentHash = assemblyCurrentHash,
                    baselineHash = assemblyBaselineHash,
                    mvCurrentHash = assemblyExpectedCurrentHash,
                    mvBaselineHash = assemblyExpectedBaselineHash,
                });
                if (!string.IsNullOrWhiteSpace(retryAssemblyName) &&
                    string.Equals(assemblyPlan.assemblyName, retryAssemblyName, StringComparison.Ordinal))
                {
                    retryValidated = true;
                }
            }
            if (loadedAssemblies.Count != runtimePlan.assemblies.Length ||
                !loadedAssemblies.ContainsKey(MainAssemblyName))
            {
                throw new InvalidDataException("DHE runtime plan must load exactly its declared assemblies and include " + MainAssemblyName + ".");
            }

            LoadedDheAssembly mainLoaded = loadedAssemblies[MainAssemblyName];
            byte[] current = ReadStreamingAssetBytes(mainLoaded.plan.current);
            byte[] mv = ReadStreamingAssetBytes(mainLoaded.plan.mv);
            byte[] snapshot = ReadStreamingAssetBytes(mainLoaded.plan.snapshot);
            DheBuildIdentityData buildIdentity = JsonUtility.FromJson<DheBuildIdentityData>(
                System.Text.Encoding.UTF8.GetString(ReadStreamingAssetBytes(BuildIdentityFile)));
            byte[] currentHash = Sha256(current);
            byte[] baselineHash = mainLoaded.baselineHash;
            byte[] expectedCurrentHash = mainLoaded.mvCurrentHash;
            byte[] expectedBaselineHash = mainLoaded.mvBaselineHash;
            LoadImageErrorCode loadError = LoadImageErrorCode.OK;

            MethodInfo addMethod = typeof(DheDemoCalculator).GetMethod("Add", BindingFlags.Public | BindingFlags.Static);
            MethodInfo stableMethod = typeof(DheDemoCalculator).GetMethod("Stable", BindingFlags.Public | BindingFlags.Static);
            MethodInfo addViaStableMethod = typeof(DheDemoCalculator).GetMethod("AddViaStable", BindingFlags.Public | BindingFlags.Static);
            MethodInfo addPairMethod = typeof(DheDemoCalculator).GetMethod("AddPair", BindingFlags.Public | BindingFlags.Static);
            MethodInfo wideMethod = typeof(DheDemoCalculator).GetMethod("Wide", BindingFlags.Public | BindingFlags.Static);
            MethodInfo touchMethod = typeof(DheDemoCalculator).GetMethod("Touch", BindingFlags.Public | BindingFlags.Static);
            MethodInfo instanceStableMethod = typeof(DheDemoCalculator).GetMethod("InstanceStable", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo instanceAddMethod = typeof(DheDemoCalculator).GetMethod("InstanceAdd", BindingFlags.Public | BindingFlags.Instance);
            MethodInfo instanceAddViaStableMethod = typeof(DheDemoCalculator).GetMethod("InstanceAddViaStable", BindingFlags.Public | BindingFlags.Instance);
            if (addMethod == null || stableMethod == null || addViaStableMethod == null || addPairMethod == null ||
                wideMethod == null || touchMethod == null || instanceStableMethod == null ||
                instanceAddMethod == null || instanceAddViaStableMethod == null)
            {
                throw new MissingMethodException(typeof(DheDemoCalculator).FullName);
            }

            bool addChanged = RuntimeApi.IsDifferentialMethodChanged(addMethod);
            bool stableChanged = RuntimeApi.IsDifferentialMethodChanged(stableMethod);
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
            CapabilityDirectRun directCapability = ExecuteCapabilityDirect();
            CapabilityRun capability = ExecuteCapabilityReflection();
            long metadataStressResult = ExecuteMetadataStress(loadedAssemblies);
            int metadataSecondaryReflectionResult = ExecuteSecondaryChanged(
                loadedAssemblies, "HybridCLR.MetadataStress", "HybridCLR.Lab.MetadataStress.DheSecondaryCases");
            string crossAssemblyResult = ExecuteCrossAssembly(loadedAssemblies);
            int crossSecondaryReflectionResult = ExecuteSecondaryChanged(
                loadedAssemblies, "HybridCLR.CrossAssemblyDerived", "HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe");
            bool currentHashValidated = ByteArraysEqual(currentHash, expectedCurrentHash);
            bool baselineHashValidated = ByteArraysEqual(baselineHash, expectedBaselineHash);
            string embeddedSnapshotHash = HybridCLRDheBuildIdentity.AotSnapshotSha256;
            bool embeddedSnapshotHashValidated = !string.IsNullOrWhiteSpace(embeddedSnapshotHash) &&
                string.Equals(ToHex(snapshot), embeddedSnapshotHash, StringComparison.OrdinalIgnoreCase);
            bool snapshotHashValidated = ByteArraysEqual(snapshot, expectedBaselineHash) && embeddedSnapshotHashValidated;
            string expectedTarget = GetArgument("-labTarget");
            bool buildIdentityValidated = buildIdentity != null && buildIdentity.identityVersion == 2 &&
                string.Equals(buildIdentity.aotSnapshotKind, "managed-assembly-plus-generated-cpp-v1", StringComparison.Ordinal) &&
                string.Equals(buildIdentity.target, expectedTarget, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.mainBaselineAssemblySha256, ToHex(baselineHash), StringComparison.OrdinalIgnoreCase) &&
                string.Equals(buildIdentity.mainSnapshotSha256, ToHex(snapshot), StringComparison.OrdinalIgnoreCase) &&
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
            bool changedBehaviorValidated = changedMethodCount == 0
                ? noOpAotBehaviorValidated
                : (addResult == 101 && stableResult == 4 &&
                    addViaStableResult == 104 && addPairResult == 107 && wideResult == 1005L &&
                    touchValue == 705 && instanceAddResult == 201 && instanceStableResult == 6 &&
                    instanceAddViaStableResult == 206 && addChanged && !stableChanged &&
                    addViaStableChanged && addPairChanged && wideChanged && touchChanged &&
                    !instanceStableChanged && instanceAddChanged && instanceAddViaStableChanged &&
                    mainInterpreterEntryCount >= 7 && mainAotEntryCount >= 3 && capability.passed &&
                    directCapability.passed && managedSecondaryChanged && !managedSecondaryUnchanged &&
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
                plannedDheAssemblies = GetAssemblyNames(runtimePlan),
                loadedDheAssemblies = GetAssemblyNames(loadedAssemblies),
                target = GetArgument("-labTarget"),
                changedMethodCount = changedMethodCount,
                expectedChangedMethodCount = changedMethodCount,
                dispatchProbeValidated = dispatchProbeValidated,
                noOpAotBehaviorValidated = noOpAotBehaviorValidated,
                changedProbeChanged = addChanged,
                unchangedProbeChanged = stableChanged,
                dispatchProbeError = dispatchProbeValidated ? null : "DHE changed/unchanged dispatch assertions failed.",
                transactionStatus = transactionStatus,
                retryValidated = retryValidated,
                retryAssemblyName = changedMethodCount == 0 ? null : retryAssemblyName,
                retryFailure = retryValidated ? retryFailureCode.ToString() : null,
                assemblyValidations = loadedAssemblies.Values.OrderBy(item => item.plan.assemblyName, StringComparer.Ordinal)
                    .Select(item => new DheAssemblyValidation
                    {
                        assemblyName = item.plan.assemblyName,
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
                unchangedMethod = stableChanged ? "interpreter" : "aot",
                changedCallingUnchangedMethod = addViaStableChanged ? "interpreter + AOT callee" : "aot",
                changedMultiArgumentMethod = addPairChanged ? "interpreter" : "aot",
                changedInt64Method = wideChanged ? "interpreter" : "aot",
                changedVoidMethod = touchChanged ? "interpreter" : "aot",
                changedInstanceMethod = instanceAddChanged ? "interpreter" : "aot",
                unchangedInstanceMethod = instanceStableChanged ? "interpreter" : "aot",
                changedInstanceCallingUnchangedMethod = instanceAddViaStableChanged ? "interpreter + AOT callee" : "aot",
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
                unchangedToken = stableMethod.MetadataToken,
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

        private static CapabilityDirectRun ExecuteCapabilityDirect()
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
                    result.genericContainerResult == 107 && result.nullableResult == 103 &&
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

        private static CapabilityRun ExecuteCapabilityReflection()
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
                    result.genericContainerResult == 107 && result.nullableResult == 103 &&
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

        private static string[] GetAssemblyNames(DheRuntimePlanData plan)
        {
            return plan.assemblies.Select(item => item.assemblyName).OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        private static string[] GetAssemblyNames(Dictionary<string, LoadedDheAssembly> assemblies)
        {
            return assemblies.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        }

        private static byte[] ReadStreamingAssetBytes(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            {
                throw new InvalidDataException("DHE runtime plan contains a non-relative StreamingAssets path: " + relativePath);
            }
            string normalized = relativePath.Replace('\\', '/');
            if (normalized.StartsWith("/", StringComparison.Ordinal) ||
                System.Text.RegularExpressions.Regex.IsMatch(normalized, "(^|/)\\.\\.(/|$)"))
            {
                throw new InvalidDataException("DHE runtime plan path escapes StreamingAssets: " + relativePath);
            }
            string streamingRoot = Path.GetFullPath(Application.streamingAssetsPath).TrimEnd('\\', '/');
            string path = Path.GetFullPath(Path.Combine(
                streamingRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
            string rootPrefix = streamingRoot + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("DHE runtime plan path escapes StreamingAssets: " + relativePath);
            }
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Streaming asset was not found", path);
            }
            return File.ReadAllBytes(path);
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

        private static byte[] CreateInvalidMetaVersion(byte[] mvBytes)
        {
            if (mvBytes == null || mvBytes.Length < 88)
            {
                throw new InvalidDataException("DHE MV payload is too short for the transaction probe.");
            }
            int assemblyNameSize = checked((int)BitConverter.ToUInt32(mvBytes, 12));
            int methodCount = checked((int)BitConverter.ToUInt32(mvBytes, 16));
            int tokenOffset = checked(88 + assemblyNameSize);
            if (methodCount <= 0 || tokenOffset < 88 || tokenOffset > mvBytes.Length - 4 ||
                tokenOffset + 4 * methodCount > mvBytes.Length)
            {
                throw new InvalidDataException("DHE MV payload has no method token for the transaction probe.");
            }
            byte[] invalidMv = (byte[])mvBytes.Clone();
            // 0x0600ffff is outside the demo assembly's method table and keeps
            // all hash/header fields intact, so failure occurs in method
            // preparation after the homologous image has been registered.
            byte[] invalidToken = BitConverter.GetBytes(0x0600ffffu);
            Buffer.BlockCopy(invalidToken, 0, invalidMv, tokenOffset, invalidToken.Length);
            return invalidMv;
        }

        private static bool ByteArraysEqual(byte[] left, byte[] right)
        {
            if (left.Length != right.Length) return false;
            int difference = 0;
            for (int i = 0; i < left.Length; i++) difference |= left[i] ^ right[i];
            return difference == 0;
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
            public DheAssemblyPlanData[] assemblies;
        }

        [Serializable]
        private sealed class DheAssemblyPlanData
        {
            public string assemblyName;
            public string target;
            public string current;
            public string mv;
            public string snapshot;
            public string currentSha256;
            public string baselineSha256;
            public string mvSha256;
            public string snapshotSha256;
        }

        private sealed class LoadedDheAssembly
        {
            public DheAssemblyPlanData plan;
            public Assembly assembly;
            public byte[] currentHash;
            public byte[] baselineHash;
            public byte[] mvCurrentHash;
            public byte[] mvBaselineHash;
        }

        [Serializable]
        private sealed class DheAssemblyValidation
        {
            public string assemblyName;
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

        [Serializable]
        private sealed class DheBuildIdentityData
        {
            public int identityVersion;
            public string target;
            public string baselineAssemblySha256;
            public string aotSnapshotSha256;
            public string mainBaselineAssemblySha256;
            public string mainSnapshotSha256;
            public string aotSnapshotKind;
            public string nativeGuardSourceSha256;
            public string nativeManifestSha256;
        }

        [Serializable]
        private sealed class DheRun
        {
            public int schemaVersion = 1;
            public string format = "hybridclr.dhe-player-result.json";
            public string target;
            public bool passed;
            public string error;
            public string loadError;
            public string assemblyName;
            public string[] plannedDheAssemblies;
            public string[] loadedDheAssemblies;
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
