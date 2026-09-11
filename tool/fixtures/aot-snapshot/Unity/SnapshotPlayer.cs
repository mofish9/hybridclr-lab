using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    public static class SnapshotPlayer
    {
        [Serializable] private sealed class Plan { public Record[] assemblies; }
        [Serializable] private sealed class Record { public string assemblyName; public string current; }
        [Serializable] private sealed class Result
        {
            public bool passed;
            public bool resourceUpdate;
            public string baseId, aotAnalysisSnapshotSha256, error, stage;
            public int loadedAssemblies, revision, sentinel;
            public int moduleRunsBeforeLoad = -1;
            public int moduleConstantBeforeLoad, moduleConstantAfterLoad, moduleConstantFresh, moduleConstantValue;
            public int ordinaryModuleRunsBeforeLoad = -1, ordinaryModuleRunsAfterLoad = -1;
            public string[] plannedAssemblies, loadedAssemblyNames, differentialAssemblies, interpreterOnlyAssemblies;
            public string[] records;
            public string[] recoveryChecks;
            public string[] precommitChecks, unityChecks, preparationChecks, referenceCacheChecks;
            public int unityBaseDelta;
            public long ordinaryAotReferenceResult;
            public long ordinaryAotEchoExtra;
            public int ordinaryAotStaticNeighbor;
            public string[] virtualNoopChecks;
            public string[] unityAssetChecks;
            public string[] genericParentNoopChecks;
            public int genericParentNoopMethods, genericParentNoopAotEntries, genericParentNoopInterpreterEntries;
            public string[] virtualReceiverChecks;
            public string[] parentTransitionCacheChecks;
            public string[] typeDeletionCacheChecks;
            public int virtualNoopMethods, virtualNoopAotEntries, virtualNoopInterpreterEntries;
        }
        private sealed class Provider : IDheRuntimeAssetProvider
        {
            public string ResourceRoot;
            private string PathFor(string path) => ResourceRoot != null &&
                !path.Contains("/BaseMetaVersion/")
                ? Path.Combine(ResourceRoot, path.Substring("Assets/StreamingAssets/SnapshotDHE/".Length))
                : Path.Combine(Application.streamingAssetsPath, path.Substring("Assets/StreamingAssets/".Length));
            public bool Exists(string path) => File.Exists(PathFor(path));
            public byte[] LoadBytes(string path) => File.ReadAllBytes(PathFor(path));
            public string LoadText(string path) => File.ReadAllText(PathFor(path));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-dheDeliveryRoot") >= 0) return;
            if (Array.IndexOf(args, "-frozenEntryPlan") >= 0) return;
            if (Array.IndexOf(args, "-mixedTransactionPlan") >= 0) return;
            int index = Array.IndexOf(args, "-snapshotResult");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("-snapshotResult");
            var result = new Result();
            try
            {
                var identity = DheBuildIdentity.Create();
                result.baseId = identity.BaseId; result.aotAnalysisSnapshotSha256 = identity.AotAnalysisSnapshotSha256;
                var unityType = typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.UnityCases.EvolvingBehaviour");
                if (unityType != null) result.unityBaseDelta = (int)unityType.GetField("Delta", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetRawConstantValue();
                UnityReferenceCachePlayer referenceCache = Array.IndexOf(args, "-unityReferenceCacheProbe") >= 0
                    ? UnityReferenceCachePlayer.Capture(unityType) : null;
                VirtualSignatureReceiverCache virtualReceiverCache = Array.IndexOf(args, "-virtualSignatureOldReceiverProbe") >= 0
                    ? VirtualSignatureReceiverCache.Capture(typeof(ValueLayout.Factory).Assembly) : null;
                ParentTransitionReceiverCache parentTransitionCache = Array.IndexOf(args, "-parentTransitionCacheProbe") >= 0
                    ? ParentTransitionReceiverCache.Capture(typeof(ValueLayout.Factory).Assembly) : null;
                TypeDeletionReceiverCache typeDeletionCache = Array.IndexOf(args, "-typeDeletionCacheProbe") >= 0
                    ? TypeDeletionReceiverCache.Capture(typeof(ValueLayout.Factory).Assembly) : null;
                var moduleState = typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.ModuleEvolution.ModuleState");
                System.Reflection.FieldInfo moduleConstant = null;
                if (moduleState != null)
                {
                    result.stage = "before-module-version-selection";
                    result.moduleRunsBeforeLoad = (int)moduleState.GetField("Runs").GetValue(null);
                    if (result.moduleRunsBeforeLoad != 0)
                        throw new InvalidDataException("Hotfix AOT module initialized before the Current version was selected.");
                    moduleConstant = moduleState.GetField("ExpectedVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                    result.moduleConstantBeforeLoad = (int)moduleConstant.GetRawConstantValue();
                }
                var ordinaryModule = typeof(HybridCLR.Lab.ValueLayoutNative.NativeBoundary).Assembly.GetType("HybridCLR.Lab.ValueLayoutNative.OrdinaryModuleState");
                if (ordinaryModule != null)
                {
                    result.ordinaryModuleRunsBeforeLoad = (int)ordinaryModule.GetField("Runs").GetValue(null);
                    if (result.ordinaryModuleRunsBeforeLoad != 1)
                        throw new InvalidDataException("Ordinary AOT module must retain normal startup initialization.");
                }
                int resourceIndex = Array.IndexOf(args, "-snapshotResourceRoot");
                var provider = new Provider { ResourceRoot = resourceIndex < 0 ? null : args[resourceIndex + 1] };
                result.resourceUpdate = provider.ResourceRoot != null;
                const string root = "Assets/StreamingAssets/SnapshotDHE/";
                string planPath = root + (result.resourceUpdate ? "dhe-runtime-plan.json" : "DheRuntimePlan.json");
                string error;
                result.stage = "initialize";
                bool initialized = result.resourceUpdate
                    ? DheRuntime.InitializeFromResourceUpdate(provider, identity, root + "dhe-resource-update.json", out error, root)
                    : DheRuntime.Initialize(provider, identity, out error, planPath, root);
                if (!initialized)
                    throw new InvalidDataException(error);
                var plan = JsonUtility.FromJson<Plan>(provider.LoadText(planPath));
                var records = plan.assemblies.ToDictionary(row => row.assemblyName, StringComparer.OrdinalIgnoreCase);
                result.plannedAssemblies = DheRuntime.PlannedAssemblyNames;
                result.differentialAssemblies = DheRuntime.DifferentialAssemblyNames;
                result.interpreterOnlyAssemblies = DheRuntime.InterpreterOnlyAssemblyNames;
                result.stage = "load-current-batch";
                byte[][] currentDlls = result.plannedAssemblies.Select(name => provider.LoadBytes(records[name].current)).ToArray();
                int preparationProbe = Array.IndexOf(args, "-publicPreparationProbe");
                if (preparationProbe >= 0)
                {
                    result.preparationChecks = PublicPreparationPlayer.Verify(result.plannedAssemblies, currentDlls, args[preparationProbe + 1]);
                    result.error = DheRuntime.LastLoadError; result.stage = "expected-preparation-failure"; result.passed = true;
                    File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true)); Application.Quit(0); return;
                }
                if (Array.IndexOf(args, "-publicPrecommitProbe") >= 0)
                    result.precommitChecks = PublicPrecommitPlayer.RejectThenRestore(result.plannedAssemblies, currentDlls);
                bool loadedCurrent = DheRuntime.LoadCurrentAssemblyImages(result.plannedAssemblies, currentDlls, out var code, out error);
                if (Array.IndexOf(args, "-publicFailureProbe") >= 0)
                {
                    if (loadedCurrent) throw new InvalidOperationException("Expected the deliberate module initialization failure.");
                    result.recoveryChecks = PublicLoadFailurePlayer.Verify(provider, identity, root, result.plannedAssemblies, currentDlls, code, error);
                    result.loadedAssemblyNames = DheRuntime.LoadedAssemblyNames;
                    result.loadedAssemblies = result.plannedAssemblies.Intersect(result.loadedAssemblyNames, StringComparer.OrdinalIgnoreCase).Count();
                    result.error = error; result.stage = "expected-module-failure"; result.passed = true;
                    File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true)); Application.Quit(0); return;
                }
                if (!loadedCurrent)
                    throw new InvalidDataException(code + ":" + error);
                // Post-load probe failures must still report the committed
                // assembly set and distinguish their stage from native load.
                result.loadedAssemblyNames = DheRuntime.LoadedAssemblyNames;
                result.loadedAssemblies = result.plannedAssemblies.Intersect(result.loadedAssemblyNames, StringComparer.OrdinalIgnoreCase).Count();
                if (virtualReceiverCache != null)
                {
                    result.stage = "virtual-signature-old-receiver";
                    result.virtualReceiverChecks = virtualReceiverCache.Verify();
                }
                if (parentTransitionCache != null)
                {
                    result.stage = "parent-transition-cached-receiver";
                    result.parentTransitionCacheChecks = parentTransitionCache.Verify();
                }
                if (referenceCache != null)
                {
                    result.referenceCacheChecks = referenceCache.Verify(out string cacheFailure);
                    if (cacheFailure != null) throw new InvalidOperationException(cacheFailure);
                }
                if (typeDeletionCache != null)
                {
                    result.stage = "type-deletion-cached-receiver";
                    result.typeDeletionCacheChecks = typeDeletionCache.Verify();
                }
                result.precommitChecks = PublicPrecommitPlayer.VerifyRetry(result.precommitChecks);
                // LoadedAssemblyNames also includes authenticated frozen AOT
                // sources. Count only the Current payload for this assertion.
                result.loadedAssemblyNames = DheRuntime.LoadedAssemblyNames;
                result.loadedAssemblies = result.plannedAssemblies.Intersect(result.loadedAssemblyNames, StringComparer.OrdinalIgnoreCase).Count();
                if (moduleConstant != null)
                {
                    int constantIndex = Array.IndexOf(args, "-expectedModuleConstant");
                    int expectedConstant = constantIndex < 0 ? result.moduleConstantBeforeLoad : int.Parse(args[constantIndex + 1]);
                    result.moduleConstantAfterLoad = (int)moduleConstant.GetRawConstantValue();
                    result.moduleConstantValue = (int)moduleConstant.GetValue(null);
                    result.moduleConstantFresh = (int)typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.ModuleEvolution.ModuleState")
                        .GetField("ExpectedVersion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetRawConstantValue();
                    if (result.moduleConstantAfterLoad != expectedConstant || result.moduleConstantFresh != expectedConstant ||
                        result.moduleConstantValue != expectedConstant)
                        throw new InvalidDataException("DHE literal reflection did not select the expected Current value.");
                }
                if (ordinaryModule != null)
                {
                    result.ordinaryModuleRunsAfterLoad = (int)ordinaryModule.GetField("Runs").GetValue(null);
                    if (result.ordinaryModuleRunsAfterLoad != 1)
                        throw new InvalidDataException("DHE load reinitialized the ordinary AOT module.");
                }
                result.stage = "business-entry";
                result.revision = ValueLayout.Factory.GetRevision();
                result.sentinel = ValueLayout.Factory.UnchangedRevision();
                var probe = typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.ValueLayout.ResourceEvolutionProbe");
                if (probe != null)
                {
                    result.records = (string[])probe.GetMethod("Run").Invoke(null, null);
                    var native = System.Reflection.Assembly.Load("HybridCLR.ValueLayoutNative").GetType("HybridCLR.Lab.ValueLayoutNative.NativeBoundary");
                    result.ordinaryAotReferenceResult = (long)native.GetMethod("ResourceResult").Invoke(null, null);
                    result.ordinaryAotStaticNeighbor = (int)native.GetMethod("StaticNeighbor").Invoke(null, null);
                }
                int expectedIndex = Array.IndexOf(args, "-expectedRevision");
                int expectedRevision = expectedIndex < 0 ? 41 : int.Parse(args[expectedIndex + 1]);
                int assemblyIndex = Array.IndexOf(args, "-expectedAssemblies");
                int expectedAssemblies = assemblyIndex < 0 ? 3 : int.Parse(args[assemblyIndex + 1]);
                result.passed = expectedAssemblies > 0 && result.loadedAssemblies == expectedAssemblies &&
                    result.plannedAssemblies.Length == expectedAssemblies && result.revision == expectedRevision &&
                    result.sentinel == 5 &&
                    !RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("UnchangedRevision"));
                result.stage = "complete";
                if (Array.IndexOf(args, "-virtualSignatureNoopProbe") >= 0)
                {
                    if (!result.passed) throw new InvalidOperationException("Base business entry failed before virtual no-op validation.");
                    result.passed = false; result.stage = "virtual-signature-noop";
                    var assembly = typeof(ValueLayout.Factory).Assembly;
                    var methods = assembly.GetType("HybridCLR.Lab.VirtualSignatures.Processor", true)
                        .GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly)
                        .Where(method => method.IsVirtual).ToArray();
                    result.virtualNoopMethods = methods.Length;
                    if (methods.Any(RuntimeApi.IsDifferentialMethodChanged))
                        throw new InvalidDataException("Unchanged virtual implementation was marked changed.");
                    RuntimeApi.ResetDifferentialDispatchCounters();
                    result.virtualNoopChecks = (string[])assembly.GetType("HybridCLR.Lab.VirtualSignatures.Cases", true)
                        .GetMethod("Run").Invoke(null, null);
                    result.virtualNoopAotEntries = RuntimeApi.GetDifferentialAotEntryCount();
                    result.virtualNoopInterpreterEntries = RuntimeApi.GetDifferentialInterpreterEntryCount();
                    result.passed = methods.Length == 6 && result.virtualNoopChecks.Length == 25 &&
                        result.virtualNoopAotEntries > 0 && result.virtualNoopInterpreterEntries == 0;
                    result.stage = "virtual-signature-noop-complete";
                }
                if (Array.IndexOf(args, "-genericParentNoopProbe") >= 0)
                {
                    if (!result.passed) throw new InvalidOperationException("Prior checks failed before generic parent no-op validation.");
                    result.passed = false; result.stage = "generic-parent-noop";
                    var assembly = typeof(ValueLayout.Factory).Assembly;
                    var parent = assembly.GetType("HybridCLR.Lab.VirtualSignatures.Processor", true).BaseType;
                    if (!parent.IsGenericType || parent.GetGenericTypeDefinition().FullName !=
                        "HybridCLR.Lab.GenericPhysicalParents.GenericParent`1")
                        throw new InvalidDataException("Expected an AOT generic physical parent.");
                    var methods = parent.GetMethods(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
                    result.genericParentNoopMethods = methods.Length;
                    if (methods.Length != 7 || methods.Any(RuntimeApi.IsDifferentialMethodChanged))
                        throw new InvalidDataException("Unchanged generic parent implementations were not preserved.");
                    RuntimeApi.ResetDifferentialDispatchCounters();
                    result.genericParentNoopChecks = (string[])assembly.GetType("HybridCLR.Lab.GenericPhysicalParents.Cases", true)
                        .GetMethod("Run").Invoke(null, null);
                    result.genericParentNoopAotEntries = RuntimeApi.GetDifferentialAotEntryCount();
                    result.genericParentNoopInterpreterEntries = RuntimeApi.GetDifferentialInterpreterEntryCount();
                    result.passed = result.genericParentNoopChecks.Length == 29 && result.genericParentNoopAotEntries > 0 &&
                        result.genericParentNoopInterpreterEntries == 0;
                    result.stage = "generic-parent-noop-complete";
                }
                int assetProbe = Array.IndexOf(args, "-unityAssetProbe");
                if (assetProbe >= 0)
                {
                    if (!result.passed) throw new InvalidOperationException("Business entry failed before Unity assets.");
                    int revisionIndex = Array.IndexOf(args, "-unityAssetRevision");
                    if (revisionIndex < 0) throw new ArgumentException("Missing authored asset revision.");
                    result.passed = false; result.stage = "unity-assets";
                    UnityAssetPlayer.Begin(args[assetProbe + 1] == "current", int.Parse(args[revisionIndex + 1]), (checks, failure) => {
                        result.unityAssetChecks = checks; result.error = failure; result.passed = failure == null; result.stage = "unity-assets-complete";
                        File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true)); Application.Quit(result.passed ? 0 : 1);
                    });
                    return;
                }
                int unityProbe = Array.IndexOf(args, "-unityBehaviourProbe");
                if (unityProbe >= 0)
                {
                    if (!result.passed) throw new InvalidOperationException("Business entry failed before Unity callback validation.");
                    result.passed = false; result.stage = "unity-component-frames";
                    int expectedDelta = args[unityProbe + 1] == "current" ? 2 : result.unityBaseDelta;
                    bool[] expectedSelections = { false, false };
                    if (args[unityProbe + 1] == "current")
                    {
                        int selections = Array.IndexOf(args, "-unityBehaviourSelection");
                        if (selections < 0 || selections + 1 >= args.Length)
                            throw new ArgumentException("Current lifecycle validation needs bound method selections.");
                        expectedSelections = args[selections + 1].Split(',').Select(bool.Parse).ToArray();
                    }
                    UnityBehaviourPlayer.Begin(expectedDelta, expectedSelections, (checks, failure) => {
                        result.unityChecks = checks; result.error = failure; result.passed = failure == null; result.stage = "unity-component-complete";
                        File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true)); Application.Quit(result.passed ? 0 : 1);
                    });
                    return;
                }
            }
            catch (Exception exception) { result.error = exception.ToString(); }
            File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true));
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
