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
            public string[] plannedAssemblies, loadedAssemblyNames, differentialAssemblies, interpreterOnlyAssemblies;
            public string[] records;
            public long ordinaryAotReferenceResult;
            public long ordinaryAotEchoExtra;
            public int ordinaryAotStaticNeighbor;
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
            if (Array.IndexOf(args, "-frozenEntryPlan") >= 0) return;
            int index = Array.IndexOf(args, "-snapshotResult");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("-snapshotResult");
            var result = new Result();
            try
            {
                var identity = DheBuildIdentity.Create();
                result.baseId = identity.BaseId; result.aotAnalysisSnapshotSha256 = identity.AotAnalysisSnapshotSha256;
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
                if (!DheRuntime.LoadCurrentAssemblyImages(result.plannedAssemblies,
                    result.plannedAssemblies.Select(name => provider.LoadBytes(records[name].current)).ToArray(), out var code, out error))
                    throw new InvalidDataException(code + ":" + error);
                // LoadedAssemblyNames also includes authenticated frozen AOT
                // sources. Count only the Current payload for this assertion.
                result.loadedAssemblyNames = DheRuntime.LoadedAssemblyNames;
                result.loadedAssemblies = result.plannedAssemblies.Intersect(result.loadedAssemblyNames, StringComparer.OrdinalIgnoreCase).Count();
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
            }
            catch (Exception exception) { result.error = exception.ToString(); }
            File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true));
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
