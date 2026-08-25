using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterReflectionAsyncAndThreadCases(List<CaseDefinition> cases)
        {
            RegisterCase(cases, "reflection_make_generic_type", "reflection", ReflectionMakeGenericType, "Int32:17", features: new[] { "reflection", "generic-type" });
            RegisterCase(cases, "reflection_make_generic_method", "reflection", ReflectionMakeGenericMethod, "hello:42", features: new[] { "reflection", "generic-method" });
            RegisterCase(cases, "reflection_attribute_read", "reflection", ReflectionAttributeRead, "lab:7", features: new[] { "reflection", "attribute" });
            RegisterCase(cases, "reflection_property_field_access", "reflection", ReflectionPropertyFieldAccess, "21,33", features: new[] { "reflection", "property", "field" });
            RegisterCase(cases, "reflection_enum_metadata", "reflection", ReflectionEnumMetadata, "MetadataByteEnum,1", features: new[] { "reflection", "enum" });
            RegisterCase(cases, "reflection_nested_type", "reflection", ReflectionNestedType, "NestedProbe", features: new[] { "reflection", "nested-type" });
            RegisterCase(cases, "reflection_interface_base_chain", "reflection", ReflectionInterfaceBaseChain, "True,True", features: new[] { "reflection", "base", "interface" });
            RegisterCase(cases, "reflection_constructor_invoke", "reflection", ReflectionConstructorInvoke, "probe:12", features: new[] { "reflection", "constructor" });
            RegisterCase(cases, "reflection_delegate_create", "reflection", ReflectionDelegateCreate, "49", features: new[] { "reflection", "delegate" });
            RegisterCase(cases, "iterator_finally_dispose", "iterator", IteratorFinallyDispose, "1,disposed", features: new[] { "iterator", "finally", "dispose" });
            RegisterCase(cases, "async_state_machine_result", "async", AsyncStateMachineResult, "55", features: new[] { "async", "state-machine" });
            RegisterCase(cases, "async_await_yield", "async", AsyncAwaitYield, "13", features: new[] { "async", "await", "yield" });
            RegisterCase(cases, "task_when_all_order", "async", TaskWhenAllOrder, "3,5,7", features: new[] { "task", "when-all" });
            RegisterCase(cases, "thread_name_and_join", "thread", ThreadNameAndJoin, "worker:24", features: new[] { "thread", "join" });
            RegisterCase(cases, "thread_interlocked_exchange", "thread", ThreadInterlockedExchange, "9,4", features: new[] { "thread", "interlocked", "exchange" });
            RegisterCase(cases, "weak_reference_lifecycle", "runtime", WeakReferenceLifecycle, "True", features: new[] { "gc", "weak-reference" });
        }

        private static CaseObservation ReflectionMakeGenericType()
        {
            Type generic = typeof(ReflectionBox<>).MakeGenericType(typeof(int));
            object instance = Activator.CreateInstance(generic, new object[] { 17 })!;
            PropertyInfo property = generic.GetProperty(nameof(ReflectionBox<int>.Value))!;
            return new CaseObservation(generic.GetGenericArguments()[0].Name + ":" + property.GetValue(instance));
        }

        private static CaseObservation ReflectionMakeGenericMethod()
        {
            MethodInfo method = typeof(ReflectionMethods).GetMethod(nameof(ReflectionMethods.Echo))!;
            MethodInfo closed = method.MakeGenericMethod(typeof(int));
            object value = closed.Invoke(null, new object[] { 42 })!;
            MethodInfo textMethod = method.MakeGenericMethod(typeof(string));
            object text = textMethod.Invoke(null, new object[] { "hello" })!;
            return new CaseObservation(text + ":" + value);
        }

        private static CaseObservation ReflectionAttributeRead()
        {
            LabMarkerAttribute attribute = (LabMarkerAttribute)typeof(MetadataReflectionTarget).GetCustomAttribute(typeof(LabMarkerAttribute))!;
            return new CaseObservation(attribute.Name + ":" + attribute.Version.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation ReflectionPropertyFieldAccess()
        {
            MetadataReflectionTarget target = new MetadataReflectionTarget(21);
            FieldInfo field = typeof(MetadataReflectionTarget).GetField(nameof(MetadataReflectionTarget.MutableField))!;
            PropertyInfo property = typeof(MetadataReflectionTarget).GetProperty(nameof(MetadataReflectionTarget.Value))!;
            field.SetValue(target, 33);
            return Observation((int)property.GetValue(target)!, target.MutableField);
        }

        private static CaseObservation ReflectionEnumMetadata()
        {
            Type type = typeof(MetadataByteEnum);
            Array values = Enum.GetValues(type);
            return new CaseObservation(type.Name + "," + values.Length.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation ReflectionNestedType()
        {
            Type nested = typeof(MetadataReflectionTarget).GetNestedType("NestedProbe", BindingFlags.NonPublic)!;
            return new CaseObservation(nested.Name);
        }

        private static CaseObservation ReflectionInterfaceBaseChain()
        {
            Type type = typeof(MetadataReflectionTarget);
            return new CaseObservation(FormattableString.Invariant($"{typeof(ReflectionBase).IsAssignableFrom(type)},{typeof(IReflectionMarker).IsAssignableFrom(type)}"));
        }

        private static CaseObservation ReflectionConstructorInvoke()
        {
            ConstructorInfo constructor = typeof(MetadataReflectionTarget).GetConstructor(new[] { typeof(int) })!;
            MetadataReflectionTarget target = (MetadataReflectionTarget)constructor.Invoke(new object[] { 12 });
            return new CaseObservation("probe:" + target.Value.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation ReflectionDelegateCreate()
        {
            MethodInfo method = typeof(ReflectionMethods).GetMethod(nameof(ReflectionMethods.Add))!;
            Func<int, int, int> function = (Func<int, int, int>)Delegate.CreateDelegate(typeof(Func<int, int, int>), method);
            return Observation(function(20, 29));
        }

        private static CaseObservation IteratorFinallyDispose()
        {
            IteratorProbe probe = new IteratorProbe();
            int first;
            using (IEnumerator<int> enumerator = probe.Run().GetEnumerator())
            {
                enumerator.MoveNext();
                first = enumerator.Current;
            }

            return new CaseObservation(first.ToString(CultureInfo.InvariantCulture) + "," + probe.Disposed);
        }

        private static CaseObservation AsyncStateMachineResult()
        {
            return Observation(AsyncValue().GetAwaiter().GetResult());
        }

        private static async Task<int> AsyncValue()
        {
            int value = 21;
            await Task.CompletedTask;
            value += 34;
            return value;
        }

        private static CaseObservation AsyncAwaitYield()
        {
            // Run off Unity's synchronization context so the synchronous test harness
            // can observe Task.Yield without blocking the continuation it schedules.
            return Observation(Task.Run(() => AsyncYieldValue().GetAwaiter().GetResult()).GetAwaiter().GetResult());
        }

        private static async Task<int> AsyncYieldValue()
        {
            await Task.Yield();
            return 13;
        }

        private static CaseObservation TaskWhenAllOrder()
        {
            Task<int[]> task = Task.WhenAll(Task.FromResult(3), Task.FromResult(5), Task.FromResult(7));
            return new CaseObservation(string.Join(",", task.GetAwaiter().GetResult()));
        }

        private static CaseObservation ThreadNameAndJoin()
        {
            string result = string.Empty;
            Thread worker = new Thread(() =>
            {
                Thread.CurrentThread.Name = "worker";
                result = Thread.CurrentThread.Name + ":24";
            });
            worker.Start();
            worker.Join();
            return new CaseObservation(result);
        }

        private static CaseObservation ThreadInterlockedExchange()
        {
            int value = 4;
            int old = Interlocked.Exchange(ref value, 9);
            return Observation(value, old);
        }

        private static CaseObservation WeakReferenceLifecycle()
        {
            object target = new object();
            WeakReference reference = new WeakReference(target);
            bool alive = reference.IsAlive;
            GC.KeepAlive(target);
            return new CaseObservation(alive.ToString());
        }

        [LabMarker("lab", 7)]
        private class MetadataReflectionTarget : ReflectionBase, IReflectionMarker
        {
            public MetadataReflectionTarget() : this(0)
            {
            }

            public MetadataReflectionTarget(int value)
            {
                Value = value;
                MutableField = 0;
            }

            public int Value { get; }

            public int MutableField;

            private sealed class NestedProbe
            {
            }
        }

        private abstract class ReflectionBase
        {
        }

        private interface IReflectionMarker
        {
        }

        private sealed class ReflectionBox<T>
        {
            public ReflectionBox(T value)
            {
                Value = value;
            }

            public T Value { get; }
        }

        private static class ReflectionMethods
        {
            public static T Echo<T>(T value)
            {
                return value;
            }

            public static int Add(int left, int right)
            {
                return left + right;
            }
        }

        [AttributeUsage(AttributeTargets.Class)]
        private sealed class LabMarkerAttribute : Attribute
        {
            public LabMarkerAttribute(string name, int version)
            {
                Name = name;
                Version = version;
            }

            public string Name { get; }

            public int Version { get; }
        }

        private sealed class IteratorProbe
        {
            public string Disposed { get; private set; } = "not-disposed";

            public IEnumerable<int> Run()
            {
                try
                {
                    yield return 1;
                    yield return 2;
                }
                finally
                {
                    Disposed = "disposed";
                }
            }
        }

        private enum MetadataByteEnum : byte
        {
            One = 1,
        }
    }
}
