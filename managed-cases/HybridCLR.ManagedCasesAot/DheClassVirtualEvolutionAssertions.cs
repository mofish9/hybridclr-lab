#if DHE_EVOLUTION_CURRENT && DHE_CLASS_VIRTUAL_BASE
using System;
using System.Linq;
using System.Reflection;

namespace HybridCLR.Lab.ManagedCasesAot
{
    internal static class DheClassVirtualEvolutionAssertions
    {
        public static void Validate(Action<string, Action> check)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value =>
                value.GetName().Name == "HybridCLR.CrossAssemblyDerived") ?? Assembly.Load("HybridCLR.CrossAssemblyDerived");
            Type probe = assembly.GetType("HybridCLR.Lab.CrossAssemblyDerived.ClassVirtualEvolutionProbe", true);
            foreach (var group in new[]
            {
#if DHE_CLASS_VIRTUAL_CURRENT
                ("concurrent-first-touch", "ConcurrentFirstTouch"), ("native-callers", "NativeCallers"),
                ("inserted-and-overrides", "InsertedAndOverrides"), ("new-slot", "NewSlot"), ("generics", "Generics"),
                ("delegates", "Delegates"), ("reflection", "Reflection"), ("base-definitions", "BaseDefinitions"), ("exceptions", "Exceptions"),
#endif
                ("common", "Common"),
            })
            {
                check("class-virtual-" + group.Item1, () =>
                {
                    try { probe.GetMethod(group.Item2)!.Invoke(null, null); }
                    catch (TargetInvocationException exception)
                    {
                        throw new InvalidOperationException((exception.InnerException ?? exception).ToString());
                    }
                });
            }
        }
    }
}
#endif
