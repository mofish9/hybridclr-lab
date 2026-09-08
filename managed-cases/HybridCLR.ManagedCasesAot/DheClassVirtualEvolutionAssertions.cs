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
            foreach (string group in new[]
            {
#if DHE_CLASS_VIRTUAL_CURRENT
                "ConcurrentFirstTouch", "NativeCallers", "InsertedAndOverrides", "NewSlot", "Generics",
                "Delegates", "Reflection", "BaseDefinitions", "Exceptions",
#endif
                "Common",
            })
            {
                check("class-virtual-" + group, () =>
                {
                    try { probe.GetMethod(group)!.Invoke(null, null); }
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
