using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

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
#endif
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
#if DHE_RESOURCE_CURRENT
            records.Add("added-field=" + entity.Extra + ":" + entity.AddedReference);
            var method = typeof(ResourceEntity).GetMethod("AddedMethod");
            records.Add("added-method=" + method.Invoke(entity, new object[] { 11 }));
            records.Add("optional-default=" + method.GetParameters()[0].DefaultValue);
            records.Add("added-type=" + new AddedResourceType().Value.Compute());
            records.Add("static-added=" + ResourceStaticState.AddedStorage);
#elif DHE_RESOURCE_BASE_NEW
            records.Add("added-field=" + entity.Extra);
#endif
            records.Add("static-initializations=" + ResourceStaticState.InitializationCount);
            records.Add("static-reflection=" + typeof(ResourceStaticState).GetField("InitializationCount").GetValue(null));
            records.Add("sentinel=" + Factory.UnchangedRevision());
            return records.ToArray();
        }
    }
}
