using System;
using System.Runtime.CompilerServices;

namespace HybridCLR.Lab.ModuleEvolution
{
    public static class InlineHotfixCallee
    {
        public static int Value = 7;
        public static int Read()
        {
#if INLINE_CURRENT
            return Value + 1;
#else
            return Value;
#endif
        }
    }
    public static class InlineHotfixCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static int Invoke() { return InlineHotfixCallee.Read() + 10; }
    }
    public static class InlineHotfixCases
    {
        public static void Verify()
        {
#if INLINE_CURRENT
            const int expected = 18;
            const bool changed = true;
#else
            const int expected = 17;
            const bool changed = false;
#endif
            if (HybridCLR.RuntimeApi.IsDifferentialMethodChanged(typeof(InlineHotfixCaller).GetMethod("Invoke")) ||
                HybridCLR.RuntimeApi.IsDifferentialMethodChanged(typeof(InlineHotfixCallee).GetMethod("Read")) != changed)
                throw new InvalidOperationException("Inline caller/callee dispatch selection is wrong.");
            // Finish class initialization before measuring the call itself.
            if (InlineHotfixCaller.Invoke() != expected) throw new InvalidOperationException("Inline hotfix warm call used stale code.");
            HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
            int actual = InlineHotfixCaller.Invoke();
            int aot = HybridCLR.RuntimeApi.GetDifferentialAotEntryCount();
            int interpreted = HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
            if (actual != expected || aot < 2 || interpreted != (changed ? 1 : 0))
                throw new InvalidOperationException("Inline hotfix dispatch failed: " + actual + ":" + aot + ":" + interpreted);
            Console.WriteLine("DHE inline hotfix pass: " + actual + ":" + aot + ":" + interpreted);
        }
    }
}
