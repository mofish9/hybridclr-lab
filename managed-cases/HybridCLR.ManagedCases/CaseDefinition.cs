using System;
using System.Collections.Generic;

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
            string? expectedExceptionType = null,
            string layer = "managed-core",
            params string[] features)
        {
            Id = id;
            Category = category;
            Execute = execute;
            ExpectedReturnValue = expectedReturnValue;
            ExpectedSideEffect = expectedSideEffect;
            ExpectedExceptionType = expectedExceptionType;
            Layer = layer;
            Features = features;
        }

        public string Id { get; }

        public string Category { get; }

        public Func<CaseObservation> Execute { get; }

        public string? ExpectedReturnValue { get; }

        public string? ExpectedSideEffect { get; }

        public string? ExpectedExceptionType { get; }

        public string Layer { get; }

        public IReadOnlyList<string> Features { get; }
    }
}
