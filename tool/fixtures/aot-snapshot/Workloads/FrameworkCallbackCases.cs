extern alias model;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using ReferenceValue = model::HybridCLR.Lab.VirtualSignatures.ReferenceValue;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;

namespace HybridCLR.Lab.FrameworkCallbacks
{
    public static class Cases
    {
        private static readonly object Marker = new object();
        private const long PacketExtra = 90000000000L;
        private const long ReferenceExtra = 4000000000L;

        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }

        private static ReferenceValue Reference(int count) => new ReferenceValue { Value = count, Extra = ReferenceExtra + count };
        private static Packet Value(int count) => new Packet { Count = count, Extra = PacketExtra + count,
            Marker = Marker, Anchor = Reference(count) };
        private static Packet[] Values() => new[] { Value(3), Value(1), Value(2) };
        private static void Validate(ReferenceValue value)
        {
            if (value == null || value.Extra != ReferenceExtra + value.Value) throw new InvalidOperationException("reference payload fields");
        }
        private static void Validate(Packet value)
        {
            Validate(value.Anchor);
            if (!ReferenceEquals(value.Marker, Marker) || value.Extra != PacketExtra + value.Count || value.Anchor.Value != value.Count)
                throw new InvalidOperationException("value payload fields");
        }
        private static int Compare(Packet a, Packet b) { Validate(a); Validate(b); return a.Count.CompareTo(b.Count); }
        private static int Compare(ReferenceValue a, ReferenceValue b) { Validate(a); Validate(b); return a.Value.CompareTo(b.Value); }
        private static void Sorted(IList<Packet> values)
        {
            if (values.Count != 3) throw new InvalidOperationException("sorted count");
            for (int i = 0; i < values.Count; ++i) { Validate(values[i]); if (values[i].Count != i + 1) throw new InvalidOperationException("sorted order"); }
        }
        private sealed class PacketComparer : IComparer<Packet>, IEqualityComparer<Packet>
        {
            public int Compare(Packet a, Packet b) => Cases.Compare(a, b);
            public bool Equals(Packet a, Packet b) { Validate(a); Validate(b); return a.Count == b.Count; }
            public int GetHashCode(Packet value) { Validate(value); return value.Count; }
        }
        private sealed class ReferenceComparer : IEqualityComparer<ReferenceValue>
        {
            public bool Equals(ReferenceValue a, ReferenceValue b) { Validate(a); Validate(b); return a.Value == b.Value; }
            public int GetHashCode(ReferenceValue value) { Validate(value); return value.Value; }
        }
        private struct StructComparer
        {
            public int Salt;
            public int Compare(Packet a, Packet b)
            {
                if (Salt != 7) throw new InvalidOperationException("delegate lost boxed receiver snapshot");
                return Cases.Compare(a, b);
            }
        }
        private sealed class CallbackException : Exception { }
        private static bool CallbackFailure(Action invoke)
        {
            try { invoke(); }
            catch (Exception error)
            {
                while (error is InvalidOperationException && error.InnerException != null) error = error.InnerException;
                return error is CallbackException;
            }
            return false;
        }

