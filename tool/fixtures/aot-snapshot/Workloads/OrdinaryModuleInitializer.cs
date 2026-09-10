using System;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}
namespace HybridCLR.Lab.ValueLayoutNative
{
    public static class OrdinaryModuleState
    {
        public static int Runs;
        [ModuleInitializer]
        public static void Initialize()
        {
            Runs++;
            Console.WriteLine("DHE ordinary module: " + Runs);
        }
    }
}
