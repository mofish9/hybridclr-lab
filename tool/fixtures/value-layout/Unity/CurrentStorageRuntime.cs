using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace HybridCLR.Lab
{
    // A diagnostic entry to the candidate native image preparation path.
    // This is not the public package loader or a full-workflow pass.
    public static class CurrentStorageRuntime
    {
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern int Load(byte[][] dlls, byte[][] baseMv, byte[][] currentMv, uint[][] types, uint[][] methods);
        [MethodImpl(MethodImplOptions.InternalCall)]
        public static extern MethodInfo Resolve(MethodInfo method);

        [Serializable] private class Selection { public string name; public uint[] types; public uint[] methods; }
        [Serializable] private class Plan { public string format; public bool releaseReady; public Selection[] assemblies; }
        [Serializable] private class Result
        {
            public bool passed; public string stage; public string error;
            public int loadCode = -1; public string[] records; public bool consumerPassed;
            public int interpreterEntries; public string scope = "Current storage research probe";
        }

        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor) return;
            string root = Argument("-currentStoragePayload"), output = Argument("-currentStorageResult");
            if (string.IsNullOrEmpty(root) || string.IsNullOrEmpty(output))
                throw new ArgumentException("Missing Current storage probe paths.");
            if (File.Exists(output)) throw new IOException("Result output must be new.");
            var result = new Result();
            Action save = () => { File.WriteAllText(output, JsonUtility.ToJson(result, true)); Debug.Log("Current storage stage: " + result.stage); };
            try
            {
                result.stage = "read-payload"; save();
                var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Path.Combine(root, "plan.json")));
                if (plan.format != "hybridclr.dhe-current-storage-probe" || plan.releaseReady) throw new InvalidDataException("Not a research probe plan.");
                var dlls = plan.assemblies.Select(item => File.ReadAllBytes(Path.Combine(root, "current", item.name + ".dll"))).ToArray();
                var before = plan.assemblies.Select(item => File.ReadAllBytes(Path.Combine(root, "base", item.name + ".mv"))).ToArray();
                var after = plan.assemblies.Select(item => File.ReadAllBytes(Path.Combine(root, "current", item.name + ".mv"))).ToArray();
                result.stage = "load-images"; save();
                result.loadCode = Load(dlls, before, after, plan.assemblies.Select(item => item.types).ToArray(), plan.assemblies.Select(item => item.methods).ToArray());
                if (result.loadCode != 0) throw new InvalidOperationException("DHE load failed: " + result.loadCode);
                result.stage = "model"; save();
                MethodInfo model = Assembly.Load("HybridCLR.ValueLayoutModel").GetType("HybridCLR.Lab.ValueLayout.ValueLayoutProbe", true).GetMethod("Run");
                result.records = (string[])Resolve(model).Invoke(null, null);
                result.stage = "consumer-and-native-boundary"; save();
                MethodInfo consumer = Assembly.Load("HybridCLR.ValueLayoutConsumer").GetType("HybridCLR.Lab.ValueLayoutConsumer.Calls", true).GetMethod("Run");
                result.consumerPassed = (bool)Resolve(consumer).Invoke(null, null);
                result.passed = result.records.Length == 14 && result.records.All(record => record.EndsWith("\tpassed", StringComparison.Ordinal)) && result.consumerPassed;
                result.interpreterEntries = HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
                result.stage = "complete";
            }
            catch (Exception error) { result.error = error.ToString(); Debug.LogException(error); }
            save();
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
