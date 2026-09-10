extern alias model;
using System;
using System.Collections.Generic;
using System.Reflection;
using Processor = model::HybridCLR.Lab.VirtualSignatures.Processor;
using ProcessorRoot = model::HybridCLR.Lab.VirtualSignatures.ProcessorRoot;
using Packet = model::HybridCLR.Lab.VirtualSignatures.Packet;
using IOperations = model::HybridCLR.Lab.VirtualSignatures.IOperations;
using FormerParent = model::HybridCLR.Lab.ParentEvolution.ProcessorMiddle;

namespace HybridCLR.Lab.ParentTransitions
{
#if PARENT_REPLACEMENT
    public sealed class ReplacementMarker { public int Value = 1237; }
    public abstract class ReplacementParent : ProcessorRoot
    {
        public static int ConstructorCalls;
        public static bool ThrowConstruction;
        public long ReplacementExtra;
        public object ReplacementReference;
        protected ReplacementParent()
        {
            if (ThrowConstruction) throw new InvalidOperationException("replacement-constructor-expected");
            ReplacementExtra = 70000000003L; ReplacementReference = new ReplacementMarker(); ++ConstructorCalls;
        }
        public long ReadReplacement() => ReplacementExtra;
        public long ReplacementProperty { get => ReplacementExtra; set => ReplacementExtra = value; }
        public event Action ReplacementEvent;
        public void RaiseReplacementEvent() => ReplacementEvent?.Invoke();
    }
#endif
    public static class Cases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-virtualSignatureProbe") >= 0) Run();
        }
        private static bool Reject(Action action)
        {
            try { action(); }
            catch (TargetException) { return true; }
            catch (ArgumentException) { return true; }
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
                    passed.Add(name); Console.WriteLine("DHE parent transition check: " + name);
                }
                catch (Exception error) { ++failures; Console.WriteLine("DHE parent transition failure: " + name + ": " + error); }
            }
            int formerCalls = FormerParent.ConstructorCalls;
#if PARENT_REPLACEMENT
            int replacementCalls = ReplacementParent.ConstructorCalls;
            Type expectedParent = typeof(ReplacementParent);
            Console.WriteLine("DHE parent transition mode: replacement");
#else
            Type expectedParent = typeof(ProcessorRoot);
            Console.WriteLine("DHE parent transition mode: removal");
#endif
            var instance = new Processor(); object boxed = instance; var type = typeof(Processor); var former = typeof(FormerParent);
            Check("current-direct-parent", () => type.BaseType == expectedParent && boxed.GetType().BaseType == expectedParent);
            Check("root-contract-retained", () => typeof(ProcessorRoot).IsAssignableFrom(type) && typeof(IOperations).IsAssignableFrom(type));
            Check("former-parent-not-assignable", () => !former.IsAssignableFrom(type) && !type.IsSubclassOf(former));
            Check("former-parent-cast-rejected", () => !(boxed is FormerParent) && !former.IsInstanceOfType(boxed));
            Check("former-fields-not-inherited", () => type.GetField("ParentExtra") == null && type.GetField("ParentReference") == null);
            Check("former-method-not-inherited", () => type.GetMethod("ReadParentExtra") == null);
            Check("former-property-not-inherited", () => type.GetProperty("ParentProperty") == null);
            Check("former-event-not-inherited", () => type.GetEvent("ParentEvent") == null);
            Check("former-constructor-not-run", () => FormerParent.ConstructorCalls == formerCalls);
            Check("own-fields-retained", () => instance.Bias == 25 && instance.Extra == 1000);
            Check("own-field-reflection", () => (int)type.GetField("Bias").GetValue(instance) == 25 &&
                (long)type.GetField("Extra").GetValue(instance) == 1000 && type.GetField("Extra").DeclaringType == type);
            Check("root-method-definition", () => type.GetMethod("CopyValue").GetBaseDefinition().DeclaringType == typeof(ProcessorRoot));
            Check("root-virtual-value", () => {
                ProcessorRoot root = instance; var result = root.CopyValue(new Packet { Count = 3, Extra = 90000000003L, Marker = boxed });
                return result.Count == 1028 && result.Extra == 90000000020L && ReferenceEquals(result.Marker, boxed);
            });
            Check("interface-value", () => {
                IOperations op = instance; var result = op.CopyValue(new Packet { Count = 7, Extra = 80000000001L, Marker = boxed });
                return result.Count == 1032 && result.Extra == 80000000018L && ReferenceEquals(result.Marker, boxed);
            });
            Check("generic-root-call", () => ReferenceEquals(((ProcessorRoot)instance).Identity<object>(boxed), boxed));
            Check("reflection-construction", () => {
                var created = (Processor)Activator.CreateInstance(type); return created.Bias == 25 && created.Extra == 1000 && created.GetType() == type;
            });
            Check("former-field-rejects-current", () => { var field = former.GetField("ParentExtra"); return field != null && Reject(() => field.GetValue(instance)); });
            Check("former-method-rejects-current", () => { var method = former.GetMethod("ReadParentExtra"); return method != null && Reject(() => method.Invoke(instance, null)); });
            Check("former-property-rejects-current", () => { var property = former.GetProperty("ParentProperty"); return property != null && Reject(() => property.GetValue(instance, null)); });
            Check("former-event-rejects-current", () => { var ev = former.GetEvent("ParentEvent"); Action handler = () => {}; return ev != null && Reject(() => ev.AddEventHandler(instance, handler)); });
            Check("rejection-preserves-own-data", () => instance.Bias == 25 && instance.Extra == 1000);
