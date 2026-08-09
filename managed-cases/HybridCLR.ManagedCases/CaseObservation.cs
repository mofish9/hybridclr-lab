namespace HybridCLR.Lab.ManagedCases
{
    public sealed class CaseObservation
    {
        public CaseObservation(string returnValue, string sideEffect = "")
        {
            ReturnValue = returnValue;
            SideEffect = sideEffect;
        }

        public string ReturnValue { get; }

        public string SideEffect { get; }
    }
}

