using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using HybridCLR.Lab.ValueLayoutNative;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    public static class FrozenEntryPlayer
    {
        [Serializable] private class Record { public string name, dll, before, after, dllSha256, beforeSha256, afterSha256, invalidBefore, invalidBeforeSha256; public int sourceKind; public uint[] types, methods, excluded; }
        [Serializable] private class Plan { public string format, baseId; public bool releaseReady; public Record[] records; }
        [Serializable] private class Check { public string name; public bool passed; }
        [Serializable] private class Result { public bool passed; public string stage, error; public int loadCode = -1; public Check[] checks; }
        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string FormatException(Exception error) => error.Message + ":" + error.ToString();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor || Argument("-frozenEntryPlan") == null) return;
            string output = Argument("-frozenEntryResult");
            var result = new Result(); var checks = new List<Check>();
            void Check(string name, bool passed)
            {
                checks.Add(new Check { name = name, passed = passed });
                if (!passed) throw new InvalidOperationException(name);
            }
            void Save(string stage) { result.stage = stage; result.checks = checks.ToArray(); File.WriteAllText(output, JsonUtility.ToJson(result, true)); }
            object CreateValue() => typeof(ValueLayout.Factory).GetMethod("Create").Invoke(null, null);
            try
            {
                Save("base-aot-entry");
                var plan = JsonUtility.FromJson<Plan>(File.ReadAllText(Argument("-frozenEntryPlan")));
                Check("research-plan-bound-to-base", !plan.releaseReady && plan.format == "hybridclr.frozen-entry-probe" && plan.baseId == DheBuildIdentity.Create().BaseId);
                object original = CreateValue();
                RuntimeApi.ResetDifferentialDispatchCounters();
                object baseline = NativeBoundary.FrozenCopyBox(original);
                Check("base-direct-copy-aot", RuntimeApi.GetDifferentialAotEntryCount() > 0 && RuntimeApi.GetDifferentialInterpreterEntryCount() == 0);
                Check("base-value-copy", !ReferenceEquals(original, baseline) && (int)baseline.GetType().GetField("Count").GetValue(baseline) == 17);
                Check("base-has-old-layout", baseline.GetType().GetField("Extra") == null);
                byte[] Read(string path, string hash)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    using (var sha = SHA256.Create())
                        Check("hash:" + Path.GetFileName(path), BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Equals(hash, StringComparison.OrdinalIgnoreCase));
                    return bytes;
                }
                var dlls = plan.records.Select(row => Read(row.dll, row.dllSha256)).ToArray();
                var before = plan.records.Select(row => Read(row.before, row.beforeSha256)).ToArray();
                var after = plan.records.Select(row => Read(row.after, row.afterSha256)).ToArray();
                var types = plan.records.Select(row => row.types).ToArray();
                var methods = plan.records.Select(row => row.methods).ToArray();
                var kinds = plan.records.Select(row => row.sourceKind).ToArray();
                var excluded = plan.records.Select(row => row.excluded).ToArray();
                Check("frozen-and-mutable-batch", kinds.Contains(1) && kinds.Contains(0));
                int retryIndex = Array.FindIndex(plan.records, row => !string.IsNullOrEmpty(row.invalidBefore));
                if (retryIndex >= 0)
                {
                    Save("invalid-base-registration");
                    var invalid = (byte[][])before.Clone();
                    invalid[retryIndex] = Read(plan.records[retryIndex].invalidBefore, plan.records[retryIndex].invalidBeforeSha256);
                    var failure = RuntimeApi.LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlanAndSources(
                        dlls, invalid, after, types, methods, kinds, excluded);
                    Check("invalid-base-rejected-after-metadata-preparation", failure == LoadImageErrorCode.DHE_MV_REGISTRATION_FAILED);
                    RuntimeApi.ResetDifferentialDispatchCounters();
                    object retained = NativeBoundary.FrozenCopyBox(original);
                    Check("failed-batch-keeps-base-execution", RuntimeApi.GetDifferentialAotEntryCount() > 0 &&
                        RuntimeApi.GetDifferentialInterpreterEntryCount() == 0 && retained.GetType().GetField("Extra") == null);
                }
                Save("load-frozen-and-mutable");
                result.loadCode = (int)RuntimeApi.LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlanAndSources(dlls, before, after, types, methods, kinds, excluded);
                Check("native-source-transaction", result.loadCode == 0);
                Save("frozen-corlib-virtual-behavior");
                Check("unaffected-exception-virtuals", FormatException(new InvalidOperationException("frozen-virtual-sentinel"))
                    .Contains("frozen-virtual-sentinel"));
                Save("direct-current-copy");
                object current = CreateValue();
                FieldInfo extra = current.GetType().GetField("Extra"), reference = current.GetType().GetField("Reference");
                Check("current-fields-visible", extra != null && reference != null);
                extra.SetValue(current, 90000000001L); object marker = new object(); reference.SetValue(current, marker);
                RuntimeApi.ResetDifferentialDispatchCounters();
                object copied = NativeBoundary.FrozenCopyBox(current);
                Check("current-direct-entry-interpreted", RuntimeApi.GetDifferentialAotEntryCount() > 0 && RuntimeApi.GetDifferentialInterpreterEntryCount() > 0);
                Check("current-direct-copy-preserves-added-fields", !ReferenceEquals(copied, current) &&
                    (long)extra.GetValue(copied) == 90000000001L && ReferenceEquals(reference.GetValue(copied), marker));
                Save("ordinary-inline-owner");
                object inlined = NativeBoundary.FrozenInlineBox(current);
                Check("ordinary-inline-copy-preserves-added-fields", (long)extra.GetValue(inlined) == 90000000001L && ReferenceEquals(reference.GetValue(inlined), marker));
                Save("reflection-value-signature");
                object echoed = typeof(NativeBoundary).GetMethod("Echo").Invoke(null, new object[] { current });
                Check("reflected-value-copy-preserves-added-fields", (long)extra.GetValue(echoed) == 90000000001L && ReferenceEquals(reference.GetValue(echoed), marker));
                RuntimeApi.ResetDifferentialDispatchCounters();
                int sentinel = NativeBoundary.FrozenSentinel();
                Check("unaffected-ordinary-method-remains-aot", sentinel == 137 && RuntimeApi.GetDifferentialAotEntryCount() > 0 && RuntimeApi.GetDifferentialInterpreterEntryCount() == 0);
                Check("unaffected-hotfix-method-remains-unchanged", !RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("UnchangedRevision")));
                result.passed = true; Save("complete");
            }
            catch (Exception error) { result.error = error.ToString(); Save(result.stage); }
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
