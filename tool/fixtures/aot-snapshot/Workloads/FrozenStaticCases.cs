extern alias model;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using HybridCLR.Lab.ValueLayoutNative;

namespace HybridCLR.Lab.ResourceCases
{
    using Payload = model::HybridCLR.Lab.ValueLayout.Payload;
    public static class FrozenStaticCases
    {
        private const long Extra = 50000000003L;
        private static Payload Make(int count, object marker) { return new Payload { Count = count, Extra = Extra + count, Reference = marker }; }
        private static void Require(bool value, string name) { if (!value) throw new InvalidOperationException("Frozen static case failed: " + name); }
        private static void Value(Payload value, int count, object marker, string name)
        { Require(value.Count == count && value.Extra == Extra + count && ReferenceEquals(value.Reference, marker), name); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference InstallReference()
        { object marker = new object(); NativeStaticOwner.Set(Make(59, marker)); return new WeakReference(marker); }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void VerifyNativeDispatch()
        {
            HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
            int neighbor = NativeStaticOwner.ReadNeighbor(); int runs = NativeStaticOwner.ReadRuns();
            int native = HybridCLR.RuntimeApi.GetDifferentialAotEntryCount();
            int interpreted = HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
            Require(neighbor == 101 && runs == 1 && native >= 2 && interpreted == 0, "unchanged-readers-stay-aot");
        }
        public static string[] Run()
        {
            var records = new List<string>(); object marker = new object(); Payload value = Make(53, marker);
            Action<string, Action> test = (name, action) => {
                Console.WriteLine("DHE case begin: " + name); action(); records.Add(name); Console.WriteLine("DHE case pass: " + name);
            };
            test("ordinary-static-initializer", () => {
                var first = NativeStaticOwner.Get(); Require(first.Count == 23 && first.Extra == 0 && first.Reference == null &&
                    NativeStaticOwner.ReadRuns() == 1 && NativeStaticOwner.ReadNeighbor() == 101, "initial-values");
            });
            test("ordinary-static-readonly-initializer", () => {
                var first = NativeStaticOwner.ReadonlyCopy(); Require(first.Count == 29 && first.Extra == 0 && first.Reference == null, "readonly-value");
            });
            test("ordinary-static-value-copy", () => {
                NativeStaticOwner.Set(value); var copy = NativeStaticOwner.Get(); Value(copy, 53, marker, "value-copy");
                copy.Count = 999; Value(NativeStaticOwner.Get(), 53, marker, "independent-copy");
            });
            test("ordinary-static-byref-address", () => {
                ref Payload address = ref NativeStaticOwner.Address(); address = Make(57, marker);
                Value(NativeStaticOwner.Get(), 57, marker, "byref-whole-value"); address.Count++; address.Extra++;
                Value(NativeStaticOwner.Value, 58, marker, "byref-fields");
            });
            test("ordinary-static-unchanged-neighbor", () => Require(NativeStaticOwner.ReadNeighbor() == 101 && NativeStaticOwner.ReadRuns() == 1, "neighbor-and-count"));
            test("ordinary-static-reflection", () => {
                var field = typeof(NativeStaticOwner).GetField("Value");
                Require(field != null && field.DeclaringType == typeof(NativeStaticOwner) && field.FieldType == typeof(Payload), "logical-field");
                Value((Payload)field.GetValue(null), 58, marker, "reflection-read"); field.SetValue(null, value);
                Value(NativeStaticOwner.Get(), 53, marker, "reflection-write");
            });
            test("ordinary-static-gc-reference", () => {
                var weak = InstallReference(); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                Require(weak.IsAlive, "weak-reference-alive"); Value(NativeStaticOwner.Get(), 59, weak.Target, "retained-reference");
            });
            test("ordinary-static-whole-value-clear", () => {
                NativeStaticOwner.Clear(); var empty = NativeStaticOwner.Get();
                Require(empty.Count == 0 && empty.Extra == 0 && empty.Reference == null && NativeStaticOwner.ReadNeighbor() == 101, "cleared-value");
            });
            test("ordinary-static-generic-fixed-value", () => {
                Require(NativeStaticGeneric<int>.Get().Count == 37, "generic-initial-value");
                NativeStaticGeneric<int>.Set(value); NativeStaticGeneric<string>.Set(Make(61, marker));
                Value(NativeStaticGeneric<int>.Get(), 53, marker, "generic-int"); Value(NativeStaticGeneric<string>.Get(), 61, marker, "generic-string");
                Require(NativeStaticGeneric<int>.Runs == 1 && NativeStaticGeneric<string>.Runs == 1, "generic-cctors");
            });
            test("ordinary-static-generic-argument", () => {
                NativeStaticArgument<Payload>.Set(value); NativeStaticArgument<long>.Set(Extra);
                Value(NativeStaticArgument<Payload>.Get(), 53, marker, "generic-payload");
                Require(NativeStaticArgument<long>.Get() == Extra && NativeStaticArgument<long>.Runs == 1 && NativeStaticArgument<Payload>.Runs == 1, "generic-argument-isolation");
            });
            test("ordinary-static-mixed-instance-and-static", () => {
                var owner = (NativeMixedStaticOwner)NativeMixedStaticOwner.Make(value);
                Value((Payload)owner.Read(), 53, marker, "mixed-instance"); NativeMixedStaticOwner.Set(Make(67, marker));
                Value((Payload)NativeMixedStaticOwner.Get(), 67, marker, "mixed-static");
                Require(NativeMixedStaticOwner.ReadNeighbor() == 107 && NativeMixedStaticOwner.ReadRuns() == 1, "mixed-initializer");
            });
            test("ordinary-static-mixed-reflection", () => {
                var field = typeof(NativeMixedStaticOwner).GetField("Shared"); field.SetValue(null, value);
                Value((Payload)field.GetValue(null), 53, marker, "mixed-reflection");
                Value((Payload)NativeMixedStaticOwner.Get(), 53, marker, "mixed-reflection-native-reader");
            });
            test("ordinary-static-concurrent-cctor", () => {
                var threads = new Thread[4]; var errors = new Exception[4]; var ready = new ManualResetEvent(false);
                for (int i = 0; i < threads.Length; ++i) { int slot = i; threads[i] = new Thread(() => {
                    try { ready.WaitOne(); var first = NativeConcurrentStaticOwner.Get();
                        Require(first.Count == 211 && first.Extra == 0 && first.Reference == null, "concurrent-value"); }
                    catch (Exception error) { errors[slot] = error; }
                }); threads[i].Start(); }
                ready.Set(); foreach (var thread in threads) Require(thread.Join(10000), "concurrent-join"); ready.Dispose();
                foreach (var error in errors) if (error != null) throw error;
                Require(NativeStaticCounters.ConcurrentRuns == 1, "one-concurrent-cctor");
            });
            test("ordinary-static-cached-initializer-failure", () => {
                for (int i = 0; i < 2; ++i) { bool failed = false;
                    try { NativeFailingStaticOwner.Get(); }
                    catch (TypeInitializationException error) { failed = error.InnerException is InvalidOperationException && error.InnerException.Message == "frozen-static-expected"; }
                    Require(failed, "initializer-exception"); }
                Require(NativeStaticCounters.FailureRuns == 1, "cached-initializer-failure");
            });
            test("ordinary-static-unchanged-native-dispatch", () => {
                // The reference host cannot call IL2CPP internal calls. The
                // Player additionally proves that unaffected neighbor readers
                // still enter AOT after frozen-source storage adaptation.
                if (typeof(object).Assembly.GetName().Name == "mscorlib")
                    VerifyNativeDispatch();
            });
            return records.ToArray();
        }
    }
}
