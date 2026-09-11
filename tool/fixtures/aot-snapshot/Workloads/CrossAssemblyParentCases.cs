extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using IOperations = model::HybridCLR.Lab.VirtualSignatures.IOperations;

namespace HybridCLR.Lab.CrossAssemblyParents
{
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
                    passed.Add(name); Console.WriteLine("DHE cross parent check: " + name);
                }
                catch (Exception error) { ++failures; Console.WriteLine("DHE cross parent failure: " + name + ": " + error); }
            }
            int before = CrossParent.ConstructorCalls;
            var instance = new Processor(); object boxed = instance; var type = typeof(Processor); var parent = typeof(CrossParent);
            var view = (CrossParent)boxed;
            Check("current-parent", () => type.BaseType == parent && boxed.GetType().BaseType == parent);
            Check("parent-assembly", () => parent.Assembly.GetName().Name == "HybridCLR.ValueLayoutOther" && parent.Assembly != type.Assembly);
            Check("root-assembly", () => parent.BaseType == typeof(ProcessorRoot) && parent.BaseType.Assembly == type.Assembly);
            Check("parent-qualified-lookup", () => Type.GetType(parent.AssemblyQualifiedName, true) == parent && parent.Assembly.GetType(parent.FullName, true) == parent);
            Check("owner-assembly-has-no-parent-definition", () => type.Assembly.GetType(parent.FullName) == null);
            Check("logical-parent-cast", () => boxed is CrossParent && parent.IsInstanceOfType(boxed) && parent.IsAssignableFrom(type));
            Check("root-contract", () => typeof(ProcessorRoot).IsAssignableFrom(type) && typeof(IOperations).IsAssignableFrom(type));
            Check("parent-constructor-once", () => CrossParent.ConstructorCalls == before + 1);
            Check("parent-field-defaults", () => view.CrossVersion == 311 && view.ParentExtra == 70000000003L && ((CrossMarker)view.ParentReference).Value == 1237);
            Check("child-field-layout", () => instance.Bias == 25 && instance.Extra == 1000);
            Check("field-owner", () => type.GetField("ParentExtra").DeclaringType == parent && type.GetField("ParentExtra").ReflectedType == type);
            Check("field-owner-assembly", () => type.GetField("ParentExtra").DeclaringType.Assembly == parent.Assembly);
            Check("field-read", () => (long)type.GetField("ParentExtra").GetValue(instance) == 70000000003L);
            Check("field-write", () => { type.GetField("ParentExtra").SetValue(instance, 70000000009L); return view.ParentExtra == 70000000009L && instance.Bias == 25 && instance.Extra == 1000; });
            Check("method-owner", () => type.GetMethod("ReadParentExtra").DeclaringType == parent && type.GetMethod("ReadParentExtra").ReflectedType == type);
            Check("method-invoke", () => (long)type.GetMethod("ReadParentExtra").Invoke(instance, null) == 70000000009L);
            Check("property-owner", () => type.GetProperty("ParentProperty").DeclaringType == parent && type.GetProperty("ParentProperty").ReflectedType == type);
            Check("property-roundtrip", () => { var p = type.GetProperty("ParentProperty"); p.SetValue(instance, 70000000027L, null); return (long)p.GetValue(instance, null) == 70000000027L && view.ParentExtra == 70000000027L; });
            Check("event-owner", () => type.GetEvent("ParentEvent").DeclaringType == parent && type.GetEvent("ParentEvent").ReflectedType == type);
            Check("event-roundtrip", () => {
                int count = 0; Action action = () => ++count; var e = type.GetEvent("ParentEvent");
                e.AddEventHandler(instance, action); view.RaiseParentEvent(); e.RemoveEventHandler(instance, action); view.RaiseParentEvent(); return count == 1;
            });
            Check("declared-only", () => {
                const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly;
                return type.GetField("ParentExtra", flags) == null && type.GetMethod("ReadParentExtra", flags) == null &&
                    type.GetProperty("ParentProperty", flags) == null && type.GetEvent("ParentEvent", flags) == null && type.GetField("Bias", flags) != null;
            });
            Check("private-parent-filtered", () => type.GetProperty("PrivateParentProperty", BindingFlags.Instance | BindingFlags.NonPublic) == null);
            Check("inherited-static-property", () => (int)type.GetProperty("ParentStaticProperty", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy).GetValue(null, null) == 311);
            Check("root-virtual", () => { var value = ((ProcessorRoot)instance).CopyValue(new Packet { Count = 3, Extra = 90000000003L, Marker = boxed }); return value.Count == 1028 && value.Extra == 90000000020L && ReferenceEquals(value.Marker, boxed); });
            Check("interface-virtual", () => { var value = ((IOperations)instance).CopyValue(new Packet { Count = 7, Extra = 80000000001L, Marker = boxed }); return value.Count == 1032 && value.Extra == 80000000018L && ReferenceEquals(value.Marker, boxed); });
            Check("generic-root-parent", () => ReferenceEquals(((ProcessorRoot)instance).Identity<CrossParent>(view), boxed));
            Check("generic-root-value", () => { var value = ((ProcessorRoot)instance).Identity<Packet>(new Packet { Count = 5, Extra = 60000000001L, Marker = boxed }); return value.Count == 5 && value.Extra == 60000000001L && ReferenceEquals(value.Marker, boxed); });
            Check("root-method-definition", () => type.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            Check("reflection-construction", () => { var next = (Processor)Activator.CreateInstance(type); return next.Bias == 25 && next.Extra == 1000 && ((CrossParent)(object)next).ParentExtra == 70000000003L; });
            Check("parent-reference-gc", () => { var weak = new WeakReference(view.ParentReference); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); return weak.IsAlive && ReferenceEquals(weak.Target, view.ParentReference) && ((CrossMarker)weak.Target).Value == 1237; });
            Check("independent-instances", () => { var next = (CrossParent)(object)new Processor(); return next.ParentExtra == 70000000003L && view.ParentExtra == 70000000027L && !ReferenceEquals(next.ParentReference, view.ParentReference); });
            Check("constructor-exception", () => { CrossParent.ThrowConstruction = true; try { new Processor(); return false; } catch (InvalidOperationException error) { return error.Message == "cross-parent-constructor-expected"; } finally { CrossParent.ThrowConstruction = false; } });
            Check("child-data-preserved", () => instance.Bias == 25 && instance.Extra == 1000 && view.CrossVersion == 311 && view.ParentExtra == 70000000027L);
            if (failures != 0) throw new InvalidOperationException("Cross parent failures: " + failures);
            Console.WriteLine("DHE cross parent pass: " + passed.Count); return passed.ToArray();
        }
    }
}
