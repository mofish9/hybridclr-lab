using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using HybridCLR.Lab.ValueLayoutNative;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    public static class MixedTransactionPlayer
    {
        [Serializable] private class Record
        { public string name, dll, before, after, dllSha256, beforeSha256, afterSha256, invalidBefore, invalidBeforeSha256; public int sourceKind; public uint[] types, methods, excluded, conditional; }
        [Serializable] private class Peer { public string name, dll, sha256; }
        [Serializable] private class Plan { public string format, baseId; public Record[] records; public Peer[] peers; }
        [Serializable] private class Check { public string name; public bool passed; }
        [Serializable] private class Result
        { public bool passed; public int pid, revision, failureCode = -1, retryCode = -1; public string mode, stage, error; public string[] moduleInitializers; public Check[] checks; }
        private static readonly List<Check> Checks = new List<Check>();
        private static readonly List<string> Modules = new List<string>();
        private static byte[][] Dlls, Before, After, Peers;
        private static uint[][] Types, Methods, Excluded, Conditional;
        private static int[] Kinds;
        private static Plan ActivePlan;
        private static string Mode;
        private static string Argument(string name)
        { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        private static void CheckValue(string name, bool value)
        { Checks.Add(new Check { name = name, passed = value }); if (!value) throw new InvalidOperationException(name); }
        private static LoadImageErrorCode Load(byte[][] before = null, byte[][] peers = null) =>
            RuntimeApi.LoadDifferentialHybridAssemblyBatch(Dlls, before ?? Before, After, Types, Methods, Kinds, Excluded, Conditional, peers ?? Peers);
        private static bool Visible(string name) => AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == name);

        // Called by the actual <Module> .cctor of each separately compiled DLL.
        [UnityEngine.Scripting.Preserve]
        public static void ModuleInitialized(string name)
        {
            Modules.Add(name);
            CheckValue("initializer-once:" + name, Modules.Count(value => value == name) == 1);
            CheckValue("initializer-all-peers-visible:" + name, ActivePlan.peers.All(peer => Visible(peer.name)));
            CheckValue("initializer-current-value:" + name, CurrentValueCopy());
            Exception workerError = null; LoadImageErrorCode nested = LoadImageErrorCode.NOT_IMPLEMENT;
            var worker = new Thread(() => {
                try
                {
                    if (Assembly.Load("HybridCLR.ValueLayoutAdded").GetType("HybridCLR.Lab.AddedResource.AddedProbe", true) == null)
                        throw new InvalidOperationException("Missing interpreter peer");
                    nested = Load();
                }
                catch (Exception error) { workerError = error; }
            }) { IsBackground = true };
            worker.Start();
            CheckValue("initializer-load-lock-released:" + name, worker.Join(10000));
            if (workerError != null) throw workerError;
            CheckValue("initializer-nested-duplicate-rejected:" + name, nested == LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED);
            if (Mode == "initializer-failure" && name == "HybridCLR.TransactionInitializer")
                throw new InvalidOperationException("mixed-module-expected-failure");
        }

        private static bool CurrentValueCopy()
        {
            object current = ValueLayout.Factory.Create();
            FieldInfo extra = current.GetType().GetField("Extra"), reference = current.GetType().GetField("Reference");
            if (extra == null || reference == null) return false;
            object marker = new object(); extra.SetValue(current, 91000000013L); reference.SetValue(current, marker);
            object copied = NativeBoundary.FrozenCopyBox(current);
            return !ReferenceEquals(copied, current) && (int)copied.GetType().GetField("Count").GetValue(copied) == 17 &&
                (long)extra.GetValue(copied) == 91000000013L && ReferenceEquals(reference.GetValue(copied), marker);
        }
        private static void OldState(string stage)
        {
            RuntimeApi.ResetDifferentialDispatchCounters();
            object original = ValueLayout.Factory.Create(); object copy = NativeBoundary.FrozenCopyBox(original);
            int native = RuntimeApi.GetDifferentialAotEntryCount(), interpreted = RuntimeApi.GetDifferentialInterpreterEntryCount();
            CheckValue(stage + ":base-aot-dispatch", native > 0 && interpreted == 0);
            CheckValue(stage + ":base-value-and-fields", (int)copy.GetType().GetField("Count").GetValue(copy) == 17 && copy.GetType().GetField("Extra") == null);
            CheckValue(stage + ":peers-not-published", ActivePlan.peers.All(peer => !Visible(peer.name)));
            CheckValue(stage + ":no-module-side-effects", Modules.Count == 0);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor || Argument("-mixedTransactionPlan") == null) return;
            var result = new Result { mode = Mode = Argument("-mixedTransactionMode") ?? "retry", pid = System.Diagnostics.Process.GetCurrentProcess().Id };
            string output = Argument("-mixedTransactionResult");
            void Save(string stage)
            { result.stage = stage; result.checks = Checks.ToArray(); result.moduleInitializers = Modules.ToArray(); File.WriteAllText(output, JsonUtility.ToJson(result, true)); }
            try
            {
                Save("read-plan");
                ActivePlan = JsonUtility.FromJson<Plan>(File.ReadAllText(Argument("-mixedTransactionPlan")));
                CheckValue("plan-bound-to-base", ActivePlan.format == "hybridclr.mixed-transaction-probe" && ActivePlan.baseId == DheBuildIdentity.Create().BaseId);
                CheckValue("known-mode", Mode == "retry" || Mode == "retry-reversed" || Mode == "initializer-failure");
                byte[] Read(string path, string hash)
                {
                    byte[] bytes = File.ReadAllBytes(path);
                    using (var sha = SHA256.Create())
                        CheckValue("hash:" + Path.GetFileName(path), BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").Equals(hash, StringComparison.OrdinalIgnoreCase));
                    return bytes;
                }
                Dlls = ActivePlan.records.Select(row => Read(row.dll, row.dllSha256)).ToArray();
                Before = ActivePlan.records.Select(row => Read(row.before, row.beforeSha256)).ToArray();
                After = ActivePlan.records.Select(row => Read(row.after, row.afterSha256)).ToArray();
                Peers = ActivePlan.peers.Select(row => Read(row.dll, row.sha256)).ToArray();
                Types = ActivePlan.records.Select(row => row.types).ToArray(); Methods = ActivePlan.records.Select(row => row.methods).ToArray();
                Kinds = ActivePlan.records.Select(row => row.sourceKind).ToArray(); Excluded = ActivePlan.records.Select(row => row.excluded).ToArray();
                Conditional = ActivePlan.records.Select(row => row.conditional).ToArray();
                CheckValue("three-source-kinds", Kinds.Contains(0) && Kinds.Contains(1) && Peers.Length == 3);
                OldState("before");
                int invalidIndex = Array.FindIndex(ActivePlan.records, row => !string.IsNullOrEmpty(row.invalidBefore));
                CheckValue("registration-fault-present", invalidIndex >= 0);
                var invalid = (byte[][])Before.Clone(); invalid[invalidIndex] = Read(ActivePlan.records[invalidIndex].invalidBefore, ActivePlan.records[invalidIndex].invalidBeforeSha256);
                Save("reject-registration"); result.failureCode = (int)Load(invalid);
                CheckValue("registration-rejected", result.failureCode == (int)LoadImageErrorCode.DHE_MV_REGISTRATION_FAILED);
                OldState("after-registration-failure");
                var changedPeers = (byte[][])Peers.Clone(); changedPeers[0] = changedPeers[0].Concat(new byte[] { 0 }).ToArray();
                CheckValue("changed-peer-retry-rejected", Load(peers: changedPeers) == LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED);
                CheckValue("missing-peer-retry-rejected", Load(peers: Peers.Take(Peers.Length - 1).ToArray()) == LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED);
                OldState("after-invalid-retries");
                if (Mode == "retry-reversed")
                {
                    Array.Reverse(Dlls); Array.Reverse(Before); Array.Reverse(After); Array.Reverse(Types); Array.Reverse(Methods);
                    Array.Reverse(Kinds); Array.Reverse(Excluded); Array.Reverse(Conditional); Array.Reverse(Peers);
                }
                Save("corrected-retry"); Exception initializerError = null;
                try { result.retryCode = (int)Load(); } catch (Exception error) { initializerError = error; }
                if (Mode == "initializer-failure")
                {
                    CheckValue("module-exception-propagated", initializerError is TypeInitializationException && initializerError.ToString().Contains("mixed-module-expected-failure"));
                    CheckValue("module-failure-keeps-committed-metadata", ActivePlan.peers.All(peer => Visible(peer.name)) && CurrentValueCopy());
                    CheckValue("module-failure-does-not-rerun-batch", Load() == LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED);
                    CheckValue("module-failure-ran-once", Modules.Count == 1 && Modules[0] == "HybridCLR.TransactionInitializer");
                }
                else
                {
                    if (initializerError != null) throw initializerError;
                    CheckValue("corrected-retry-committed", result.retryCode == 0);
                    CheckValue("both-initializers-completed", Modules.Count == 2 && Modules.Distinct().Count() == 2);
                    Save("business-entry"); result.revision = ValueLayout.Factory.GetRevision();
                    CheckValue("full-current-entry", result.revision == 73);
                    CheckValue("duplicate-batch-rejected", Load() == LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED);
                    CheckValue("duplicate-does-not-run-modules", Modules.Count == 2);
                }
                result.passed = Checks.All(check => check.passed); Save("complete");
            }
            catch (Exception error) { result.error = error.ToString(); Save(result.stage); }
            Application.Quit(result.passed ? 0 : 1);
        }
    }
}
