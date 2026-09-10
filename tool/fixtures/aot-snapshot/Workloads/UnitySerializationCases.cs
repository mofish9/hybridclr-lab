extern alias model;
using System;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;

namespace HybridCLR.Lab.UnitySerialization
{
    public static class SerializationCases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unitySerializationProbe") >= 0) Run();
        }

        private static void Run()
        {
            int count = 0, disabled = State.Disabled, destroyed = State.Destroyed;
            void Require(bool value, string name)
            {
                if (!value) throw new InvalidOperationException("Unity serialization: " + name);
                ++count; Console.WriteLine("DHE Unity serialization check: " + name);
            }
            GameObject source = null, clone = null;
            try
            {
                source = new GameObject("DHE serialization source"); source.SetActive(false);
                var component = (Evolving)source.AddComponent(typeof(Evolving));
                component.Value = 17; component.Extra = 91000000019L;
                Require(component.Enabled == 0 && State.Disabled == disabled && State.Destroyed == destroyed,
                    "inactive-source-has-no-callback-effects");
                string json = JsonUtility.ToJson(component);
                Console.WriteLine("DHE Unity serialization json: " + json);
                Require(json.Contains("\"Value\":17"), "json-reads-existing-field");
                Require(json.Contains("\"Extra\":91000000019"), "json-reads-added-field");
                JsonUtility.FromJsonOverwrite("{\"Value\":29,\"Extra\":91000000031}", component);
                Require(component.Value == 29, "json-writes-existing-field");
                Require(component.Extra == 91000000031L, "json-writes-added-field");
                JsonUtility.FromJsonOverwrite("{\"Value\":43}", component);
                Require(component.Value == 43 && component.Extra == 91000000031L, "old-json-preserves-added-field");
                clone = UnityEngine.Object.Instantiate(source);
                var copied = (Evolving)clone.GetComponent(typeof(Evolving));
                Require(copied != null && copied.Value == 43, "clone-copies-existing-field");
                Require(copied.Extra == 91000000031L, "clone-copies-added-field");
                copied.Value = 53; copied.Extra = 91000000057L;
                Require(component.Value == 43 && component.Extra == 91000000031L, "cloned-storage-is-independent");
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Require(copied.Value == 53 && copied.Extra == 91000000057L, "cloned-storage-survives-gc");
            }
            finally
            {
                if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                if (source != null) UnityEngine.Object.DestroyImmediate(source);
            }
            Require(State.Disabled == disabled && State.Destroyed == destroyed, "fixture-preserves-lifecycle-state");
            Console.WriteLine("DHE Unity serialization pass: " + count);
        }
    }
}
