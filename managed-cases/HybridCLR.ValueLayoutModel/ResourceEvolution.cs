using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace HybridCLR.Lab.ValueLayout
{
    public class ResourceEntity
    {
        public int Value = 7;
#if DHE_RESOURCE_BASE_NEW || DHE_RESOURCE_CURRENT
        public long Extra = 11000000003L;
#endif
#if DHE_RESOURCE_CURRENT
        public object AddedReference = "current-reference";
        public int AddedMethod(int offset = 3) => Value + offset;
        public string AddedDefaults(string text = "hello", object value = null, DayOfWeek day = DayOfWeek.Wednesday,
            long number = 31000000007L, bool flag = true) => text;
        public T AddedGenericDefault<T>(T value = default(T)) => value;
#endif
        public int ExistingDefault(int offset =
#if DHE_RESOURCE_CURRENT
            17
#else
            2
#endif
        ) => Value + offset;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public virtual long Compute()
        {
#if DHE_RESOURCE_CURRENT
            return Value + Extra + AddedReference.ToString().Length;
#elif DHE_RESOURCE_BASE_NEW
            return Value + Extra;
#else
            return Value;
#endif
        }
    }
    public sealed class ResourceChild : ResourceEntity
    {
        public override long Compute() => base.Compute() + 5;
    }
    public static class ResourceStaticState
    {
        public static int InitializationCount;
        public static int UnchangedStorage;
#if DHE_RESOURCE_CURRENT
        public static long AddedStorage;
#endif
        static ResourceStaticState()
        {
            InitializationCount++;
            UnchangedStorage = 29;
#if DHE_RESOURCE_CURRENT
            AddedStorage = 31000000007L;
#endif
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int ReadUnchangedStorage() => UnchangedStorage;
    }
    public static class ResourceInitCounters
    {
        public static int FailureCount;
        public static int ConcurrentCount;
    }
    public static class ResourceRecursiveState
    {
        public static int Count;
        public static int RecursiveRead;
#if DHE_RESOURCE_CURRENT
        public static long Added;
#endif
        static ResourceRecursiveState()
        {
            Count++;
#if DHE_RESOURCE_CURRENT
            typeof(ResourceRecursiveState).GetField("Added").SetValue(null, 37L);
#endif
            RecursiveRead = Read();
        }
        public static int Read() => Count;
    }
    public static class ResourceFailingState
    {
        public static int Value;
#if DHE_RESOURCE_CURRENT
        public static long Added;
#endif
        static ResourceFailingState()
        {
            ResourceInitCounters.FailureCount++;
#if DHE_RESOURCE_CURRENT
            Added = 41;
#endif
            throw new InvalidOperationException("resource-cctor-failure");
        }
        public static int Read() => Value;
    }
    public static class ResourceConcurrentState
    {
        public static int Value;
#if DHE_RESOURCE_CURRENT
        public static long Added;
#endif
        static ResourceConcurrentState()
        {
            Interlocked.Increment(ref ResourceInitCounters.ConcurrentCount);
            Thread.Sleep(20);
#if DHE_RESOURCE_CURRENT
            Added = 43;
#endif
            Value = 47;
        }
        public static int Read() => Value;
    }
    public static class ResourceGenericState<T>
    {
        public static int Count;
#if DHE_RESOURCE_CURRENT
        public static long Added;
#endif
        static ResourceGenericState()
        {
            Count++;
#if DHE_RESOURCE_CURRENT
            Added = 53;
#endif
        }
        public static int Read() => Count;
    }
#if DHE_RESOURCE_CURRENT
    public sealed class AddedResourceType { public ResourceEntity Value = new ResourceEntity(); }
#endif
    public static class ResourceEvolutionProbe
    {
        public static string[] Run()
        {
            var records = new List<string> { "revision=" + Factory.GetRevision() };
            var entity = new ResourceEntity();
            records.Add("virtual=" + entity.Compute());
            records.Add("derived-virtual=" + new ResourceChild().Compute());
            var fields = new List<string>();
            foreach (var field in typeof(ResourceEntity).GetFields(BindingFlags.Public | BindingFlags.Instance)) fields.Add(field.Name);
            fields.Sort(StringComparer.Ordinal);
            records.Add("fields=" + string.Join(",", fields));
            records.Add("reference-type=" + entity.GetType().FullName);
            records.Add("static-unchanged=" + ResourceStaticState.ReadUnchangedStorage());
            var existingParameter = typeof(ResourceEntity).GetMethod("ExistingDefault").GetParameters()[0];
            records.Add("existing-default=" + existingParameter.DefaultValue);
#if DHE_RESOURCE_CURRENT
            records.Add("added-field=" + entity.Extra + ":" + entity.AddedReference);
            var method = typeof(ResourceEntity).GetMethod("AddedMethod");
            records.Add("added-method=" + method.Invoke(entity, new object[] { 11 }));
            records.Add("optional-default=" + method.GetParameters()[0].DefaultValue);
            records.Add("parameter-identity=" + (method.GetParameters()[0].Member == method));
            foreach (var parameter in typeof(ResourceEntity).GetMethod("AddedDefaults").GetParameters())
                records.Add("default-" + parameter.Name + "=" + (parameter.DefaultValue ?? "<null>"));
            records.Add("generic-default=" + (typeof(ResourceEntity).GetMethod("AddedGenericDefault")
                .MakeGenericMethod(typeof(string)).GetParameters()[0].DefaultValue ?? "<null>"));
            records.Add("added-type=" + new AddedResourceType().Value.Compute());
            records.Add("static-added=" + ResourceStaticState.AddedStorage);
#elif DHE_RESOURCE_BASE_NEW
            records.Add("added-field=" + entity.Extra);
#endif
            records.Add("static-initializations=" + ResourceStaticState.InitializationCount);
            records.Add("static-reflection=" + typeof(ResourceStaticState).GetField("InitializationCount").GetValue(null));
            records.Add("recursive=" + ResourceRecursiveState.Read() + ":" + ResourceRecursiveState.RecursiveRead);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                try { ResourceFailingState.Read(); records.Add("cctor-failure=missing"); }
                catch (TypeInitializationException error) { records.Add("cctor-failure=" + error.InnerException.Message); }
            }
            records.Add("cctor-failure-count=" + ResourceInitCounters.FailureCount);
            var threads = new Thread[8];
            var values = new long[8];
            var errors = new string[8];
            var start = new ManualResetEvent(false);
            for (int index = 0; index < threads.Length; index++)
            {
                int slot = index;
                threads[index] = new Thread(() =>
                {
                    start.WaitOne();
                    try
                    {
#if DHE_RESOURCE_CURRENT
                        values[slot] = slot % 2 == 0 ? ResourceConcurrentState.Read()
                            : (long)typeof(ResourceConcurrentState).GetField("Added").GetValue(null);
#else
                        values[slot] = ResourceConcurrentState.Read();
#endif
                    }
                    catch (Exception error) { errors[slot] = error.GetType().FullName; }
                });
                threads[index].Start();
            }
            start.Set();
            foreach (var thread in threads) if (!thread.Join(10000)) throw new TimeoutException("resource initialization");
            start.Dispose();
            records.Add("concurrent-values=" + string.Join(",", values));
            records.Add("concurrent-errors=" + string.Join(",", errors));
            records.Add("concurrent-count=" + ResourceInitCounters.ConcurrentCount);
            records.Add("generic-count=" + ResourceGenericState<string>.Read() + ":" + ResourceGenericState<int>.Read());
#if DHE_RESOURCE_CURRENT
            records.Add("generic-added=" + ResourceGenericState<string>.Added + ":" + ResourceGenericState<int>.Added);
#endif
            records.Add("sentinel=" + Factory.UnchangedRevision());
            return records.ToArray();
        }
    }
}
