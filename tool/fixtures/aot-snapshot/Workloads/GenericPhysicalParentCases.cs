extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using IOperations = model::HybridCLR.Lab.VirtualSignatures.IOperations;

namespace HybridCLR.Lab.GenericPhysicalParents
{
    public static class Cases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }
        public static string[] Run()
        {
            var checks = new List<string>();
            void Check(string name, bool valid)
            {
                if (!valid) throw new InvalidOperationException("Generic physical parent failed: " + name);
                checks.Add(name); Console.WriteLine("DHE generic physical parent check: " + name);
            }
            var owner = typeof(Processor); var parent = typeof(GenericParent<Packet>);
            int before = GenericParent<Packet>.ConstructorCalls;
            var child = new Processor(); var view = (GenericParent<Packet>)(object)child;
            var marker = new object();
            var packet = new Packet { Count = 7, Extra = 90000000003L, Marker = marker };
            bool Same(Packet value) => value.Count == packet.Count && value.Extra == packet.Extra && ReferenceEquals(value.Marker, marker);
            Check("closed-parent-identity", owner.BaseType == parent && child.GetType().BaseType == parent);
            Check("cross-assembly-parent", parent.Assembly != owner.Assembly && parent.Assembly.GetName().Name == "HybridCLR.ValueLayoutOther");
            Check("generic-definition", parent.GetGenericTypeDefinition() == typeof(GenericParent<>) && parent.GetGenericArguments()[0] == typeof(Packet));
            Check("logical-parent-casts", (object)child is GenericParent<Packet> && parent.IsAssignableFrom(owner) && parent.IsInstanceOfType(child));
            Check("immutable-root", parent.BaseType == typeof(ProcessorRoot) && typeof(IOperations).IsAssignableFrom(owner));
            Check("constructor-once", GenericParent<Packet>.ConstructorCalls == before + 1);
            Check("inherited-default-layout", view.GenericValue.Count == 0 && view.GenericValue.Marker == null && view.ParentExtra == 70000000003L);
            Check("child-layout", child.Bias == 25 && child.Extra == 1000L);
            view.GenericValue = packet;
            Check("generic-value-field", Same(view.GenericValue) && child.Bias == 25 && child.Extra == 1000L);
            var field = owner.GetField("GenericValue");
            Check("generic-field-owner", field.DeclaringType == parent && field.ReflectedType == owner && field.FieldType == typeof(Packet));
            Check("generic-field-read", Same((Packet)field.GetValue(child)));
            field.SetValue(child, packet);
            Check("generic-field-write", Same(view.GenericValue));
            Check("inherited-generic-method", Same(view.ReadGenericValue()));
            var method = owner.GetMethod("ReadGenericValue");
            Check("generic-method-owner", method.DeclaringType == parent && method.ReflectedType == owner && method.ReturnType == typeof(Packet));
            Check("generic-method-reflection", Same((Packet)method.Invoke(child, null)));
            Check("inherited-generic-virtual", Same(view.EchoParent(packet)));
            var property = owner.GetProperty("ParentProperty");
            Check("generic-property-owner", property.DeclaringType == parent && property.PropertyType == typeof(Packet));
            property.SetValue(child, packet, null);
            Check("generic-property-roundtrip", Same((Packet)property.GetValue(child, null)));
            var ev = owner.GetEvent("ParentEvent"); int events = 0; Action handler = () => ++events;
            ev.AddEventHandler(child, handler); view.RaiseParentEvent(); ev.RemoveEventHandler(child, handler); view.RaiseParentEvent();
            Check("generic-parent-event", ev.DeclaringType == parent && events == 1);
            var copied = ((ProcessorRoot)child).CopyValue(packet);
            Check("root-virtual", copied.Count == 1032 && copied.Extra == 90000000020L && ReferenceEquals(copied.Marker, marker));
            copied = ((IOperations)child).CopyValue(packet);
            Check("interface-virtual", copied.Count == 1032 && copied.Extra == 90000000020L && ReferenceEquals(copied.Marker, marker));
            Check("root-method-definition", owner.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            GenericParent<Packet>.StaticMarker = 31; GenericParent<long>.StaticMarker = 47; GenericParent<object>.StaticMarker = 59;
            Check("closed-static-isolation", GenericParent<Packet>.StaticMarker == 31 && GenericParent<long>.StaticMarker == 47 && GenericParent<object>.StaticMarker == 59);
            Check("generic-root-value", Same(((ProcessorRoot)child).Identity<Packet>(packet)));
            var next = (Processor)Activator.CreateInstance(owner);
            Check("reflection-construction", next.Bias == 25 && next.Extra == 1000L && ((GenericParent<Packet>)(object)next).GenericValue.Marker == null);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check("inherited-value-reference-gc", Same(view.GenericValue) && ((ParentMarker)view.ParentReference).Value == 1927);
            Check("independent-objects", !ReferenceEquals(view.ParentReference, ((GenericParent<Packet>)(object)next).ParentReference));
            bool expected = false; GenericParent<Packet>.ThrowConstruction = true;
            try { new Processor(); }
            catch (InvalidOperationException error) { expected = error.Message == "generic-parent-constructor-expected"; }
            finally { GenericParent<Packet>.ThrowConstruction = false; }
            Check("constructor-exception", expected);
            Check("child-data-preserved", child.Bias == 25 && child.Extra == 1000L && Same(view.GenericValue));
            Console.WriteLine("DHE generic physical parent pass: " + checks.Count);
            return checks.ToArray();
        }
    }
}
