using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    internal static class UnityDeliveryPlayer
    {
        internal static DheDelivery Active;
        [Serializable] private sealed class Result
        {
            public bool passed, metadataCommitted, restartRequired;
            public string baseId, manifestSha256, error, stage;
            public string[] checks;
            public int revision;
        }
        private sealed class Files : IDheRuntimeStreamingAssetProvider
        {
            internal string Root;
            private string PathFor(string path)
            {
                DheDeliveryManifest.RequirePath(path);
                return Path.Combine(Root, path.Replace('/', Path.DirectorySeparatorChar));
            }
            public bool Exists(string path) => File.Exists(PathFor(path));
            public byte[] LoadBytes(string path) => File.ReadAllBytes(PathFor(path));
            public string LoadText(string path) => File.ReadAllText(PathFor(path));
            public Stream OpenRead(string path) => File.OpenRead(PathFor(path));
        }
        private sealed class Embedded : IDheRuntimeAssetProvider
        {
            private string PathFor(string path)
            {
                const string root = "Assets/StreamingAssets/";
                DheDeliveryManifest.RequirePath(path);
                if (!path.StartsWith(root, StringComparison.Ordinal)) throw new IOException("Invalid embedded path.");
                return Path.Combine(Application.streamingAssetsPath, path.Substring(root.Length));
            }
            public bool Exists(string path) => File.Exists(PathFor(path));
            public byte[] LoadBytes(string path) => File.ReadAllBytes(PathFor(path));
            public string LoadText(string path) => File.ReadAllText(PathFor(path));
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            string[] args = Environment.GetCommandLineArgs(); int at = Array.IndexOf(args, "-dheDeliveryRoot");
            if (Application.isEditor || at < 0) return;
            string report = args[Array.IndexOf(args, "-snapshotResult") + 1];
            string hash = args[Array.IndexOf(args, "-dheDeliveryHash") + 1];
            var result = new Result { manifestSha256 = hash, stage = "prepare" };
            void Finish(string error)
            {
                result.error = error; result.passed = error == null;
                result.metadataCommitted = DheRuntime.MetadataCommitted; result.restartRequired = DheRuntime.RestartRequired;
                File.WriteAllText(report, JsonUtility.ToJson(result, true)); Application.Quit(result.passed ? 0 : 1);
            }
            try
            {
                var identity = DheBuildIdentity.Create(); result.baseId = identity.BaseId;
                if (!DheRuntime.TryPrepareDelivery(new Files { Root = args[at + 1] }, new Embedded(), identity,
                        "dhe-delivery.json", hash, out var delivery, out string error)) throw new InvalidDataException(error);
                result.stage = "load";
                if (!delivery.LoadCurrentAssemblies(out _, out error)) throw new InvalidDataException(error);
                Active = delivery;
                UnityAssetPlayer.LoadDeliveryAsset = delivery.LoadAssetBytes;
                result.stage = "business";
                result.revision = (int)typeof(ValueLayout.Factory).GetMethod("GetRevision").Invoke(null, null);
                if (result.revision != 73) throw new InvalidDataException("Unexpected Current revision.");
                result.stage = "assets";
                UnityAssetPlayer.Begin(true, 2, (checks, failure) => {
                    result.checks = checks; result.stage = "complete";
                    Finish(failure ?? (checks.Length == 42 ? null : "Incomplete asset checks."));
                });
            }
            catch (Exception exception) { Finish(exception.ToString()); }
        }
    }
}
