extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;

namespace HybridCLR.Lab.ParentEvolution
{
    public sealed class ParentMarker { public int Value = 917; }

    public abstract class ProcessorMiddle : ProcessorRoot
    {
        public static int ConstructorCalls;
        public static bool ThrowConstruction;
        public long ParentExtra;
        public object ParentReference;
        protected ProcessorMiddle()
        {
            if (ThrowConstruction) throw new InvalidOperationException("parent-constructor-expected");
            ParentExtra = 60000000001L; ParentReference = new ParentMarker(); ++ConstructorCalls;
        }
        public long ReadParentExtra() => ParentExtra;
        public long ParentProperty { get => ParentExtra; set => ParentExtra = value; }
        public static long ParentStaticProperty => 42;
        public event Action ParentEvent;
        public void RaiseParentEvent() => ParentEvent?.Invoke();
        private long ParentPrivateProperty => -1;
        private long ReadPrivateParent() => -1;
    }

    public static class Cases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }

        public static string[] Run()
        {
            var passed = new List<string>(); int failures = 0;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    passed.Add(name); Console.WriteLine("DHE parent evolution check: " + name);
                }
                catch (Exception error) { ++failures; Console.WriteLine("DHE parent evolution failure: " + name + ": " + error); }
            }
            int before = ProcessorMiddle.ConstructorCalls;
            var instance = new Processor(); object boxed = instance; ProcessorRoot root = instance;
            var type = typeof(Processor); var middleType = typeof(ProcessorMiddle);
            Check("current-direct-parent", () => type.BaseType == middleType && boxed.GetType().BaseType == middleType);
            Check("root-ancestry", () => middleType.BaseType == typeof(ProcessorRoot) && type.IsSubclassOf(middleType) &&
                typeof(ProcessorRoot).IsAssignableFrom(type));
            Check("runtime-middle-cast", () => boxed is ProcessorMiddle && middleType.IsInstanceOfType(boxed));
            Check("parent-constructor-once", () => ProcessorMiddle.ConstructorCalls == before + 1);
            Check("inherited-default-fields", () => ((ProcessorMiddle)boxed).ParentExtra == 60000000001L &&
                ((ParentMarker)((ProcessorMiddle)boxed).ParentReference).Value == 917);
            Check("original-field-offsets", () => instance.Bias == 25 && instance.Extra == 1000);
            Check("inherited-field-owner", () => type.GetField("ParentExtra").DeclaringType == middleType &&
                type.GetField("ParentReference").DeclaringType == middleType);
            Check("inherited-reflection-read", () => (long)type.GetField("ParentExtra").GetValue(instance) == 60000000001L);
            Check("inherited-reflection-write", () => {
                type.GetField("ParentExtra").SetValue(instance, 60000000009L);
                return ((ProcessorMiddle)boxed).ParentExtra == 60000000009L && instance.Bias == 25 && instance.Extra == 1000;
            });
            Check("inherited-method-invoke", () => (long)type.GetMethod("ReadParentExtra").Invoke(instance, null) == 60000000009L);
            Check("virtual-dispatch-through-root", () => {
                var value = new Packet { Count = 3, Extra = 90000000003L, Marker = boxed };
                var result = root.CopyValue(value);
                return result.Count == 1028 && result.Extra == 90000000020L && ReferenceEquals(result.Marker, boxed);
            });
            Check("generic-dispatch-through-root", () => ReferenceEquals(root.Identity<ProcessorMiddle>((ProcessorMiddle)boxed), boxed));
            Check("root-method-definition", () => type.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            Check("parent-reference-survives-gc", () => {
                var weak = new WeakReference(((ProcessorMiddle)boxed).ParentReference);
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                return weak.IsAlive && ReferenceEquals(weak.Target, ((ProcessorMiddle)boxed).ParentReference) &&
                    ((ParentMarker)((ProcessorMiddle)boxed).ParentReference).Value == 917;
            });
            Check("independent-instances", () => {
                var second = (ProcessorMiddle)(object)new Processor();
                return second.ParentExtra == 60000000001L && !ReferenceEquals(second.ParentReference, ((ProcessorMiddle)boxed).ParentReference) &&
                    ((ProcessorMiddle)boxed).ParentExtra == 60000000009L;
            });
            Check("parent-constructor-exception", () => {
                ProcessorMiddle.ThrowConstruction = true;
                try { new Processor(); return false; }
                catch (InvalidOperationException error) { return error.Message == "parent-constructor-expected"; }
                finally { ProcessorMiddle.ThrowConstruction = false; }
            });
            Check("inherited-field-reflected-type", () => type.GetField("ParentExtra").ReflectedType == type);
            Check("inherited-method-owner", () => type.GetMethod("ReadParentExtra").DeclaringType == middleType &&
                type.GetMethod("ReadParentExtra").ReflectedType == type);
            const BindingFlags own = BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.Instance;
            Check("declared-only-inherited-fields-absent", () => type.GetField("ParentExtra", own) == null && type.GetField("Bias", own) != null);
            Check("declared-only-inherited-methods-absent", () => type.GetMethod("ReadParentExtra", own) == null && type.GetMethod("CopyValue", own) != null);
            Check("inherited-property-owner", () => type.GetProperty("ParentProperty").DeclaringType == middleType &&
                type.GetProperty("ParentProperty").ReflectedType == type);
            Check("inherited-property-read-write", () => {
                var target = new Processor(); var property = type.GetProperty("ParentProperty");
                if ((long)property.GetValue(target, null) != 60000000001L) return false;
                property.SetValue(target, 60000000027L, null);
                return ((ProcessorMiddle)(object)target).ParentExtra == 60000000027L && target.Bias == 25 && target.Extra == 1000;
            });
            Check("declared-only-inherited-properties-absent", () => type.GetProperty("ParentProperty", own) == null);
            Check("inherited-event-owner", () => type.GetEvent("ParentEvent").DeclaringType == middleType &&
                type.GetEvent("ParentEvent").ReflectedType == type);
            Check("inherited-event-add-remove", () => {
                var target = (ProcessorMiddle)(object)new Processor(); int count = 0; Action handler = () => ++count;
                var ev = type.GetEvent("ParentEvent"); ev.AddEventHandler(target, handler); target.RaiseParentEvent();
                ev.RemoveEventHandler(target, handler); target.RaiseParentEvent(); return count == 1;
            });
            Check("declared-only-inherited-events-absent", () => type.GetEvent("ParentEvent", own) == null);
            Check("nonpublic-inherited-members-filtered", () =>
                type.GetMethod("ReadPrivateParent", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
                type.GetProperty("ParentPrivateProperty", BindingFlags.Instance | BindingFlags.NonPublic) == null &&
                middleType.GetMethod("ReadPrivateParent", BindingFlags.Instance | BindingFlags.NonPublic) != null);
            Check("inherited-static-flatten-hierarchy", () => {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Static;
                var property = type.GetProperty("ParentStaticProperty", flags | BindingFlags.FlattenHierarchy);
                return type.GetProperty("ParentStaticProperty", flags) == null && property.DeclaringType == middleType &&
                    property.ReflectedType == type && (long)property.GetValue(null, null) == 42;
            });
            if (failures != 0) throw new InvalidOperationException("DHE parent evolution failures: " + failures);
            Console.WriteLine("DHE parent evolution pass: " + passed.Count); return passed.ToArray();
        }
    }
}
