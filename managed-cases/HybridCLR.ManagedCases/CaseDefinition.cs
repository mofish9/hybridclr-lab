using System;

namespace HybridCLR.Lab.ManagedCases
{
    public sealed class CaseDefinition
    {
        public CaseDefinition(
            string id,
            string category,
            Func<CaseObservation> execute,
            string? expectedReturnValue,
            string? expectedSideEffect = "",
            string? expectedExceptionType = null)
        {
            Id = id;
            Category = category;
            Execute = execute;
            ExpectedReturnValue = expectedReturnValue;
            ExpectedSideEffect = expectedSideEffect;
            ExpectedExceptionType = expectedExceptionType;
        }

        public string Id { get; }

        public string Category { get; }

        public Func<CaseObservation> Execute { get; }

        public string? ExpectedReturnValue { get; }

        public string? ExpectedSideEffect { get; }

        public string? ExpectedExceptionType { get; }
    }
}

