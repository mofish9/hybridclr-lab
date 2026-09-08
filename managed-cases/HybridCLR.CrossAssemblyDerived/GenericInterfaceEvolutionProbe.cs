#if DHE_GENERIC_INTERFACE_BASE
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.CrossAssemblyDerived
{
    public struct GenericInterfacePayload
    {
        public long Number;
        public double Fraction;
        public string Text;
    }

    public sealed class CrossRevisionNativeChild : CrossRevisionValue<int> { }

    public static class GenericInterfaceNativeCallers
    {
        // Both Base and Current contain these exact callers. Receiver arguments
        // keep the dispatch indirect; NoInlining preserves the observable boundary.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Integer(ICrossRevisionValue<int> receiver, int value) => receiver.RoundTrip(value);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Reference(ICrossRevisionValue<string> receiver, string value) => receiver.RoundTrip(value);

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static GenericInterfacePayload Value(ICrossRevisionValue<GenericInterfacePayload> receiver,
            GenericInterfacePayload value) => receiver.RoundTrip(value);
    }

#if DHE_GENERIC_INTERFACE_CURRENT
    public sealed class CrossRevisionInterpretedChild : CrossRevisionValue<string> { }

    public struct CrossRevisionValueStruct : ICrossRevisionValue<int>
    {
        public int AddedValue(int value) => value + 4000;
        public U EchoGeneric<U>(int value, U result) => result;
        public int RoundTrip(int value) => value + 5000;
        public int Added(int value) => value + 6000;
        public U EchoAdded<U>(U value) => value;
        public int Compute(int value) => value + 7000;
    }

    public static class GenericInterfaceEvolutionProbe
    {
        public static void NativeCallers()
        {
            var routes = new List<string>();
            Require(Observe("Integer", () => GenericInterfaceNativeCallers.Integer(new CrossRevisionValue<int>(), 37), routes) == 37,
                "native integer caller kept original RoundTrip slot");
            Require(Observe("Reference", () => GenericInterfaceNativeCallers.Reference(new CrossRevisionValue<string>(), "native"), routes) == "native",
                "native reference caller kept original RoundTrip slot");
            var value = new GenericInterfacePayload { Number = 90000000009L, Fraction = 2.25, Text = "payload" };
            GenericInterfacePayload result = Observe("Value", () => GenericInterfaceNativeCallers.Value(new CrossRevisionValue<GenericInterfacePayload>(), value), routes);
            Require(result.Number == value.Number && result.Fraction == value.Fraction && result.Text == value.Text,
                "native struct return with managed reference");
            Require(GenericInterfaceNativeCallers.Integer(new CrossRevisionValueStruct(), 41) == 5041 &&
                GenericInterfaceNativeCallers.Reference(new CrossRevisionInterpretedChild(), "child") == "child",
                "native callers accept interpreted implementations");
            string path = Environment.GetEnvironmentVariable("HYBRIDCLR_DHE_EVOLUTION_EVIDENCE");
            if (!string.IsNullOrEmpty(path) && routes.Count != 0)
                File.WriteAllLines(path + ".generic-interface-callers", routes);
        }

        private static T Observe<T>(string name, Func<T> action, List<string> routes)
        {
            Type api = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("HybridCLR.RuntimeApi", false)).FirstOrDefault(type => type != null);
            if (api == null) return action(); // CLR reference has no HybridCLR native runtime.
            MethodInfo changed = api.GetMethod("IsDifferentialMethodChanged")!;
            MethodInfo aot = api.GetMethod("GetDifferentialAotEntryCount")!;
            MethodInfo interpreted = api.GetMethod("GetDifferentialInterpreterEntryCount")!;
            bool isChanged = (bool)changed.Invoke(null, new object[] { typeof(GenericInterfaceNativeCallers).GetMethod(name)! });
            int beforeAot = (int)aot.Invoke(null, null), beforeInterpreted = (int)interpreted.Invoke(null, null);
            T result = action();
            int aotDelta = (int)aot.Invoke(null, null) - beforeAot;
            int interpretedDelta = (int)interpreted.Invoke(null, null) - beforeInterpreted;
            routes.Add(name + "\t" + (isChanged ? "1" : "0") + "\t" + aotDelta + "\t" + interpretedDelta);
            return result;
        }

        public static void Dispatch()
        {
            ICrossRevisionValue<int> integer = new CrossRevisionValue<int>();
            ICrossRevisionValue<string> reference = new CrossRevisionValue<string>();
            Require(integer.AddedValue(43) == 0 && integer.RoundTrip(43) == 43 &&
                reference.AddedValue("new") == null && reference.RoundTrip("old") == "old",
                "inserted and retained generic slots have distinct behavior");
            ICrossRevisionValue<GenericInterfacePayload> payload = new CrossRevisionValue<GenericInterfacePayload>();
            var value = new GenericInterfacePayload { Number = 49, Fraction = 4.5, Text = "struct" };
            Require(payload.AddedValue(value).Text == null && payload.RoundTrip(value).Text == "struct",
                "generic slot struct return");
        }

        public static void GenericMethod()
        {
            ICrossRevisionValue<int> integer = new CrossRevisionValue<int>();
            ICrossRevisionValue<string> reference = new CrossRevisionValue<string>();
            Require(integer.EchoGeneric(47, "method") == "method" &&
                reference.EchoGeneric("input", 90000000011L) == 90000000011L, "two generic contexts");
            var value = new GenericInterfacePayload { Number = 53, Fraction = 6.5, Text = "generic" };
            Require(integer.EchoGeneric(1, value).Text == "generic", "generic method struct return");
        }

        public static void Delegates()
        {
            ICrossRevisionValue<int> receiver = new CrossRevisionValue<int>();
            Func<int, int> added = receiver.AddedValue, retained = receiver.RoundTrip;
            Func<int, string, string> generic = receiver.EchoGeneric<string>;
            Require(added(59) == 0 && retained(59) == 59 && generic(59, "delegate") == "delegate",
                "closed delegates select logical slots");
        }

        public static void Reflection()
        {
            Type contract = typeof(ICrossRevisionValue<string>);
            var receiver = new CrossRevisionValue<string>();
            MethodInfo added = contract.GetMethod("AddedValue")!;
            MethodInfo generic = contract.GetMethod("EchoGeneric")!;
            MethodInfo closed = generic.MakeGenericMethod(typeof(long));
            Require(added.DeclaringType == contract && added.ReturnType == typeof(string) &&
                added.Invoke(receiver, new object[] { "reflection" }) == null, "closed declaration reflection");
            Require(closed.DeclaringType == contract && closed.GetGenericMethodDefinition() == generic &&
                (long)closed.Invoke(receiver, new object[] { "input", 90000000013L }) == 90000000013L,
                "closed interface generic method reflection");
            Require(typeof(ICrossRevisionValue<>).GetMethod("AddedValue")!.ReturnType.IsGenericParameter,
                "open interface parameter identity");
        }

        public static void Maps()
        {
            foreach (Type argument in new[] { typeof(int), typeof(string), typeof(GenericInterfacePayload) })
            {
                Type contract = typeof(ICrossRevisionValue<>).MakeGenericType(argument);
                Type implementation = typeof(CrossRevisionValue<>).MakeGenericType(argument);
                InterfaceMapping map = implementation.GetInterfaceMap(contract);
                Require(map.InterfaceType == contract && map.TargetType == implementation &&
                    map.InterfaceMethods.Length == 3 && map.TargetMethods.Length == 3, "closed map size");
                Require(map.InterfaceMethods.Select(method => method.Name).OrderBy(name => name, StringComparer.Ordinal)
                    .SequenceEqual(new[] { "AddedValue", "EchoGeneric", "RoundTrip" }), "closed map method set");
                Require(map.InterfaceMethods.All(method => method.DeclaringType == contract) &&
                    map.TargetMethods.All(method => method.DeclaringType == implementation), "closed map owners");
            }
        }

        public static void Inherited()
        {
            ICrossRevisionValue<int> native = new CrossRevisionNativeChild();
            ICrossRevisionValue<string> interpreted = new CrossRevisionInterpretedChild();
            Require(native.AddedValue(61) == 0 && native.RoundTrip(61) == 61 &&
                native.EchoGeneric(61, "native-child") == "native-child", "existing child inherits new methods");
            Require(interpreted.AddedValue("input") == null && interpreted.RoundTrip("output") == "output" &&
                interpreted.EchoGeneric("input", 67) == 67, "new child inherits closed Current parent");
            InterfaceMapping map = typeof(CrossRevisionNativeChild).GetInterfaceMap(typeof(ICrossRevisionValue<int>));
            Require(map.TargetMethods.All(method => method.DeclaringType == typeof(CrossRevisionValue<int>)),
                "native child's inherited generic method owners");
        }

        public static void BoxedAndConstrained()
        {
            var value = new CrossRevisionValueStruct();
            ICrossRevisionValue<int> boxed = value;
            Require(boxed.AddedValue(71) == 4071 && boxed.RoundTrip(71) == 5071 &&
                boxed.EchoGeneric(71, "boxed") == "boxed", "boxed new value receiver");
            Require(Constrained(ref value, 73) == 9146 && ConstrainedGeneric(ref value, "constrained") == "constrained",
                "constrained generic interface invocation");
        }

        public static void Identity()
        {
            Type contract = typeof(ICrossRevisionValue<int>), parent = typeof(CrossRevisionValue<int>);
            Require(contract.GetGenericTypeDefinition() == typeof(ICrossRevisionValue<>) &&
                contract.IsAssignableFrom(parent) && contract.IsAssignableFrom(typeof(CrossRevisionNativeChild)),
                "existing generic interface assignability");
            Require(typeof(CrossRevisionNativeChild).BaseType == parent &&
                typeof(CrossRevisionInterpretedChild).BaseType == typeof(CrossRevisionValue<string>) &&
                contract.Assembly == parent.Assembly, "logical parent and assembly identity");
        }

        private static int Constrained<T>(ref T value, int input) where T : struct, ICrossRevisionValue<int> =>
            value.AddedValue(input) + value.RoundTrip(input);
        private static U ConstrainedGeneric<T, U>(ref T value, U result) where T : struct, ICrossRevisionValue<int> =>
            value.EchoGeneric(79, result);
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
#endif
}
#endif
