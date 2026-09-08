#if DHE_EVOLUTION_CURRENT && DHE_CROSS_INTERFACE_CURRENT
using System;
using System.Linq;
using System.Reflection;

namespace HybridCLR.Lab.ManagedCasesAot
{
    internal static class DheCrossInterfaceEvolutionAssertions
    {
        public static void Validate(Action<string, Action> check)
        {
            Assembly assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(value =>
                value.GetName().Name == "HybridCLR.CrossAssemblyDerived") ?? Assembly.Load("HybridCLR.CrossAssemblyDerived");
            Type probe = assembly.GetType("HybridCLR.Lab.CrossAssemblyDerived.CrossInterfaceEvolutionProbe", true);
            var groups = new[]
            {
                ("cross-interface-base-dispatch", "BaseDispatch"),
                ("cross-interface-inherited-dispatch", "InheritedDispatch"),
                ("cross-interface-generic-method", "GenericMethod"),
                ("cross-interface-delegates", "Delegates"),
                ("cross-interface-reflection", "Reflection"),
                ("cross-interface-map", "InterfaceMap"),
                ("cross-interface-child", "ChildInterface"),
                ("cross-interface-generic-type", "GenericInterface"),
                ("cross-interface-explicit", "ExplicitImplementation"),
                ("cross-interface-type-identity", "TypeIdentity"),
            };
            foreach (var group in groups)
            {
                check(group.Item1, () =>
                {
                    MethodInfo method = probe.GetMethod(group.Item2) ?? throw new MissingMethodException(probe.FullName, group.Item2);
                    try { method.Invoke(null, null); }
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
