extern alias model;
using System;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;

namespace HybridCLR.Lab.HierarchyQueries
{
    public sealed class GenericControl<T> { }
    public static class QueryCases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unityHierarchyQueryProbe") < 0) return;
            int passed = 0, failed = 0, disabled = State.Disabled, destroyed = State.Destroyed;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    passed++; Console.WriteLine("DHE Unity hierarchy query check: " + name);
                }
                catch (Exception error)
                {
                    failed++; Console.WriteLine("DHE Unity hierarchy query failure: " + name + ": " + error);
                }
            }
            var owner = new GameObject("DHE Current hierarchy query"); owner.SetActive(false);
            try
            {
                Type type = typeof(model::HybridCLR.Lab.ValueLayout.Factory).Assembly.GetType(typeof(Evolving).FullName, true);
                var component = (Evolving)owner.AddComponent(type); component.Value = 37; component.Extra = 91000000019L;
                Type contract = typeof(ISerializationCallbackReceiver);
                Check("public-type-interface-list", () => Array.IndexOf(type.GetInterfaces(), contract) >= 0);
                Check("instance-type-interface-list", () => Array.IndexOf(component.GetType().GetInterfaces(), contract) >= 0);
                Check("public-interface-by-name", () => type.GetInterface(contract.FullName) == contract);
                Check("public-base-type", () => type.BaseType == typeof(MonoBehaviour));
                Check("public-subclass", () => type.IsSubclassOf(typeof(MonoBehaviour)));
                Check("subclass-not-reflexive", () => !type.IsSubclassOf(type));
                Type parameter = typeof(GenericControl<>).GetGenericArguments()[0];
                Check("generic-parameter-base", () => parameter.BaseType == typeof(object));
                Check("generic-parameter-not-subclass-self", () => !parameter.IsSubclassOf(parameter));
                Check("current-field-data", () => component.Value == 37 && component.Extra == 91000000019L &&
                    (int)type.GetField("Value").GetValue(component) == 37);
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
            Check("fixture-preserves-lifecycle-state", () => State.Disabled == disabled && State.Destroyed == destroyed);
            if (failed != 0) throw new InvalidOperationException("DHE hierarchy query failures: " + failed);
            Console.WriteLine("DHE Unity hierarchy query pass: " + passed);
        }
    }
}
