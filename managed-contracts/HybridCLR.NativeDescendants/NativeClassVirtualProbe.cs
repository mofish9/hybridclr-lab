// This entire assembly is ordinary AOT. It is compiled once against Base and
// must never be rebuilt against Current or distributed in the hotfix payload.
#nullable disable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HybridCLR.Lab.CrossAssemblyDerived;

namespace HybridCLR.Lab.NativeDescendants
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true)]
    public sealed class NativeVirtualMarkerAttribute : Attribute
    {
        public NativeVirtualMarkerAttribute(string label) { Label = label; }
        public string Label { get; }
    }
    public class NativeOverride : RevisionVirtualRoot
    {
        [NativeVirtualMarker("middle")]
        public override int Stable(int value) => value + 7000;
        public int ExplicitBase(int value) => base.Stable(value);
    }
    public sealed class NativeGrandchild : NativeOverride
    {
        public override int Stable(int value) => value + 7100;
    }
    public class NativeHider : RevisionVirtualRoot
    {
        public new virtual int Stable(int value) => value + 7200;
    }
    public sealed class NativeHiddenChild : NativeHider
    {
        public override int Stable(int value) => value + 7300;
    }
    public sealed class NativeInherited : RevisionVirtualChild { }
    public class NativeGenericOverride<T> : RevisionGenericVirtual<T>
    {
        public override U Echo<U>(T input, U value) => value;
    }
    public sealed class NativeGenericChild : NativeGenericOverride<int> { }

    public static class NativeClassVirtualProbe
    {
        public static string[] Run()
        {
            var records = new List<string>();
            Check("base-definitions", BaseDefinitions, records);
            Check("native-overrides", NativeOverrides, records);
            Check("current-inherited", CurrentInherited, records);
            Check("new-slot", NewSlot, records);
            Check("reflection", Reflection, records);
            Check("delegates", Delegates, records);
            Check("generics", Generics, records);
            Check("direct-and-base-calls", DirectAndBaseCalls, records);
            Check("inherited-attributes", InheritedAttributes, records);
            return records.ToArray();
        }

        private static void Check(string name, Action action, List<string> records)
        {
            try { action(); records.Add(name + "\tpassed"); }
            catch (Exception exception) { records.Add(name + "\t" + exception); }
        }
        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
        private static bool IsCurrent => typeof(RevisionVirtualRoot).GetMethod("Inserted") != null;
        private static int InheritedOffset => IsCurrent ? 2000 : 10;

        private static void BaseDefinitions()
        {
            foreach (Type type in new[] { typeof(NativeOverride), typeof(NativeGrandchild) })
            {
                MethodInfo method = type.GetMethod("Stable").GetBaseDefinition();
                Require(method.DeclaringType == typeof(RevisionVirtualRoot) && method.Name == "Stable",
                    type.Name + " root must be RevisionVirtualRoot.Stable, actual=" + method.DeclaringType + "." + method.Name);
            }
            MethodInfo hidden = typeof(NativeHiddenChild).GetMethod("Stable").GetBaseDefinition();
            Require(hidden.DeclaringType == typeof(NativeHider) && hidden.Name == "Stable", "native new-slot root");
            MethodInfo generic = typeof(NativeGenericChild).GetMethod("Echo").GetBaseDefinition();
            Require(generic.DeclaringType == typeof(RevisionGenericVirtual<int>) && generic.Name == "Echo",
                "native closed generic root, actual=" + generic.DeclaringType + "." + generic.Name);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CallRoot(RevisionVirtualRoot receiver, int value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int CallHidden(NativeHider receiver, int value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static U CallGeneric<U>(RevisionGenericVirtual<int> receiver, U value) => receiver.Echo(1, value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int KnownHotfixLeaf(int value) => new RevisionVirtualLeaf().Stable(value);

        private static void NativeOverrides()
        {
            Require(CallRoot(new NativeOverride(), 11) == 7011 && CallRoot(new NativeGrandchild(), 13) == 7113,
                "unchanged native overrides must remain selected");
        }
        private static void CurrentInherited()
        {
            Require(CallRoot(new NativeInherited(), 17) == InheritedOffset + 17, "native receiver inherits Current override");
        }
        private static void NewSlot()
        {
            var receiver = new NativeHiddenChild();
            Require(CallRoot(receiver, 19) == 29 && CallHidden(receiver, 19) == 7319, "native new-slot and original slots stay distinct");
            Require(typeof(NativeHiddenChild).GetMethods().Count(method => method.Name == "Stable") == 2,
                "reflection exposes separate native new-slot and root declarations");
        }
        private static void Reflection()
        {
            MethodInfo stable = typeof(RevisionVirtualRoot).GetMethod("Stable");
            Require((int)stable.Invoke(new NativeGrandchild(), new object[] { 23 }) == 7123 &&
                (int)stable.Invoke(new NativeInherited(), new object[] { 23 }) == InheritedOffset + 23,
                "reflection preserves native and Current overrides");
            Require(typeof(NativeGrandchild).GetMethods().Count(method => method.Name == "Stable") == 1,
                "overrides are not duplicate reflected members");
            MethodInfo added = typeof(RevisionVirtualRoot).GetMethod("Inserted");
            if (added != null)
                Require((int)added.Invoke(new NativeInherited(), new object[] { 29 }) == 1029,
                    "new Current virtual inherited by native receiver");
        }
        private static void Delegates()
        {
            Func<int, int> own = new NativeGrandchild().Stable, inherited = new NativeInherited().Stable;
            Func<int, int> hidden = new NativeHiddenChild().Stable;
            Require(own(31) == 7131 && inherited(31) == InheritedOffset + 31 && hidden(31) == 7331, "native closed delegates");
        }
        private static void Generics()
        {
            var receiver = new NativeGenericChild();
            var value = new RevisionVirtualPayload { Number = 90000000021L, Fraction = 7.5, Text = "native-generic" };
            var result = CallGeneric(receiver, value);
            Require(CallGeneric(receiver, 37) == 37 && CallGeneric(receiver, "generic") == "generic" &&
                result.Number == value.Number && result.Fraction == value.Fraction && result.Text == value.Text,
                "native generic virtual ABI including references in a value return");
            MethodInfo echo = typeof(RevisionGenericVirtual<int>).GetMethod("Echo").MakeGenericMethod(typeof(string));
            Require((string)echo.Invoke(receiver, new object[] { 1, "reflection" }) == "reflection", "native generic reflection invoke");
        }
        private static void DirectAndBaseCalls()
        {
            Require(new NativeOverride().ExplicitBase(41) == 51, "explicit base call must not dispatch to override");
            Require(KnownHotfixLeaf(43) == InheritedOffset + 43, "concretely constructed sealed hotfix receiver");
        }
        private static void InheritedAttributes()
        {
            var marker = typeof(NativeGrandchild).GetMethod("Stable").GetCustomAttributes(typeof(NativeVirtualMarkerAttribute), true)
                .Cast<NativeVirtualMarkerAttribute>().Single();
            Require(marker.Label == "middle", "immediate native parent attributes must not be skipped");
        }
    }
}
