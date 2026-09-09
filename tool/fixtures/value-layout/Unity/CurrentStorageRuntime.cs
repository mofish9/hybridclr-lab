using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace HybridCLR.Lab
{
    // Public native API qualification. Resource build/staging/identity handling
    // remains a separate gate; ordinary reflection must need no lab resolver.
    public static class CurrentStorageRuntime
    {
        [Serializable] private class Selection { public string name; public uint[] types; public uint[] methods; }
        [Serializable] private class Plan { public string format; public bool releaseReady; public Selection[] assemblies; }
        [Serializable] private class Result
        {
            public bool passed; public string stage; public string error;
            public int loadCode = -1; public string[] records; public bool consumerPassed;
            public int interpreterEntries; public string scope = "Public RuntimeApi storage transaction and ordinary reflection; resource workflow not qualified";
            public int rejectedInvalidPlans;
            public bool reflectionPassed, revisionPassed;
            public bool reflectionSignaturePassed, reflectionValueCallsPassed;
            public int directRevision, reflectedRevision, revisionInterpreterEntries, unchangedAotEntries;
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
                var types = plan.assemblies.Select(item => item.types).ToArray();
                var methods = plan.assemblies.Select(item => item.methods).ToArray();
                result.stage = "reject-invalid-plans"; save();
                Action<uint[][], uint[][]> reject = (selectedTypes, selectedMethods) =>
                {
                    var code = RuntimeApi.LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlan(
                        dlls, before, after, selectedTypes, selectedMethods);
                    if (code != LoadImageErrorCode.DHE_MV_BAD_FORMAT)
                        throw new InvalidOperationException("Invalid plan was not rejected: " + code);
                    result.rejectedInvalidPlans++;
                };
                reject(null, methods);
                reject(Array.Empty<uint[]>(), methods);
                var invalidTypes = (uint[][])types.Clone(); invalidTypes[0] = null;
                reject(invalidTypes, methods);
                invalidTypes[0] = new[] { 0x02ffffffu }; reject(invalidTypes, methods);
                var invalidMethods = (uint[][])methods.Clone();
                invalidMethods[0] = new[] { 0x06000001u, 0x06000001u };
                reject(types, invalidMethods);
                result.stage = "load-images"; save();
                result.loadCode = (int)RuntimeApi.LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlan(
                    dlls, before, after, types, methods);
                if (result.loadCode != 0) throw new InvalidOperationException("DHE load failed: " + result.loadCode);
                result.stage = "revision"; save();
                int expectedRevision = int.Parse(Argument("-expectedRevision") ?? "41");
                bool requireGuards = Argument("-requireGuards") == "true";
                HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
                result.directRevision = ValueLayout.Factory.GetRevision();
                result.revisionInterpreterEntries = HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
                HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
                int sentinel = ValueLayout.Factory.UnchangedRevision();
                result.unchangedAotEntries = HybridCLR.RuntimeApi.GetDifferentialAotEntryCount();
                MethodInfo revision = typeof(ValueLayout.Factory).GetMethod("GetRevision");
                result.reflectedRevision = (int)revision.Invoke(null, null);
                result.revisionPassed = result.directRevision == expectedRevision &&
                    result.reflectedRevision == expectedRevision && sentinel == 5 &&
                    !HybridCLR.RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("UnchangedRevision")) &&
                    (!requireGuards || (result.unchangedAotEntries > 0 &&
                    (expectedRevision == 41 ? result.revisionInterpreterEntries == 0 : result.revisionInterpreterEntries > 0)));
                result.stage = "reflection"; save();
                object box = typeof(ValueLayout.Factory).GetMethod("Create").Invoke(null, null);
                Type payloadType = box.GetType();
                Type callsType = Assembly.Load("HybridCLR.ValueLayoutConsumer").GetType("HybridCLR.Lab.ValueLayoutConsumer.Calls", true);
                MethodInfo copyMethod = callsType.GetMethod("DirectCopy"), refMethod = callsType.GetMethod("RefRoundTrip");
                result.reflectionSignaturePassed = typeof(ValueLayout.Factory).GetMethod("Create").ReturnType == payloadType &&
                    copyMethod.ReturnType == payloadType && copyMethod.GetParameters()[0].ParameterType == payloadType &&
                    refMethod.GetParameters()[0].ParameterType.GetElementType() == payloadType &&
                    copyMethod.GetParameters()[0].Member == copyMethod &&
                    copyMethod.GetParameters()[0].Member.DeclaringType == callsType;
                object copied = copyMethod.Invoke(null, new[] { box });
                object genericCopy = typeof(ValueLayout.Factory).GetMethod("Identity").MakeGenericMethod(payloadType).Invoke(null, new[] { box });
                object[] refArgs = { box }; refMethod.Invoke(null, refArgs);
                MethodInfo hasCurrent = typeof(ValueLayout.Factory).GetMethod("HasCurrentFields");
                MethodInfo nullableCopy = callsType.GetMethod("NullableCopy");
                object nullableValue = nullableCopy.Invoke(null, new[] { box });
                result.reflectionValueCallsPassed = new[] { copied, genericCopy, refArgs[0], nullableValue }
                    .All(value => value != null && value.GetType() == payloadType && (bool)hasCurrent.Invoke(null, new[] { value })) &&
                    nullableCopy.Invoke(null, new object[] { null }) == null;
                FieldInfo count = box.GetType().GetField("Count");
                count.SetValue(box, 29);
                object foreign = Activator.CreateInstance(Assembly.Load("HybridCLR.ValueLayoutOther")
                    .GetType("HybridCLR.Lab.ValueLayout.Payload", true));
                bool foreignRejected = false;
                try { count.GetValue(foreign); }
                catch (ArgumentException) { foreignRejected = true; }
                bool foreignArgumentRejected = false;
                try { copyMethod.Invoke(null, new[] { foreign }); }
                catch (ArgumentException) { foreignArgumentRejected = true; }
                result.reflectionPassed = count.DeclaringType.IsInstanceOfType(box) &&
                    (int)count.GetValue(box) == 29 && foreignRejected && foreignArgumentRejected &&
                    result.reflectionSignaturePassed && result.reflectionValueCallsPassed;
                result.stage = "model"; save();
                MethodInfo model = Assembly.Load("HybridCLR.ValueLayoutModel").GetType("HybridCLR.Lab.ValueLayout.ValueLayoutProbe", true).GetMethod("Run");
                result.records = (string[])model.Invoke(null, null);
                result.stage = "consumer-and-native-boundary"; save();
                MethodInfo consumer = Assembly.Load("HybridCLR.ValueLayoutConsumer").GetType("HybridCLR.Lab.ValueLayoutConsumer.Calls", true).GetMethod("Run");
                result.consumerPassed = (bool)consumer.Invoke(null, null);
                result.passed = result.records.Length == 14 && result.records.All(record => record.EndsWith("\tpassed", StringComparison.Ordinal)) && result.consumerPassed && result.reflectionPassed && result.revisionPassed;
                result.interpreterEntries = HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
                result.stage = "complete";
            }
            catch (Exception error) { result.error = error.ToString(); Debug.LogException(error); }
            save();
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
