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
            public string baseId, aotAnalysisSnapshotSha256, error;
            public int loadedAssemblies, revision, sentinel;
        }
        private sealed class Provider : IDheRuntimeAssetProvider
        {
            private string PathFor(string path) => Path.Combine(Application.streamingAssetsPath,
                path.Substring("Assets/StreamingAssets/".Length));
            public bool Exists(string path) => File.Exists(PathFor(path));
            public byte[] LoadBytes(string path) => File.ReadAllBytes(PathFor(path));
            public string LoadText(string path) => File.ReadAllText(PathFor(path));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor) return;
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-snapshotResult");
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException("-snapshotResult");
            var result = new Result();
            try
            {
                var identity = DheBuildIdentity.Create();
                result.baseId = identity.BaseId; result.aotAnalysisSnapshotSha256 = identity.AotAnalysisSnapshotSha256;
                var provider = new Provider();
                const string root = "Assets/StreamingAssets/SnapshotDHE/";
                const string planPath = root + "DheRuntimePlan.json";
                if (!DheRuntime.Initialize(provider, identity, out string error, planPath, root))
                    throw new InvalidDataException(error);
                var plan = JsonUtility.FromJson<Plan>(provider.LoadText(planPath));
                if (!DheRuntime.LoadAssemblyImages(plan.assemblies.Select(row => row.assemblyName).ToArray(),
                    plan.assemblies.Select(row => provider.LoadBytes(row.current)).ToArray(), out var code, out error))
                    throw new InvalidDataException(code + ":" + error);
                result.loadedAssemblies = plan.assemblies.Length;
                result.revision = ValueLayout.Factory.GetRevision();
                result.sentinel = ValueLayout.Factory.UnchangedRevision();
                result.passed = result.loadedAssemblies == 3 && result.revision == 41 && result.sentinel == 5 &&
                    !RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("UnchangedRevision"));
            }
            catch (Exception exception) { result.error = exception.ToString(); }
            File.WriteAllText(args[index + 1], JsonUtility.ToJson(result, true));
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
