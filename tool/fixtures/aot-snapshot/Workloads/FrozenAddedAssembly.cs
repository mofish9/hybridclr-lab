extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using HybridCLR.Lab.ValueLayoutNative;

namespace HybridCLR.Lab.AddedResource
{
    using Payload = model::HybridCLR.Lab.ValueLayout.Payload;
    public sealed class AddedBox<T> { public T Value; }
    public abstract class AddedBase : model::HybridCLR.Lab.ResourceCases.FrozenResourceCases.IReader<Payload>
    { public abstract Payload Read(); }
    public static class AddedCache<T>
    { public static T Value; public static int Runs; static AddedCache() { Runs++; } }

    public static class AddedProbe
    {
        public const long Extra = 70000000017L;
        private static T Copy<T>(T value) { return value; }
        public static void Require(bool ok, string name)
        { if (!ok) throw new InvalidOperationException("Added assembly case failed: " + name); }
        public static void Value(Payload value, object marker, string name)
        { Require(value.Count == 17 && value.Extra == Extra && ReferenceEquals(value.Reference, marker), name); }
        public static string[] Run(AddedBase subject, Func<Payload, Payload> callback)
        {
            object marker = new object(); var value = new Payload { Count = 17, Extra = Extra, Reference = marker };
            var records = new List<string>();
            Action<string, Action> test = (name, action) => {
                Console.WriteLine("DHE case begin: " + name); action(); records.Add(name);
                Console.WriteLine("DHE case pass: " + name);
            };
            test("added-assembly-identity", () => Require(Assembly.GetExecutingAssembly().GetName().Name == "HybridCLR.ValueLayoutAdded" &&
                typeof(AddedProbe).Assembly != typeof(Payload).Assembly, "separate-assembly"));
            test("added-assembly-open-generic-copy", () => Value(Copy(value), marker, "generic-copy"));
            test("added-assembly-ordinary-frozen-copy", () => {
                Value(NativeBoundary.Echo(value), marker, "ordinary-echo");
                Value((Payload)NativeBoundary.FrozenCopyBox(value), marker, "ordinary-box");
                Value((Payload)NativeBoundary.FrozenInlineBox(value), marker, "ordinary-inline");
            });
            test("added-assembly-generic-container", () => {
                var values = new List<Payload>(); for (int i = 0; i < 32; ++i) values.Add(value);
                foreach (var item in values) Value(item, marker, "list-value");
                Value(new AddedBox<Payload> { Value = value }.Value, marker, "generic-owner");
            });
            test("added-assembly-callback", () => Value(callback(value), marker, "model-callback"));
            test("added-assembly-virtual-interface", () => {
                var first = subject.Read(); Value(first, first.Reference, "virtual-value");
                var reader = (model::HybridCLR.Lab.ResourceCases.FrozenResourceCases.IReader<Payload>)subject;
                Value(reader.Read(), first.Reference, "interface-value");
            });
            test("added-assembly-generic-static", () => {
                AddedCache<Payload>.Value = value; Value(AddedCache<Payload>.Value, marker, "generic-static");
                AddedCache<long>.Value = Extra; Require(AddedCache<long>.Value == Extra && AddedCache<long>.Runs == 1 &&
                    AddedCache<Payload>.Runs == 1, "generic-static-isolation");
            });
            return records.ToArray();
        }
    }
}
