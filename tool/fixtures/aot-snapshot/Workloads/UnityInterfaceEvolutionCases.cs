extern alias model;
using System;
using System.Reflection;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;

namespace HybridCLR.Lab.InterfaceEvolution
{
    // The compiler moves this implementation onto the existing Current class.
    public sealed class DisposalTemplate : IDisposable
    {
        public void Dispose() { EvolutionCases.Disposed(); }
    }

    public sealed class RemovedInterfaceTemplate
    {
        public void OnBeforeSerialize() { model::HybridCLR.Lab.UnityReference.SerializationCallbackCases.Before((Evolving)(object)this); }
        public void OnAfterDeserialize() { model::HybridCLR.Lab.UnityReference.SerializationCallbackCases.After((Evolving)(object)this); }
    }

    public static class EvolutionCases
    {
        private static int disposed;
        public static void Disposed() { disposed++; }
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unityInterfaceEvolutionProbe") >= 0) Run();
        }

        private static bool SupportsCallback(object value) { return value is ISerializationCallbackReceiver; }
        private static bool SupportsDispose(object value) { return value is IDisposable; }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run()
        {
#if INTERFACE_COMPILER_REMOVAL
            const bool replacement = false, removedMethods = false;
            const string mode = "interface-remove-compiler";
#elif INTERFACE_REPLACEMENT
            const bool replacement = true, removedMethods = true;
            const string mode = "interface-replace";
#elif REMOVE_INTERFACE_METHODS
            const bool replacement = false, removedMethods = true;
            const string mode = "interface-remove-methods";
#else
            const bool replacement = false, removedMethods = false;
            const string mode = "interface-remove";
#endif
            Console.WriteLine("DHE interface evolution mode: " + mode);
            int passed = 0, failed = 0, disabled = State.Disabled, destroyed = State.Destroyed;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    passed++; Console.WriteLine("DHE Unity interface evolution check: " + name);
                }
                catch (Exception error)
                {
                    failed++; Console.WriteLine("DHE Unity interface evolution failure: " + name + ": " + error);
                }
            }
            Type counters = typeof(model::HybridCLR.Lab.UnityReference.SerializationCallbackCases);
            const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
            FieldInfo beforeCount = counters.GetField("beforeCount", hidden), afterCount = counters.GetField("afterCount", hidden);
            void Reset() { beforeCount.SetValue(null, 0); afterCount.SetValue(null, 0); }
            bool NoCallbacks() { return (int)beforeCount.GetValue(null) == 0 && (int)afterCount.GetValue(null) == 0; }
            var source = new GameObject("DHE removed interface source"); source.SetActive(false);
            GameObject clone = null;
            try
            {
                Type type = typeof(model::HybridCLR.Lab.ValueLayout.Factory).Assembly.GetType(typeof(Evolving).FullName, true);
                var component = (Evolving)source.AddComponent(type);
                component.Value = 37; component.Extra = 91000000019L;
                Type callback = typeof(ISerializationCallbackReceiver), disposable = typeof(IDisposable);
                Check("current-interface-enumeration", () => Array.IndexOf(type.GetInterfaces(), callback) < 0 &&
                    (Array.IndexOf(type.GetInterfaces(), disposable) >= 0) == replacement);
                Check("removed-interface-name", () => type.GetInterface(callback.FullName) == null);
                Check("removed-interface-type-query", () => !callback.IsAssignableFrom(type));
                Check("removed-interface-instance-query", () => !callback.IsInstanceOfType(component));
                Check("removed-interface-managed-cast", () => !SupportsCallback(component));
                Reset();
                Check("removed-or-retained-method-behavior", () => {
                    var before = type.GetMethod("OnBeforeSerialize"); var after = type.GetMethod("OnAfterDeserialize");
                    if (removedMethods) return before == null && after == null;
#if INTERFACE_COMPILER_REMOVAL
                    if (before == null || after == null || before.IsVirtual || after.IsVirtual || before.IsFinal || after.IsFinal)
                        return false;
#endif
                    before.Invoke(component, null); after.Invoke(component, null);
                    return (int)beforeCount.GetValue(null) == 1 && (int)afterCount.GetValue(null) == 1;
                });
                Check("replacement-type-query", () => disposable.IsAssignableFrom(type) == replacement &&
                    disposable.IsInstanceOfType(component) == replacement);
                disposed = 0;
                Check("replacement-managed-dispatch", () => {
                    if (!replacement) return !SupportsDispose(component);
                    ((IDisposable)(object)component).Dispose(); return disposed == 1;
                });
                Check("replacement-reflected-dispatch", () => {
                    var method = type.GetMethod("Dispose");
                    if (!replacement) return method == null;
                    method.Invoke(component, null); return disposed == 2;
                });
                Reset();
                Check("json-without-removed-before-callback", () => {
                    string json = JsonUtility.ToJson(component, false);
                    return NoCallbacks() && json.Contains("\"Value\":37");
                });
                Reset();
                Check("json-without-removed-after-callback", () => {
                    JsonUtility.FromJsonOverwrite("{\"Value\":49}", component);
                    return NoCallbacks() && component.Value == 49 && component.Extra == 91000000019L;
                });
                Reset();
                Check("clone-without-removed-callbacks", () => {
                    clone = UnityEngine.Object.Instantiate(source);
                    var copied = clone.GetComponent(type) as Evolving;
                    return NoCallbacks() && copied != null && copied.Value == 49 && copied.Extra == component.Extra;
                });
                Check("native-component-lookup", () => ReferenceEquals(source.GetComponent(type), component));
                Check("public-component-identity", () => component.GetType() == type);
                var marker = new WeakReference(component.Marker = new object());
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Check("current-fields-survive-gc", () => marker.IsAlive && ReferenceEquals(marker.Target, component.Marker) &&
                    component.Value == 49 && component.Extra == 91000000019L);
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(source);
            }
            Check("fixture-preserves-lifecycle-state", () => State.Disabled == disabled && State.Destroyed == destroyed);
            if (failed != 0) throw new InvalidOperationException("DHE interface evolution failures: " + failed);
            Console.WriteLine("DHE Unity interface evolution pass: " + passed);
        }
    }
}
