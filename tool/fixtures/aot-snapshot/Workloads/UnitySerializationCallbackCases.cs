extern alias model;
using System;
using System.Threading;
using UnityEngine;
#if SERIALIZATION_CALLBACK_CONTROL
using Evolving = HybridCLR.Lab.UnityReference.SerializationCallbackTemplate;
#else
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
#endif
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;

namespace HybridCLR.Lab.UnityReference
{
    // The fixture compiler moves these exact managed bodies to the existing
    // EvolvingBehaviour and adds this interface to that definition in Current.
#if SERIALIZATION_CALLBACK_CONTROL
    public sealed class SerializationCallbackTemplate : MonoBehaviour, ISerializationCallbackReceiver
#else
    public sealed class SerializationCallbackTemplate : ISerializationCallbackReceiver
#endif
    {
#if SERIALIZATION_CALLBACK_CONTROL
        public int Value;
        public long Extra;
#endif
        public void OnBeforeSerialize() { SerializationCallbackCases.Before((Evolving)(object)this); }
        public void OnAfterDeserialize() { SerializationCallbackCases.After((Evolving)(object)this); }
    }

    public static class SerializationCallbackCases
    {
        private static int beforeCount, afterCount, beforeValue, afterValue;
        public static void Before(Evolving value)
        {
            Interlocked.Exchange(ref beforeValue, value.Value);
            Interlocked.Increment(ref beforeCount);
        }
        public static void After(Evolving value)
        {
            Interlocked.Exchange(ref afterValue, value.Value);
            Interlocked.Increment(ref afterCount);
        }
        private static int Read(ref int value) { return Interlocked.CompareExchange(ref value, 0, 0); }
        private static void Reset()
        {
            Interlocked.Exchange(ref beforeCount, 0); Interlocked.Exchange(ref afterCount, 0);
            Interlocked.Exchange(ref beforeValue, -1); Interlocked.Exchange(ref afterValue, -1);
        }
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unitySerializationCallbackProbe") >= 0) Run();
        }
        private static void Run()
        {
#if SERIALIZATION_CALLBACK_CONTROL
            Console.WriteLine("DHE callback fixture: new-component-control");
#else
            Console.WriteLine("DHE callback fixture: existing-interface");
#endif
            int passed = 0, failed = 0, disabled = State.Disabled, destroyed = State.Destroyed;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    ++passed; Console.WriteLine("DHE Unity serialization callback check: " + name);
                }
                catch (Exception error)
                {
                    ++failed; Console.WriteLine("DHE Unity serialization callback failure: " + name + ": " + error);
                }
            }
            var source = new GameObject("DHE serialization callback source"); source.SetActive(false);
            GameObject clone = null;
            try
            {
                var type = typeof(model::HybridCLR.Lab.ValueLayout.Factory).Assembly.GetType(typeof(Evolving).FullName, true);
                var component = (Evolving)source.AddComponent(type);
                component.Value = 37; component.Extra = 91000000019L;
                Check("interface-current-type", () => typeof(ISerializationCallbackReceiver).IsAssignableFrom(type) &&
                    typeof(ISerializationCallbackReceiver).IsInstanceOfType(component));
                Reset();
                Check("direct-interface-before", () => {
                    ((ISerializationCallbackReceiver)(object)component).OnBeforeSerialize();
                    return Read(ref beforeCount) == 1 && Read(ref beforeValue) == 37;
                });
                Check("direct-interface-after", () => {
                    ((ISerializationCallbackReceiver)(object)component).OnAfterDeserialize();
                    return Read(ref afterCount) == 1 && Read(ref afterValue) == 37;
                });
                Reset();
                Check("native-json-before", () => {
                    string json = JsonUtility.ToJson(component, false);
                    Console.WriteLine("DHE callback JSON before: count=" + Read(ref beforeCount) + " value=" + Read(ref beforeValue));
                    return Read(ref beforeCount) > 0 && Read(ref beforeValue) == 37 && json.Contains("\"Value\":37");
                });
                Reset();
                Check("native-json-after", () => {
                    JsonUtility.FromJsonOverwrite("{\"Value\":49}", component);
                    Console.WriteLine("DHE callback JSON after: count=" + Read(ref afterCount) + " value=" + Read(ref afterValue));
                    return Read(ref afterCount) > 0 && Read(ref afterValue) == 49 && component.Value == 49;
                });
                Reset();
                clone = UnityEngine.Object.Instantiate(source);
                Console.WriteLine("DHE callback clone: before=" + Read(ref beforeCount) + " after=" + Read(ref afterCount));
                Check("native-clone-before", () => Read(ref beforeCount) > 0 && Read(ref beforeValue) == 49);
                Check("native-clone-after-and-fields", () => {
                    var copied = clone.GetComponent(type) as Evolving;
                    return Read(ref afterCount) > 0 && Read(ref afterValue) == 49 && copied != null &&
                        copied.Value == 49 && copied.Extra == component.Extra;
                });
                Check("native-query-current-type", () => ReferenceEquals(source.GetComponent(type), component));
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                UnityEngine.Object.DestroyImmediate(source);
            }
            Check("fixture-preserves-lifecycle-state", () => State.Disabled == disabled && State.Destroyed == destroyed);
            Console.WriteLine("DHE Unity serialization callback totals: " + passed + ":" + failed);
            if (failed != 0) throw new InvalidOperationException("Unity serialization callback assertions failed: " + failed);
            Console.WriteLine("DHE Unity serialization callback pass: " + passed);
        }
    }
}
