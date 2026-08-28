using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.ManagedCasesAot
{
    // This type is deliberately ordinary AOT code. The DHE build changes only
    // method bodies; the type layout and every method signature stay stable.
    public class DheDemoCalculator
    {
        // Present in both baseline and current assemblies so Touch can expose
        // a void method's side effect without changing the type layout.
        public static int TouchValue;

        public static int Add(int value)
        {
#if DHE_CURRENT
            return value + 100;
#else
            return value + 1;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Stable(int value)
        {
            return value * 2;
        }

        public static int AddViaStable(int value)
        {
#if DHE_CURRENT
            return Stable(value) + 100;
#else
            return Stable(value) + 1;
#endif
        }

        public static int AddPair(int left, int right)
        {
#if DHE_CURRENT
            return left + right + 100;
#else
            return left + right + 1;
#endif
        }

        public static long Wide(long value)
        {
#if DHE_CURRENT
            return value + 1000L;
#else
            return value + 1L;
#endif
        }

        public static void Touch(int value)
        {
#if DHE_CURRENT
            TouchValue = value + 700;
#else
            TouchValue = value + 7;
#endif
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int InstanceStable(int value)
        {
            return value * 3;
        }

        public int InstanceAdd(int value)
        {
#if DHE_CURRENT
            return value + 200;
#else
            return value + 2;
#endif
        }

        public int InstanceAddViaStable(int value)
        {
#if DHE_CURRENT
            return InstanceStable(value) + 200;
#else
            return InstanceStable(value) + 2;
#endif
        }
    }
}
