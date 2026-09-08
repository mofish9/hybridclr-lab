#if DHE_CROSS_INTERFACE_CURRENT
using System;
using System.Linq;
using System.Reflection;
using HybridCLR.Lab.ManagedCases;

namespace HybridCLR.Lab.CrossAssemblyDerived
{
    public interface ICrossRevisionChild : ICrossAssemblyLazyVTableContract
    {
        int Extra(int value);
    }

    public sealed class CrossRevisionChild : CrossAssemblyLazyVTableBase, ICrossRevisionChild
    {
        public int Extra(int value) => value + 2000;
    }

    public interface ICrossRevisionValue<T> : ICrossAssemblyLazyVTableContract
    {
#if DHE_GENERIC_INTERFACE_CURRENT
        T AddedValue(T value);
        U EchoGeneric<U>(T value, U result);
#endif
        T RoundTrip(T value);
    }

#if DHE_GENERIC_INTERFACE_BASE
    public class CrossRevisionValue<T> : CrossAssemblyLazyVTableBase, ICrossRevisionValue<T>
#else
    public sealed class CrossRevisionValue<T> : CrossAssemblyLazyVTableBase, ICrossRevisionValue<T>
#endif
    {
#if DHE_GENERIC_INTERFACE_CURRENT
        public T AddedValue(T value) => default!;
        public U EchoGeneric<U>(T value, U result) => result;
#endif
        public T RoundTrip(T value) => value;
    }

    public sealed class CrossRevisionExplicit : ICrossAssemblyLazyVTableContract
    {
        int ICrossAssemblyLazyVTableContract.Added(int value) => value + 3000;
        T ICrossAssemblyLazyVTableContract.EchoAdded<T>(T value) => value;
        public int Compute(int value) => value + 300;
    }

    public static class CrossInterfaceEvolutionProbe
    {
        public static void BaseDispatch()
        {
            ICrossAssemblyLazyVTableContract contract = new CrossAssemblyLazyVTableBase();
            Require(contract.Added(3) == 1013 && contract.Compute(3) == 13, "base dispatch");
        }

        public static void InheritedDispatch()
        {
            ICrossAssemblyLazyVTableContract contract = new CrossAssemblyLazyVTableDerived();
            Require(contract.Added(5) == 1030 && contract.Compute(5) == 30, "inherited interface implementation");
            Require(CrossAssemblyLazyVTableProbe.Run() == "derived:26:34", "unchanged native caller and original slot");
        }

        public static void GenericMethod()
        {
            ICrossAssemblyLazyVTableContract contract = new CrossAssemblyLazyVTableDerived();
            Require(contract.EchoAdded(7) == 7 && contract.EchoAdded("generic") == "generic", "generic method calls");
            var receiver = new CrossAssemblyLazyVTableDerived();
            Require(ConstrainedEcho(ref receiver, 70000000009L) == 70000000009L, "constrained generic method");
        }

        public static void Delegates()
        {
            ICrossAssemblyLazyVTableContract contract = new CrossAssemblyLazyVTableDerived();
            Func<int, int> added = contract.Added;
            Func<string, string> echo = contract.EchoAdded<string>;
            Require(added(7) == 1034 && echo("delegate") == "delegate", "closed interface delegates");
        }

        public static void Reflection()
        {
            Type contract = typeof(ICrossAssemblyLazyVTableContract);
            var receiver = new CrossAssemblyLazyVTableDerived();
            MethodInfo added = contract.GetMethod("Added") ?? throw new MissingMethodException("Added");
            Require(added.DeclaringType == contract && (int)added.Invoke(receiver, new object[] { 9 }) == 1038,
                "ordinary interface reflection");
            MethodInfo echo = contract.GetMethod("EchoAdded") ?? throw new MissingMethodException("EchoAdded");
            MethodInfo closed = echo.MakeGenericMethod(typeof(long));
            Require(closed.GetGenericMethodDefinition() == echo && closed.DeclaringType == contract &&
                (long)closed.Invoke(receiver, new object[] { 90000000001L }) == 90000000001L,
                "generic interface reflection");
        }

        public static void InterfaceMap()
        {
            ValidateMap(typeof(CrossAssemblyLazyVTableBase), new CrossAssemblyLazyVTableBase(), 1021);
            ValidateMap(typeof(CrossAssemblyLazyVTableDerived), new CrossAssemblyLazyVTableDerived(), 1042);
        }

