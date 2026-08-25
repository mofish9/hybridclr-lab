using System;
using System.Collections.Generic;
using System.Globalization;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterGenericAndValueTypeCases(List<CaseDefinition> cases)
        {
            RegisterCase(cases, "generic_list_value_types", "generic", GenericListValueTypes, "1,2,3", "9", features: new[] { "list", "generic-sharing", "int" });
            RegisterCase(cases, "generic_dictionary_struct", "generic", GenericDictionaryStruct, "33,2", features: new[] { "dictionary", "generic-sharing", "struct" });
            RegisterCase(cases, "generic_constraint_class", "generic", GenericConstraintClass, "21", features: new[] { "constraint.class", "interface" });
            RegisterCase(cases, "generic_constraint_new", "generic", GenericConstraintNew, "created", features: new[] { "constraint.new", "constructor" });
            RegisterCase(cases, "generic_virtual_method", "generic", GenericVirtualMethod, "42", features: new[] { "generic-virtual", "dispatch" });
            RegisterCase(cases, "generic_interface_struct", "generic", GenericInterfaceStruct, "27,30", features: new[] { "generic-interface", "constrained-call" });
            RegisterCase(cases, "generic_static_per_type", "generic", GenericStaticPerType, "2,1", features: new[] { "generic-static", "cctor" });
            RegisterCase(cases, "generic_default_value", "generic", GenericDefaultValue, "0,False", features: new[] { "default", "valuetype" });
            RegisterCase(cases, "generic_nullable_roundtrip", "generic", GenericNullableRoundtrip, "17,False", features: new[] { "nullable", "generic" });
            RegisterCase(cases, "generic_tuple_roundtrip", "generic", GenericTupleRoundtrip, "3:5", features: new[] { "tuple", "generic" });
            RegisterCase(cases, "generic_delegate_factory", "generic", GenericDelegateFactory, "56", features: new[] { "delegate", "generic-method" });
            RegisterCase(cases, "generic_covariance", "generic", GenericCovariance, "a,b,c", features: new[] { "variance", "interface" });
            RegisterCase(cases, "generic_contravariance", "generic", GenericContravariance, "-1", features: new[] { "contravariance", "comparer" });
            RegisterCase(cases, "struct_copy_semantics", "value-type", StructCopySemantics, "2,9", features: new[] { "struct-copy", "field" });
            RegisterCase(cases, "nested_struct_layout", "value-type", NestedStructLayout, "1,2,3,6", features: new[] { "nested-struct", "layout" });
            RegisterCase(cases, "readonly_struct_method", "value-type", ReadonlyStructMethod, "30", features: new[] { "readonly-struct", "method" });
            RegisterCase(cases, "struct_ref_return", "value-type", StructRefReturn, "18,18", features: new[] { "ref-return", "ldelema" });
            RegisterCase(cases, "in_parameter_struct", "value-type", InParameterStruct, "35", features: new[] { "in", "struct" });
            RegisterCase(cases, "generic_out_parameter", "generic", GenericOutParameter, "42", features: new[] { "out", "generic" });
            RegisterCase(cases, "enum_underlying_sizes", "value-type", EnumUnderlyingSizes, "255,9223372036854775807", features: new[] { "enum", "underlying-type" });
            RegisterCase(cases, "generic_boxing_unboxing", "value-type", GenericBoxingUnboxing, "19,19", features: new[] { "boxing", "generic" });
            RegisterCase(cases, "generic_array_transform", "generic", GenericArrayTransform, "2,4,6", features: new[] { "array", "generic-method" });
        }

        private static CaseObservation GenericListValueTypes()
        {
            List<int> integers = new List<int> { 1, 2, 3 };
            List<SmallValue> values = new List<SmallValue> { new SmallValue(4), new SmallValue(5) };
            return new CaseObservation(
                JoinInts(integers),
                (values[0].Value + values[1].Value).ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation GenericDictionaryStruct()
        {
            Dictionary<int, SmallValue> values = new Dictionary<int, SmallValue>
            {
                [1] = new SmallValue(11),
                [2] = new SmallValue(22),
            };
            return Observation(values[1].Value + values[2].Value, values.Count);
        }

        private static CaseObservation GenericConstraintClass()
        {
            return Observation(ApplyClassConstraint(new NamedValue(7), 3));
        }

        private static int ApplyClassConstraint<T>(T value, int amount) where T : class, IAddable
        {
            return value.Add(amount);
        }

        private static CaseObservation GenericConstraintNew()
        {
            CreatedValue value = Create<CreatedValue>();
            return new CaseObservation(value.Text);
        }

        private static T Create<T>() where T : class, new()
        {
            return new T();
        }

        private static CaseObservation GenericVirtualMethod()
        {
            GenericProcessor<int> processor = new DoublerProcessor();
            return Observation(processor.Process(21));
        }

        private static CaseObservation GenericInterfaceStruct()
        {
            ScaledValue value = new ScaledValue(3);
            return Observation(ApplyGenericInterface(value, 9), ApplyGenericInterface(value, 10));
        }

        private static int ApplyGenericInterface<T>(T value, int input) where T : struct, IScaler
        {
            return value.Scale(input);
        }

        private static CaseObservation GenericStaticPerType()
        {
            int first = GenericCounter<int>.Touch();
            int second = GenericCounter<int>.Touch();
            int other = GenericCounter<string>.Touch();
            return Observation(second, other);
        }

        private static CaseObservation GenericDefaultValue()
        {
            SmallValue value = default;
            string text = default(string) == null ? "False" : "True";
            return new CaseObservation(FormattableString.Invariant($"{value.Value},{text}"));
        }

        private static CaseObservation GenericNullableRoundtrip()
        {
            int? present = Identity<int?>(17);
            int? missing = Identity<int?>(null);
            return new CaseObservation(FormattableString.Invariant($"{present.GetValueOrDefault()},{missing.HasValue}"));
        }

        private static T Identity<T>(T value)
        {
            return value;
        }

        private static CaseObservation GenericTupleRoundtrip()
        {
            ValueTuple<int, int> pair = Identity((3, 5));
            return new CaseObservation(FormattableString.Invariant($"{pair.Item1}:{pair.Item2}"));
        }

        private static CaseObservation GenericDelegateFactory()
        {
            Func<int, int> function = MakeFunction<int>(value => value + 14);
            return Observation(function(42));
        }

        private static Func<T, T> MakeFunction<T>(Func<T, T> function)
        {
            return function;
        }

        private static CaseObservation GenericCovariance()
        {
            IEnumerable<string> strings = new[] { "a", "b", "c" };
            IEnumerable<object> objects = strings;
            return new CaseObservation(string.Join(",", objects));
        }

        private static CaseObservation GenericContravariance()
        {
            IComparer<object> comparer = Comparer<object>.Default;
            IComparer<string> strings = comparer;
            return Observation(strings.Compare("a", "b"));
        }

        private static CaseObservation StructCopySemantics()
        {
            SmallValue first = new SmallValue(2);
            SmallValue second = first;
            second.Value = 9;
            return Observation(first.Value, second.Value);
        }

        private static CaseObservation NestedStructLayout()
        {
            NestedValue value = new NestedValue(1, new SmallValue(2), 3);
            return new CaseObservation(FormattableString.Invariant($"{value.First},{value.Inner.Value},{value.Last},{value.Sum}"));
        }

        private static CaseObservation ReadonlyStructMethod()
        {
            ReadonlyValue value = new ReadonlyValue(5, 6);
            return Observation(value.Sum());
        }

        private static CaseObservation StructRefReturn()
        {
            SmallValue[] values = { new SmallValue(3), new SmallValue(7) };
            ref SmallValue selected = ref Select(values, 1);
            selected.Value += 11;
            return Observation(values[1].Value, selected.Value);
        }

        private static ref SmallValue Select(SmallValue[] values, int index)
        {
            return ref values[index];
        }

        private static CaseObservation InParameterStruct()
        {
            SmallValue value = new SmallValue(12);
            return Observation(ReadIn(value));
        }

        private static int ReadIn(in SmallValue value)
        {
            return value.Value + 23;
        }

        private static CaseObservation GenericOutParameter()
        {
            ParseValue("42", out int value);
            return Observation(value);
        }

        private static void ParseValue<T>(string text, out T value)
        {
            value = (T)Convert.ChangeType(text, typeof(T), CultureInfo.InvariantCulture);
        }

        private static CaseObservation EnumUnderlyingSizes()
        {
            ByteEnum small = ByteEnum.Max;
            LongEnum large = LongEnum.Max;
            return Observation((byte)small, (long)large);
        }

        private static CaseObservation GenericBoxingUnboxing()
        {
            object boxed = Box(19);
            int value = Unbox<int>(boxed);
            return Observation(value, (int)boxed);
        }

        private static object Box<T>(T value)
        {
            return value!;
        }

        private static T Unbox<T>(object value)
        {
            return (T)value;
        }

        private static CaseObservation GenericArrayTransform()
        {
            int[] values = TransformArray(new[] { 1, 2, 3 }, value => value * 2);
            return new CaseObservation(JoinInts(values));
        }

        private static T[] TransformArray<T>(T[] values, Func<T, T> transform)
        {
            T[] result = new T[values.Length];
            for (int i = 0; i < values.Length; i++) result[i] = transform(values[i]);
            return result;
        }

        private interface IAddable
        {
            int Add(int amount);
        }

        private interface IScaler
        {
            int Scale(int value);
        }

        private sealed class NamedValue : IAddable
        {
            private readonly int _value;

            public NamedValue(int value)
            {
                _value = value;
            }

            public int Add(int amount)
            {
                return _value * amount;
            }
        }

        private sealed class CreatedValue
        {
            public string Text { get; } = "created";
        }

        private abstract class GenericProcessor<T>
        {
            public abstract T Process(T value);
        }

        private sealed class DoublerProcessor : GenericProcessor<int>
        {
            public override int Process(int value)
            {
                return value * 2;
            }
        }

        private readonly struct ScaledValue : IScaler
        {
            private readonly int _factor;

            public ScaledValue(int factor)
            {
                _factor = factor;
            }

            public int Scale(int value)
            {
                return value * _factor;
            }
        }

        private static class GenericCounter<T>
        {
            private static int _count;

            public static int Touch()
            {
                return ++_count;
            }
        }

        private struct SmallValue
        {
            public SmallValue(int value)
            {
                Value = value;
            }

            public int Value;
        }

        private readonly struct NestedValue
        {
            public NestedValue(int first, SmallValue inner, int last)
            {
                First = first;
                Inner = inner;
                Last = last;
            }

            public int First { get; }

            public SmallValue Inner { get; }

            public int Last { get; }

            public int Sum => First + Inner.Value + Last;
        }

        private readonly struct ReadonlyValue
        {
            private readonly int _first;
            private readonly int _second;

            public ReadonlyValue(int first, int second)
            {
                _first = first;
                _second = second;
            }

            public int Sum()
            {
                return _first * _second;
            }
        }

        private enum ByteEnum : byte
        {
            Max = byte.MaxValue,
        }

        private enum LongEnum : long
        {
            Max = long.MaxValue,
        }
    }
}
