#if DHE_CLASS_VIRTUAL_BASE
#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HybridCLR.Lab.CrossAssemblyDerived
{
    public class RevisionVirtualRoot
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public virtual int Inserted(int value) => value + 1000;
#endif
        public virtual int Stable(int value) => value + 10;
        public virtual int RemovedOverride(int value) => value + 20;
        public virtual int Hidden(int value) => value + 30;
    }

    public class RevisionVirtualChild : RevisionVirtualRoot
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public override int Stable(int value) => value + 2000;
        public new virtual int Hidden(int value) => value + 3000;
#else
        public override int RemovedOverride(int value) => value + 40;
#endif
    }

    public sealed class RevisionVirtualLeaf : RevisionVirtualChild { }
    public sealed class RevisionVirtualKept : RevisionVirtualRoot
    {
        public override int Stable(int value) => value + 50;
    }
#if DHE_CLASS_VIRTUAL_CURRENT
    public sealed class RevisionVirtualNewLeaf : RevisionVirtualChild
    {
        public override int Hidden(int value) => value + 4000;
    }
#endif

    public abstract class RevisionAbstractRoot
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public abstract int Added(int value);
#endif
        public abstract int Stable(int value);
    }
    public sealed class RevisionAbstractChild : RevisionAbstractRoot
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public override int Added(int value) => value + 5000;
#endif
        public override int Stable(int value) => value + 60;
    }

    public struct RevisionVirtualPayload
    {
        public long Number;
        public double Fraction;
        public string Text;
    }
    public class RevisionGenericVirtual<T>
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public virtual T Inserted(T value) => default!;
        public virtual U AddedGeneric<U>(T input, U value) => value;
#endif
        public virtual T Stable(T value) => value;
        public virtual U Echo<U>(T input, U value) => value;
    }
    public class RevisionGenericVirtualChild<T> : RevisionGenericVirtual<T>
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public override T Inserted(T value) => value;
        public override U AddedGeneric<U>(T input, U value) => value;
        public override U Echo<U>(T input, U value) => value;
