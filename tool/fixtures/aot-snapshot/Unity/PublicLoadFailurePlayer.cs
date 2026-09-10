using System;
using System.Collections.Generic;
using System.Linq;

namespace HybridCLR.Lab.Snapshot
{
    internal static class PublicLoadFailurePlayer
    {
        internal static string[] Verify(IDheRuntimeAssetProvider provider, DheRuntimeIdentity identity, string root,
            string[] names, byte[][] dlls, LoadImageErrorCode code, string error)
        {
            var checks = new List<string>();
            void Require(bool value, string name)
            {
                if (!value) throw new InvalidOperationException("Public failure verification: " + name);
                checks.Add(name); Console.WriteLine("DHE public failure check: " + name);
            }
            Require(code == LoadImageErrorCode.DHE_INITIALIZATION_FAILED, "post-commit-code");
            Require(error.Contains("DHE deliberate public module failure") && DheRuntime.LastLoadError == error, "original-exception-preserved");
            Require(DheRuntime.RestartRequired && DheRuntime.MetadataCommitted && DheRuntime.LoadState == DheLoadState.RestartRequired, "committed-restart-state");
            Require(names.All(name => DheRuntime.LoadedAssemblyNames.Contains(name)), "managed-published-set");
            Require(names.All(name => AppDomain.CurrentDomain.GetAssemblies().Count(assembly => assembly.GetName().Name == name) == 1), "native-published-set");
            Require(RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("GetRevision")), "changed-native-dispatch-published");
            var state = typeof(ValueLayout.Factory).Assembly.GetType("HybridCLR.Lab.ModuleEvolution.ModuleState", true);
            int Runs() => (int)state.GetField("Runs").GetValue(null);
            Require(Runs() == 1, "initializer-ran-once");
            bool resetRejected = false;
            try { DheRuntime.Reset(); } catch (InvalidOperationException) { resetRejected = true; }
            Require(resetRejected, "reset-rejected");
            Require(!DheRuntime.InitializeFromResourceUpdate(provider, identity, root + "dhe-resource-update.json", out _, root), "reinitialize-rejected");
            Require(!DheRuntime.LoadCurrentAssemblyImages(names, dlls, out var retryCode, out string retryError) &&
                retryCode == LoadImageErrorCode.DHE_RESTART_REQUIRED && retryError == error, "same-resource-retry-rejected");
            Require(!DheRuntime.LoadAssemblyImages(names, dlls, out retryCode, out retryError) &&
                retryCode == LoadImageErrorCode.DHE_RESTART_REQUIRED && retryError == error, "legacy-batch-retry-rejected");
            Require(!DheRuntime.LoadAssemblyImage(names[0], dlls[0], out retryCode, out retryError) &&
                retryCode == LoadImageErrorCode.DHE_RESTART_REQUIRED && retryError == error, "single-image-retry-rejected");
            Require(!DheRuntime.LoadInterpreterAssemblyImage(names[0], dlls[0], out var loaded, out retryCode, out retryError) &&
                loaded == null && retryCode == LoadImageErrorCode.DHE_RESTART_REQUIRED && retryError == error, "interpreter-retry-rejected");
            Require(Runs() == 1 && DheRuntime.RestartRequired && DheRuntime.MetadataCommitted && DheRuntime.LastLoadError == error &&
                names.SequenceEqual(DheRuntime.PlannedAssemblyNames), "failure-state-survives-all-attempts");
            var ordinary = typeof(HybridCLR.Lab.ValueLayoutNative.NativeBoundary).Assembly.GetType("HybridCLR.Lab.ValueLayoutNative.OrdinaryModuleState");
            if (ordinary != null) Require((int)ordinary.GetField("Runs").GetValue(null) == 1, "ordinary-initializer-unchanged");
            return checks.ToArray();
        }
    }
}
