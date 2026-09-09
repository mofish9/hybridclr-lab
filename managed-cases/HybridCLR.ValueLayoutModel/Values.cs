using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.ValueLayout
{
    public struct Payload
    {
        public int Count;
#if DHE_VALUE_LAYOUT_CURRENT
        public long Extra;
        public object Reference;
#endif
    }
    public struct Nested { public Payload Value; public int Tail; }
    public struct GenericValue<T> { public T Value; public short Marker; }
    public struct GenericGrowing<T>
    {
        public int Count;
#if DHE_VALUE_LAYOUT_CURRENT
        public T Added;
#endif
    }
    public struct Shrinking
    {
        public int Kept;
#if !DHE_VALUE_LAYOUT_CURRENT
        public long Removed;
#endif
    }
    public struct Reordered
    {
#if DHE_VALUE_LAYOUT_CURRENT
        public int Number;
        public long Wide;
#else
        public long Wide;
        public int Number;
#endif
    }
    public struct Retyped
    {
#if DHE_VALUE_LAYOUT_CURRENT
        public long Value;
#else
        public int Value;
#endif
    }
    public struct UnchangedValue { public int Value; }
    public class InlineOwner { public Nested Value; public int Neighbor; }
    public sealed class InlineChild : InlineOwner { public long End; }
    public class ReferenceOwner { public InlineOwner Value; public int Neighbor; }
    public class GenericOwner<T> { public T Value; public int Neighbor; }

    public static class Factory
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GetRevision()
        {
#if DHE_METHOD_UPDATE || DHE_RESOURCE_CURRENT
            return 73;
#elif DHE_RESOURCE_BASE_NEW
            return 51;
#else
            return 41;
#endif
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int UnchangedRevision() => 5;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Payload Create()
        {
            var value = new Payload { Count = 17 };
#if DHE_VALUE_LAYOUT_CURRENT
            value.Extra = 90000000001L;
            value.Reference = new object();
#endif
            return value;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object Box(Payload value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static T Identity<T>(T value) => value;
        public static bool HasCurrentFields(Payload value)
        {
#if DHE_VALUE_LAYOUT_CURRENT
            return value.Count == 17 && value.Extra == 90000000001L && value.Reference != null;
#else
            return value.Count == 17;
#endif
        }
    }

    public static class ValueLayoutProbe
    {
        public static string[] Run()
        {
            var records = new List<string>();
            Check("default-and-new-fields", () =>
            {
                Payload value = default(Payload);
                Require(value.Count == 0, "default original field");
#if DHE_VALUE_LAYOUT_CURRENT
                Require(value.Extra == 0 && value.Reference == null, "default added fields");
#endif
            }, records);
            Check("independent-value-copies", () =>
            {
                Payload first = Factory.Create(), second = first;
                second.Count = 29;
                Require(first.Count == 17 && second.Count == 29, "copy original field");
#if DHE_VALUE_LAYOUT_CURRENT
                second.Extra = 31;
                Require(first.Extra == 90000000001L && second.Extra == 31 &&
                    ReferenceEquals(first.Reference, second.Reference), "copy value and reference fields");
#endif
            }, records);
            Check("nested-values", () =>
            {
                var first = new Nested { Value = Factory.Create(), Tail = 37 };
                var second = first;
                second.Value.Count = 41;
                Require(first.Value.Count == 17 && second.Value.Count == 41 && second.Tail == 37, "nested copy");
            }, records);
            Check("generic-values", () =>
            {
                var first = new GenericValue<Payload> { Value = Factory.Create(), Marker = 43 };
                var second = Factory.Identity(first);
                second.Value.Count = 47;
                Require(first.Value.Count == 17 && second.Value.Count == 47 && second.Marker == 43, "generic copy");
            }, records);
            Check("generic-layout-addition", () =>
            {
                var value = new GenericGrowing<Payload> { Count = 53 };
#if DHE_VALUE_LAYOUT_CURRENT
                value.Added = Factory.Create();
                Require(value.Added.Count == 17, "generic added value");
#endif
                Require(value.Count == 53, "generic original field");
            }, records);
            Check("array-elements-and-aliases", () =>
            {
                var items = new Payload[2];
                items[0] = Factory.Create(); items[1] = items[0];
                ref Payload alias = ref items[0];
                alias.Count = 59;
                Require(items[0].Count == 59 && items[1].Count == 17, "array elements and byref");
#if DHE_VALUE_LAYOUT_CURRENT
                alias.Extra = 61;
                Require(items[1].Extra == 90000000001L, "array new fields are independent");
#endif
            }, records);
            Check("array-copy-and-clone", () =>
            {
                var original = new[] { Factory.Create(), Factory.Create() };
                var copy = new Payload[2]; Array.Copy(original, copy, 2);
                var clone = (Payload[])original.Clone();
                copy[0].Count = 67; clone[1].Count = 71;
                Require(original[0].Count == 17 && original[1].Count == 17 &&
                    copy[0].Count == 67 && clone[1].Count == 71, "bulk value copies");
            }, records);
            Check("nullable-values", () =>
            {
                Payload? first = Factory.Create(), empty = null;
                Payload value = first.Value; value.Count = 73;
                Require(first.Value.Count == 17 && value.Count == 73 && !empty.HasValue, "nullable copy");
            }, records);
            Check("boxing-and-reflection", () =>
            {
                object boxed = Factory.Box(Factory.Create());
                typeof(Payload).GetField("Count").SetValue(boxed, 79);
                Require(((Payload)boxed).Count == 79, "boxed field");
#if DHE_VALUE_LAYOUT_CURRENT
                FieldInfo extra = typeof(Payload).GetField("Extra");
                extra.SetValue(boxed, 80000000003L);
                Require(((Payload)boxed).Extra == 80000000003L, "boxed new field");
#endif
            }, records);
            Check("generic-collections", () =>
            {
                var values = new List<Payload> { Factory.Create(), Factory.Create() };
                Payload copy = values[0]; copy.Count = 83; values[1] = copy;
                Require(values[0].Count == 17 && values[1].Count == 83, "List value storage");
                var map = new Dictionary<int, Payload> { [1] = copy };
                Require(map[1].Count == 83, "Dictionary value storage");
            }, records);
            Check("reference-containers-and-derived-layout", () =>
            {
                var owner = new InlineChild { Value = new Nested { Value = Factory.Create(), Tail = 89 },
                    Neighbor = 97, End = 90000000007L };
                Require(owner.Value.Value.Count == 17 && owner.Value.Tail == 89 &&
                    owner.Neighbor == 97 && owner.End == 90000000007L, "class and derived offsets");
                var generic = new GenericOwner<Payload> { Value = Factory.Create(), Neighbor = 101 };
                Require(generic.Value.Count == 17 && generic.Neighbor == 101, "generic class offsets");
            }, records);
            Check("removed-and-retyped-fields", () =>
            {
                var shrunk = new Shrinking { Kept = 103 };
                var retyped = new Retyped { Value = 107 };
                Require(shrunk.Kept == 103 && retyped.Value == 107, "shrink and replace");
#if DHE_VALUE_LAYOUT_CURRENT
                Require(typeof(Shrinking).GetField("Removed") == null &&
                    typeof(Retyped).GetField("Value").FieldType == typeof(long), "current reflection");
#endif
            }, records);
            Check("reordered-fields", () =>
            {
                var value = new Reordered { Number = 109, Wide = 90000000011L };
                Require(value.Number == 109 && value.Wide == 90000000011L, "reordered offsets");
            }, records);
            Check("managed-reference-gc", () =>
            {
                var values = new Payload[1]; values[0] = Factory.Create();
#if DHE_VALUE_LAYOUT_CURRENT
                var weak = new WeakReference(values[0].Reference);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Require(weak.IsAlive && ReferenceEquals(weak.Target, values[0].Reference), "new GC descriptor");
#endif
                GC.KeepAlive(values);
            }, records);
            return records.ToArray();
        }
        private static void Check(string name, Action action, List<string> records)
        {
            try { action(); records.Add(name + "\tpassed"); }
            catch (Exception exception) { records.Add(name + "\t" + exception); }
        }
        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