#if PARENT_REPLACEMENT
            var replacement = (ReplacementParent)boxed;
            Check("replacement-constructor-count", () => ReplacementParent.ConstructorCalls == replacementCalls + 2);
            Check("replacement-fields", () => replacement.ReplacementExtra == 70000000003L && ((ReplacementMarker)replacement.ReplacementReference).Value == 1237);
            Check("replacement-field-owner", () => type.GetField("ReplacementExtra").DeclaringType == expectedParent && type.GetField("ReplacementExtra").ReflectedType == type);
            Check("replacement-field-roundtrip", () => {
                type.GetField("ReplacementExtra").SetValue(instance, 70000000009L);
                return replacement.ReplacementExtra == 70000000009L && instance.Bias == 25 && instance.Extra == 1000;
            });
            Check("replacement-method-invoke", () => (long)type.GetMethod("ReadReplacement").Invoke(instance, null) == 70000000009L);
            Check("replacement-property-roundtrip", () => {
                var property = type.GetProperty("ReplacementProperty"); property.SetValue(instance, 70000000027L, null);
                return property.DeclaringType == expectedParent && property.ReflectedType == type && (long)property.GetValue(instance, null) == 70000000027L;
            });
            Check("replacement-event-roundtrip", () => {
                int count = 0; Action handler = () => ++count; var ev = type.GetEvent("ReplacementEvent");
                ev.AddEventHandler(instance, handler); replacement.RaiseReplacementEvent();
                ev.RemoveEventHandler(instance, handler); replacement.RaiseReplacementEvent();
                return count == 1 && ev.DeclaringType == expectedParent && ev.ReflectedType == type;
            });
            Check("replacement-reference-survives-gc", () => {
                var weak = new WeakReference(replacement.ReplacementReference); GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                return weak.IsAlive && ReferenceEquals(weak.Target, replacement.ReplacementReference) && ((ReplacementMarker)weak.Target).Value == 1237;
            });
            Check("replacement-independent-instances", () => {
                var next = (ReplacementParent)(object)new Processor(); return next.ReplacementExtra == 70000000003L &&
                    replacement.ReplacementExtra == 70000000027L && !ReferenceEquals(next.ReplacementReference, replacement.ReplacementReference);
            });
            Check("replacement-constructor-exception", () => {
                ReplacementParent.ThrowConstruction = true;
                try { new Processor(); return false; }
                catch (InvalidOperationException error) { return error.Message == "replacement-constructor-expected"; }
                finally { ReplacementParent.ThrowConstruction = false; }
            });
#endif
            if (failures != 0) throw new InvalidOperationException("Parent transition failures: " + failures);
            Console.WriteLine("DHE parent transition pass: " + passed.Count); return passed.ToArray();
        }
    }
}
