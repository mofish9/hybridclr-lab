extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
#if DHE_PARENT_REFERENCE_ARGUMENT
using ParentPacket = HybridCLR.Lab.GenericPhysicalParents.ReferencePacket;
#else
using ParentPacket = model::HybridCLR.Lab.VirtualSignatures.Packet;
#endif
using IOperations = model::HybridCLR.Lab.VirtualSignatures.IOperations;

namespace HybridCLR.Lab.GenericPhysicalParents
{
    public static class Cases
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference InstallMarker(GenericParent<ParentPacket> owner)
        {
            var marker = new ParentMarker();
            owner.GenericValue = new ParentPacket { Count = 19, Extra = 90000000029L, Marker = marker };
            return new WeakReference(marker);
        }
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
            var owner = typeof(Processor); var parent = typeof(GenericParent<ParentPacket>);
            int before = GenericParent<ParentPacket>.ConstructorCalls;
            var child = new Processor(); var view = (GenericParent<ParentPacket>)(object)child;
            var marker = new object();
            var packet = new ParentPacket { Count = 7, Extra = 90000000003L, Marker = marker };
            var rootPacket = new Packet { Count = 7, Extra = 90000000003L, Marker = marker };
            bool Same(ParentPacket value) => value.Count == packet.Count && value.Extra == packet.Extra && ReferenceEquals(value.Marker, marker);
            Check("closed-parent-identity", owner.BaseType == parent && child.GetType().BaseType == parent);
            Check("cross-assembly-parent", parent.Assembly != owner.Assembly && parent.Assembly.GetName().Name == "HybridCLR.ValueLayoutOther");
            Check("generic-definition", parent.GetGenericTypeDefinition() == typeof(GenericParent<>) && parent.GetGenericArguments()[0] == typeof(ParentPacket));
            Check("logical-parent-casts", (object)child is GenericParent<ParentPacket> && parent.IsAssignableFrom(owner) && parent.IsInstanceOfType(child));
            Check("immutable-root", parent.BaseType == typeof(ProcessorRoot) && typeof(IOperations).IsAssignableFrom(owner));
            Check("constructor-once", GenericParent<ParentPacket>.ConstructorCalls == before + 1);
            Check("inherited-default-layout", EqualityComparer<ParentPacket>.Default.Equals(view.GenericValue, default(ParentPacket)) && view.ParentExtra == 70000000003L);
            Check("child-layout", child.Bias == 25 && child.Extra == 1000L);
            view.GenericValue = packet;
            Check("generic-value-field", Same(view.GenericValue) && child.Bias == 25 && child.Extra == 1000L);
            var field = owner.GetField("GenericValue");
            Check("generic-field-owner", field.DeclaringType == parent && field.ReflectedType == owner && field.FieldType == typeof(ParentPacket));
            Check("generic-field-read", Same((ParentPacket)field.GetValue(child)));
            field.SetValue(child, packet);
            Check("generic-field-write", Same(view.GenericValue));
            Check("inherited-generic-method", Same(view.ReadGenericValue()));
            var method = owner.GetMethod("ReadGenericValue");
            Check("generic-method-owner", method.DeclaringType == parent && method.ReflectedType == owner && method.ReturnType == typeof(ParentPacket));
            Check("generic-method-reflection", Same((ParentPacket)method.Invoke(child, null)));
            Check("inherited-generic-virtual", Same(view.EchoParent(packet)));
            var property = owner.GetProperty("ParentProperty");
            Check("generic-property-owner", property.DeclaringType == parent && property.PropertyType == typeof(ParentPacket));
            property.SetValue(child, packet, null);
            Check("generic-property-roundtrip", Same((ParentPacket)property.GetValue(child, null)));
            var ev = owner.GetEvent("ParentEvent"); int events = 0; Action handler = () => ++events;
            ev.AddEventHandler(child, handler); view.RaiseParentEvent(); ev.RemoveEventHandler(child, handler); view.RaiseParentEvent();
            Check("generic-parent-event", ev.DeclaringType == parent && events == 1);
            var copied = ((ProcessorRoot)child).CopyValue(rootPacket);
            Check("root-virtual", copied.Count == 1032 && copied.Extra == 90000000020L && ReferenceEquals(copied.Marker, marker));
            copied = ((IOperations)child).CopyValue(rootPacket);
            Check("interface-virtual", copied.Count == 1032 && copied.Extra == 90000000020L && ReferenceEquals(copied.Marker, marker));
            Check("root-method-definition", owner.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            GenericParent<ParentPacket>.StaticMarker = 31; GenericParent<long>.StaticMarker = 47; GenericParent<object>.StaticMarker = 59;
            Check("closed-static-isolation", GenericParent<ParentPacket>.StaticMarker == 31 && GenericParent<long>.StaticMarker == 47 && GenericParent<object>.StaticMarker == 59);
            Check("generic-root-value", Same(((ProcessorRoot)child).Identity<ParentPacket>(packet)));
            var next = (Processor)Activator.CreateInstance(owner);
            Check("reflection-construction", next.Bias == 25 && next.Extra == 1000L &&
                EqualityComparer<ParentPacket>.Default.Equals(((GenericParent<ParentPacket>)(object)next).GenericValue, default(ParentPacket)));
            var weak = InstallMarker(view);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check("inherited-value-reference-gc", weak.IsAlive && ReferenceEquals(weak.Target, view.GenericValue.Marker) &&
                ((ParentMarker)view.GenericValue.Marker).Value == 1927 && view.GenericValue.Count == 19 &&
                view.GenericValue.Extra == 90000000029L && ((ParentMarker)view.ParentReference).Value == 1927);
            view.GenericValue = packet;
            Check("independent-objects", !ReferenceEquals(view.ParentReference, ((GenericParent<ParentPacket>)(object)next).ParentReference));
            bool expected = false; GenericParent<ParentPacket>.ThrowConstruction = true;
            try { new Processor(); }
            catch (InvalidOperationException error) { expected = error.Message == "generic-parent-constructor-expected"; }
            finally { GenericParent<ParentPacket>.ThrowConstruction = false; }
            Check("constructor-exception", expected);
            Check("child-data-preserved", child.Bias == 25 && child.Extra == 1000L && Same(view.GenericValue));
            Console.WriteLine("DHE generic physical parent pass: " + checks.Count);
            return checks.ToArray();
        }
    }
}
