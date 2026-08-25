using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterExceptionAndRuntimeCases(List<CaseDefinition> cases)
        {
            RegisterCase(cases, "multiple_exception_catches", "exception", MultipleExceptionCatches, "argument", features: new[] { "catch", "type-test" });
            RegisterCase(cases, "finally_continue", "exception", FinallyContinue, "9", features: new[] { "finally", "continue" });
            RegisterCase(cases, "using_dispose_order", "exception", UsingDisposeOrder, "body,resource-dispose", features: new[] { "using", "dispose" });
            RegisterCase(cases, "custom_exception_catch", "exception", CustomExceptionCatch, "custom:77", features: new[] { "custom-exception", "catch" });
            RegisterCase(cases, "deferred_iterator_exception", "exception", DeferredIteratorException, "InvalidOperationException", features: new[] { "iterator", "exception" });
            RegisterCase(cases, "async_exception_unwrap", "async", AsyncExceptionUnwrap, "InvalidOperationException", features: new[] { "async", "exception" });
            RegisterCase(cases, "task_continuation_order", "async", TaskContinuationOrder, "A,B,C", features: new[] { "task", "continuation" });
            RegisterCase(cases, "null_reference_catch", "exception", NullReferenceCatch, "null", features: new[] { "nullref", "catch" });
            RegisterCase(cases, "invalid_cast_catch", "exception", InvalidCastCatch, "cast", features: new[] { "castclass", "catch" });
            RegisterCase(cases, "divide_by_zero_catch", "exception", DivideByZeroCatch, "divide", features: new[] { "div", "exception" });
            RegisterCase(cases, "index_out_of_range_catch", "exception", IndexOutOfRangeCatch, "index", features: new[] { "array", "exception" });
            RegisterCase(cases, "filter_falls_through", "exception", FilterFallsThrough, "outer", features: new[] { "filter", "catch" });
            RegisterCase(cases, "finally_replaces_exception", "exception", FinallyReplacesException, "finally", features: new[] { "finally", "unwind" });
            RegisterCase(cases, "nested_using_dispose_order", "exception", NestedUsingDisposeOrder, "inner,inner-dispose,outer-dispose", features: new[] { "using", "nested-finally" });
            RegisterCase(cases, "monitor_lock_mutation", "thread", MonitorLockMutation, "3", features: new[] { "monitor", "lock" });
            RegisterCase(cases, "interlocked_increment", "thread", InterlockedIncrement, "8", features: new[] { "interlocked", "thread" });
            RegisterCase(cases, "thread_static_isolation", "thread", ThreadStaticIsolation, "0,9", features: new[] { "thread-static", "thread" });
            RegisterCase(cases, "volatile_visibility", "thread", VolatileVisibility, "1", features: new[] { "volatile", "thread" });
        }

        private static CaseObservation MultipleExceptionCatches()
        {
            try
            {
                throw new ArgumentException("argument");
            }
            catch (InvalidOperationException)
            {
                return new CaseObservation("invalid");
            }
            catch (ArgumentException)
            {
                return new CaseObservation("argument");
            }
        }

        private static CaseObservation FinallyContinue()
        {
            int total = 0;
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    if (i % 2 == 0) continue;
                    total += i;
                }
                finally
                {
                    total++;
                }
            }

            return Observation(total);
        }

        private static CaseObservation UsingDisposeOrder()
        {
            List<string> trace = new List<string>();
            using (TraceResource resource = new TraceResource(trace, "resource"))
            {
                trace.Add("body");
            }

            return new CaseObservation(string.Join(",", trace));
        }

        private static CaseObservation CustomExceptionCatch()
        {
            try
            {
                throw new LabException(77);
            }
            catch (LabException exception)
            {
                return new CaseObservation("custom:" + exception.Code.ToString(CultureInfo.InvariantCulture));
            }
        }

        private static CaseObservation DeferredIteratorException()
        {
            try
            {
                foreach (int ignored in ThrowingSequence())
                {
                    _ = ignored;
                }
            }
            catch (Exception exception)
            {
                return new CaseObservation(exception.GetType().Name);
            }

            return new CaseObservation("none");
        }

        private static IEnumerable<int> ThrowingSequence()
        {
            yield return 1;
            throw new InvalidOperationException("iterator");
        }

        private static CaseObservation AsyncExceptionUnwrap()
        {
            try
            {
                ThrowAsync().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                return new CaseObservation(exception.GetType().Name);
            }

            return new CaseObservation("none");
        }

        private static async Task ThrowAsync()
        {
            await Task.CompletedTask;
            throw new InvalidOperationException("async");
        }

        private static CaseObservation TaskContinuationOrder()
        {
            List<string> trace = new List<string>();
            Task<int> task = Task.FromResult(4);
            Task<int> first = task.ContinueWith(completed =>
            {
                trace.Add("A");
                return completed.Result + 1;
            });
            Task second = first.ContinueWith(completed => trace.Add("B"));
            second.GetAwaiter().GetResult();
            trace.Add("C");
            return new CaseObservation(string.Join(",", trace));
        }

        private static CaseObservation NullReferenceCatch()
        {
            try
            {
                string? value = null;
                _ = value!.Length;
            }
            catch (NullReferenceException)
            {
                return new CaseObservation("null");
            }

            return new CaseObservation("none");
        }

        private static CaseObservation InvalidCastCatch()
        {
            try
            {
                object value = "text";
                _ = (int)value;
            }
            catch (InvalidCastException)
            {
                return new CaseObservation("cast");
            }

            return new CaseObservation("none");
        }

        private static CaseObservation DivideByZeroCatch()
        {
            try
            {
                int zero = 0;
                _ = 10 / zero;
            }
            catch (DivideByZeroException)
            {
                return new CaseObservation("divide");
            }

            return new CaseObservation("none");
        }

        private static CaseObservation IndexOutOfRangeCatch()
        {
            try
            {
                int[] values = new int[1];
                _ = values[2];
            }
            catch (IndexOutOfRangeException)
            {
                return new CaseObservation("index");
            }

            return new CaseObservation("none");
        }

        private static CaseObservation FilterFallsThrough()
        {
            try
            {
                try
                {
                    throw new ArgumentException("filter");
                }
                catch (ArgumentException exception) when (IsNeverTrue(exception))
                {
                    return new CaseObservation("wrong");
                }
            }
            catch (ArgumentException)
            {
                return new CaseObservation("outer");
            }

        }

        private static bool IsNeverTrue(Exception exception)
        {
            return exception.Message == "impossible";
        }

        private static CaseObservation FinallyReplacesException()
        {
            try
            {
                try
                {
                    throw new InvalidOperationException("original");
                }
                finally
                {
                    throw new ArgumentException("finally");
                }
            }
            catch (ArgumentException)
            {
                return new CaseObservation("finally");
            }
        }

        private static CaseObservation NestedUsingDisposeOrder()
        {
            List<string> trace = new List<string>();
            using (TraceResource outer = new TraceResource(trace, "outer"))
            using (TraceResource inner = new TraceResource(trace, "inner"))
            {
                trace.Add("inner");
            }

            return new CaseObservation(string.Join(",", trace));
        }

        private static CaseObservation MonitorLockMutation()
        {
            object gate = new object();
            int value = 0;
            lock (gate)
            {
                value++;
                lock (gate)
                {
                    value += 2;
                }
            }

            return Observation(value);
        }

        private static CaseObservation InterlockedIncrement()
        {
            int value = 0;
            for (int i = 0; i < 8; i++) Interlocked.Increment(ref value);
            return Observation(value);
        }

        [ThreadStatic]
        private static int ThreadStaticValue;

        private static CaseObservation ThreadStaticIsolation()
        {
            ThreadStaticValue = 9;
            int main = ThreadStaticValue;
            int worker = -1;
            Thread thread = new Thread(() => worker = ThreadStaticValue);
            thread.Start();
            thread.Join();
            return Observation(worker, main);
        }

        private static CaseObservation VolatileVisibility()
        {
            int value = 0;
            Thread thread = new Thread(() => Volatile.Write(ref value, 1));
            thread.Start();
            thread.Join();
            return Observation(Volatile.Read(ref value));
        }

        private sealed class TraceResource : IDisposable
        {
            private readonly List<string> _trace;
            private readonly string _name;

            public TraceResource(List<string> trace, string name)
            {
                _trace = trace;
                _name = name;
            }

            public void Dispose()
            {
                _trace.Add(_name + "-dispose");
            }
        }

        private sealed class LabException : Exception
        {
            public LabException(int code) : base("lab")
            {
                Code = code;
            }

            public int Code { get; }
        }
    }
}
