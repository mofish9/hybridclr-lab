using System;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
namespace HybridCLR.Lab.ModuleEvolution
{
    public static class ModuleState
    {
        public static int Runs;
        public static int Version;
#if MODULE_CHAIN
        public static int SecondRuns;
        [ModuleInitializer]
        public static void InitializeSecond()
        {
            SecondRuns++;
            Console.WriteLine("DHE second AOT module initializer: " + SecondRuns);
        }
#endif
#if MODULE_CURRENT
        private const int ExpectedVersion = 202;
#else
        private const int ExpectedVersion = 101;
#endif
#if !MODULE_REMOVED
        [ModuleInitializer]
#endif
        public static void Initialize()
        {
            Runs++; Version = ExpectedVersion;
            Console.WriteLine("DHE selected module: " + Version + ":" + Runs);
#if MODULE_PUBLIC_REENTRY
            Exception failure = null;
            var worker = new System.Threading.Thread(() => {
                try
                {
                    if (HybridCLR.DheRuntime.LoadCurrentAssemblyImages(null, null, out var code, out _) || code.ToString() != "DHE_LOAD_IN_PROGRESS")
                        throw new InvalidOperationException("Public reentry did not report in-progress.");
                    bool resetRejected = false;
                    try { HybridCLR.DheRuntime.Reset(); } catch (InvalidOperationException) { resetRejected = true; }
                    if (!resetRejected) throw new InvalidOperationException("Public reentry reset was accepted.");
                    if (HybridCLR.DheRuntime.Initialize(null, null, out _)) throw new InvalidOperationException("Public reentry configuration was accepted.");
                }
                catch (Exception error) { failure = error; }
            }) { IsBackground = true };
            worker.Start(); if (!worker.Join(10000)) throw new TimeoutException("Public reentry deadlocked.");
            if (failure != null) throw failure;
            Console.WriteLine("DHE public reentry pass");
#endif
#if MODULE_FAILURE
            throw new InvalidOperationException("DHE deliberate public module failure");
#endif
        }
        public static void Verify()
        {
#if MODULE_CHAIN
            if (SecondRuns != 1) throw new InvalidOperationException("Second module initializer did not execute exactly once.");
#endif
#if MODULE_REMOVED
            if (Runs != 0 || Version != 0) throw new InvalidOperationException("Removed module initializer executed.");
#else
            if (Runs != 1 || Version != ExpectedVersion) throw new InvalidOperationException("Wrong module initialization version/count: " + Version + ":" + Runs);
#endif
            Console.WriteLine("DHE module evolution pass: " + Version + ":" + Runs);
        }
    }
}
