#if DHE_EVOLUTION_CURRENT && DHE_GENERIC_FIELDS_CURRENT && DHE_FIELD_ADDRESSES_CURRENT
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCasesAot
{
    internal static class DheFieldAddressEvolutionAssertions
    {
        private struct ReferenceValue
        {
            public object Payload;
            public int Number;
        }

        public static void Validate(Action<string, Action> check)
        {
            check("field-address-primitive-alias", () =>
            {
                var item = new DheAddedGenericType<int>(17);
                ref int value = ref item.AddedValue;
                value = 23;
                Require(item.AddedValue == 23, "ref store");
                item.AddedValue = 29;
                Require(value == 29, "direct store preserves alias");
                Field<int>().SetValue(item, 31);
                Require(value == 31 && item.Value == 17, "reflection store and original field identity");
                ref int second = ref item.AddedValue;
                second = 37;
                Require(value == 37, "repeated address aliases");
            });
            check("field-address-ref-return", () =>
            {
                var item = new DheAddedGenericType<int>(11);
                ref int value = ref Reference(item);
                Field<int>().SetValue(item, 43);
                value += 2;
                Require(item.AddedValue == 45 && item.Value == 11, "generic ref return");
            });
            check("field-address-ref-out-boundary", () =>
            {
                var item = new DheAddedGenericType<int>(7);
                ref int value = ref item.AddedValue;
                Require(int.TryParse("73", out value), "AOT out call");
                Require(Interlocked.Add(ref value, 4) == 77 && item.AddedValue == 77, "AOT ref call");
            });
            check("field-address-nullable-alias", () =>
            {
                var item = new DheAddedGenericType<int?>(19);
                ref int? value = ref item.AddedValue;
                Require(!value.HasValue, "nullable default");
                value = 41;
                Require((int)Field<int?>().GetValue(item) == 41, "nullable ref store");
                Field<int?>().SetValue(item, null);
                Require(!value.HasValue, "nullable null store preserves alias");
                item.AddedValue = 47;
                Require(value.Value == 47 && item.Value == 19, "nullable direct store preserves alias");
            });
            check("field-address-struct-alias", () =>
            {
                var item = new DheAddedGenericType<SmallValue>(new SmallValue(3, 5));
                ref SmallValue value = ref item.AddedValue;
                value.Number = 53;
                value.Wide = 0x23456789aL;
                SmallValue reflected = (SmallValue)Field<SmallValue>().GetValue(item);
                Require(reflected.Number == 53 && reflected.Wide == 0x23456789aL, "struct ref mutation");
                Field<SmallValue>().SetValue(item, new SmallValue(59, 61));
                Require(value.Number == 59 && value.Wide == 61 && item.Value.Number == 3,
                    "struct reflection replacement preserves alias");
            });
            check("field-address-reference-alias", () =>
            {
                var item = new DheAddedGenericType<string>("original");
                ref string value = ref item.AddedValue;
                Require(value == null, "reference default");
                value = "first";
                Require(Interlocked.CompareExchange(ref value, "second", "first") == "first",
                    "reference compare exchange");
                Field<string>().SetValue(item, "third");
                Require(value == "third" && item.AddedValue == "third", "reference reflection store");
            });
            check("field-address-neighbor-storage", () =>
            {
                var item = new DheAddedGenericType<int>(11);
                ref int value = ref item.AddedValue;
                value = 67;
                int count = 0;
                foreach (FieldInfo field in typeof(DheAddedGenericType<int>).GetFields())
                    if (field.Name.StartsWith("AddressPadding", StringComparison.Ordinal))
                    {
                        field.SetValue(item, ++count);
                        Require(value == 67, "neighbor storage preserves field address");
                    }
                item.AddedItems = new[] { 71 };
                Field<int>().SetValue(item, 73);
                Require(count == 32 && value == 73 && item.AddedItems[0] == 71 && item.Value == 11,
                    "neighbor storage and original fields");
            });
            check("field-address-interlocked-concurrent", () =>
            {
                var item = new DheAddedGenericType<long>(17);
                var tasks = new Task[8];
                for (int index = 0; index < tasks.Length; ++index)
                    tasks[index] = Task.Run(() =>
                    {
                        for (int repeat = 0; repeat < 128; ++repeat)
                            Interlocked.Increment(ref item.AddedValue);
                    });
                Task.WaitAll(tasks);
                Require(item.AddedValue == 1024 && item.Value == 17, "one published address under concurrency");
            });
            check("field-address-struct-reference-gc", () =>
            {
                var item = new DheAddedGenericType<ReferenceValue>(default);
                ref ReferenceValue value = ref item.AddedValue;
                WeakReference weak = WritePayload(ref value);
                Collect();
                Require(weak.IsAlive && ReferenceEquals(weak.Target, value.Payload) && value.Number == 79,
                    "inline struct reference write barrier");
                Field<ReferenceValue>().SetValue(item, new ReferenceValue { Payload = "new", Number = 83 });
                Require((string)value.Payload == "new" && value.Number == 83, "struct reference reflection store");
            });
            check("field-address-owner-lifetime", () =>
            {
                ref int value = ref CreateOwnerReference(out WeakReference weak);
                Collect();
                Require(weak.IsAlive && value == 89, "field reference retains owner");
                value = 97;
                Require(((DheAddedGenericType<int>)weak.Target).AddedValue == 97, "live owner reads same field");
            });
            check("field-address-owner-cycle-collection", () =>
            {
                WeakReference? weak = null;
                var thread = new Thread(() => weak = CreateCycle());
                thread.Start();
                thread.Join();
                for (int attempt = 0; attempt < 4 && weak!.IsAlive; ++attempt) Collect();
                Require(weak != null && !weak.IsAlive, "unreachable owner and cell cycle is collectible");
            });
            check("field-address-null-owner", () =>
            {
                bool caught = false;
                try { Reference<int>(null!) = 101; }
                catch (NullReferenceException) { caught = true; }
                Require(caught, "null field owner");
            });
        }

        private static FieldInfo Field<T>() => typeof(DheAddedGenericType<T>).GetField("AddedValue")!;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ref T Reference<T>(DheAddedGenericType<T> item) => ref item.AddedValue;

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static ref int CreateOwnerReference(out WeakReference weak)
        {
            var item = new DheAddedGenericType<int>(7);
            item.AddedValue = 89;
            weak = new WeakReference(item);
            return ref item.AddedValue;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference CreateCycle()
        {
            var item = new DheAddedGenericType<object>(null!);
            Reference(item) = item;
            return new WeakReference(item);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference WritePayload(ref ReferenceValue value)
        {
            var payload = new byte[97];
            value.Payload = payload;
            value.Number = 79;
            return new WeakReference(payload);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Collect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
