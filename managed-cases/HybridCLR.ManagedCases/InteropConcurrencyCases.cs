using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterInteropConcurrencyCases(List<CaseDefinition> cases)
        {
            RegisterCase(cases, "pinvoke_process_id", "interop", PInvokeProcessId, "True", features: new[] { "p-invoke", "native-call" });
            RegisterCase(cases, "reverse_pinvoke_qsort", "interop", ReversePInvokeQsort, "1,2,3,4", features: new[] { "reverse-p-invoke", "delegate", "native-call" });
            RegisterCase(cases, "delegate_instance_target_lifetime", "runtime", DelegateInstanceTargetLifetime, "43,True", features: new[] { "delegate", "gc", "target-lifetime" });
            RegisterCase(cases, "delegate_multicast_remove", "dispatch", DelegateMulticastRemove, "B", features: new[] { "delegate", "multicast", "remove" });
            RegisterCase(cases, "array_covariance_type_mismatch", "array", ArrayCovarianceTypeMismatch, "mismatch", features: new[] { "array", "covariance", "stelem", "exception" });
            RegisterCase(cases, "array_null_reference_roundtrip", "array", ArrayNullReferenceRoundtrip, "True,hotfix", features: new[] { "array", "reference", "null" });
            RegisterCase(cases, "async_multiple_awaits", "async", AsyncMultipleAwaits, "30", features: new[] { "async", "await", "state-machine", "multi-await" });
            RegisterCase(cases, "async_when_any_completed", "async", AsyncWhenAnyCompleted, "9", features: new[] { "async", "task", "when-any" });
            RegisterCase(cases, "async_cancellation_propagation", "async", AsyncCancellationPropagation, "canceled", features: new[] { "async", "cancellation", "exception" });
            RegisterCase(cases, "custom_awaiter", "async", CustomAwaiterCase, "37", features: new[] { "async", "awaiter", "state-machine" });
            RegisterCase(cases, "iterator_yield_break_cleanup", "iterator", IteratorYieldBreakCleanup, "1,disposed", features: new[] { "iterator", "yield-break", "finally", "dispose" });
            RegisterCase(cases, "nested_finally_leave", "exception", NestedFinallyLeave, "27,inner,outer", features: new[] { "exception", "finally", "leave", "nested" });
            RegisterCase(cases, "concurrent_interpreter_reentry", "thread", ConcurrentInterpreterReentry, "148", features: new[] { "thread", "task", "concurrent", "interpreter-reentry" });
            RegisterCase(cases, "concurrent_generic_reentry", "thread", ConcurrentGenericReentry, "196", features: new[] { "thread", "task", "concurrent", "generic-sharing" });
            RegisterCase(cases, "reflection_private_member", "reflection", ReflectionPrivateMember, "_value:31", features: new[] { "reflection", "private-field" });
            RegisterCase(cases, "reflection_invoke_exception", "reflection", ReflectionInvokeException, "InvalidOperationException", features: new[] { "reflection", "invoke", "exception" });
        }

        private static CaseObservation PInvokeProcessId()
        {
            return Observation(GetCurrentProcessIdPlatform() != 0);
        }

#if HYBRIDCLR_TARGET_ANDROID
        [DllImport("libc.so", EntryPoint = "getpid")]
        private static extern int GetCurrentProcessIdUnix();

        [DllImport("__InternalDynamic", EntryPoint = "getpid")]
        private static extern int GetCurrentProcessIdInternal();

        private static uint GetCurrentProcessIdPlatform()
        {
            try
            {
                return unchecked((uint)GetCurrentProcessIdInternal());
            }
            catch (DllNotFoundException)
            {
                return unchecked((uint)GetCurrentProcessIdUnix());
            }
            catch (EntryPointNotFoundException)
            {
                return unchecked((uint)GetCurrentProcessIdUnix());
            }
        }
#else
        [DllImport("kernel32.dll", EntryPoint = "GetCurrentProcessId")]
        private static extern uint GetCurrentProcessIdWindows();

        private static uint GetCurrentProcessIdPlatform()
        {
            return GetCurrentProcessIdWindows();
        }
