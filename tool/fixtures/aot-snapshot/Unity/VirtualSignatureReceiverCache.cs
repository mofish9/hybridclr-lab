using System;
using System.Collections.Generic;
using System.Reflection;

namespace HybridCLR.Lab.Snapshot
{
    internal sealed class VirtualSignatureReceiverCache
    {
        private object oldReceiver;
        private MethodInfo cachedMethod;
        private Type cachedType;

        internal static VirtualSignatureReceiverCache Capture(Assembly assembly)
        {
            var type = assembly.GetType("HybridCLR.Lab.VirtualSignatures.Processor", true);
            var value = new VirtualSignatureReceiverCache {
                cachedType = type, cachedMethod = type.GetMethod("Fail"), oldReceiver = Activator.CreateInstance(type)
            };
            if (value.cachedMethod == null || type.GetField("Extra") != null)
                throw new InvalidOperationException("Old receiver probe requires the original AOT layout.");
            return value;
        }

        internal string[] Verify()
        {
            var checks = new List<string>();
            void Check(string name, bool valid)
            {
                if (!valid) throw new InvalidOperationException("Virtual receiver check failed: " + name);
                checks.Add(name); Console.WriteLine("DHE virtual receiver check: " + name);
            }
            var fresh = cachedType.GetMethod("Fail");
            Check("logical-method-identity", fresh == cachedMethod && RuntimeApi.IsDifferentialMethodChanged(cachedMethod));
            var currentReceiver = Activator.CreateInstance(cachedType);
            var extra = cachedType.GetField("Extra");
            Check("current-receiver-storage", extra != null && (long)extra.GetValue(currentReceiver) == 1000L &&
                currentReceiver.GetType() == oldReceiver.GetType());
            bool Reject(MethodInfo method)
            {
                try { method.Invoke(oldReceiver, null); }
                catch (TargetException)
                {
                    // Reflection rejects the physical receiver before invoking
                    // the body. This is the normal MethodInfo contract.
                    Console.WriteLine("DHE virtual receiver rejection: TargetException");
                    return true;
                }
                catch (TargetInvocationException error)
                {
                    bool guarded = error.InnerException is ExecutionEngineException &&
                        error.InnerException.Message.Contains("old AOT ABI");
                    if (guarded) Console.WriteLine("DHE virtual receiver rejection: old-AOT-frame");
                    return guarded;
                }
                return false;
            }
            Check("cached-method-rejects-old-receiver", Reject(cachedMethod));
            Check("fresh-method-rejects-old-receiver", Reject(fresh));
            bool currentBody = false;
            try { fresh.Invoke(currentReceiver, null); }
            catch (TargetInvocationException error)
            {
                currentBody = error.InnerException is InvalidOperationException &&
                    error.InnerException.Message == "virtual-signature-expected";
            }
            Check("current-receiver-invokes-body", currentBody);
            Check("rejection-preserves-receivers", (int)cachedType.GetField("Bias").GetValue(oldReceiver) == 25 &&
                (long)extra.GetValue(currentReceiver) == 1000L);
            return checks.ToArray();
        }
    }
}
