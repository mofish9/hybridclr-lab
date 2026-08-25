using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using HybridCLR.Lab.BoundaryContracts;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterBoundaryCases(List<CaseDefinition> cases)
        {
            const string layer = "player-boundary";
            RegisterCase(cases, "boundary_interpreter_calls_aot", "boundary", BoundaryInterpreterCallsAot, "42", layer: layer, features: new[] { "interp-to-aot", "static-call" });
            RegisterCase(cases, "boundary_aot_calls_interface", "boundary", BoundaryAotCallsInterface, "39", layer: layer, features: new[] { "aot-to-interp", "interface" });
            RegisterCase(cases, "boundary_aot_calls_struct_interface", "boundary", BoundaryAotCallsStructInterface, "3,15,7", "25", layer: layer, features: new[] { "aot-to-interp", "struct", "interface" });
            RegisterCase(cases, "boundary_aot_calls_string_interface", "boundary", BoundaryAotCallsStringInterface, "prefix:hotfix-service", layer: layer, features: new[] { "aot-to-interp", "string" });
            RegisterCase(cases, "boundary_aot_calls_virtual", "boundary", BoundaryAotCallsVirtual, "30", "BoundaryDerived", layer: layer, features: new[] { "aot-to-interp", "virtual" });
            RegisterCase(cases, "boundary_aot_reads_virtual_property", "boundary", BoundaryAotReadsVirtualProperty, "BoundaryDerived", layer: layer, features: new[] { "aot-to-interp", "virtual", "property" });
            RegisterCase(cases, "boundary_aot_calls_delegate", "boundary", BoundaryAotCallsDelegate, "42", layer: layer, features: new[] { "aot-to-interp", "delegate" });
            RegisterCase(cases, "boundary_aot_calls_func", "boundary", BoundaryAotCallsFunc, "34", layer: layer, features: new[] { "aot-to-interp", "delegate", "generic-delegate" });
            RegisterCase(cases, "boundary_aot_generic_struct_echo", "boundary", BoundaryAotGenericStructEcho, "4,8,7", layer: layer, features: new[] { "aot-to-interp", "generic", "struct" });
            RegisterCase(cases, "boundary_aot_constrained_struct", "boundary", BoundaryAotConstrainedStruct, "27", layer: layer, features: new[] { "aot-to-interp", "generic", "constraint" });
            RegisterCase(cases, "boundary_aot_sum_array", "boundary", BoundaryAotSumArray, "15", layer: layer, features: new[] { "aot-to-interp", "array" });
            RegisterCase(cases, "boundary_aot_ref_out_struct", "boundary", BoundaryAotRefOutStruct, "3,6,7", "16", layer: layer, features: new[] { "aot-to-interp", "ref", "out", "struct" });
            RegisterCase(cases, "boundary_aot_catches_interpreter_exception", "boundary", BoundaryAotCatchesInterpreterException, "91", layer: layer, features: new[] { "aot-to-interp", "exception" });
            RegisterCase(cases, "boundary_interpreter_catches_aot_exception", "boundary", BoundaryInterpreterCatchesAotException, "ArgumentException", layer: layer, features: new[] { "interp-to-aot", "exception" });
            RegisterCase(cases, "boundary_aot_boxes_struct", "boundary", BoundaryAotBoxesStruct, "5,12,3", layer: layer, features: new[] { "aot-to-interp", "boxing", "struct" });
            RegisterCase(cases, "boundary_repeated_aot_calls", "boundary", BoundaryRepeatedAotCalls, "55", layer: layer, features: new[] { "interp-to-aot", "call-frequency" });
            RegisterCase(cases, "boundary_aot_generic_string_echo", "boundary", BoundaryAotGenericStringEcho, "boundary", layer: layer, features: new[] { "aot-to-interp", "generic", "string" });
            RegisterCase(cases, "boundary_null_interface_exception", "boundary", BoundaryNullInterfaceException, "NullReferenceException", layer: layer, features: new[] { "aot-to-interp", "null", "exception" });
            RegisterCase(cases, "boundary_object_array_roundtrip", "boundary", BoundaryObjectArrayRoundtrip, "1,hotfix,3", layer: layer, features: new[] { "aot-to-interp", "array", "object" });
            RegisterCase(cases, "boundary_enum_payload_roundtrip", "boundary", BoundaryEnumPayloadRoundtrip, "7", layer: layer, features: new[] { "aot-to-interp", "enum", "struct" });
            RegisterCase(cases, "boundary_fgs_hot_enum", "boundary", BoundaryFgsHotEnum, "Large", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "enum" });
            RegisterCase(cases, "boundary_fgs_large_struct", "boundary", BoundaryFgsLargeStruct, "3,5,7.5,11", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "struct" });
            RegisterCase(cases, "boundary_fgs_nullable_struct", "boundary", BoundaryFgsNullableStruct, "13,True", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "nullable", "struct" });
            RegisterCase(cases, "boundary_fgs_ref_swap", "boundary", BoundaryFgsRefSwap, "9,2", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "ref", "struct" });
            RegisterCase(cases, "boundary_fgs_generic_delegate", "boundary", BoundaryFgsGenericDelegate, "7,12,4.5,15", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "struct" });
            RegisterCase(cases, "boundary_fgs_aot_calls_hot_generic_delegate", "boundary", BoundaryFgsAotCallsHotGenericDelegate, "131,137,13.5,139", layer: layer, features: new[] { "aot-to-interp", "full-generic-sharing", "delegate", "generic-method", "struct-return" });
            RegisterCase(cases, "boundary_fgs_generic_interface", "boundary", BoundaryFgsGenericInterface, "6,8,2.5,10", layer: layer, features: new[] { "interp-to-aot", "aot-to-interp", "full-generic-sharing", "interface", "struct" });
            RegisterCase(cases, "boundary_fgs_generic_container", "boundary", BoundaryFgsGenericContainer, "17,19,1.5,21", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "generic-class", "struct" });
            RegisterCase(cases, "boundary_fgs_generic_virtual", "boundary", BoundaryFgsGenericVirtual, "23,29,3.5,31", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "virtual", "struct" });
            RegisterCase(cases, "boundary_fgs_reflection_make_generic", "boundary", BoundaryFgsReflectionMakeGeneric, "37,41,6.5,43", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "reflection", "struct" });
            RegisterCase(cases, "boundary_fgs_nested_array", "boundary", BoundaryFgsNestedArray, "2:47", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "array", "struct" });
            RegisterCase(cases, "boundary_fgs_direct_interface_void", "boundary", BoundaryFgsDirectInterfaceVoid, "1", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "interface", "void-return" });
            RegisterCase(cases, "boundary_fgs_direct_interface_large_return", "boundary", BoundaryFgsDirectInterfaceLargeReturn, "51,53,7.5,55", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "interface", "struct-return" });
            RegisterCase(cases, "boundary_fgs_direct_interface_small_return", "boundary", BoundaryFgsDirectInterfaceSmallReturn, "231", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "interface", "small-return" });
            RegisterCase(cases, "boundary_fgs_direct_virtual_void", "boundary", BoundaryFgsDirectVirtualVoid, "1", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "virtual", "void-return" });
            RegisterCase(cases, "boundary_fgs_direct_virtual_large_return", "boundary", BoundaryFgsDirectVirtualLargeReturn, "61,67,8.5,71", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "virtual", "struct-return" });
            RegisterCase(cases, "boundary_fgs_direct_virtual_small_return", "boundary", BoundaryFgsDirectVirtualSmallReturn, "239", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "virtual", "small-return" });
            RegisterCase(cases, "boundary_fgs_delegate_static_void", "boundary", BoundaryFgsDelegateStaticVoid, "1", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "void-return" });
            RegisterCase(cases, "boundary_fgs_delegate_static_large_return", "boundary", BoundaryFgsDelegateStaticLargeReturn, "73,79,9.5,83", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "struct-return" });
            RegisterCase(cases, "boundary_fgs_delegate_static_small_return", "boundary", BoundaryFgsDelegateStaticSmallReturn, "241", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "small-return" });
            RegisterCase(cases, "boundary_fgs_delegate_closed_instance", "boundary", BoundaryFgsDelegateClosedInstance, "89,97,10.5,101", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "closed-instance" });
            RegisterCase(cases, "boundary_fgs_delegate_open_instance", "boundary", BoundaryFgsDelegateOpenInstance, "103,107,11.5,109", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "open-instance" });
            RegisterCase(cases, "boundary_fgs_delegate_multicast_void", "boundary", BoundaryFgsDelegateMulticastVoid, "2", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "delegate", "multicast", "void-return" });
            RegisterCase(cases, "boundary_fgs_calli_void", "boundary", BoundaryFgsCalliVoid, "1", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "calli", "void-return" });
            RegisterCase(cases, "boundary_fgs_calli_large_return", "boundary", BoundaryFgsCalliLargeReturn, "113,127,12.5,131", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "calli", "struct-return" });
            RegisterCase(cases, "boundary_fgs_calli_small_return", "boundary", BoundaryFgsCalliSmallReturn, "251", layer: layer, features: new[] { "interp-to-aot", "full-generic-sharing", "calli", "small-return" });
        }

        private static CaseObservation BoundaryInterpreterCallsAot()
        {
            return Observation(AotBoundaryHost.Add(12, 30));
        }

        private static CaseObservation BoundaryAotCallsInterface()
        {
            return Observation(AotBoundaryHost.CallInterface(new BoundaryService(), 13));
        }

        private static CaseObservation BoundaryAotCallsStructInterface()
        {
            BoundaryPayload input = new BoundaryPayload(2, 5, BoundaryKind.Alpha);
            BoundaryPayload output = AotBoundaryHost.CallInterfaceWithStruct(new BoundaryService(), input);
            return new CaseObservation(
                FormattableString.Invariant($"{output.Number},{output.Wide},{(long)output.Kind}"),
                output.Score.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation BoundaryAotCallsStringInterface()
        {
            return new CaseObservation(AotBoundaryHost.CallInterfaceWithString(new BoundaryService(), "prefix"));
        }

        private static CaseObservation BoundaryAotCallsVirtual()
        {
            return new CaseObservation(
                AotBoundaryHost.CallVirtual(new BoundaryDerived(), 10).ToString(CultureInfo.InvariantCulture),
                AotBoundaryHost.ReadVirtualProperty(new BoundaryDerived()));
        }

        private static CaseObservation BoundaryAotReadsVirtualProperty()
        {
            return new CaseObservation(AotBoundaryHost.ReadVirtualProperty(new BoundaryDerived()));
        }

        private static CaseObservation BoundaryAotCallsDelegate()
        {
            BoundaryCallback callback = (left, right) => left + right;
            return Observation(AotBoundaryHost.CallDelegate(callback, 12, 30));
        }

        private static CaseObservation BoundaryAotCallsFunc()
        {
            Func<int, int> callback = value => value * 2;
            return Observation(AotBoundaryHost.CallFunc(callback, 17));
        }

        private static CaseObservation BoundaryAotGenericStructEcho()
        {
            BoundaryPayload value = new BoundaryPayload(4, 8, BoundaryKind.Beta);
            BoundaryPayload result = AotBoundaryHost.Echo(value);
            return new CaseObservation(FormattableString.Invariant($"{result.Number},{result.Wide},{(long)result.Kind}"));
        }

        private static CaseObservation BoundaryAotConstrainedStruct()
        {
            return Observation(AotBoundaryHost.CallConstrained(new BoundaryValue(27)));
        }

        private static CaseObservation BoundaryAotSumArray()
        {
            return Observation(AotBoundaryHost.SumArray(new[] { 1, 2, 3, 4, 5 }));
        }

        private static CaseObservation BoundaryAotRefOutStruct()
        {
            BoundaryPayload value = new BoundaryPayload(1, 2, BoundaryKind.Alpha);
            AotBoundaryHost.Mutate(ref value, out int score);
            return new CaseObservation(FormattableString.Invariant($"{value.Number},{value.Wide},{(long)value.Kind}"), score.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation BoundaryAotCatchesInterpreterException()
        {
            return Observation(AotBoundaryHost.CatchInterpreterException(() => throw new InvalidOperationException("hotfix")));
        }

        private static CaseObservation BoundaryInterpreterCatchesAotException()
        {
            try
            {
                _ = AotBoundaryHost.ThrowAotException();
            }
            catch (Exception exception)
            {
                return new CaseObservation(exception.GetType().Name);
            }

            return new CaseObservation("none");
        }

        private static CaseObservation BoundaryAotBoxesStruct()
        {
            BoundaryPayload value = new BoundaryPayload(5, 12, BoundaryKind.Alpha);
            object boxed = AotBoundaryHost.BoxPayload(value);
            BoundaryPayload result = (BoundaryPayload)boxed;
            return new CaseObservation(FormattableString.Invariant($"{result.Number},{result.Wide},{(long)result.Kind}"));
        }

        private static CaseObservation BoundaryRepeatedAotCalls()
        {
            int total = 0;
            for (int i = 1; i <= 10; i++) total += AotBoundaryHost.Add(i, 0);
            return Observation(total);
        }

        private static CaseObservation BoundaryAotGenericStringEcho()
        {
            return new CaseObservation(AotBoundaryHost.Echo("boundary"));
        }

        private static CaseObservation BoundaryNullInterfaceException()
        {
            try
            {
                _ = AotBoundaryHost.CallInterface(null!, 1);
            }
            catch (Exception exception)
            {
                return new CaseObservation(exception.GetType().Name);
            }

            return new CaseObservation("none");
        }

        private static CaseObservation BoundaryObjectArrayRoundtrip()
        {
            object[] values = { 1, "hotfix", 3 };
            return new CaseObservation(FormattableString.Invariant($"{values[0]},{values[1]},{values[2]}"));
        }

        private static CaseObservation BoundaryEnumPayloadRoundtrip()
        {
            BoundaryPayload value = new BoundaryPayload(0, 0, BoundaryKind.Beta);
            BoundaryPayload result = AotBoundaryHost.Echo(value);
            return Observation((long)result.Kind);
        }

        private static CaseObservation BoundaryFgsHotEnum()
        {
            HotBoundaryEnum result = AotBoundaryHost.Echo(HotBoundaryEnum.Large);
            return new CaseObservation(result.ToString());
        }

        private static CaseObservation BoundaryFgsLargeStruct()
        {
            return ObserveLarge(AotBoundaryHost.Echo(new LargeBoundaryValue(3, 5, 7.5, 11)));
        }

        private static CaseObservation BoundaryFgsNullableStruct()
        {
            LargeBoundaryValue? value = AotBoundaryHost.Echo<LargeBoundaryValue?>(new LargeBoundaryValue(13, 17, 2.5, 19));
            return new CaseObservation(FormattableString.Invariant($"{value.GetValueOrDefault().First},{value.HasValue}"));
        }

        private static CaseObservation BoundaryFgsRefSwap()
        {
            LargeBoundaryValue left = new LargeBoundaryValue(2, 3, 4.5, 5);
            LargeBoundaryValue right = new LargeBoundaryValue(9, 10, 11.5, 12);
            AotBoundaryHost.Swap(ref left, ref right);
            return Observation(left.First, right.First);
        }

        private static CaseObservation BoundaryFgsGenericDelegate()
        {
            LargeBoundaryValue result = AotBoundaryHost.CallGenericDelegate(
                (LargeBoundaryValue value) => new LargeBoundaryValue(value.First + 2, value.Wide + 3, value.Floating + 0.5, (short)(value.Last + 4)),
                new LargeBoundaryValue(5, 9, 4.0, 11));
            return ObserveLarge(result);
        }

        private static CaseObservation BoundaryFgsAotCallsHotGenericDelegate()
        {
            LargeBoundaryValue result = AotBoundaryHost.CallGenericDelegate(
                HotGenericEcho<LargeBoundaryValue>,
                new LargeBoundaryValue(131, 137, 13.5, 139));
            return ObserveLarge(result);
        }

        private static T HotGenericEcho<T>(T value)
        {
            return value;
        }

        private static CaseObservation BoundaryFgsGenericInterface()
        {
            LargeBoundaryValue result = AotBoundaryHost.CallGenericInterface(
                new LargeBoundaryService(),
                new LargeBoundaryValue(5, 6, 1.5, 7));
            return ObserveLarge(result);
        }

        private static CaseObservation BoundaryFgsGenericContainer()
        {
            AotGenericBox<LargeBoundaryValue> box = new AotGenericBox<LargeBoundaryValue>(new LargeBoundaryValue(17, 19, 1.5, 21));
            return ObserveLarge(box.Value);
        }

        private static CaseObservation BoundaryFgsGenericVirtual()
        {
            LargeBoundaryValue result = AotBoundaryHost.CallGenericVirtual(
                new BoundaryDerived(),
                new LargeBoundaryValue(23, 29, 3.5, 31));
            return ObserveLarge(result);
        }

        private static CaseObservation BoundaryFgsReflectionMakeGeneric()
        {
            MethodInfo definition = typeof(AotBoundaryHost).GetMethod(nameof(AotBoundaryHost.Echo))!;
            MethodInfo method = definition.MakeGenericMethod(typeof(LargeBoundaryValue));
            object result = method.Invoke(null, new object[] { new LargeBoundaryValue(37, 41, 6.5, 43) })!;
            return ObserveLarge((LargeBoundaryValue)result);
        }

        private static CaseObservation BoundaryFgsNestedArray()
        {
            LargeBoundaryValue[] values =
            {
                new LargeBoundaryValue(43, 44, 4.5, 45),
                new LargeBoundaryValue(47, 48, 5.5, 49),
            };
            LargeBoundaryValue[] result = AotBoundaryHost.Echo(values);
            return new CaseObservation(FormattableString.Invariant($"{result.Length}:{result[1].First}"));
        }

        private static CaseObservation BoundaryFgsDirectInterfaceVoid()
        {
            AotBoundaryHost.ResetGenericSinkCount();
            IGenericSink<LargeBoundaryValue> sink = new AotGenericBoundary<LargeBoundaryValue>();
            sink.Store(new LargeBoundaryValue(47, 48, 4.5, 49));
            return Observation(AotBoundaryHost.GetGenericSinkCount());
        }

        private static CaseObservation BoundaryFgsDirectInterfaceLargeReturn()
        {
            IGenericBoundary<LargeBoundaryValue> service = new AotGenericBoundary<LargeBoundaryValue>();
            return ObserveLarge(service.Convert(new LargeBoundaryValue(51, 53, 7.5, 55)));
        }

        private static CaseObservation BoundaryFgsDirectInterfaceSmallReturn()
        {
            IGenericBoundary<byte> service = new AotGenericBoundary<byte>();
            return Observation(service.Convert(231));
        }

        private static CaseObservation BoundaryFgsDirectVirtualVoid()
        {
            AotBoundaryHost.ResetGenericSinkCount();
            AotGenericVirtualBase<LargeBoundaryValue> service = new AotGenericVirtualDerived<LargeBoundaryValue>();
            service.Store(new LargeBoundaryValue(57, 59, 6.5, 61));
            return Observation(AotBoundaryHost.GetGenericSinkCount());
        }

        private static CaseObservation BoundaryFgsDirectVirtualLargeReturn()
        {
            AotGenericVirtualBase<LargeBoundaryValue> service = new AotGenericVirtualDerived<LargeBoundaryValue>();
            return ObserveLarge(service.Transform(new LargeBoundaryValue(61, 67, 8.5, 71)));
        }

        private static CaseObservation BoundaryFgsDirectVirtualSmallReturn()
        {
            AotGenericVirtualBase<byte> service = new AotGenericVirtualDerived<byte>();
            return Observation(service.Transform(239));
        }

        private static CaseObservation BoundaryFgsDelegateStaticVoid()
        {
            AotBoundaryHost.ResetGenericSinkCount();
            Action<LargeBoundaryValue> sink = AotBoundaryHost.Sink<LargeBoundaryValue>;
            sink(new LargeBoundaryValue(67, 71, 8.5, 73));
            return Observation(AotBoundaryHost.GetGenericSinkCount());
        }

        private static CaseObservation BoundaryFgsDelegateStaticLargeReturn()
        {
            Func<LargeBoundaryValue, LargeBoundaryValue> echo = AotBoundaryHost.Echo<LargeBoundaryValue>;
            return ObserveLarge(echo(new LargeBoundaryValue(73, 79, 9.5, 83)));
        }

        private static CaseObservation BoundaryFgsDelegateStaticSmallReturn()
        {
            Func<byte, byte> echo = AotBoundaryHost.Echo<byte>;
            return Observation(echo(241));
        }

        private static CaseObservation BoundaryFgsDelegateClosedInstance()
        {
            AotGenericBoundary<LargeBoundaryValue> service = new AotGenericBoundary<LargeBoundaryValue>();
            Func<LargeBoundaryValue, LargeBoundaryValue> echo = service.Convert;
            return ObserveLarge(echo(new LargeBoundaryValue(89, 97, 10.5, 101)));
        }

        private static CaseObservation BoundaryFgsDelegateOpenInstance()
        {
            MethodInfo method = typeof(AotGenericBoundary<LargeBoundaryValue>).GetMethod(nameof(AotGenericBoundary<LargeBoundaryValue>.Convert))!;
            var echo = (Func<AotGenericBoundary<LargeBoundaryValue>, LargeBoundaryValue, LargeBoundaryValue>)Delegate.CreateDelegate(
                typeof(Func<AotGenericBoundary<LargeBoundaryValue>, LargeBoundaryValue, LargeBoundaryValue>),
                null,
                method);
            return ObserveLarge(echo(
                new AotGenericBoundary<LargeBoundaryValue>(),
                new LargeBoundaryValue(103, 107, 11.5, 109)));
        }

        private static CaseObservation BoundaryFgsDelegateMulticastVoid()
        {
            AotBoundaryHost.ResetGenericSinkCount();
            Action<LargeBoundaryValue> sink = AotBoundaryHost.Sink<LargeBoundaryValue>;
            sink += AotBoundaryHost.Sink<LargeBoundaryValue>;
            sink(new LargeBoundaryValue(109, 113, 12.5, 127));
            return Observation(AotBoundaryHost.GetGenericSinkCount());
        }

        private static unsafe CaseObservation BoundaryFgsCalliVoid()
        {
            AotBoundaryHost.ResetGenericSinkCount();
            delegate* managed<LargeBoundaryValue, void> sink = &AotBoundaryHost.Sink<LargeBoundaryValue>;
            sink(new LargeBoundaryValue(109, 113, 12.5, 127));
            return Observation(AotBoundaryHost.GetGenericSinkCount());
        }

        private static unsafe CaseObservation BoundaryFgsCalliLargeReturn()
        {
            delegate* managed<LargeBoundaryValue, LargeBoundaryValue> echo = &AotBoundaryHost.Echo<LargeBoundaryValue>;
            return ObserveLarge(echo(new LargeBoundaryValue(113, 127, 12.5, 131)));
        }

        private static unsafe CaseObservation BoundaryFgsCalliSmallReturn()
        {
            delegate* managed<byte, byte> echo = &AotBoundaryHost.Echo<byte>;
            return Observation(echo(251));
        }

        private static CaseObservation ObserveLarge(LargeBoundaryValue value)
        {
            return new CaseObservation(FormattableString.Invariant($"{value.First},{value.Wide},{value.Floating},{value.Last}"));
        }

        private sealed class BoundaryService : IBoundaryService
        {
            public int Calculate(int value)
            {
                return value * 3;
            }

            public BoundaryPayload Transform(BoundaryPayload value)
            {
                return new BoundaryPayload(value.Number + 1, value.Wide + 10, BoundaryKind.Beta);
            }

            public string Describe(string prefix)
            {
                return prefix + ":hotfix-service";
            }
        }

        private sealed class BoundaryDerived : BoundaryBase
        {
            public BoundaryDerived() : base(5)
            {
            }

            public override string Name => nameof(BoundaryDerived);

            public override int Process(int value)
            {
                return base.Process(value) * 2;
            }
        }

        private readonly struct BoundaryValue : IBoundaryValue
        {
            private readonly int _value;

            public BoundaryValue(int value)
            {
                _value = value;
            }

            public int GetValue()
            {
                return _value;
            }
        }

        private sealed class LargeBoundaryService : IGenericBoundary<LargeBoundaryValue>
        {
            public LargeBoundaryValue Convert(LargeBoundaryValue value)
            {
                return new LargeBoundaryValue(value.First + 1, value.Wide + 2, value.Floating + 1.0, (short)(value.Last + 3));
            }
        }

        private readonly struct LargeBoundaryValue
        {
            public LargeBoundaryValue(int first, long wide, double floating, short last)
            {
                First = first;
                Wide = wide;
                Floating = floating;
                Last = last;
            }

            public int First { get; }
            public long Wide { get; }
            public double Floating { get; }
            public short Last { get; }
        }

        private enum HotBoundaryEnum : ushort
        {
            Large = 60000,
        }
    }
}
