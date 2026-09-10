using System;
using System.Collections.Generic;
using System.Reflection;

namespace HybridCLR.Lab.Snapshot
{
    internal sealed class ParentTransitionReceiverCache
    {
        private Type owner, former;
        private object oldReceiver;
        private FieldInfo field, bias, extra;
        private MethodInfo method, raise, fail;
        private PropertyInfo property;
        private EventInfo ev;
        private int events;
        internal static ParentTransitionReceiverCache Capture(Assembly assembly)
        {
            var value = new ParentTransitionReceiverCache();
            value.owner = assembly.GetType("HybridCLR.Lab.VirtualSignatures.Processor", true);
            value.former = value.owner.BaseType;
            if (value.former.FullName != "HybridCLR.Lab.ParentEvolution.ProcessorMiddle")
                throw new InvalidOperationException("Parent transition cache requires the inserted-parent Base.");
            value.oldReceiver = Activator.CreateInstance(value.owner);
            value.field = value.owner.GetField("ParentExtra"); value.bias = value.owner.GetField("Bias"); value.extra = value.owner.GetField("Extra");
            value.method = value.owner.GetMethod("ReadParentExtra"); value.raise = value.owner.GetMethod("RaiseParentEvent");
            value.property = value.owner.GetProperty("ParentProperty"); value.ev = value.owner.GetEvent("ParentEvent"); value.fail = value.owner.GetMethod("Fail");
            if (value.field == null || value.bias == null || value.extra == null || value.method == null || value.raise == null ||
                value.property == null || value.ev == null || value.fail == null) throw new InvalidOperationException("Missing original parent handles.");
            value.field.SetValue(value.oldReceiver, 60000000111L);
            Action handler = () => ++value.events; value.ev.AddEventHandler(value.oldReceiver, handler); value.raise.Invoke(value.oldReceiver, null);
            if (value.events != 1) throw new InvalidOperationException("Base event did not execute.");
            return value;
        }
        private static bool Reject(Action action)
        {
            try { action(); }
            catch (TargetException) { return true; }
            catch (ArgumentException) { return true; }
            return false;
        }
        internal string[] Verify()
        {
            var checks = new List<string>();
            void Check(string name, bool valid)
            {
                if (!valid) throw new InvalidOperationException("Parent transition cache failed: " + name);
                checks.Add(name); Console.WriteLine("DHE parent transition cache check: " + name);
            }
            var current = Activator.CreateInstance(owner);
            Check("logical-type-stable", current.GetType() == owner && oldReceiver.GetType() == owner);
            Check("parent-selection-changed", owner.BaseType != former && owner.GetField("ParentExtra") == null);
            Check("current-own-fields", (int)owner.GetField("Bias").GetValue(current) == 25 && (long)owner.GetField("Extra").GetValue(current) == 1000L);
            Check("cached-field-retains-old-storage", (long)field.GetValue(oldReceiver) == 60000000111L);
            Check("cached-method-retains-old-storage", (long)method.Invoke(oldReceiver, null) == 60000000111L);
            Check("cached-property-retains-old-storage", (long)property.GetValue(oldReceiver, null) == 60000000111L);
            raise.Invoke(oldReceiver, null); Check("cached-event-retains-old-storage", events == 2);
            Check("cached-field-rejects-current", Reject(() => field.GetValue(current)));
            Check("cached-method-rejects-current", Reject(() => method.Invoke(current, null)));
            Check("cached-property-rejects-current", Reject(() => property.GetValue(current, null)));
            Action unexpected = () => { throw new InvalidOperationException("Invalid parent event must not execute."); };
            Check("cached-event-rejects-current", Reject(() => ev.AddEventHandler(current, unexpected)));
            Check("selected-body-rejects-old-receiver", Reject(() => fail.Invoke(oldReceiver, null)));
            bool currentBody = false;
            try { fail.Invoke(current, null); }
            catch (TargetInvocationException error) { currentBody = error.InnerException is InvalidOperationException && error.InnerException.Message == "virtual-signature-expected"; }
            Check("selected-body-accepts-current", currentBody);
            Check("rejection-preserves-both-objects", (long)field.GetValue(oldReceiver) == 60000000111L &&
                (int)bias.GetValue(oldReceiver) == 25 && (long)extra.GetValue(oldReceiver) == 1000L &&
                (int)owner.GetField("Bias").GetValue(current) == 25 && (long)owner.GetField("Extra").GetValue(current) == 1000L && events == 2);
            return checks.ToArray();
        }
    }
}
