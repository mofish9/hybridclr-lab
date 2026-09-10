extern alias model;
using System;
using System.Threading;

namespace HybridCLR.Lab.ValueLayoutNative
{
    using Payload = model::HybridCLR.Lab.ValueLayout.Payload;

    public static class NativeStaticOwner
    {
        public static Payload Value;
        public static readonly Payload ReadonlyValue;
        public static int Neighbor;
        public static int Runs;
        static NativeStaticOwner()
        { Runs++; Value.Count = 23; ReadonlyValue = new Payload { Count = 29 }; Neighbor = 101; }
        public static Payload Get() { return Value; }
        public static void Set(Payload value) { Value = value; }
        public static ref Payload Address() { return ref Value; }
        public static Payload ReadonlyCopy() { return ReadonlyValue; }
        public static int ReadNeighbor() { return Neighbor; }
        public static int ReadRuns() { return Runs; }
        public static void Clear() { Value = default(Payload); }
    }

    public static class NativeStaticGeneric<T>
    {
        public static Payload Value;
        public static int Runs;
        static NativeStaticGeneric() { Runs++; Value.Count = 37; }
        public static void Set(Payload value) { Value = value; }
        public static Payload Get() { return Value; }
    }

    public static class NativeStaticArgument<T>
    {
        public static T Value;
        public static int Runs;
        static NativeStaticArgument() { Runs++; }
        public static void Set(T value) { Value = value; }
        public static T Get() { return Value; }
    }

    public sealed class NativeMixedStaticOwner
    {
        public Payload Instance;
        public static Payload Shared;
        public static int Neighbor;
        public static int Runs;
        static NativeMixedStaticOwner() { Runs++; Shared.Count = 41; Neighbor = 107; }
        public NativeMixedStaticOwner(Payload value) { Instance = value; }
        public static object Make(object value) { return new NativeMixedStaticOwner((Payload)value); }
        public object Read() { return Instance; }
        public static void Set(object value) { Shared = (Payload)value; }
        public static object Get() { return Shared; }
        public static int ReadNeighbor() { return Neighbor; }
        public static int ReadRuns() { return Runs; }
    }

    public static class NativeStaticCounters
    {
        public static int ConcurrentRuns;
        public static int FailureRuns;
    }
    public static class NativeConcurrentStaticOwner
    {
        public static Payload Value;
        static NativeConcurrentStaticOwner()
        { Interlocked.Increment(ref NativeStaticCounters.ConcurrentRuns); Thread.Sleep(1); Value.Count = 211; }
        public static Payload Get() { return Value; }
    }
    public static class NativeFailingStaticOwner
    {
        public static Payload Value;
        static NativeFailingStaticOwner()
        { NativeStaticCounters.FailureRuns++; throw new InvalidOperationException("frozen-static-expected"); }
        public static Payload Get() { return Value; }
    }
}
