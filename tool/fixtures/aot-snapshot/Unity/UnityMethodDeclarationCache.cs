using System;
using System.Reflection;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    internal sealed class UnityMethodDeclarationCache
    {
        private const MethodAttributes SlotFlags = MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;
        private static readonly string[] Names = { "OnBeforeSerialize", "OnAfterDeserialize" };
        private Type type;
        private MethodInfo[] methods;
        private MethodAttributes[] attributes;
        private int expectedCount;

        internal static UnityMethodDeclarationCache Capture(Type type)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-unityMethodDeclarationBaseMethods");
            if (index < 0) return null;
            if (index + 1 >= args.Length || !int.TryParse(args[index + 1], out int expected) || (expected != 0 && expected != 2))
                throw new InvalidOperationException("Method cache fixture requires a bound Base method count.");
            var result = new UnityMethodDeclarationCache { type = type, methods = new MethodInfo[2], attributes = new MethodAttributes[2], expectedCount = expected };
            int count = 0;
            for (int i = 0; i < Names.Length; i++)
            {
                MethodInfo method = result.methods[i] = type.GetMethod(Names[i]);
                if (method == null) continue;
                count++; result.attributes[i] = method.Attributes;
                if ((method.Attributes & SlotFlags) != SlotFlags || method.GetBaseDefinition() != method || method.DeclaringType != type)
                    throw new InvalidOperationException("Expected compiler interface declarations in Base.");
            }
            if (count != expected) throw new InvalidOperationException("Base method cache inventory differs from its snapshot.");
            Console.WriteLine("DHE method declaration Base cache: " + count);
            return result;
        }

        internal void Verify(Component current, Action<string, Func<bool>> check)
        {
            var fresh = new[] { type.GetMethod(Names[0]), type.GetMethod(Names[1]) };
            check("current-method-nonvirtual-flags", () => Array.TrueForAll(fresh,
                method => method != null && (method.Attributes & SlotFlags) == 0 && !method.IsVirtual && !method.IsFinal));
            check("current-method-declaring-types", () => Array.TrueForAll(fresh, method => method.DeclaringType == type && method.ReflectedType == type));
            check("current-method-base-definitions", () => Array.TrueForAll(fresh, method => method.GetBaseDefinition() == method));
            check("current-method-enumeration", () => {
                foreach (string name in Names)
                {
                    var declared = Array.FindAll(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), method => method.Name == name);
                    var inherited = Array.FindAll(type.GetMethods(BindingFlags.Public | BindingFlags.Instance), method => method.Name == name);
                    if (declared.Length != 1 || inherited.Length != 1 || declared[0] != inherited[0] || (declared[0].Attributes & SlotFlags) != 0) return false;
                }
                return type.GetMethod("ToString", Type.EmptyTypes) != null;
            });
            if (expectedCount == 0)
            {
                check("base-had-no-callback-methods", () => Array.TrueForAll(methods, method => method == null));
                return;
            }
            check("cached-methods-were-virtual", () => Array.TrueForAll(attributes, flags => (flags & SlotFlags) == SlotFlags));
            check("cached-methods-now-nonvirtual", () => Array.TrueForAll(methods, method => (method.Attributes & SlotFlags) == 0 && !method.IsVirtual && !method.IsFinal));
            check("cached-method-identity-stable", () => methods[0] == fresh[0] && methods[1] == fresh[1]);
            check("cached-method-base-definitions", () => Array.TrueForAll(methods, method => method.GetBaseDefinition() == method && method.DeclaringType == type));
            check("cached-method-current-invocation", () => {
                Type counters = type.Assembly.GetType("HybridCLR.Lab.UnityReference.SerializationCallbackCases", true);
                const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
                FieldInfo before = counters.GetField("beforeCount", hidden), after = counters.GetField("afterCount", hidden);
                before.SetValue(null, 0); after.SetValue(null, 0);
                methods[0].Invoke(current, null); methods[1].Invoke(current, null);
                return (int)before.GetValue(null) == 1 && (int)after.GetValue(null) == 1;
            });
        }
    }
}
