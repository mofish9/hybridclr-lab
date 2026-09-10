extern alias model;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;

namespace HybridCLR.Lab.UnityReference
{
    public static class GenericReferenceCases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unityReferenceGenericProbe") >= 0) Run();
        }

        private static object Construct(Type type)
        {
            foreach (var constructor in type.GetConstructors())
                if (constructor.GetParameters().Length == 0) return constructor.Invoke(new object[0]);
            throw new InvalidOperationException("Missing default constructor: " + type);
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
                    ++passed; Console.WriteLine("DHE Unity reference generic check: " + name);
                }
                catch (Exception error)
                {
                    ++failed; Console.WriteLine("DHE Unity reference generic failure: " + name + ": " + error);
                }
            }
            var gameObject = new GameObject("DHE generic reference source"); gameObject.SetActive(false);
            try
            {
                Type publicType = typeof(model::HybridCLR.Lab.ValueLayout.Factory).Assembly.GetType(typeof(Evolving).FullName, true);
                var component = (Evolving)gameObject.AddComponent(publicType);
                var owner = new model::HybridCLR.Lab.ValueLayout.GenericOwner<Evolving> { Value = component, Neighbor = 7 };
                Type ownerType = typeof(model::HybridCLR.Lab.ValueLayout.GenericOwner<>).MakeGenericType(publicType);
                Check("owner-type-identity", () => owner.GetType() == ownerType && ownerType == typeof(model::HybridCLR.Lab.ValueLayout.GenericOwner<Evolving>));
                Check("owner-public-type-accepts", () => ownerType.IsInstanceOfType(owner));
                Check("owner-reflection-field-roundtrip", () => {
                    var field = ownerType.GetField("Value"); field.SetValue(owner, component);
                    return ReferenceEquals(field.GetValue(owner), component);
                });
                Check("owner-reflection-construction", () => {
                    object created = Construct(ownerType); ownerType.GetField("Value").SetValue(created, component);
                    return created is model::HybridCLR.Lab.ValueLayout.GenericOwner<Evolving> typed && ReferenceEquals(typed.Value, component);
                });
                var list = new List<Evolving> { component };
                Type listType = typeof(List<>).MakeGenericType(publicType);
                Check("list-type-identity", () => list.GetType() == listType && listType == typeof(List<Evolving>));
                Check("list-public-type-accepts", () => listType.IsInstanceOfType(list));
                Check("list-direct-content", () => list.Count == 1 && ReferenceEquals(list[0], component));
                Check("list-reflection-construction", () => Construct(listType) is List<Evolving>);
                Check("list-reflection-add", () => {
                    var created = Construct(listType); listType.GetMethod("Add").Invoke(created, new object[] { component });
                    return created is List<Evolving> typed && typed.Count == 1 && ReferenceEquals(typed[0], component);
                });
                var array = new Evolving[] { component };
                Type arrayType = publicType.MakeArrayType();
                Check("array-type-identity", () => array.GetType() == arrayType && arrayType == typeof(Evolving[]));
                Check("array-public-type-accepts", () => arrayType.IsInstanceOfType(array));
                Check("array-reflection-roundtrip", () => {
                    var created = Array.CreateInstance(publicType, 1); created.SetValue(component, 0);
                    return created is Evolving[] typed && ReferenceEquals(typed[0], component);
                });
                var nested = new List<List<Evolving>> { list };
                Type nestedType = typeof(List<>).MakeGenericType(listType);
                Check("nested-list-type-identity", () => nested.GetType() == nestedType && nestedType == typeof(List<List<Evolving>>));
                Check("nested-list-public-type-accepts", () => nestedType.IsInstanceOfType(nested));
                Check("invariance-remains-strict", () => !typeof(List<object>).IsInstanceOfType(list) && !typeof(List<object>).IsAssignableFrom(listType));
                Check("array-object-covariance", () => typeof(object[]).IsInstanceOfType(array));
                Check("concurrent-type-identity", () => {
                    var results = new bool[4]; var workers = new Thread[results.Length];
                    for (int index = 0; index < workers.Length; ++index)
                    {
                        int slot = index;
                        workers[index] = new Thread(() => {
                            try
                            {
                                bool valid = true;
                                for (int iteration = 0; iteration < 32; ++iteration)
                                    valid &= typeof(Evolving) == publicType && typeof(List<Evolving>) == listType;
                                results[slot] = valid;
                            }
                            catch { results[slot] = false; }
                        });
                        workers[index].Start();
                    }
                    foreach (var worker in workers) if (!worker.Join(10000)) return false;
                    foreach (bool valid in results) if (!valid) return false;
                    return true;
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(gameObject); }
            Check("fixture-preserves-lifecycle-state", () => State.Disabled == disabled && State.Destroyed == destroyed);
            Console.WriteLine("DHE Unity reference generic totals: " + passed + ":" + failed);
            if (failed != 0) throw new InvalidOperationException("Unity generic reference assertions failed: " + failed);
            Console.WriteLine("DHE Unity reference generic pass: " + passed);
        }
    }
}
