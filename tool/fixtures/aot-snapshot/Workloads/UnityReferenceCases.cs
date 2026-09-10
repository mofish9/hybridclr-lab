extern alias model;
using System;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;
using Factory = model::HybridCLR.Lab.ValueLayout.Factory;

namespace HybridCLR.Lab.UnityReference
{
    public static class ReferenceCases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unityReferenceProbe") >= 0) Run();
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run()
        {
            int passed = 0, failed = 0, disabled = State.Disabled, destroyed = State.Destroyed;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    ++passed; Console.WriteLine("DHE Unity reference check: " + name);
                }
                catch (Exception error)
                {
                    ++failed; Console.WriteLine("DHE Unity reference failure: " + name + ": " + error);
                }
            }
            Type publicType = typeof(Factory).Assembly.GetType("HybridCLR.Lab.UnityCases.EvolvingBehaviour", true);
            Type currentType = typeof(Evolving);
            Check("public-type-matches-current-type", () => publicType == currentType);
            GameObject source = null, clone = null;
            try
            {
                source = new GameObject("DHE reference identity source"); source.SetActive(false);
                Component raw = source.AddComponent(publicType);
                Type actual = raw.GetType();
                Console.WriteLine("DHE Unity reference types: public=" + publicType.AssemblyQualifiedName +
                    "; current=" + currentType.AssemblyQualifiedName + "; actual=" + actual.AssemblyQualifiedName);
                Check("runtime-type-matches-public-type", () => actual == publicType);
                Check("runtime-type-equals-public-type", () => actual.Equals(publicType));
                Check("public-type-accepts-instance", () => publicType.IsInstanceOfType(raw));
                Check("current-type-accepts-instance", () => currentType.IsInstanceOfType(raw));
                Check("public-type-assignable-from-runtime-type", () => publicType.IsAssignableFrom(actual));
                Check("runtime-type-assignable-from-public-type", () => actual.IsAssignableFrom(publicType));
                var component = (Evolving)raw;
                component.Value = 17; component.Extra = 91000000019L;
                var value = publicType.GetField("Value"); var extra = publicType.GetField("Extra");
                Check("field-declaring-type-matches-public-type", () => value.DeclaringType == publicType && extra.DeclaringType == publicType);
                Check("reflected-existing-field-read", () => (int)value.GetValue(raw) == 17);
                Check("reflected-added-field-read", () => (long)extra.GetValue(raw) == 91000000019L);
                Check("reflected-fields-write-current-storage", () => {
                    value.SetValue(raw, 29); extra.SetValue(raw, 91000000031L);
                    return component.Value == 29 && component.Extra == 91000000031L;
                });
                Check("native-get-component-public-type", () => ReferenceEquals(source.GetComponent(publicType), raw));
                clone = UnityEngine.Object.Instantiate(source);
                Check("clone-preserves-type-and-fields", () => {
                    var copied = clone.GetComponent(publicType);
                    return copied != null && copied.GetType() == publicType &&
                        (int)value.GetValue(copied) == component.Value && (long)extra.GetValue(copied) == component.Extra;
                });
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
            Check("fixture-preserves-lifecycle-state", () => State.Disabled == disabled && State.Destroyed == destroyed);
            Console.WriteLine("DHE Unity reference totals: " + passed + ":" + failed);
            if (failed != 0) throw new InvalidOperationException("Unity reference identity assertions failed: " + failed);
            Console.WriteLine("DHE Unity reference pass: " + passed);
        }
    }
}