#endif

        private static CaseObservation ReversePInvokeQsort()
        {
            int[] values = { 4, 1, 3, 2 };
            IntPtr buffer = Marshal.AllocHGlobal(values.Length * sizeof(int));
            try
            {
                Marshal.Copy(values, 0, buffer, values.Length);
                CompareCallback callback = CompareInts;
                QsortPlatform(buffer, (UIntPtr)values.Length, (UIntPtr)sizeof(int), Marshal.GetFunctionPointerForDelegate(callback));
                Marshal.Copy(buffer, values, 0, values.Length);
                return new CaseObservation(JoinInts(values));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int CompareCallback(IntPtr left, IntPtr right);

#if HYBRIDCLR_TARGET_ANDROID
        [DllImport("libc.so", EntryPoint = "qsort", CallingConvention = CallingConvention.Cdecl)]
        private static extern void QsortUnix(IntPtr @base, UIntPtr count, UIntPtr size, IntPtr compare);

        [DllImport("__InternalDynamic", EntryPoint = "qsort", CallingConvention = CallingConvention.Cdecl)]
        private static extern void QsortInternal(IntPtr @base, UIntPtr count, UIntPtr size, IntPtr compare);

        private static void QsortPlatform(IntPtr @base, UIntPtr count, UIntPtr size, IntPtr compare)
        {
            try
            {
                QsortInternal(@base, count, size, compare);
            }
            catch (DllNotFoundException)
            {
                QsortUnix(@base, count, size, compare);
            }
            catch (EntryPointNotFoundException)
            {
                QsortUnix(@base, count, size, compare);
            }
        }
#else
        [DllImport("msvcrt.dll", EntryPoint = "qsort", CallingConvention = CallingConvention.Cdecl)]
        private static extern void QsortWindows(IntPtr @base, UIntPtr count, UIntPtr size, IntPtr compare);

        private static void QsortPlatform(IntPtr @base, UIntPtr count, UIntPtr size, IntPtr compare)
        {
            QsortWindows(@base, count, size, compare);
        }
#endif

        [AOT.MonoPInvokeCallback(typeof(CompareCallback))]
        private static int CompareInts(IntPtr left, IntPtr right)
        {
            return Marshal.ReadInt32(left).CompareTo(Marshal.ReadInt32(right));
        }

        private static CaseObservation DelegateInstanceTargetLifetime()
        {
            WeakReference weakTarget;
            Func<int, int> callback = CreateTargetDelegate(out weakTarget);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int value = callback(3);
            return Observation(value, weakTarget.IsAlive);
        }

        private static Func<int, int> CreateTargetDelegate(out WeakReference weakTarget)
        {
            Target target = new Target(40);
            weakTarget = new WeakReference(target);
            return target.Add;
        }

        private static CaseObservation DelegateMulticastRemove()
        {
            List<string> trace = new List<string>();
            Action first = () => trace.Add("A");
            Action second = () => trace.Add("B");
            Action? combined = first + second;
            combined = combined - first;
            combined!();
            return new CaseObservation(string.Join(",", trace));
        }

        private static CaseObservation ArrayCovarianceTypeMismatch()
        {
            try
            {
                object[] values = new string[1];
                values[0] = 7;
            }
            catch (ArrayTypeMismatchException)
            {
                return new CaseObservation("mismatch");
            }

            return new CaseObservation("none");
        }

        private static CaseObservation ArrayNullReferenceRoundtrip()
        {
            string[] values = new string[2];
            values[1] = "hotfix";
            return Observation(values[0] == null, values[1]);
        }

        private static CaseObservation AsyncMultipleAwaits()
        {
            return Observation(Task.Run(() => MultipleAwaitValue().GetAwaiter().GetResult()).GetAwaiter().GetResult());
        }

        private static async Task<int> MultipleAwaitValue()
        {
            int result = await Task.FromResult(10);
            result += await Task.FromResult(20);
            return result;
        }

        private static CaseObservation AsyncWhenAnyCompleted()
        {
            TaskCompletionSource<int> pending = new TaskCompletionSource<int>();
            Task<int> completed = Task.FromResult(9);
            Task<Task<int>> winner = Task.WhenAny(pending.Task, completed);
            return Observation(winner.GetAwaiter().GetResult().GetAwaiter().GetResult());
        }

        private static CaseObservation AsyncCancellationPropagation()
        {
            CancellationTokenSource source = new CancellationTokenSource();
            source.Cancel();
            try
            {
                Task.Delay(1, source.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException exception) when (exception.CancellationToken == source.Token)
            {
                return new CaseObservation("canceled");
            }

            return new CaseObservation("none");
        }

        private static CaseObservation CustomAwaiterCase()
        {
            return Observation(Task.Run(() => AwaitImmediate().GetAwaiter().GetResult()).GetAwaiter().GetResult());
        }

        private static async Task<int> AwaitImmediate()
        {
            return await new ImmediateAwaiter(37);
        }

        private static CaseObservation IteratorYieldBreakCleanup()
        {
            IteratorCleanupProbe probe = new IteratorCleanupProbe();
            int first;
            using (IEnumerator<int> enumerator = probe.Run().GetEnumerator())
            {
                enumerator.MoveNext();
                first = enumerator.Current;
                enumerator.MoveNext();
            }

            return new CaseObservation(first.ToString(CultureInfo.InvariantCulture) + "," + probe.State);
        }

        private static CaseObservation NestedFinallyLeave()
        {
            int state = 0;
            string trace = string.Empty;
            try
            {
                try
                {
                    state += 7;
                    trace += "inner";
                }
                finally
                {
                    state += 20;
                }
            }
            finally
            {
                trace += ",outer";
            }

            return Observation(state, trace);
        }

        private static CaseObservation ConcurrentInterpreterReentry()
        {
            Task<int>[] tasks = new Task<int>[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                int value = i;
                tasks[i] = Task.Run(() => Add(value, 15));
            }

            Task.WaitAll(tasks);
            int total = 0;
            foreach (Task<int> task in tasks) total += task.Result;
            return Observation(total);
        }

        private static CaseObservation ConcurrentGenericReentry()
        {
            Task<int>[] tasks = new Task<int>[8];
            for (int i = 0; i < tasks.Length; i++)
            {
                int value = i + 1;
                tasks[i] = Task.Run(() => GenericAdd(value, 20));
            }

            Task.WaitAll(tasks);
            int total = 0;
            foreach (Task<int> task in tasks) total += task.Result;
            return Observation(total);
        }

        private static T Add<T>(T left, T right) where T : struct
        {
            return (T)(object)((int)(object)left + (int)(object)right);
        }

        private static int GenericAdd<T>(T left, T right) where T : struct
        {
            return (int)(object)Add(left, right);
        }

        private static CaseObservation ReflectionPrivateMember()
        {
            PrivateReflectionTarget target = new PrivateReflectionTarget(31);
            FieldInfo field = typeof(PrivateReflectionTarget).GetField("_value", BindingFlags.Instance | BindingFlags.NonPublic)!;
            return new CaseObservation(field.Name + ":" + field.GetValue(target));
        }

        private static CaseObservation ReflectionInvokeException()
        {
            MethodInfo method = typeof(InteropReflectionMethods).GetMethod("ThrowForReflection", BindingFlags.Static | BindingFlags.NonPublic)!;
            try
            {
                method.Invoke(null, null);
            }
            catch (TargetInvocationException exception)
            {
                return new CaseObservation(exception.InnerException!.GetType().Name);
            }

            return new CaseObservation("none");
        }

        private sealed class Target
        {
            private readonly int _base;

            public Target(int value)
            {
                _base = value;
            }

            public int Add(int value)
            {
                return _base + value;
            }
        }

        private sealed class ImmediateAwaiter : System.Runtime.CompilerServices.INotifyCompletion
        {
            private readonly int _value;

            public ImmediateAwaiter(int value)
            {
                _value = value;
            }

            public bool IsCompleted => true;

            public int GetResult()
            {
                return _value;
            }

            public void OnCompleted(Action continuation)
            {
                continuation();
            }

            public ImmediateAwaiter GetAwaiter()
            {
                return this;
            }
        }

        private sealed class IteratorCleanupProbe
        {
            public string State { get; private set; } = "not-disposed";

            public IEnumerable<int> Run()
            {
                try
                {
                    yield return 1;
                    yield break;
                }
                finally
                {
                    State = "disposed";
                }
            }
        }

        private sealed class PrivateReflectionTarget
        {
            private readonly int _value;

            public PrivateReflectionTarget(int value)
            {
                _value = value;
            }
        }

        private static class InteropReflectionMethods
        {
            private static void ThrowForReflection()
            {
                throw new InvalidOperationException("reflection");
            }
        }

    }
}

namespace AOT
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type delegateType)
        {
            DelegateType = delegateType;
        }

        public Type DelegateType { get; }
    }
}
