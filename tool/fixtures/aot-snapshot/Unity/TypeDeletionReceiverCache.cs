using System;
using System.Collections.Generic;
using System.Reflection;

namespace HybridCLR.Lab.Snapshot
{
    internal sealed class TypeDeletionReceiverCache
    {
        private Assembly assembly;
        private Type owner, former, markerType;
        private object oldReceiver;
        private FieldInfo field, reference, bias, extra;
        private MethodInfo method, raise, fail;
        private PropertyInfo property;
        private EventInfo ev;
        private ConstructorInfo markerConstructor;
        private Func<long> reader;
        private Action handler;
        private int events;
        private const long Original = 60000000111L;

        internal static TypeDeletionReceiverCache Capture(Assembly assembly)
        {
            var value = new TypeDeletionReceiverCache { assembly = assembly };
            value.owner = assembly.GetType("HybridCLR.Lab.VirtualSignatures.Processor", true);
            value.former = value.owner.BaseType;
            if (value.former.FullName != "HybridCLR.Lab.ParentEvolution.ProcessorMiddle")
                throw new InvalidOperationException("Deletion cache requires an existing-parent Base.");
            value.markerType = assembly.GetType("HybridCLR.Lab.ParentEvolution.ParentMarker", true);
            value.oldReceiver = Activator.CreateInstance(value.owner);
            value.field = value.owner.GetField("ParentExtra"); value.reference = value.owner.GetField("ParentReference");
            value.bias = value.owner.GetField("Bias"); value.extra = value.owner.GetField("Extra");
            value.method = value.owner.GetMethod("ReadParentExtra"); value.raise = value.owner.GetMethod("RaiseParentEvent");
            value.fail = value.owner.GetMethod("Fail"); value.property = value.owner.GetProperty("ParentProperty");
            value.ev = value.owner.GetEvent("ParentEvent"); value.markerConstructor = value.markerType.GetConstructor(Type.EmptyTypes);
            if (value.field == null || value.reference == null || value.bias == null || value.extra == null ||
                value.method == null || value.raise == null || value.fail == null || value.property == null ||
                value.ev == null || value.markerConstructor == null)
                throw new InvalidOperationException("Missing original deletion cache handles.");
            value.field.SetValue(value.oldReceiver, Original);
            value.reader = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), value.oldReceiver, value.method);
            value.handler = () => ++value.events;
            value.ev.AddEventHandler(value.oldReceiver, value.handler); value.raise.Invoke(value.oldReceiver, null);
            if (value.events != 1 || value.reader() != Original || (long)value.method.Invoke(value.oldReceiver, null) != Original ||
                (long)value.property.GetValue(value.oldReceiver, null) != Original ||
                value.markerConstructor.Invoke(null).GetType() != value.markerType || value.reference.GetValue(value.oldReceiver) == null)
                throw new InvalidOperationException("Original deletion capture did not execute its Base operations.");
            Console.WriteLine("DHE type deletion cache captured: original parent, object, fields, method, delegate, property, event, constructor");
            return value;
        }

        private static bool Deleted(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                while (error is TargetInvocationException && error.InnerException != null) error = error.InnerException;
                if (error is MissingMethodException) return true;
                throw;
            }
            return false;
        }
        private static bool WrongReceiver(Action action)
        {
            try { action(); }
            catch (TargetException) { return true; }
            catch (ArgumentException) { return true; }
            return false;
        }

        internal string[] Verify()
        {
            var checks = new List<string>(); int failures = 0;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    checks.Add(name); Console.WriteLine("DHE type deletion cache check: " + name);
                }
                catch (Exception error) { ++failures; Console.WriteLine("DHE type deletion cache failure: " + name + ": " + error); }
            }
            var current = Activator.CreateInstance(owner);
            Check("cached-type-name-stable", () => former.FullName == "HybridCLR.Lab.ParentEvolution.ProcessorMiddle" &&
                markerType.FullName == "HybridCLR.Lab.ParentEvolution.ParentMarker");
            Check("fresh-lookup-absent", () => assembly.GetType(former.FullName) == null && Type.GetType(former.AssemblyQualifiedName) == null);
            Check("cached-child-type-stable", () => oldReceiver.GetType() == owner && current.GetType() == owner);
            Check("logical-parent-removed", () => owner.BaseType.FullName == "HybridCLR.Lab.VirtualSignatures.ProcessorRoot" && !former.IsAssignableFrom(owner));
            Check("physical-parent-retained", () => former.IsInstanceOfType(oldReceiver));
            Check("current-parent-rejected", () => !former.IsInstanceOfType(current));
            Check("cached-field-owner-stable", () => field.DeclaringType == former && field.ReflectedType == owner);
            Check("cached-field-read-old", () => (long)field.GetValue(oldReceiver) == Original);
            Check("cached-field-write-old", () => { field.SetValue(oldReceiver, Original + 1); return (long)field.GetValue(oldReceiver) == Original + 1; });
            Check("cached-field-rejects-current", () => WrongReceiver(() => field.GetValue(current)));
            Check("cached-field-write-rejects-current", () => WrongReceiver(() => field.SetValue(current, Original + 2)));
            Check("cached-field-rejects-unrelated", () => WrongReceiver(() => field.GetValue(new object())));
            Check("cached-method-removed", () => Deleted(() => method.Invoke(oldReceiver, null)));
            Check("cached-delegate-removed", () => Deleted(() => reader()));
            Check("cached-property-get-removed", () => Deleted(() => property.GetValue(oldReceiver, null)));
            Check("cached-property-set-removed", () => Deleted(() => property.SetValue(oldReceiver, Original + 3, null)));
            Check("cached-event-add-removed", () => Deleted(() => ev.AddEventHandler(oldReceiver, new Action(() => events += 1000))));
            Check("cached-event-remove-removed", () => Deleted(() => ev.RemoveEventHandler(oldReceiver, handler)));
            Check("cached-event-raise-removed", () => Deleted(() => raise.Invoke(oldReceiver, null)));
            Check("cached-constructor-removed", () => Deleted(() => markerConstructor.Invoke(null)));
            Check("fresh-removed-members-absent", () => owner.GetField("ParentExtra") == null && owner.GetMethod("ReadParentExtra") == null &&
                owner.GetProperty("ParentProperty") == null && owner.GetEvent("ParentEvent") == null);
            Check("old-own-fields-retained", () => (int)bias.GetValue(oldReceiver) == 25 && (long)extra.GetValue(oldReceiver) == 1000L);
            Check("current-own-fields", () => (int)owner.GetField("Bias").GetValue(current) == 25 && (long)owner.GetField("Extra").GetValue(current) == 1000L);
            Check("changed-body-rejects-old", () => WrongReceiver(() => fail.Invoke(oldReceiver, null)));
            Check("changed-body-accepts-current", () => {
                try { fail.Invoke(current, null); }
                catch (TargetInvocationException error) { return error.InnerException is InvalidOperationException && error.InnerException.Message == "virtual-signature-expected"; }
                return false;
            });
            Check("deleted-reference-survives-gc", () => {
                var weak = new WeakReference(reference.GetValue(oldReceiver));
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                return weak.IsAlive && ReferenceEquals(weak.Target, reference.GetValue(oldReceiver)) && weak.Target.GetType() == markerType;
            });
            Check("removed-marker-lookup-absent", () => assembly.GetType(markerType.FullName) == null);
            Check("base-event-effects-stable", () => events == 1);
            Check("old-field-restored", () => {
                field.SetValue(oldReceiver, Original);
                return (long)field.GetValue(oldReceiver) == Original && (int)bias.GetValue(oldReceiver) == 25 &&
                    (long)extra.GetValue(oldReceiver) == 1000L && (int)owner.GetField("Bias").GetValue(current) == 25 &&
                    (long)owner.GetField("Extra").GetValue(current) == 1000L;
            });
            Check("unchanged-aot-sentinel", () => ValueLayout.Factory.UnchangedRevision() == 5 &&
                !RuntimeApi.IsDifferentialMethodChanged(typeof(ValueLayout.Factory).GetMethod("UnchangedRevision")));
            if (failures != 0) throw new InvalidOperationException("Type deletion cache failures: " + failures);
            return checks.ToArray();
        }
    }
}
