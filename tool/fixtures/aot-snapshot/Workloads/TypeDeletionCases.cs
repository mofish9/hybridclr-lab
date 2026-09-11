extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using IOperations = model::HybridCLR.Lab.VirtualSignatures.IOperations;

namespace HybridCLR.Lab.TypeDeletion
{
    public static class Cases
    {
        private const string Deleted = "HybridCLR.Lab.ParentEvolution.ProcessorMiddle";
        private const string DeletedNamespace = "HybridCLR.Lab.ParentEvolution.";
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }
        private static bool RejectType(Action action)
        {
            try { action(); }
            catch (TypeLoadException) { return true; }
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
                    passed.Add(name); Console.WriteLine("DHE type deletion check: " + name);
                }
                catch (Exception error) { ++failures; Console.WriteLine("DHE type deletion failure: " + name + ": " + error); }
            }
            var type = typeof(Processor); var assembly = type.Assembly;
            string qualified = Deleted + ", " + assembly.FullName;
            Check("assembly-lookup-absent", () => assembly.GetType(Deleted) == null);
            Check("assembly-ignore-case-absent", () => assembly.GetType(Deleted.ToLowerInvariant(), false, true) == null);
            Check("assembly-throwing-lookup", () => RejectType(() => assembly.GetType(Deleted, true)));
            Check("qualified-lookup-absent", () => Type.GetType(qualified, false) == null);
            Check("qualified-throwing-lookup", () => RejectType(() => Type.GetType(qualified, true)));
            Check("all-types-exclude-deleted", () => {
                bool retained = false;
                foreach (var item in assembly.GetTypes()) {
                    if (item.FullName.StartsWith(DeletedNamespace, StringComparison.Ordinal)) return false;
                    retained |= item == type;
                }
                return retained;
            });
            Check("exported-types-exclude-deleted", () => {
                bool retained = false;
                foreach (var item in assembly.GetExportedTypes()) {
                    if (item.FullName.StartsWith(DeletedNamespace, StringComparison.Ordinal)) return false;
                    retained |= item == type;
                }
                return retained;
            });
            Check("defined-types-exclude-deleted", () => {
                bool retained = false;
                foreach (var item in assembly.DefinedTypes) {
                    if (item.FullName.StartsWith(DeletedNamespace, StringComparison.Ordinal)) return false;
                    retained |= item.AsType() == type;
                }
                return retained;
            });
            Check("companion-type-absent", () => assembly.GetType(DeletedNamespace + "ParentMarker") == null &&
                assembly.GetType(DeletedNamespace + "Cases") == null);
            var instance = new Processor(); ProcessorRoot root = instance; object boxed = instance;
            Check("current-parent", () => type.BaseType == typeof(ProcessorRoot) && boxed.GetType().BaseType == typeof(ProcessorRoot));
            Check("root-assignability", () => typeof(ProcessorRoot).IsAssignableFrom(type) && typeof(IOperations).IsInstanceOfType(boxed));
            Check("deleted-members-absent", () => type.GetField("ParentExtra") == null && type.GetField("ParentReference") == null &&
                type.GetMethod("ReadParentExtra") == null && type.GetProperty("ParentProperty") == null && type.GetEvent("ParentEvent") == null);
            Check("own-field-layout", () => instance.Bias == 25 && instance.Extra == 1000);
            Check("own-field-reflection", () => (int)type.GetField("Bias").GetValue(instance) == 25 &&
                (long)type.GetField("Extra").GetValue(instance) == 1000 && type.GetField("Extra").DeclaringType == type);
            Check("construction-reflection", () => {
                var created = (Processor)Activator.CreateInstance(type);
                return created.Bias == 25 && created.Extra == 1000 && created.GetType() == type;
            });
            Check("root-virtual", () => {
                var value = root.CopyValue(new Packet { Count = 3, Extra = 90000000003L, Marker = boxed });
                return value.Count == 1028 && value.Extra == 90000000020L && ReferenceEquals(value.Marker, boxed);
            });
            Check("interface-virtual", () => {
                var value = ((IOperations)instance).CopyValue(new Packet { Count = 7, Extra = 80000000001L, Marker = boxed });
                return value.Count == 1032 && value.Extra == 80000000018L && ReferenceEquals(value.Marker, boxed);
            });
            Check("generic-dispatch", () => ReferenceEquals(root.Identity<object>(boxed), boxed));
            Check("root-method-definition", () => type.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            Check("reflection-virtual", () => {
                var value = (Packet)type.GetMethod("CopyValue").Invoke(instance, new object[] {
                    new Packet { Count = 5, Extra = 60000000001L, Marker = boxed } });
                return value.Count == 1030 && value.Extra == 60000000018L && ReferenceEquals(value.Marker, boxed);
            });
            Check("reference-survives-gc", () => {
                var marker = new object(); var value = root.CopyValue(new Packet { Count = 1, Marker = marker });
                var weak = new WeakReference(marker); marker = null;
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                return weak.IsAlive && ReferenceEquals(weak.Target, value.Marker) && instance.Bias == 25 && instance.Extra == 1000;
            });
            Check("independent-objects", () => {
                var next = new Processor(); next.Bias = 31;
                return instance.Bias == 25 && next.Bias == 31 && next.Extra == 1000 && next.GetType() == type;
            });
            if (failures != 0) throw new InvalidOperationException("Type deletion failures: " + failures);
            Console.WriteLine("DHE type deletion pass: " + passed.Count); return passed.ToArray();
        }
    }
}
