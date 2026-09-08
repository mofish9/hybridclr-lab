#if DHE_EVOLUTION_CURRENT && DHE_INTERFACE_EVOLUTION_CURRENT
using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.ManagedCasesAot
{
    internal static class DheInterfaceEvolutionAssertions
    {
        public static void Validate(Action<string, Action> check)
        {
            check("interface-evolution-class-dispatch", () =>
            {
                IIntOperation operation = new DheEvolutionOperation();
                Require(operation.Added(7) == 5007 && operation.Apply(7) == 3007,
                    "explicit class implementation and original slot");
                Require(DheCapabilityCases.InterfaceCall(operation, 7) == 3107,
                    "existing interface caller keeps original slot");
            });
            check("interface-evolution-boxed-struct", () =>
            {
                IIntOperation operation = new IntOperationStruct();
                Require(operation.Added(11) == 55 && operation.Apply(11) == 22,
                    "boxed value-type implementation");
                Require(DheCapabilityCases.InterfaceCall(operation, 11) == 122,
                    "existing caller on boxed receiver");
            });
            check("interface-evolution-constrained-call", () =>
            {
                var value = new IntOperationStruct();
                var reference = new DheEvolutionOperation();
                Require(Constrained(ref value, 13) == 65 && Constrained(ref reference, 13) == 5013,
                    "constrained value and reference receiver");
                Require(DheCapabilityCases.GenericConstrained(value, 13) == 126,
                    "existing constrained caller");
            });
            check("interface-evolution-delegates", () =>
            {
                IIntOperation reference = new DheEvolutionOperation();
                IIntOperation value = new IntOperationStruct();
                IntOperation referenceCall = reference.Added;
                IntOperation valueCall = value.Added;
                Require(referenceCall(17) == 5017 && valueCall(17) == 85,
                    "interface method group delegate");
                Require(DheCapabilityCases.DelegateCall(referenceCall, 17) == 5117 &&
                    DheCapabilityCases.DelegateCall(valueCall, 17) == 185,
                    "existing delegate caller enters new implementation");
            });
            check("interface-evolution-reflection-invoke", () =>
            {
                MethodInfo method = typeof(IIntOperation).GetMethod("Added") ??
                    throw new MissingMethodException(typeof(IIntOperation).FullName, "Added");
                Require(method.DeclaringType == typeof(IIntOperation) &&
                    method.IsAbstract && method.IsVirtual, "logical interface method declaration");
                Require((int)method.Invoke(new DheEvolutionOperation(), new object[] { 19 })! == 5019 &&
                    (int)method.Invoke(new IntOperationStruct(), new object[] { 19 })! == 95,
                    "reflection invokes actual implementation");
            });
            check("interface-evolution-map", () =>
            {
                ValidateMap(typeof(DheEvolutionOperation), new DheEvolutionOperation(), 5023);
                ValidateMap(typeof(IntOperationStruct), new IntOperationStruct(), 115);
            });
            check("interface-evolution-type-identity", () =>
            {
                string[] methods = typeof(IIntOperation).GetMethods().Select(method => method.Name)
                    .OrderBy(name => name, StringComparer.Ordinal).ToArray();
                Require(methods.SequenceEqual(new[] { "Added", "Apply" }), "current interface method set");
                Require(typeof(IIntOperation).IsAssignableFrom(typeof(DheEvolutionOperation)) &&
                    typeof(IIntOperation).IsAssignableFrom(typeof(IntOperationStruct)), "interface assignability");
                Require(typeof(DheEvolutionOperation).GetInterfaces().Contains(typeof(IIntOperation)) &&
                    typeof(IntOperationStruct).GetInterfaces().Contains(typeof(IIntOperation)),
                    "canonical interface identity");
            });
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int Constrained<T>(ref T operation, int value) where T : IIntOperation
            => operation.Added(value);

        private static void ValidateMap(Type type, object receiver, int expected)
        {
            InterfaceMapping map = type.GetInterfaceMap(typeof(IIntOperation));
            Require(map.InterfaceType == typeof(IIntOperation) && map.TargetType == type &&
                map.InterfaceMethods.Length == 2 && map.TargetMethods.Length == 2,
                "current interface map extent and identity");
            int added = Array.FindIndex(map.InterfaceMethods, method => method.Name == "Added");
            int original = Array.FindIndex(map.InterfaceMethods, method => method.Name == "Apply");
            Require(added >= 0 && original >= 0 && added != original,
                "new and original interface map slots");
            Require(map.TargetMethods[added].DeclaringType == type &&
                (int)map.TargetMethods[added].Invoke(receiver, new object[] { 23 })! == expected,
                "mapped implementation identity and invocation");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