        public static string[] Run()
        {
            var passed = new List<string>(); int failures = 0;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    passed.Add(name); Console.WriteLine("DHE framework callback check: " + name);
                }
                catch (Exception error) { ++failures; Console.WriteLine("DHE framework callback failure: " + name + ": " + error); }
            }
            Check("array-sort-value-comparison", () => { var values = Values(); Array.Sort(values, Compare); Sorted(values); return true; });
            Check("list-sort-value-comparison", () => { var values = new List<Packet>(Values()); values.Sort(Compare); Sorted(values); return true; });
            Check("list-sort-value-comparer", () => { var values = new List<Packet>(Values()); values.Sort(new PacketComparer()); Sorted(values); return true; });
            Check("array-sort-reference-comparison", () => {
                var values = new[] { Reference(3), Reference(1), Reference(2) }; var first = values[1];
                Array.Sort(values, Compare); for (int i = 0; i < values.Length; ++i) { Validate(values[i]); if (values[i].Value != i + 1) return false; }
                return ReferenceEquals(first, values[0]);
            });
            Check("list-find-value-predicate", () => {
                var values = new List<Packet>(Values()); var found = values.Find(value => { Validate(value); return value.Count == 2; });
                Validate(found); return found.Count == 2 && ReferenceEquals(found.Anchor, values[2].Anchor);
            });
            Check("list-convert-value-reference", () => {
                var values = new List<Packet>(Values()); var result = values.ConvertAll(value => { Validate(value); return value.Anchor; });
                for (int i = 0; i < values.Count; ++i) if (!ReferenceEquals(result[i], values[i].Anchor)) return false; return true;
            });
            Check("array-convert-value-reference", () => {
                var values = Values(); var result = Array.ConvertAll(values, value => { Validate(value); return value.Anchor; });
                for (int i = 0; i < values.Length; ++i) if (!ReferenceEquals(result[i], values[i].Anchor)) return false; return true;
            });
            Check("list-foreach-value-action", () => { int sum = 0; new List<Packet>(Values()).ForEach(value => { Validate(value); sum += value.Count; }); return sum == 6; });
            Check("binary-search-value-comparer", () => {
                var values = new List<Packet>(new[] { Value(1), Value(2), Value(3) }); return values.BinarySearch(Value(2), new PacketComparer()) == 1;
            });
            Check("dictionary-value-comparer", () => {
                var map = new Dictionary<Packet, ReferenceValue>(new PacketComparer()); var value = Value(2); map.Add(value, value.Anchor);
                for (int i = 10; i < 50; ++i) map.Add(Value(i), Reference(i));
                return map.TryGetValue(Value(2), out var found) && ReferenceEquals(found, value.Anchor) && map.Count == 41;
            });
            Check("hashset-reference-comparer", () => {
                var set = new HashSet<ReferenceValue>(new ReferenceComparer()); set.Add(Reference(2));
                return !set.Add(Reference(2)) && set.Contains(Reference(2)) && set.Count == 1;
            });
            Check("struct-target-comparison", () => {
                var target = new StructComparer { Salt = 7 }; Comparison<Packet> call = target.Compare; target.Salt = 99;
                var values = Values(); Array.Sort(values, call); Sorted(values); return true;
            });
            Check("array-convert-existing-virtual-value", () => {
                var values = Values(); Converter<Packet, Packet> call = new Processor().CopyValue;
                var result = Array.ConvertAll(values, call);
                for (int i = 0; i < values.Length; ++i)
                {
                    Validate(values[i]);
                    if (result[i].Count != values[i].Count + 1025 || result[i].Extra != values[i].Extra + 17 ||
                        !ReferenceEquals(result[i].Marker, values[i].Marker) || !ReferenceEquals(result[i].Anchor, values[i].Anchor)) return false;
                }
                return true;
            });
            Check("list-convert-existing-virtual-reference", () => {
                var value = new ReferenceValue { Value = 17, Extra = 4000000009L };
                var values = new List<ReferenceValue> { value, value }; Converter<ReferenceValue, ReferenceValue> call = new Processor().CopyReference;
                var result = values.ConvertAll(call); return result.Count == 2 && ReferenceEquals(result[0], value) && ReferenceEquals(result[1], value);
            });
            Check("array-convert-existing-generic-value", () => {
                var values = Values(); Converter<Packet, Packet> call = new Processor().Identity<Packet>;
                var result = Array.ConvertAll(values, call);
                for (int i = 0; i < values.Length; ++i) { Validate(result[i]); if (!ReferenceEquals(result[i].Anchor, values[i].Anchor)) return false; }
                return true;
            });
            Check("task-existing-virtual-exception", () => {
                Action call = new Processor().Fail; var task = Task.Run(call);
                try { if (!task.Wait(10000)) throw new TimeoutException("framework task callback"); }
                catch (AggregateException error)
                {
                    return error.InnerExceptions.Count == 1 && error.InnerException is InvalidOperationException &&
                        error.InnerException.Message == "virtual-signature-expected";
                }
                return false;
            });
            Check("comparison-exception", () => CallbackFailure(() => Array.Sort(Values(), (a, b) => { Validate(a); Validate(b); throw new CallbackException(); })));
            Check("predicate-exception", () => CallbackFailure(() => new List<Packet>(Values()).Find(value => { Validate(value); throw new CallbackException(); })));
            if (failures != 0) throw new InvalidOperationException("DHE framework callback failures: " + failures);
            Console.WriteLine("DHE framework callback pass: " + passed.Count); return passed.ToArray();
        }
    }
}
