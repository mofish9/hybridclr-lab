#if DHE_NEGATIVE_FIXTURE
namespace HybridCLR.Lab.ManagedCasesAot
{
    // This fixture is excluded from the normal demo build. The compatibility
    // gate enables it to exercise token, method-set and layout rejection.
    public static class DheTokenDriftCases
    {
#if DHE_TOKEN_DRIFT
        public static int Second(int value) => value + 2;
        public static int First(int value) => value + 1;
#else
        public static int First(int value) => value + 1;
        public static int Second(int value) => value + 2;
#endif
    }

    public sealed class DheLayoutCases
    {
#if DHE_LAYOUT_CHANGE
        public int AddedField;
#endif
        public static int Stable(int value) => value + 3;
    }

    public static class DheFieldConstantCases
    {
#if DHE_FIELD_CONSTANT_CHANGE
        public const int Value = 2;
#else
        public const int Value = 1;
#endif
    }

#if DHE_ADD_METHOD
    public static class DheAddedCases
    {
        public static int Added(int value) => value + 4;
    }
#endif
}
#endif
