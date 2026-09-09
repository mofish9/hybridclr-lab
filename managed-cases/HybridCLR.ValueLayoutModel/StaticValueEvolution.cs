using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.ValueLayout
{
    public struct StaticPayload
    {
        public int Count;
#if DHE_RESOURCE_BASE_NEW || DHE_RESOURCE_CURRENT
        public long Extra;
#endif
#if DHE_RESOURCE_CURRENT
        public object Reference;
#endif
    }
    public struct StaticNested { public StaticPayload Value; public int Tail; }
    public static class StaticValueState
    {
        public static int InitializationCount;
        public static int UnchangedStorage;
        public static StaticPayload Value;
        public static StaticNested Nested;
        static StaticValueState()
        {
            InitializationCount++;
            UnchangedStorage = 101;
            Value = StaticValueEvolution.Create();
            Nested = new StaticNested { Value = Value, Tail = 103 };
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static StaticPayload Copy() => Value;
        public static ref StaticPayload Address() => ref Value;
    }
    public static class StaticGenericOwner<T>
    {
        public static int InitializationCount;
        public static StaticPayload Value;
        static StaticGenericOwner() { InitializationCount++; Value = StaticValueEvolution.Create(); }
    }
    public static class StaticGenericArgument<T>
    {
        public static int InitializationCount;
        public static T Value;
        static StaticGenericArgument() { InitializationCount++; }
        public static void Set(T value) { Value = value; }
        public static T Get() => Value;
    }
    public static class StaticValueEvolution
    {
        public static StaticPayload Create()
        {
            var value = new StaticPayload { Count = 107 };
#if DHE_RESOURCE_BASE_NEW || DHE_RESOURCE_CURRENT
            value.Extra = 41000000009L;
#endif
#if DHE_RESOURCE_CURRENT
            value.Reference = new string('x', 109);
#endif
            return value;
        }
        private static string Describe(StaticPayload value)
        {
            string result = value.Count.ToString();
#if DHE_RESOURCE_BASE_NEW || DHE_RESOURCE_CURRENT
            result += ":" + value.Extra;
#endif
#if DHE_RESOURCE_CURRENT
            result += ":" + (value.Reference == null ? "<null>" : value.Reference.ToString().Length.ToString());
#endif
            return result;
        }
#if DHE_RESOURCE_CURRENT
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference ReplaceForGc()
        {
            StaticValueState.Value = Create();
            return new WeakReference(StaticValueState.Value.Reference);
        }
#endif
        public static string[] Run()
        {
            var result = new List<string>();
            result.Add("static-value-initial=" + Describe(StaticValueState.Copy()));
            StaticPayload copy = StaticValueState.Copy(); copy.Count = 113;
            result.Add("static-value-copy=" + Describe(copy) + "/" + Describe(StaticValueState.Copy()));
            ref StaticPayload address = ref StaticValueState.Address(); address.Count = 127;
#if DHE_RESOURCE_CURRENT
            address.Extra = 43000000013L;
#endif
            result.Add("static-value-ref=" + Describe(StaticValueState.Copy()));
            result.Add("static-value-nested=" + Describe(StaticValueState.Nested.Value) + ":" + StaticValueState.Nested.Tail);
            FieldInfo field = typeof(StaticValueState).GetField("Value");
            result.Add("static-value-reflection=" + Describe((StaticPayload)field.GetValue(null)));
            result.Add("static-value-declaring=" + (field.DeclaringType == typeof(StaticValueState)));
            result.Add("static-value-field-type=" + (field.FieldType == typeof(StaticPayload)));
            object boxed = Create(); typeof(StaticPayload).GetField("Count").SetValue(boxed, 131);
            field.SetValue(null, boxed);
            result.Add("static-value-reflection-set=" + Describe(StaticValueState.Copy()));
#if DHE_RESOURCE_CURRENT
            var weak = ReplaceForGc();
#endif
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            result.Add("static-value-gc=" + Describe(StaticValueState.Copy()));
#if DHE_RESOURCE_CURRENT
            result.Add("static-value-gc-reference=" + weak.IsAlive);
#endif
            address = default(StaticPayload);
            result.Add("static-value-initobj=" + Describe(StaticValueState.Copy()));
            result.Add("static-value-cctor=" + StaticValueState.InitializationCount);
            result.Add("static-value-neighbor=" + StaticValueState.UnchangedStorage);
            result.Add("static-generic-owner=" + Describe(StaticGenericOwner<string>.Value) + "/" + Describe(StaticGenericOwner<int>.Value));
            result.Add("static-generic-owner-count=" + StaticGenericOwner<string>.InitializationCount + ":" + StaticGenericOwner<int>.InitializationCount);
            StaticGenericArgument<StaticPayload>.Set(Create());
            result.Add("static-generic-argument=" + Describe(StaticGenericArgument<StaticPayload>.Get()));
            result.Add("static-generic-argument-reflection=" + Describe((StaticPayload)typeof(StaticGenericArgument<StaticPayload>).GetField("Value").GetValue(null)));
            result.Add("static-generic-argument-count=" + StaticGenericArgument<StaticPayload>.InitializationCount);
            return result.ToArray();
        }
    }
}
