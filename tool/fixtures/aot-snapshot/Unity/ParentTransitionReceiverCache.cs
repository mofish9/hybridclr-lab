using System;
using System.Collections.Generic;
using System.Reflection;
using System.Globalization;

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
        private sealed class ValidationFields
        {
            public int Value = 7;
            public static int Static;
            [UnityEngine.Scripting.Preserve] public const int Constant = 17;
        }
        private sealed class GenericFields<T>
        {
            [UnityEngine.Scripting.Preserve] public static int Value;
        }
        private sealed class ConversionBinder : Binder
        {
            internal Type Former, Owner;
            internal int Calls;
            internal bool LogicalAncestryUnchanged;
            public override object ChangeType(object value, Type type, CultureInfo culture)
            {
                ++Calls;
                LogicalAncestryUnchanged = !Former.IsAssignableFrom(Owner);
                if ((string)value != "converted" || type != typeof(long)) throw new InvalidOperationException("Unexpected binder conversion.");
                return 60000000222L;
            }
            public override FieldInfo BindToField(BindingFlags flags, FieldInfo[] match, object value, CultureInfo culture) => Type.DefaultBinder.BindToField(flags, match, value, culture);
            public override MethodBase BindToMethod(BindingFlags flags, MethodBase[] match, ref object[] args, ParameterModifier[] modifiers, CultureInfo culture, string[] names, out object state) =>
                Type.DefaultBinder.BindToMethod(flags, match, ref args, modifiers, culture, names, out state);
            public override void ReorderArgumentArray(ref object[] args, object state) => Type.DefaultBinder.ReorderArgumentArray(ref args, state);
            public override MethodBase SelectMethod(BindingFlags flags, MethodBase[] match, Type[] types, ParameterModifier[] modifiers) => Type.DefaultBinder.SelectMethod(flags, match, types, modifiers);
            public override PropertyInfo SelectProperty(BindingFlags flags, PropertyInfo[] match, Type type, Type[] indexes, ParameterModifier[] modifiers) => Type.DefaultBinder.SelectProperty(flags, match, type, indexes, modifiers);
        }
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
        private static bool Throws<T>(Action action) where T : Exception
        {
            try { action(); }
            catch (T) { return true; }
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
            field.SetValue(oldReceiver, 60000000112L);
            Check("cached-field-write-retains-old-storage", (long)field.GetValue(oldReceiver) == 60000000112L);
            Check("cached-field-write-rejects-current", Throws<ArgumentException>(() => field.SetValue(current, 7L)));
            Check("field-read-rejects-unrelated", Throws<ArgumentException>(() => field.GetValue(new object())));
            Check("field-write-rejects-unrelated", Throws<ArgumentException>(() => field.SetValue(new object(), 7L)));
            Check("field-read-null-target", Throws<TargetException>(() => field.GetValue(null)));
            Check("field-write-null-target", Throws<TargetException>(() => field.SetValue(null, 7L)));
            Check("field-write-invalid-value", Throws<ArgumentException>(() => field.SetValue(oldReceiver, "invalid")));
            var binder = new ConversionBinder { Former = former, Owner = owner };
            field.SetValue(oldReceiver, "converted", BindingFlags.Default, binder, CultureInfo.InvariantCulture);
            Check("field-custom-binder-preserved", binder.Calls == 1 && (long)field.GetValue(oldReceiver) == 60000000222L);
            Check("binder-logical-ancestry-unchanged", binder.LogicalAncestryUnchanged && !former.IsAssignableFrom(owner));
            var validation = new ValidationFields(); ValidationFields.Static = 3; GenericFields<int>.Value = 4;
            var staticField = typeof(ValidationFields).GetField("Static");
            staticField.SetValue(new object(), 19);
            Check("static-field-ignores-target", (int)staticField.GetValue(null) == 19 && ValidationFields.Static == 19 && validation.Value == 7);
            var literal = typeof(ValidationFields).GetField("Constant");
            Check("literal-field-read", (int)literal.GetValue(null) == 17);
            Check("literal-field-write-rejected", Throws<FieldAccessException>(() => literal.SetValue(null, 18)));
            var openField = typeof(GenericFields<>).GetField("Value");
            Check("open-generic-field-read-rejected", Throws<InvalidOperationException>(() => openField.GetValue(null)));
            Check("open-generic-field-write-rejected", Throws<InvalidOperationException>(() => openField.SetValue(null, 5)));
            field.SetValue(oldReceiver, 60000000111L);
            Check("validation-preserves-data", (long)field.GetValue(oldReceiver) == 60000000111L && GenericFields<int>.Value == 4 &&
                (int)owner.GetField("Bias").GetValue(current) == 25 && (long)owner.GetField("Extra").GetValue(current) == 1000L);
            return checks.ToArray();
        }
    }
}
