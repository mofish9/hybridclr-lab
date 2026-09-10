using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace HybridCLR.Lab.Snapshot
{
    internal static class PublicPrecommitPlayer
    {
        internal static string[] RejectThenRestore(string[] names, byte[][] dlls)
        {
            const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
            var artifacts = (IDictionary)typeof(DheRuntime).GetField("Artifacts", hidden).GetValue(null);
            string Hash(byte[] bytes) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", ""); }
            for (int index = 0; index < names.Length; ++index)
            {
                object artifact = artifacts[names[index]];
                if (!Hash(dlls[index]).Equals((string)artifact.GetType().GetField("ExpectedCurrentSha256").GetValue(artifact), StringComparison.OrdinalIgnoreCase))
                    return null; // Let the normal public loader reject a damaged resource.
            }
            var checks = new List<string>();
            void Require(bool value, string name)
            { if (!value) throw new InvalidOperationException("Public precommit: " + name); checks.Add(name); Console.WriteLine("DHE public precommit check: " + name); }
            var model = typeof(ValueLayout.Factory).Assembly;
            string[] beforeTypes = model.GetTypes().Select(type => type.FullName).OrderBy(name => name).ToArray();
            var state = model.GetType("HybridCLR.Lab.ModuleEvolution.ModuleState", true);
            var constant = state.GetField("ExpectedVersion", BindingFlags.NonPublic | BindingFlags.Static);
            int beforeConstant = (int)constant.GetRawConstantValue();
            byte[][] badDlls = (byte[][])dlls.Clone(); badDlls[0] = badDlls[0].Concat(new byte[] { 0 }).ToArray();
            Require(!DheRuntime.LoadCurrentAssemblyImages(names, badDlls, out var code, out _) && code == LoadImageErrorCode.DHE_MV_CURRENT_HASH_MISMATCH,
                "bad-current-hash-rejected");
            Require(!DheRuntime.MetadataCommitted && !DheRuntime.RestartRequired && DheRuntime.LoadedAssemblyNames.Length == 0, "hash-rejection-has-no-native-effects");
            object target = artifacts["HybridCLR.ValueLayoutModel"];
            var mvField = target.GetType().GetField("BaseMetaVersion");
            byte[] original = (byte[])mvField.GetValue(target), current = (byte[])target.GetType().GetField("MetaVersion").GetValue(target);
            byte[] invalid = (byte[])typeof(DheRuntime).GetMethod("CreateInvalidBaseMetaVersion", hidden).Invoke(null, new object[] { original, current });
            try
            {
                // Only this fixture's in-memory copy changes. Restore it before
                // returning to the normal public Current load; assets stay intact.
                mvField.SetValue(target, invalid);
                Require(!DheRuntime.LoadCurrentAssemblyImages(names, dlls, out code, out _) && code == LoadImageErrorCode.DHE_MV_REGISTRATION_FAILED,
                    "native-registration-rejected");
                Require((int)typeof(DheRuntime).GetField("nativeLoadPhase", hidden).GetValue(null) == 2, "native-metadata-prepared-before-rejection");
                Require(DheRuntime.LoadState == DheLoadState.Rejected && !DheRuntime.MetadataCommitted && !DheRuntime.RestartRequired,
                    "public-rejection-remains-retryable");
                Require(DheRuntime.LoadedAssemblyNames.Length == 0 && (int)state.GetField("Runs").GetValue(null) == 0, "no-publication-or-initializer-effects");
                Require(!RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("GetRevision")) &&
                    ValueLayout.Factory.UnchangedRevision() == 5, "old-native-dispatch-preserved");
                Require((int)constant.GetRawConstantValue() == beforeConstant &&
                    model.GetTypes().Select(type => type.FullName).OrderBy(name => name).SequenceEqual(beforeTypes), "old-reflection-view-preserved");
                Require(DheRuntime.InterpreterOnlyAssemblyNames.All(name => !AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetName().Name == name)),
                    "pending-new-assemblies-hidden");
                bool resetRejected = false;
                try { DheRuntime.Reset(); } catch (InvalidOperationException) { resetRejected = true; }
                Require(resetRejected, "prepared-native-graph-cannot-reset");
            }
            finally { mvField.SetValue(target, original); }
            Require(ReferenceEquals(mvField.GetValue(target), original), "original-mv-restored");
            return checks.ToArray();
        }

        internal static string[] VerifyRetry(string[] checks)
        {
            if (checks == null) return null;
            if (DheRuntime.LoadState != DheLoadState.Ready || !DheRuntime.MetadataCommitted || DheRuntime.RestartRequired)
                throw new InvalidOperationException("Public corrected retry did not become ready.");
            const string name = "corrected-public-retry-committed"; Console.WriteLine("DHE public precommit check: " + name);
            return checks.Concat(new[] { name }).ToArray();
        }
    }
}
