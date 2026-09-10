extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HybridCLR.Lab.ValueLayoutNative;

namespace HybridCLR.Lab.ResourceCases
{
    using Payload = model::HybridCLR.Lab.ValueLayout.Payload;
    // Compiled against the actual captured Unity inputs and merged into Current.
    // Every case runs from the business entry, with the Base binaries unchanged.
    public static class FrozenResourceCases
    {
        private const long Extra = 90000000001L;
        private static Payload Make(int count, object marker)
        { return new Payload { Count = count, Extra = Extra + count, Reference = marker }; }
        private static void Require(bool passed, string name)
        { if (!passed) throw new InvalidOperationException("Frozen resource case failed: " + name); }
        private static void Value(Payload value, int count, object marker, string name)
        { Require(value.Count == count && value.Extra == Extra + count && ReferenceEquals(value.Reference, marker), name); }
        private static T Identity<T>(T value) { return value; }
        private static void Increment(ref Payload value) { value.Count++; value.Extra++; }
        private static Payload ExceptionCopy(Payload value)
        {
            try { throw new InvalidOperationException("expected"); }
            catch (InvalidOperationException error) when (error.Message == "expected") { value.Count++; }
            finally { value.Extra++; }
            return value;
        }

        public static string[] Run()
        {
            var records = new List<string>(); object marker = new object(); Payload value = Make(17, marker);
            Action<string, Action> test = (name, action) => {
                Console.WriteLine("DHE case begin: " + name); action(); records.Add(name);
            };
            test("ordinary-echo", () => Value(NativeBoundary.Echo(value), 17, marker, "ordinary-echo"));
            test("ordinary-box-copy", () => Value((Payload)NativeBoundary.FrozenCopyBox(value), 17, marker, "ordinary-box-copy"));
            test("ordinary-inline-owner", () => Value((Payload)NativeBoundary.FrozenInlineBox(value), 17, marker, "ordinary-inline-owner"));
            test("ordinary-unchanged-sentinel", () => Require(NativeBoundary.FrozenSentinel() == 137, "ordinary-sentinel"));
            test("nullable-value-and-box", () => {
                Payload? nullable = value; Require(nullable.HasValue, "nullable-has-value");
                Value(nullable.Value, 17, marker, "nullable-value");
                Value((Payload)(object)nullable, 17, marker, "nullable-box");
            });
            test("nullable-empty", () => {
                Payload? nullable = null; Require(!nullable.HasValue && (object)nullable == null, "nullable-empty");
                var empty = nullable.GetValueOrDefault(); Require(empty.Count == 0 && empty.Extra == 0 && empty.Reference == null, "nullable-default");
            });
            test("open-generic-value-copy", () => Value(Identity(value), 17, marker, "open-generic"));
            test("generic-value-owner", () => {
                var envelope = new Envelope<Payload> { Value = value, Marker = 151 };
                var copy = Identity(envelope); Value(copy.Value, 17, marker, "generic-envelope"); Require(copy.Marker == 151, "generic-marker");
            });
            test("generic-reference-owner", () => {
                var owner = new Owner<Payload> { Value = value }; Value(owner.Read(), 17, marker, "generic-owner");
                var unchanged = new Owner<long> { Value = Extra }; Require(unchanged.Read() == Extra, "generic-long-owner");
            });
            test("arrays-copy-resize-and-byref", () => {
                var array = new Payload[2]; array[0] = value; Array.Resize(ref array, 8);
                Array.Copy(array, 0, array, 4, 1); Increment(ref array[4]); Value(array[4], 18, marker, "array-byref");
                Value(array[0], 17, marker, "array-independent-copy"); Require(array[7].Reference == null && array[7].Extra == 0, "array-default");
            });
            test("list-growth-shift-copy-and-enumeration", () => {
                var list = new List<Payload>(); for (int i = 0; i < 64; i++) list.Add(Make(i, marker));
                list.Insert(0, value); list.RemoveAt(1); var array = list.ToArray();
                Value(array[0], 17, marker, "list-insert"); Value(array[63], 63, marker, "list-growth");
                int sum = 0; foreach (var item in list) { Value(item, item.Count, marker, "list-enumerator"); sum += item.Count; }
                Require(sum == 2033, "list-sum");
            });
            test("dictionary-value-storage-and-enumeration", () => {
                var map = new Dictionary<int, Payload>(); for (int i = 0; i < 64; i++) map.Add(i, Make(i, marker));
                Payload found; Require(map.TryGetValue(63, out found), "dictionary-found"); Value(found, 63, marker, "dictionary-out-value");
                Require(map.Remove(7) && !map.ContainsKey(7), "dictionary-remove"); int sum = 0;
                foreach (var pair in map) { Value(pair.Value, pair.Key, marker, "dictionary-enumerator"); sum += pair.Value.Count; }
                Require(sum == 2009, "dictionary-sum");
            });
            test("boxed-field-reflection", () => {
                object boxed = value; var extra = typeof(Payload).GetField("Extra"); Require(extra != null, "field-discovery");
                extra.SetValue(boxed, Extra + 18); typeof(Payload).GetField("Count").SetValue(boxed, 18);
                Value((Payload)boxed, 18, marker, "reflection-field-write"); Value(value, 17, marker, "reflection-box-independent");
            });
            test("generic-reflection-invoke", () => {
                var method = typeof(FrozenResourceCases).GetMethod("Identity", BindingFlags.Static | BindingFlags.NonPublic).MakeGenericMethod(typeof(Payload));
                Value((Payload)method.Invoke(null, new object[] { value }), 17, marker, "generic-reflection");
            });
            test("generic-delegate-invoke", () => {
                Func<Payload, Payload> call = Identity<Payload>; Value(call(value), 17, marker, "generic-delegate");
            });
            test("exception-filter-and-finally", () => Value(ExceptionCopy(value), 18, marker, "exception-copy"));
            test("current-type-static-initialization", () => {
                Require(Initialized.Runs == 1, "cctor-once");
                Value(Initialized.Value, 19, Initialized.Marker, "static-value");
                Value((Payload)typeof(Initialized).GetField("Value").GetValue(null), 19, Initialized.Marker, "static-reflection");
                Require(Initialized.Runs == 1, "reflection-does-not-repeat-cctor");
            });
            test("closed-generic-static-storage", () => {
                Cache<Payload>.Value = value; Increment(ref Cache<Payload>.Value); Value(Cache<Payload>.Value, 18, marker, "generic-static-byref");
                Cache<long>.Value = Extra; Require(Cache<long>.Value == Extra && Cache<Payload>.Runs == 1 && Cache<long>.Runs == 1, "generic-static-isolation");
            });
            test("cyclic-type-initialization", () => Require(CycleA.Value == 6 && CycleB.Value == 5, "cctor-cycle"));
            test("current-constructor-reflection-default", () => {
                var ctor = typeof(Constructed).GetConstructors()[0]; Require((int)ctor.GetParameters()[1].DefaultValue == 17, "constructor-default");
                var created = (Constructed)ctor.Invoke(new object[] { value, Type.Missing }); Value(created.Value, 17, marker, "constructor-value");
            });
            test("generic-interface-and-virtual-dispatch", () => {
                IReader<Payload> reader = new DerivedOwner(value); Value(reader.Read(), 17, marker, "interface-dispatch");
                Owner<Payload> parent = (DerivedOwner)reader; Value(parent.Read(), 17, marker, "virtual-dispatch");
            });
            test("thread-static-current-value-isolation", () => {
                ThreadValue.Value = value; bool ok = false;
                var thread = new Thread(() => { try { Require(ThreadValue.Value.Extra == 0 && ThreadValue.Value.Reference == null, "thread-static-default");
                    ThreadValue.Value = Make(21, marker); Value(ThreadValue.Value, 21, marker, "thread-static-worker"); ok = true; } catch { ok = false; } });
                thread.IsBackground = true; thread.Start(); Require(thread.Join(5000) && ok, "thread-static-worker-completion");
                Value(ThreadValue.Value, 17, marker, "thread-static-main");
            });
            test("concurrent-first-static-touch", () => {
                var gate = new ManualResetEvent(false); var threads = new Thread[4]; var success = new bool[4];
                for (int i = 0; i < threads.Length; i++) { int slot = i; threads[i] = new Thread(() => {
                    try { gate.WaitOne(); for (int j = 0; j < 8; j++) Value(ConcurrentCold.Value, 23, ConcurrentCold.Marker, "concurrent-published-value");
                        success[slot] = true; } catch { success[slot] = false; } }); threads[i].IsBackground = true; threads[i].Start(); }
                gate.Set(); for (int i = 0; i < threads.Length; i++) Require(threads[i].Join(5000) && success[i], "concurrent-worker-" + i);
                gate.Close(); Require(ConcurrentCounter.Runs == 1, "concurrent-cctor-once");
            });
            return records.ToArray();
        }

