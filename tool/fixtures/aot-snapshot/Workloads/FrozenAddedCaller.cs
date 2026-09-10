extern alias model;
using System;
using System.Collections.Generic;
using HybridCLR.Lab.AddedResource;

namespace HybridCLR.Lab.ResourceCases
{
    using Payload = model::HybridCLR.Lab.ValueLayout.Payload;
    public static class FrozenAddedCaller
    {
        // The updated original assembly acquires a parent and field type from
        // the new DLL, while that DLL refers back to the evolved Model value.
        public sealed class Derived : AddedBase
        {
            public AddedBox<Payload> Box;
            public Derived(Payload value) { Box = new AddedBox<Payload> { Value = value }; }
            public override Payload Read() { return Box.Value; }
        }
        private static Payload Echo(Payload value) { return value; }
        public static string[] Run()
        {
            object marker = new object(); var value = new Payload { Count = 17, Extra = AddedProbe.Extra, Reference = marker };
            var subject = new Derived(value);
            var records = new List<string>(AddedProbe.Run(subject, Echo));
            const string name = "added-assembly-declaration-reflection";
            Console.WriteLine("DHE case begin: " + name);
            var field = typeof(Derived).GetField("Box");
            AddedProbe.Require(field != null && field.FieldType.GetGenericTypeDefinition() == typeof(AddedBox<>), "new-field-type");
            AddedProbe.Value(((AddedBox<Payload>)field.GetValue(subject)).Value, marker, "reflected-box");
            AddedProbe.Require(typeof(Derived).BaseType == typeof(AddedBase), "new-parent-type");
            bool found = false;
            foreach (var reference in typeof(FrozenAddedCaller).Assembly.GetReferencedAssemblies())
                found |= reference.Name == "HybridCLR.ValueLayoutAdded";
            AddedProbe.Require(found, "updated-assembly-references");
            records.Add(name); Console.WriteLine("DHE case pass: " + name);
            return records.ToArray();
        }
    }
}
