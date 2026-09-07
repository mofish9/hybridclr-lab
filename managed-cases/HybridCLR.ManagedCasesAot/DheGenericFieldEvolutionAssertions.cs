#if DHE_EVOLUTION_CURRENT && DHE_GENERIC_FIELDS_CURRENT
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCasesAot
{
    internal static class DheGenericFieldEvolutionAssertions
    {
        public static void Validate(Action<string, Action> check)
        {
            check("generic-fields-concurrent-first-touch", () =>
            {
                var tasks = new Task[8];
                for (int index = 0; index < tasks.Length; index++)
                    tasks[index] = Task.Run(() =>
                    {
                        var payload = new DheDemoCalculator();
                        for (int repeat = 0; repeat < 16; repeat++)
                        {
                            var item = new DheAddedGenericType<DheDemoCalculator>(payload);
                            FieldInfo field = Field<DheDemoCalculator>(nameof(item.AddedValue));
                            item.AddedValue = payload;
                            Require(ReferenceEquals(field.GetValue(item), payload), "concurrent direct store");
                            field.SetValue(item, null);
                            Require(item.AddedValue == null && ReferenceEquals(item.Value, payload),
                                "concurrent reflection store and original field");
                        }
                    });
                Task.WaitAll(tasks);
            });
            check("generic-fields-primitive", () =>
            {
                var first = new DheAddedGenericType<int>(11);
                var second = new DheAddedGenericType<int>(12);
                FieldInfo field = Field<int>(nameof(first.AddedValue));
                Require(first.AddedValue == 0 && (int)field.GetValue(first) == 0, "primitive default");
                first.AddedValue = 31;
                Require((int)field.GetValue(first) == 31 && second.AddedValue == 0, "instance isolation");
                field.SetValue(second, 47);
                Require(second.AddedValue == 47 && first.AddedValue == 31 && first.Value == 11 && second.Value == 12,
                    "primitive roundtrip preserves original storage");
            });
            check("generic-fields-reference", () =>
            {
                var item = new DheAddedGenericType<string>("original");
                FieldInfo field = Field<string>(nameof(item.AddedValue));
                Require(item.AddedValue == null && field.GetValue(item) == null, "reference default");
                item.AddedValue = "direct";
                Require((string)field.GetValue(item) == "direct", "reference direct store");
                field.SetValue(item, "reflected");
                Require(item.AddedValue == "reflected" && item.Value == "original", "reference reflection store");
            });
            check("generic-fields-struct", () =>
            {
                var item = new DheAddedGenericType<SmallValue>(new SmallValue(3, 5));
                FieldInfo field = Field<SmallValue>(nameof(item.AddedValue));
                SmallValue empty = item.AddedValue;
                Require(empty.Number == 0 && empty.Wide == 0, "struct default");
                item.AddedValue = new SmallValue(17, 0x123456789L);
                SmallValue reflected = (SmallValue)field.GetValue(item);
                Require(reflected.Number == 17 && reflected.Wide == 0x123456789L, "struct direct store");
                field.SetValue(item, new SmallValue(19, 0x23456789aL));
                SmallValue read = item.AddedValue;
                Require(read.Number == 19 && read.Wide == 0x23456789aL && item.Value.Number == 3,
                    "struct reflection store preserves original field");
            });
            check("generic-fields-nullable", () =>
            {
                var item = new DheAddedGenericType<int?>(13);
                FieldInfo field = Field<int?>(nameof(item.AddedValue));
                Require(item.AddedValue == null && field.GetValue(item) == null, "nullable default");
                item.AddedValue = 29;
                Require((int)field.GetValue(item) == 29, "nullable boxes underlying value");
                field.SetValue(item, 37);
                Require(item.AddedValue == 37, "nullable reflection value");
                field.SetValue(item, null);
                Require(item.AddedValue == null && item.Value == 13, "nullable reflection null");
            });
            check("generic-fields-array-and-nested-type", () =>
            {
                var payload = new DheDemoCalculator();
                var values = new List<DheDemoCalculator[]> { new[] { payload } };
                var item = new DheAddedGenericType<List<DheDemoCalculator[]>>(values);
                FieldInfo valueField = Field<List<DheDemoCalculator[]>>(nameof(item.AddedValue));
                FieldInfo arrayField = Field<List<DheDemoCalculator[]>>(nameof(item.AddedItems),
                    typeof(List<DheDemoCalculator[]>[]));
                valueField.SetValue(item, values);
                item.AddedItems = new[] { values };
                Require(ReferenceEquals(item.AddedValue[0][0], payload) &&
                    ReferenceEquals(((List<DheDemoCalculator[]>[])arrayField.GetValue(item))[0][0][0], payload),
                    "nested field type and Base object identity");
                arrayField.SetValue(item, null);
                Require(item.AddedItems == null, "generic array reflection null");
            });
            check("generic-fields-static-isolation", () =>
            {
                DheAddedGenericType<int>.AddedShared = 71;
                DheAddedGenericType<string>.AddedShared = "separate";
                DheAddedGenericType<int>.AddedCount = 3;
                DheAddedGenericType<string>.AddedCount = 5;
                FieldInfo intField = Field<int>(nameof(DheAddedGenericType<int>.AddedShared));
                FieldInfo textField = Field<string>(nameof(DheAddedGenericType<string>.AddedShared));
                Require((int)intField.GetValue(null) == 71 && (string)textField.GetValue(null) == "separate",
                    "static direct store per closed type");
                intField.SetValue(null, 73);
                textField.SetValue(null, "changed");
                Require(DheAddedGenericType<int>.AddedShared == 73 &&
                    DheAddedGenericType<string>.AddedShared == "changed" &&
                    DheAddedGenericType<int>.AddedCount == 3 && DheAddedGenericType<string>.AddedCount == 5,
                    "static reflection store and non-generic field isolation");
            });
            check("generic-fields-open-and-cached-reflection", () =>
            {
                Type open = typeof(DheAddedGenericType<>);
                FieldInfo field = open.GetField("AddedValue");
                Require(field != null && field.DeclaringType == open && field.FieldType == open.GetGenericArguments()[0],
                    "open generic field metadata");
                FieldInfo first = Field<int>("AddedValue");
                Require(ReferenceEquals(first, Field<int>("AddedValue")), "stable closed reflection identity");
            });
            check("generic-fields-gc-retention", () =>
            {
                var owner = new DheAddedGenericType<object>(null!);
                WeakReference payload = StorePayload(owner);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Require(payload.IsAlive && ReferenceEquals(payload.Target, owner.AddedValue) &&
                    ((byte[])Field<object>("AddedValue").GetValue(owner))[0] == 83,
                    "generic sidecar retains reference payload");
                GC.KeepAlive(owner);
            });
        }

        private static FieldInfo Field<T>(string name, Type? expectedType = null)
        {
            Type owner = typeof(DheAddedGenericType<T>);
            FieldInfo field = owner.GetField(name) ?? throw new MissingFieldException(owner.FullName, name);
            Require(field.DeclaringType == owner && field.FieldType == (expectedType ?? typeof(T)),
                "closed field metadata: " + owner + "." + name);
            return field;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference StorePayload(DheAddedGenericType<object> owner)
        {
            var payload = new byte[41];
            payload[0] = 83;
            owner.AddedValue = payload;
            return new WeakReference(payload);
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
#endif
