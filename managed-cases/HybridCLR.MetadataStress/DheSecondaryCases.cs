namespace HybridCLR.Lab.MetadataStress
{
    // Small controlled body change kept outside the generated stress source.
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
    }
}
