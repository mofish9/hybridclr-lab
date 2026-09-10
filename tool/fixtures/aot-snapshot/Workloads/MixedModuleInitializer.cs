using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace HybridCLR.Lab.MixedModule
{
    public static class Initializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            // No test hook is added to a hotfix DLL. These are new assemblies
            // whose compiler-generated <Module> .cctor calls a Base lab hook.
            var callback = Assembly.Load("Assembly-CSharp").GetType("HybridCLR.Lab.Snapshot.MixedTransactionPlayer", true)
                .GetMethod("ModuleInitialized", BindingFlags.Static | BindingFlags.Public);
            try { callback.Invoke(null, new object[] { Assembly.GetExecutingAssembly().GetName().Name }); }
            catch (TargetInvocationException error) { throw error.InnerException; }
        }
    }
}
