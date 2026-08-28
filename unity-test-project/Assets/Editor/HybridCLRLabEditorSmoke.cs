using System;
using System.Collections.Generic;
using System.Reflection;
using HybridCLR;
using UnityEditor;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    [InitializeOnLoad]
    internal static class HybridCLRLabEditorSmoke
    {
        static HybridCLRLabEditorSmoke()
        {
            Debug.Log("[HybridCLR Lab] Tuanjie editor project loaded.");
        }

        [MenuItem("HybridCLR Lab/Validate Project")]
        public static void ValidateProject()
        {
            Assembly assembly = typeof(HybridCLRLabEditorSmoke).Assembly;
            MethodInfo intOverload = typeof(OverloadProbe).GetMethod("Touch", new[] { typeof(int) });
            MethodInfo stringOverload = typeof(OverloadProbe).GetMethod("Touch", new[] { typeof(string) });
            var descriptors = new[]
            {
                new RuntimePrewarmMethodDescriptor(typeof(OverloadProbe).FullName, "Touch", 1, 0,
                    intOverload.MetadataToken, new[] { "System.Int32" }, "System.Void"),
                new RuntimePrewarmMethodDescriptor(typeof(OverloadProbe).FullName, "Touch", 1, 0,
                    stringOverload.MetadataToken, new[] { "System.String" }, "System.Void"),
            };
            RuntimePrewarmMethodBaseQueue methodQueue = RuntimePrewarmManifest.CreateMethodBaseQueue(assembly, descriptors);
            if (methodQueue.TotalCount != 2)
                throw new InvalidOperationException("Prewarm overload descriptors did not resolve uniquely.");

            bool ambiguousRejected = false;
            try
            {
                RuntimePrewarmManifest.CreateMethodBaseQueue(assembly, new[]
                {
                    new RuntimePrewarmMethodDescriptor(typeof(OverloadProbe).FullName, "Touch", 1, 0),
                });
            }
            catch (InvalidOperationException)
            {
                ambiguousRejected = true;
            }
            if (!ambiguousRejected)
                throw new InvalidOperationException("A legacy ambiguous prewarm descriptor was accepted.");

            string genericTypeName = typeof(GenericOuter<int>.GenericInner<string>).GetGenericTypeDefinition().FullName +
                "<System.Int32,System.String>";
            RuntimePrewarmQueue typeQueue = RuntimePrewarmManifest.CreateQueue(assembly, new[] { genericTypeName });
            if (typeQueue.TotalCount != 1)
                throw new InvalidOperationException("Nested generic prewarm type did not resolve.");
            RuntimePrewarmManifestQueue incrementalTypeQueue = RuntimePrewarmManifest.CreateIncrementalQueue(
                assembly, new[] { genericTypeName, genericTypeName, "System.Int32&", "System.Int32*" });
            if (incrementalTypeQueue.TotalCount != 3 || incrementalTypeQueue.RemainingCount != 3)
                throw new InvalidOperationException("Incremental prewarm manifest queue was not deferred or deduplicated.");
            while (!incrementalTypeQueue.IsComplete)
                incrementalTypeQueue.Process(0f, 1);
            if (incrementalTypeQueue.FailedCount != 3 || incrementalTypeQueue.SucceededCount != 0)
                throw new InvalidOperationException("Incremental editor prewarm failure accounting is inconsistent.");
            RuntimePrewarmQueue compoundTypeQueue = RuntimePrewarmManifest.CreateQueue(assembly, new[]
            {
                genericTypeName + "[]",
                "System.Int32&",
                "System.Int32*",
                "System.Int32[*]",
            });
            if (compoundTypeQueue.TotalCount != 4)
                throw new InvalidOperationException("Array, byref, or pointer prewarm type did not resolve.");

            RuntimePrewarmQueue failureQueue = new RuntimePrewarmQueue(new[] { typeof(OverloadProbe), typeof(GenericOuter<int>) });
            while (!failureQueue.IsComplete)
                failureQueue.Process(10f, 2);
            if (failureQueue.FailedCount != 2 || failureQueue.SucceededCount != 0)
                throw new InvalidOperationException("Editor prewarm batch failure accounting is inconsistent.");

            RuntimePrewarmMethodManifestQueue incrementalMethodQueue =
                RuntimePrewarmManifest.CreateIncrementalMethodBaseQueue(assembly, descriptors);
            while (!incrementalMethodQueue.IsComplete)
                incrementalMethodQueue.Process(0f, 1);
            if (incrementalMethodQueue.TotalCount != 2 || incrementalMethodQueue.FailedCount != 2 ||
                incrementalMethodQueue.SucceededCount != 0)
                throw new InvalidOperationException("Incremental method manifest failure accounting is inconsistent.");

            RuntimePrewarmMethodTokenManifestQueue incrementalTokenQueue =
                RuntimePrewarmManifest.CreateIncrementalMethodTokenQueue(assembly, new[]
                {
                    descriptors[0], descriptors[0], descriptors[1],
                });
            if (incrementalTokenQueue.TotalCount != 2 || incrementalTokenQueue.RemainingCount != 2)
                throw new InvalidOperationException("Incremental token manifest queue was not deferred or deduplicated.");
            while (!incrementalTokenQueue.IsComplete)
                incrementalTokenQueue.Process(0f, 1);
            if (incrementalTokenQueue.FailedCount != 2 || incrementalTokenQueue.SucceededCount != 0)
                throw new InvalidOperationException("Incremental token manifest failure accounting is inconsistent.");

            RuntimePrewarmMethodTokenManifestQueue deferredMissingTokenQueue =
                RuntimePrewarmManifest.CreateIncrementalMethodTokenQueue(assembly, new[]
                {
                    new RuntimePrewarmMethodDescriptor("HybridCLR.Lab.Editor.MissingType", "Touch", 0, 0, 1),
                });
            bool missingTokenRejectedDuringProcess = false;
            try
            {
                deferredMissingTokenQueue.Process(0f, 1);
            }
            catch (InvalidOperationException)
            {
                missingTokenRejectedDuringProcess = true;
            }
            if (!missingTokenRejectedDuringProcess || deferredMissingTokenQueue.RemainingCount != 1)
                throw new InvalidOperationException("Incremental token manifest resolved or consumed a missing type too early.");

            Debug.Log("[HybridCLR Lab] Project and prewarm manifest validation passed.");
        }

        private sealed class OverloadProbe
        {
            public void Touch(int value) { }
            public void Touch(string value) { }
        }

        private sealed class GenericOuter<T>
        {
            public sealed class GenericInner<TInner>
            {
            }
        }
    }
}
