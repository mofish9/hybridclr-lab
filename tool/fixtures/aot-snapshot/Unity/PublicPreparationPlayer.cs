using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace HybridCLR.Lab.Snapshot
{
    // Deliberate malformed-metadata injection after authenticated initialization.
    // This does not represent an accepted production resource.
    internal static class PublicPreparationPlayer
    {
        internal static string[] Verify(string[] names, byte[][] dlls, string malformedPath)
        {
            const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
            var artifacts = (IDictionary)typeof(DheRuntime).GetField("Artifacts", hidden).GetValue(null);
            object artifact = artifacts["HybridCLR.ValueLayoutModel"];
            var mvField = artifact.GetType().GetField("MetaVersion");
            var hashField = artifact.GetType().GetField("ExpectedCurrentSha256");
            byte[] originalMv = (byte[])mvField.GetValue(artifact);
            string originalHash = (string)hashField.GetValue(artifact);
            int modelIndex = Array.IndexOf(names, "HybridCLR.ValueLayoutModel");
            if (modelIndex < 0) throw new InvalidDataException("Missing Model in preparation fixture.");
            byte[] malformed = File.ReadAllBytes(malformedPath), hash;
            using (var sha = SHA256.Create()) hash = sha.ComputeHash(malformed);
            byte[] injectedMv = (byte[])originalMv.Clone();
            if (injectedMv.Length < 60 || Encoding.ASCII.GetString(injectedMv, 0, 8) != "DHEMETA1" || BitConverter.ToUInt32(injectedMv, 8) != 1)
                throw new InvalidDataException("Unexpected fixture MV format.");
            Buffer.BlockCopy(hash, 0, injectedMv, 28, 32);
            byte[][] injectedDlls = (byte[][])dlls.Clone(); injectedDlls[modelIndex] = malformed;
            var checks = new List<string>();
            void Require(bool value, string name)
            { if (!value) throw new InvalidOperationException("Public preparation: " + name); checks.Add(name); Console.WriteLine("DHE public preparation check: " + name); }
            string failure;
            try
            {
                mvField.SetValue(artifact, injectedMv);
                hashField.SetValue(artifact, BitConverter.ToString(hash).Replace("-", ""));
                Require(!DheRuntime.LoadCurrentAssemblyImages(names, injectedDlls, out var code, out failure) &&
                    code == LoadImageErrorCode.DHE_RESTART_REQUIRED, "native-preparation-failed");
                Require((int)typeof(DheRuntime).GetField("nativeLoadPhase", hidden).GetValue(null) == 1, "actual-native-preparation-phase");
                Require(failure.Contains("type parent hierarchy contains a cycle") && failure == DheRuntime.LastLoadError,
                    "actual-native-exception-preserved");
                Require(DheRuntime.RestartRequired && !DheRuntime.MetadataCommitted && DheRuntime.LoadState == DheLoadState.RestartRequired,
                    "uncommitted-restart-required");
                Require(DheRuntime.LoadedAssemblyNames.Length == 0, "no-managed-publication");
                var state = typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.ModuleEvolution.ModuleState", true);
                Require((int)state.GetField("Runs").GetValue(null) == 0, "no-current-initializer-effect");
                Require(!RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("GetRevision")), "old-dispatch-retained");
                Require(DheRuntime.InterpreterOnlyAssemblyNames.All(name => !AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == name)),
                    "new-assemblies-unpublished");
            }
            finally { mvField.SetValue(artifact, originalMv); hashField.SetValue(artifact, originalHash); }
            Require(ReferenceEquals(mvField.GetValue(artifact), originalMv) && (string)hashField.GetValue(artifact) == originalHash,
                "original-input-records-restored");
            Require(!DheRuntime.LoadCurrentAssemblyImages(names, dlls, out var retryCode, out var retryError) &&
                retryCode == LoadImageErrorCode.DHE_RESTART_REQUIRED && retryError == failure, "corrected-retry-requires-fresh-process");
            bool resetRejected = false;
            try { DheRuntime.Reset(); } catch (InvalidOperationException) { resetRejected = true; }
            Require(resetRejected && !DheRuntime.MetadataCommitted && DheRuntime.RestartRequired, "reset-cannot-erase-partial-preparation");
            return checks.ToArray();
        }
    }
}