#endif
    }
    public sealed class RevisionGenericVirtualLeaf : RevisionGenericVirtualChild<string> { }

    public static class ClassVirtualNativeCallers
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Stable(RevisionVirtualRoot receiver, int value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Removed(RevisionVirtualRoot receiver, int value) => receiver.RemovedOverride(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Hidden(RevisionVirtualRoot receiver, int value) => receiver.Hidden(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Abstract(RevisionAbstractRoot receiver, int value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Integer(RevisionGenericVirtual<int> receiver, int value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Reference(RevisionGenericVirtual<string> receiver, string value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static RevisionVirtualPayload Value(RevisionGenericVirtual<RevisionVirtualPayload> receiver,
            RevisionVirtualPayload value) => receiver.Stable(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string Generic(RevisionGenericVirtual<int> receiver, int value) => receiver.Echo(value, "echo");
    }

    public static class ClassVirtualEvolutionProbe
    {
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        // Also run in Base/no-op to ensure these generic AOT instantiations exist.
        public static void Common()
        {
            Require(ClassVirtualNativeCallers.Stable(new RevisionVirtualRoot(), 1) == 11 &&
                ClassVirtualNativeCallers.Stable(new RevisionVirtualKept(), 1) == 51, "retained root and override");
            Require(ClassVirtualNativeCallers.Hidden(new RevisionVirtualChild(), 2) == 32 &&
                ClassVirtualNativeCallers.Abstract(new RevisionAbstractChild(), 3) == 63, "root slot and abstract");
            var value = new RevisionVirtualPayload { Number = 90000000001L, Fraction = 1.25, Text = "value" };
            var result = ClassVirtualNativeCallers.Value(new RevisionGenericVirtualChild<RevisionVirtualPayload>(), value);
            Require(ClassVirtualNativeCallers.Integer(new RevisionGenericVirtualChild<int>(), 7) == 7 &&
                ClassVirtualNativeCallers.Reference(new RevisionGenericVirtualLeaf(), "reference") == "reference" &&
                ClassVirtualNativeCallers.Generic(new RevisionGenericVirtualChild<int>(), 9) == "echo" &&
                result.Number == value.Number && result.Fraction == value.Fraction && result.Text == value.Text,
                "retained closed generic and method instantiations");
        }

#if DHE_CLASS_VIRTUAL_CURRENT
        public static void NativeCallers()
        {
            var routes = new List<string>();
            Require(Observe("Stable", () => ClassVirtualNativeCallers.Stable(new RevisionVirtualLeaf(), 11), routes) == 2011,
                "old native caller selects newly added inherited override");
            Require(Observe("Removed", () => ClassVirtualNativeCallers.Removed(new RevisionVirtualLeaf(), 13), routes) == 33,
                "removed override falls back to parent");
            Require(Observe("Hidden", () => ClassVirtualNativeCallers.Hidden(new RevisionVirtualNewLeaf(), 17), routes) == 47,
                "native root caller ignores new-slot hiding");
            Require(Observe("Abstract", () => ClassVirtualNativeCallers.Abstract(new RevisionAbstractChild(), 19), routes) == 79,
                "retained abstract slot");
            Require(Observe("Integer", () => ClassVirtualNativeCallers.Integer(new RevisionGenericVirtualChild<int>(), 23), routes) == 23,
                "native closed integer virtual");
            Require(Observe("Reference", () => ClassVirtualNativeCallers.Reference(new RevisionGenericVirtualLeaf(), "native"), routes) == "native",
                "native closed reference virtual");
            var value = new RevisionVirtualPayload { Number = 90000000003L, Fraction = 2.5, Text = "native-value" };
            var result = Observe("Value", () => ClassVirtualNativeCallers.Value(new RevisionGenericVirtualChild<RevisionVirtualPayload>(), value), routes);
            Require(result.Number == value.Number && result.Fraction == value.Fraction && result.Text == value.Text, "native value return");
            Require(Observe("Generic", () => ClassVirtualNativeCallers.Generic(new RevisionGenericVirtualChild<int>(), 29), routes) == "echo",
                "native generic virtual selects added override");
            string path = Environment.GetEnvironmentVariable("HYBRIDCLR_DHE_EVOLUTION_EVIDENCE");
            if (!string.IsNullOrEmpty(path) && routes.Count != 0)
                File.WriteAllLines(path + ".class-virtual-callers", routes);
        }

        private static T Observe<T>(string name, Func<T> action, List<string> routes)
        {
            Type api = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("HybridCLR.RuntimeApi", false)).FirstOrDefault(type => type != null);
            if (api == null) return action();
            MethodInfo changed = api.GetMethod("IsDifferentialMethodChanged")!;
            MethodInfo aot = api.GetMethod("GetDifferentialAotEntryCount")!;
            MethodInfo interpreted = api.GetMethod("GetDifferentialInterpreterEntryCount")!;
            bool isChanged = (bool)changed.Invoke(null, new object[] { typeof(ClassVirtualNativeCallers).GetMethod(name)! })!;
            int beforeAot = (int)aot.Invoke(null, null)!, beforeInterpreted = (int)interpreted.Invoke(null, null)!;
            T result = action();
            routes.Add(name + "\t" + (isChanged ? "1" : "0") + "\t" + ((int)aot.Invoke(null, null)! - beforeAot) +
                "\t" + ((int)interpreted.Invoke(null, null)! - beforeInterpreted));
            return result;
        }

        public static void InsertedAndOverrides()
        {
            RevisionVirtualRoot root = new RevisionVirtualLeaf();
            Require(root.Inserted(31) == 1031 && root.Stable(31) == 2031 && root.RemovedOverride(31) == 51,
                "interpreter resolves inserted, overridden and removed-override slots");
            Require(new RevisionVirtualKept().Stable(37) == 87, "unchanged override after slot shift");
            RevisionAbstractRoot abstractRoot = new RevisionAbstractChild();
            Require(abstractRoot.Added(41) == 5041 && abstractRoot.Stable(41) == 101, "new abstract declaration");
        }

        public static void NewSlot()
        {
            var child = new RevisionVirtualNewLeaf();
            Require(((RevisionVirtualRoot)child).Hidden(43) == 73 && ((RevisionVirtualChild)child).Hidden(43) == 4043,
                "root and new-slot calls stay distinct");
        }

        public static void Generics()
        {
            RevisionGenericVirtual<int> integer = new RevisionGenericVirtualChild<int>();
            RevisionGenericVirtual<string> reference = new RevisionGenericVirtualLeaf();
            var payload = new RevisionVirtualPayload { Number = 90000000005L, Fraction = 3.75, Text = "generic" };
            Require(integer.Inserted(47) == 47 && reference.Inserted("reference") == "reference" &&
                integer.AddedGeneric(1, payload).Text == payload.Text && reference.Echo("input", payload).Number == payload.Number,
                "new and retained generic virtual methods");
        }

        public static void Delegates()
        {
            RevisionVirtualRoot root = new RevisionVirtualLeaf();
            Func<int, int> added = root.Inserted, changed = root.Stable, removed = root.RemovedOverride;
            RevisionGenericVirtual<int> generic = new RevisionGenericVirtualChild<int>();
            Func<int, string, string> echo = generic.AddedGeneric<string>;
            Require(added(53) == 1053 && changed(53) == 2053 && removed(53) == 73 && echo(1, "delegate") == "delegate",
                "closed virtual delegates");
        }

        public static void Reflection()
        {
            var receiver = new RevisionVirtualLeaf();
            Require((int)typeof(RevisionVirtualRoot).GetMethod("Inserted")!.Invoke(receiver, new object[] { 59 })! == 1059 &&
                (int)typeof(RevisionVirtualRoot).GetMethod("Stable")!.Invoke(receiver, new object[] { 59 })! == 2059,
                "reflection selects new and retained declarations");
            MethodInfo method = typeof(RevisionGenericVirtual<string>).GetMethod("AddedGeneric")!.MakeGenericMethod(typeof(long));
            Require((long)method.Invoke(new RevisionGenericVirtualLeaf(), new object[] { "input", 90000000007L })! == 90000000007L,
                "reflection generic virtual");
        }

        public static void BaseDefinitions()
        {
            Require(typeof(RevisionVirtualChild).GetMethod("Stable")!.GetBaseDefinition().DeclaringType == typeof(RevisionVirtualRoot),
                "new override base definition");
            Require(typeof(RevisionVirtualKept).GetMethod("Stable")!.GetBaseDefinition().DeclaringType == typeof(RevisionVirtualRoot),
                "retained override base definition after slot shift");
            Require(typeof(RevisionVirtualNewLeaf).GetMethod("Hidden")!.GetBaseDefinition().DeclaringType == typeof(RevisionVirtualChild),
                "new-slot base definition");
            Require(typeof(RevisionAbstractChild).GetMethod("Added")!.GetBaseDefinition().DeclaringType == typeof(RevisionAbstractRoot),
                "added abstract base definition");
            Require(typeof(RevisionGenericVirtualChild<int>).GetMethod("Echo")!.GetBaseDefinition().DeclaringType == typeof(RevisionGenericVirtual<int>),
                "closed generic override base definition");
        }

        public static void Exceptions()
        {
            bool threw = false;
            try { ClassVirtualNativeCallers.Stable(null!, 1); }
            catch (NullReferenceException) { threw = true; }
            Require(threw, "native null receiver retains exception semantics");
        }

        // Invoke before the sequential dispatch groups in Player; this receiver
        // type and method pair must not have been warmed by another group.
        public static void ConcurrentFirstTouch()
        {
            using var ready = new CountdownEvent(8);
            using var start = new ManualResetEventSlim(false);
            var errors = new Exception[8];
            var threads = Enumerable.Range(0, 8).Select(index => new Thread(() =>
            {
                ready.Signal();
                start.Wait();
                try
                {
                    var receiver = new RevisionVirtualConcurrentChild();
                    for (int count = 0; count < 100; ++count)
                        Require(ClassVirtualNativeCallers.Stable(receiver, count) == 6000 + count &&
                            ((RevisionVirtualRoot)receiver).Inserted(count) == 1000 + count, "concurrent dispatch");
                }
                catch (Exception exception) { errors[index] = exception; }
            })).ToArray();
            foreach (Thread thread in threads) thread.Start();
            ready.Wait(); start.Set();
            foreach (Thread thread in threads) thread.Join();
            Require(errors.All(error => error == null), "concurrent failures: " + string.Join(";", errors.Select(error => error?.ToString())));
        }
#endif
    }

    public sealed class RevisionVirtualConcurrentChild : RevisionVirtualRoot
    {
#if DHE_CLASS_VIRTUAL_CURRENT
        public override int Stable(int value) => value + 6000;
#endif
    }
}
#endif