        public static void ChildInterface()
        {
            ICrossRevisionChild child = new CrossRevisionChild();
            Require(child.Added(13) == 1023 && child.Extra(13) == 2013 && child.EchoAdded("child") == "child",
                "new child interface inherits evolved Base contract");
            Require(typeof(ICrossRevisionChild).GetInterfaces().Contains(typeof(ICrossAssemblyLazyVTableContract)),
                "child interface identity");
        }

        public static void GenericInterface()
        {
            ICrossRevisionValue<int> integer = new CrossRevisionValue<int>();
            ICrossRevisionValue<string> text = new CrossRevisionValue<string>();
            Require(integer.RoundTrip(17) == 17 && integer.Added(17) == 1027 &&
                text.RoundTrip("value") == "value" && text.EchoAdded(19) == 19,
                "generic interface with inherited Base contract");
            InterfaceMapping map = typeof(CrossRevisionValue<int>).GetInterfaceMap(typeof(ICrossRevisionValue<int>));
#if DHE_GENERIC_INTERFACE_CURRENT
            int roundTrip = Array.FindIndex(map.InterfaceMethods, method => method.Name == "RoundTrip");
            Require(map.InterfaceMethods.Length == 3 && map.TargetMethods.Length == 3 && roundTrip >= 0 &&
                map.TargetMethods[roundTrip].DeclaringType == typeof(CrossRevisionValue<int>) &&
                (int)map.TargetMethods[roundTrip].Invoke(integer, new object[] { 23 }) == 23,
                "closed generic interface map");
#else
            Require(map.InterfaceMethods.Length == 1 && map.TargetMethods.Length == 1 &&
                map.TargetMethods[0].DeclaringType == typeof(CrossRevisionValue<int>) &&
                (int)map.TargetMethods[0].Invoke(integer, new object[] { 23 }) == 23,
                "closed generic interface map");
#endif
        }

        public static void ExplicitImplementation()
        {
            ICrossAssemblyLazyVTableContract contract = new CrossRevisionExplicit();
            Require(contract.Added(29) == 3029 && contract.Compute(29) == 329 &&
                contract.EchoAdded("explicit") == "explicit", "cross-assembly explicit implementation");
            InterfaceMapping map = typeof(CrossRevisionExplicit).GetInterfaceMap(typeof(ICrossAssemblyLazyVTableContract));
            int index = Array.FindIndex(map.InterfaceMethods, method => method.Name == "Added");
            Require(index >= 0 && map.TargetMethods[index].IsPrivate &&
                (int)map.TargetMethods[index].Invoke(contract, new object[] { 31 }) == 3031,
                "explicit method map and invocation");
        }

        public static void TypeIdentity()
        {
            Type contract = typeof(ICrossAssemblyLazyVTableContract);
            Type parent = typeof(CrossAssemblyLazyVTableBase);
            Type derived = typeof(CrossAssemblyLazyVTableDerived);
            Require(derived.BaseType == parent && contract.IsAssignableFrom(derived) &&
                derived.GetInterfaces().Contains(contract), "existing hierarchy identity");
            Require(contract.Assembly == parent.Assembly && derived.Assembly != parent.Assembly,
                "cross-assembly ownership");
            Require(contract.GetMethods().Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal)
                .SequenceEqual(new[] { "Added", "Compute", "EchoAdded" }), "logical interface method set");
        }

        private static T ConstrainedEcho<TReceiver, T>(ref TReceiver receiver, T value)
            where TReceiver : ICrossAssemblyLazyVTableContract => receiver.EchoAdded(value);

        private static void ValidateMap(Type receiverType, object receiver, int expected)
        {
            InterfaceMapping map = receiverType.GetInterfaceMap(typeof(ICrossAssemblyLazyVTableContract));
            Require(map.TargetType == receiverType && map.InterfaceMethods.Length == 3 && map.TargetMethods.Length == 3,
                "expanded interface map");
            int added = Array.FindIndex(map.InterfaceMethods, method => method.Name == "Added");
            int echo = Array.FindIndex(map.InterfaceMethods, method => method.Name == "EchoAdded");
            Require(added >= 0 && echo >= 0 &&
                map.TargetMethods[added].DeclaringType == typeof(CrossAssemblyLazyVTableBase) &&
                map.TargetMethods[echo].DeclaringType == typeof(CrossAssemblyLazyVTableBase), "inherited map owner");
            Require((int)map.TargetMethods[added].Invoke(receiver, new object[] { 11 }) == expected &&
                (string)map.TargetMethods[echo].MakeGenericMethod(typeof(string)).Invoke(receiver, new object[] { "map" }) == "map",
                "inherited mapped method invocation");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
