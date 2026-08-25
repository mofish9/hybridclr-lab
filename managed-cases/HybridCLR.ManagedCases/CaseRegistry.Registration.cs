using System;
using System.Collections.Generic;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterCase(
            List<CaseDefinition> cases,
            string id,
            string category,
            Func<CaseObservation> execute,
            string? expectedReturnValue,
            string expectedSideEffect = "",
            string? expectedExceptionType = null,
            string layer = "managed-core",
            params string[] features)
        {
            cases.Add(new CaseDefinition(
                id,
                category,
                execute,
                expectedReturnValue,
                expectedSideEffect,
                expectedExceptionType,
                layer,
                features));
        }
    }
}
