using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace HybridCLR.Lab.PublicModule
{
    public static class Initializer
    {
        private static int Runs;
        private static void Require(bool value, string name)
        { if (!value) throw new InvalidOperationException("Public module initialization: " + name); }
        [ModuleInitializer]
        public static void Initialize()
        {
            string name = Assembly.GetExecutingAssembly().GetName().Name;
            Console.WriteLine("DHE module begin: " + name);
            Require(Interlocked.Increment(ref Runs) == 1, "once");
            string[] names = { "HybridCLR.TransactionInitializer", "HybridCLR.TransactionPeer", "HybridCLR.ValueLayoutAdded",
                "HybridCLR.ValueLayoutModel", "HybridCLR.ValueLayoutOther", "HybridCLR.ValueLayoutConsumer" };
            var visible = AppDomain.CurrentDomain.GetAssemblies();
            foreach (string peer in names) Require(visible.Count(assembly => assembly.GetName().Name == peer) == 1, "complete-public-graph:" + peer);
            var factory = Assembly.Load("HybridCLR.ValueLayoutModel").GetType("HybridCLR.Lab.ValueLayout.Factory", true);
            object value = factory.GetMethod("Create").Invoke(null, null), marker = new object();
            var extra = value.GetType().GetField("Extra"); var reference = value.GetType().GetField("Reference");
            Require(extra != null && reference != null, "current-fields");
            extra.SetValue(value, 93000000017L); reference.SetValue(value, marker);
            var native = Assembly.Load("HybridCLR.ValueLayoutNative").GetType("HybridCLR.Lab.ValueLayoutNative.NativeBoundary", true);
            object copy = native.GetMethod("FrozenCopyBox").Invoke(null, new[] { value });
            Require(!ReferenceEquals(value, copy) && (long)extra.GetValue(copy) == 93000000017L && ReferenceEquals(reference.GetValue(copy), marker), "ordinary-value-copy");
            Require((int)factory.GetMethod("UnchangedRevision").Invoke(null, null) == 5, "unchanged-entry");
            Exception failure = null;
            var worker = new Thread(() => {
                try
                {
                    Require(Assembly.Load("HybridCLR.TransactionPeer").GetName().Name == "HybridCLR.TransactionPeer", "worker-peer");
                    Require((int)native.GetMethod("FrozenSentinel").Invoke(null, null) == 137, "worker-native");
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            worker.Start(); Require(worker.Join(10000), "metadata-lock-released");
            if (failure != null) throw failure;
            Console.WriteLine("DHE module pass: " + name);
        }
    }
}
