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
        [Serializable] private class Record { public string name, dll, before, after, dllSha256, beforeSha256, afterSha256, invalidBefore, invalidBeforeSha256; public int sourceKind; public uint[] types, methods, excluded, conditional; }
        [Serializable] private class Plan { public string format, baseId; public bool releaseReady; public Record[] records; }
        [Serializable] private class Check { public string name; public bool passed; }
        [Serializable] private class Result { public bool passed; public string stage, error, capability; public int loadCode = -1, failedBatchAotCount, failedBatchInterpreterCount; public bool failedBatchExtraVisible; public Check[] checks; }
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
            var result = new Result { capability = Argument("-frozenEntryCapability") ?? "core" }; var checks = new List<Check>();
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
                Type ModelType(string name) => typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.ValueLayout." + name, true);
                object originalNested = Activator.CreateInstance(ModelType("Nested"));
                originalNested.GetType().GetField("Value").SetValue(originalNested, original);
                originalNested.GetType().GetField("Tail").SetValue(originalNested, 163);
                object originalGeneric = Activator.CreateInstance(ModelType("GenericValue`1").MakeGenericType(original.GetType()));
                originalGeneric.GetType().GetField("Value").SetValue(originalGeneric, original);
                originalGeneric.GetType().GetField("Marker").SetValue(originalGeneric, (short)167);
                object oldReference = new object();
                object originalReferenceGeneric = Activator.CreateInstance(ModelType("GenericValue`1").MakeGenericType(typeof(object)));
                originalReferenceGeneric.GetType().GetField("Value").SetValue(originalReferenceGeneric, oldReference);
                originalReferenceGeneric.GetType().GetField("Marker").SetValue(originalReferenceGeneric, (short)179);
                object originalOther = Activator.CreateInstance(ModelType("Retyped"));
                originalOther.GetType().GetField("Value").SetValue(originalOther, 173);
                var oldWeak = new WeakReference(original);
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
                var conditional = plan.records.Select(row => row.conditional ?? Array.Empty<uint>()).ToArray();
                Check("frozen-and-mutable-batch", kinds.Contains(1) && kinds.Contains(0));
                int retryIndex = Array.FindIndex(plan.records, row => !string.IsNullOrEmpty(row.invalidBefore));
                if (retryIndex >= 0)
                {
                    Save("invalid-base-registration");
                    var invalid = (byte[][])before.Clone();
                    invalid[retryIndex] = Read(plan.records[retryIndex].invalidBefore, plan.records[retryIndex].invalidBeforeSha256);
                    var failure = RuntimeApi.LoadDifferentialHybridAssemblySources(
                        dlls, invalid, after, types, methods, kinds, excluded, conditional);
                    Check("invalid-base-rejected-after-metadata-preparation", failure == LoadImageErrorCode.DHE_MV_REGISTRATION_FAILED);
                    RuntimeApi.ResetDifferentialDispatchCounters();
                    object retained = NativeBoundary.FrozenCopyBox(original);
                    result.failedBatchAotCount = RuntimeApi.GetDifferentialAotEntryCount();
                    result.failedBatchInterpreterCount = RuntimeApi.GetDifferentialInterpreterEntryCount();
                    result.failedBatchExtraVisible = retained.GetType().GetField("Extra") != null;
                    Check("failed-batch-keeps-base-dispatch", result.failedBatchAotCount > 0 && result.failedBatchInterpreterCount == 0);
                    Check("failed-batch-keeps-base-field-view", !result.failedBatchExtraVisible);
                    Check("failed-batch-keeps-base-value", (int)retained.GetType().GetField("Count").GetValue(retained) == 17);
                    int conditionalIndex = Array.FindIndex(conditional, tokens => tokens.Length != 0);
                    if (conditionalIndex >= 0)
                    {
                        var changedConditions = (uint[][])conditional.Clone();
                        changedConditions[conditionalIndex] = Array.Empty<uint>();
                        var changedRetry = RuntimeApi.LoadDifferentialHybridAssemblySources(
                            dlls, before, after, types, methods, kinds, excluded, changedConditions);
                        Check("retry-rejects-changed-generic-conditions", changedRetry == LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED);
                    }
                }
                Save("load-frozen-and-mutable");
                result.loadCode = (int)RuntimeApi.LoadDifferentialHybridAssemblySources(dlls, before, after, types, methods, kinds, excluded, conditional);
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
                Type consumer = Assembly.Load("HybridCLR.ValueLayoutConsumer").GetType("HybridCLR.Lab.ValueLayoutConsumer.Calls", true);
                bool PreservesCurrent(object value) => value != null && (int)value.GetType().GetField("Count").GetValue(value) == 17 &&
                    (long)extra.GetValue(value) == 90000000001L && ReferenceEquals(reference.GetValue(value), marker);
                Save("capability:" + result.capability);
                switch (result.capability)
                {
                    case "core": break;
                    case "nullable":
                        var nullableCopy = consumer.GetMethod("NullableCopy");
                        object nullable = nullableCopy.Invoke(null, new object[] { current });
                        Check("nullable-copy-preserves-added-fields", PreservesCurrent(nullable));
                        Check("nullable-empty-remains-null", nullableCopy.Invoke(null, new object[] { null }) == null);
                        RuntimeApi.ResetDifferentialDispatchCounters();
                        Check("unaffected-nullable-long", UnchangedNullable(90000000149L) == 90000000149L && UnchangedNullable(null) == -1L);
                        Check("unaffected-nullable-long-remains-aot", RuntimeApi.GetDifferentialAotEntryCount() > 0 && RuntimeApi.GetDifferentialInterpreterEntryCount() == 0);
                        break;
                    case "generics":
                        var genericCopy = consumer.GetMethod("GenericCopy");
                        Type genericType = genericCopy.GetParameters()[0].ParameterType;
                        object generic = Activator.CreateInstance(genericType);
                        FieldInfo valueField = genericType.GetField("Value"), markerField = genericType.GetField("Marker");
                        valueField.SetValue(generic, current); markerField.SetValue(generic, (short)151);
                        object genericResult = genericCopy.Invoke(null, new[] { generic });
                        Check("generic-copy-preserves-added-fields", PreservesCurrent(valueField.GetValue(genericResult)) && (short)markerField.GetValue(genericResult) == 151);
                        object identity = consumer.GetMethod("OpenGenericCopy").MakeGenericMethod(current.GetType()).Invoke(null, new[] { current });
                        Check("open-generic-copy-preserves-added-fields", PreservesCurrent(identity));
                        break;
                    case "collections":
                        Type listType = typeof(List<>).MakeGenericType(current.GetType());
                        var list = (System.Collections.IList)Activator.CreateInstance(listType);
                        for (int i = 0; i < 40; i++) list.Add(current);
                        Check("list-growth-preserves-current-values", list.Count == 40 && PreservesCurrent(list[0]) && PreservesCurrent(list[39]));
                        list.Insert(5, current); list.RemoveAt(2);
                        Array listArray = (Array)listType.GetMethod("ToArray").Invoke(list, null);
                        Check("list-shift-and-to-array-preserve-current-values", listArray.Length == 40 && PreservesCurrent(listArray.GetValue(5)) && PreservesCurrent(listArray.GetValue(39)));
                        Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(int), current.GetType());
                        var dictionary = (System.Collections.IDictionary)Activator.CreateInstance(dictionaryType);
                        for (int i = 0; i < 40; i++) dictionary.Add(i, current);
                        Check("dictionary-growth-preserves-current-values", dictionary.Count == 40 && PreservesCurrent(dictionary[0]) && PreservesCurrent(dictionary[39]));
                        object[] lookup = { 37, null };
                        bool found = (bool)dictionaryType.GetMethod("TryGetValue").Invoke(dictionary, lookup);
                        Check("dictionary-try-get-value-preserves-current-byref", found && PreservesCurrent(lookup[1]));
                        RuntimeApi.ResetDifferentialDispatchCounters();
                        Check("unaffected-long-collections", UnchangedCollections() == 780L);
                        Check("unaffected-long-collections-remain-aot", RuntimeApi.GetDifferentialAotEntryCount() > 0 && RuntimeApi.GetDifferentialInterpreterEntryCount() == 0);
                        break;
                    case "arrays-byref":
                        Array values = Array.CreateInstance(current.GetType(), 2);
                        values.SetValue(current, 0);
                        Array clone = (Array)values.Clone();
                        Check("array-clone-preserves-added-fields", PreservesCurrent(clone.GetValue(0)));
                        object element = consumer.GetMethod("ArrayElement").Invoke(null, new object[] { values, 0 });
                        Check("array-element-preserves-added-fields", PreservesCurrent(element));
                        var byref = new[] { current };
                        consumer.GetMethod("RefRoundTrip").Invoke(null, byref);
                        Check("byref-copy-preserves-added-fields", PreservesCurrent(byref[0]));
                        break;
                    case "old-values":
                        object migrated = NativeBoundary.FrozenCopyBox(original);
                        Check("old-box-copy-retains-original-value", (int)migrated.GetType().GetField("Count").GetValue(migrated) == 17);
                        Check("old-box-copy-defaults-added-fields", (long)extra.GetValue(migrated) == 0L && reference.GetValue(migrated) == null);
                        Check("old-box-copy-is-independent", !ReferenceEquals(original, migrated) && (int)original.GetType().GetField("Count").GetValue(original) == 17);
                        extra.SetValue(migrated, 181L); reference.SetValue(migrated, marker);
                        object repeated = NativeBoundary.FrozenCopyBox(original);
                        Check("old-box-repeated-copy-is-independent", (long)extra.GetValue(repeated) == 0L && reference.GetValue(repeated) == null);
                        object nestedCopy = NativeBoundary.FrozenNestedCopyBox(originalNested);
                        object nestedValue = nestedCopy.GetType().GetField("Value").GetValue(nestedCopy);
                        Check("old-nested-copy-preserves-fields", (int)nestedCopy.GetType().GetField("Tail").GetValue(nestedCopy) == 163 &&
                            (int)nestedValue.GetType().GetField("Count").GetValue(nestedValue) == 17 && (long)extra.GetValue(nestedValue) == 0L && reference.GetValue(nestedValue) == null);
                        object genericCopyResult = NativeBoundary.FrozenGenericCopyBox(originalGeneric);
                        object genericValue = genericCopyResult.GetType().GetField("Value").GetValue(genericCopyResult);
                        Check("old-generic-copy-preserves-fields", (short)genericCopyResult.GetType().GetField("Marker").GetValue(genericCopyResult) == 167 &&
                            (int)genericValue.GetType().GetField("Count").GetValue(genericValue) == 17 && (long)extra.GetValue(genericValue) == 0L && reference.GetValue(genericValue) == null);
                        RuntimeApi.ResetDifferentialDispatchCounters();
                        object referenceGenericCopy = NativeBoundary.FrozenReferenceGenericCopyBox(originalReferenceGeneric);
                        Check("unaffected-generic-reference-copy-remains-aot", RuntimeApi.GetDifferentialAotEntryCount() > 0 && RuntimeApi.GetDifferentialInterpreterEntryCount() == 0);
                        Check("old-generic-copy-preserves-reference-identity", ReferenceEquals(referenceGenericCopy.GetType().GetField("Value").GetValue(referenceGenericCopy), oldReference) &&
                            (short)referenceGenericCopy.GetType().GetField("Marker").GetValue(referenceGenericCopy) == 179);
                        bool wrongTypeRejected = false;
                        try { NativeBoundary.FrozenCopyBox(originalOther); }
                        catch (InvalidCastException) { wrongTypeRejected = true; }
                        Check("unrelated-old-box-still-rejected", wrongTypeRejected);
                        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                        Check("old-and-current-boxes-survive-gc", oldWeak.IsAlive && (int)original.GetType().GetField("Count").GetValue(original) == 17 &&
                            (long)extra.GetValue(migrated) == 181L && ReferenceEquals(reference.GetValue(migrated), marker));
                        GC.KeepAlive(original); GC.KeepAlive(migrated);
                        break;
                    default: throw new ArgumentException("Unknown frozen entry capability: " + result.capability);
                }
                result.passed = true; Save("complete");
            }
            catch (Exception error) { result.error = error.ToString(); Save(result.stage); }
            Application.Quit(result.passed ? 0 : 1);
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long UnchangedNullable(long? value) => value.GetValueOrDefault(-1L);
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static long UnchangedCollections()
        {
            var list = new List<long>(); var dictionary = new Dictionary<int, long>();
            for (int i = 0; i < 40; i++) { list.Add(i); dictionary.Add(i, i); }
            long sum = 0;
            for (int i = 0; i < list.Count; i++) sum += list[i] + dictionary[i];
            return sum / 2;
        }
    }
}
