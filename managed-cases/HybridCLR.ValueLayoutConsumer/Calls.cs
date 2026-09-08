extern alias OtherValues;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HybridCLR.Lab.ValueLayout;
using OtherPayload = OtherValues::HybridCLR.Lab.ValueLayout.Payload;

namespace HybridCLR.Lab.ValueLayoutConsumer
{
    public struct LocalWrapper { public Nested Value; public object Marker; }
    public static class Calls
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Payload DirectCopy(Payload value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Nested NestedCopy(Nested value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static LocalWrapper LocalNestedCopy(LocalWrapper value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static GenericValue<Payload> GenericCopy(GenericValue<Payload> value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Payload? NullableCopy(Payload? value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object ForwardBox() => Factory.Box(Factory.Create());
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static object GenericForwardBox() => Factory.Box(Factory.Identity(Factory.Create()));
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Payload ArrayElement(Payload[] values, int index) => values[index];
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static void RefRoundTrip(ref Payload value) => value = DirectCopy(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ContainerNeighbor(InlineChild owner) => owner.Neighbor;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int GenericContainerNeighbor(GenericOwner<Payload> owner) => owner.Neighbor;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static Payload NativeRoundTrip(Payload value) => ValueLayoutNative.NativeBoundary.Echo(value);
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Unrelated(int value) => value + 1;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static UnchangedValue UnchangedCopy(UnchangedValue value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static OtherPayload OtherAssemblyCopy(OtherPayload value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static GenericValue<int> UnchangedGenericCopy(GenericValue<int> value) => value;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ExternalGenericInt(List<int> values) => values[0];
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ReferenceOnly(ReferenceOwner owner) => owner.Neighbor;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static T OpenGenericCopy<T>(T value) => value;

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static bool FullCopyMatches(Payload before, Payload after)
        {
            // The frozen Base consumer cannot name fields introduced later.
            // Compare every Current public field so an old-ABI projection that
            // silently drops added fields cannot pass on the original Count.
            foreach (var field in typeof(Payload).GetFields())
                if (!object.Equals(field.GetValue(before), field.GetValue(after))) return false;
            return true;
        }

        public static bool Run()
        {
            Payload value = Factory.Create();
            var nested = new Nested { Value = value, Tail = 19 };
            var local = new LocalWrapper { Value = nested, Marker = new object() };
            var generic = new GenericValue<Payload> { Value = value, Marker = 23 };
            if (!FullCopyMatches(value, DirectCopy(value)) || !FullCopyMatches(value, NativeRoundTrip(value)) ||
                !FullCopyMatches(value, (Payload)ForwardBox()) || !FullCopyMatches(value, (Payload)GenericForwardBox()) ||
                !FullCopyMatches(value, GenericCopy(generic).Value) || !FullCopyMatches(value, NullableCopy(value).Value) ||
                !FullCopyMatches(value, ArrayElement(new[] { value }, 0))) return false;
            Payload copy = DirectCopy(value); copy.Count = 29;
            Payload alias = value; RefRoundTrip(ref alias);
            return value.Count == 17 && copy.Count == 29 && NestedCopy(nested).Tail == 19 &&
                LocalNestedCopy(local).Value.Value.Count == 17 && GenericCopy(generic).Marker == 23 &&
                NullableCopy(value).Value.Count == 17 && ((Payload)ForwardBox()).Count == 17 &&
                ((Payload)GenericForwardBox()).Count == 17 && ArrayElement(new[] { value }, 0).Count == 17 &&
                alias.Count == 17 && ContainerNeighbor(new InlineChild { Neighbor = 31 }) == 31 &&
                GenericContainerNeighbor(new GenericOwner<Payload> { Neighbor = 37 }) == 37 &&
                Unrelated(41) == 42 && UnchangedCopy(new UnchangedValue { Value = 43 }).Value == 43 &&
                OtherAssemblyCopy(new OtherPayload { Count = 47 }).Count == 47 &&
                UnchangedGenericCopy(new GenericValue<int> { Value = 53 }).Value == 53 &&
                NativeRoundTrip(value).Count == 17 && ExternalGenericInt(new List<int> { 59 }) == 59 &&
                ReferenceOnly(new ReferenceOwner { Neighbor = 61 }) == 61 &&
                OpenGenericCopy(value).Count == 17;
        }
    }
}