        public struct Envelope<T> { public T Value; public short Marker; }
        public interface IReader<T> { T Read(); }
        public class Owner<T> { public T Value; public virtual T Read() { return Value; } }
        public sealed class DerivedOwner : Owner<Payload>, IReader<Payload>
        { public DerivedOwner(Payload value) { Value = value; } public override Payload Read() { return Value; } }
        public sealed class Constructed
        { public Payload Value; public Constructed(Payload value, int count = 17) { Require(value.Count == count, "ctor-count"); Value = value; } }
        public static class Initialized
        {
            public static int Runs; public static object Marker; public static Payload Value;
            static Initialized() { Runs++; Marker = new object(); Value = Make(19, Marker); }
        }
        public static class Cache<T>
        { public static T Value; public static int Runs; static Cache() { Runs++; } }
        public static class CycleA
        { public static int Seed, Value; static CycleA() { Seed = 3; Value = CycleB.Value + 1; } }
        public static class CycleB
        { public static int Value; static CycleB() { Value = CycleA.Seed + 2; } }
        public static class ThreadValue { [ThreadStatic] public static Payload Value; }
        public static class ConcurrentCounter { public static int Runs; }
        public static class ConcurrentCold
        {
            public static object Marker; public static Payload Value;
            static ConcurrentCold() { Interlocked.Increment(ref ConcurrentCounter.Runs); Thread.Sleep(10); Marker = new object(); Value = Make(23, Marker); }
        }
    }
}
