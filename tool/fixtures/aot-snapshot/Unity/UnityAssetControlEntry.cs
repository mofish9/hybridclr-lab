using System;
using System.IO;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    internal static class UnityAssetControlEntry
    {
        [Serializable] private sealed class Result { public bool passed; public string error; public string[] checks; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            var args = Environment.GetCommandLineArgs();
            string report = args[Array.IndexOf(args, "-controlResult") + 1];
            int revision = int.Parse(args[Array.IndexOf(args, "-unityAssetRevision") + 1]);
            UnityAssetPlayer.Begin(true, revision, (checks, error) => {
                bool passed = error == null && checks.Length == 42;
                File.WriteAllText(report, JsonUtility.ToJson(new Result { passed = passed, error = error, checks = checks }, true));
                Application.Quit(passed ? 0 : 1);
            });
        }
    }
}
