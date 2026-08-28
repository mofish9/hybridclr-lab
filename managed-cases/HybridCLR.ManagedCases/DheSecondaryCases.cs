namespace HybridCLR.Lab.ManagedCases
{
    // This assembly is deliberately changed in the current differential image
    // so the demo proves that DHE is not limited to its primary assembly.
    public static class DheSecondaryCases
    {
        public static int Changed(int value)
        {
#if DHE_CURRENT
            return value + 100;
#else
            return value + 10;
#endif
        }

        public static int Unchanged(int value)
        {
            return value * 2;
        }
    }
}
