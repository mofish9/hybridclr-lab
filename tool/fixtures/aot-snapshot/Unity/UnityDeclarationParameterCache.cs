using System;
using System.Reflection;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    internal sealed class UnityDeclarationParameterCache
    {
        private const MethodAttributes SlotFlags = MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;
        private static readonly string[] Names = { "Measure", "OnBeforeSerialize", "OnAfterDeserialize" };
        private Type type;
        private MethodInfo[] methods;
        private MethodAttributes[] attributes;
        private MethodInfo operation;
        private ParameterInfo oldParameter, oldInterfaceParameter;
        private int baseFlags, baseDefault, currentFlags, currentDefault;
        private int warmedDefault, warmedInterfaceDefault;

        internal static UnityDeclarationParameterCache Capture(Type type)
        {
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-unityDeclarationExpectation");
            if (index < 0) return null;
            if (index + 1 >= args.Length) throw new InvalidOperationException("Missing bound declaration expectation.");
            string[] parts = args[index + 1].Split(':');
            if (parts.Length != 4) throw new InvalidOperationException("Invalid bound declaration expectation.");
            var result = new UnityDeclarationParameterCache { type = type, baseFlags = int.Parse(parts[0]), baseDefault = int.Parse(parts[1]),
                currentFlags = int.Parse(parts[2]), currentDefault = int.Parse(parts[3]) };
            result.methods = Array.ConvertAll(Names, name => type.GetMethod(name));
            result.attributes = Array.ConvertAll(result.methods, method => method.Attributes);
            result.operation = type.Assembly.GetType("HybridCLR.Lab.Declarations.IOperation", true).GetMethod("Measure");
            result.oldParameter = result.methods[0].GetParameters()[0]; result.oldInterfaceParameter = result.operation.GetParameters()[0];
            result.warmedDefault = (int)result.oldParameter.DefaultValue; result.warmedInterfaceDefault = (int)result.oldInterfaceParameter.DefaultValue;
            if (Array.Exists(result.attributes, flags => (int)(flags & SlotFlags) != result.baseFlags) ||
                result.warmedDefault != result.baseDefault || result.warmedInterfaceDefault != result.baseDefault)
                throw new InvalidOperationException("Warmed Base declaration does not match the bound snapshot.");
            Console.WriteLine("DHE declaration cache expectation: " + args[index + 1]);
            return result;
        }

        internal void Verify(Component current, Action<string, Func<bool>> check)
        {
            Console.WriteLine("DHE declaration parameter objects before requery: method=" + oldParameter.DefaultValue +
                "; interface=" + oldInterfaceParameter.DefaultValue + "; expectedCurrent=" + currentDefault);
            var fresh = Array.ConvertAll(Names, name => type.GetMethod(name));
            check("declaration-base-flags-warmed", () => Array.TrueForAll(attributes, flags => (int)(flags & SlotFlags) == baseFlags));
            check("declaration-method-flags-current", () => Array.TrueForAll(methods, method => (int)(method.Attributes & SlotFlags) == currentFlags));
            check("declaration-method-identity-stable", () => methods[0] == fresh[0] && methods[1] == fresh[1] && methods[2] == fresh[2]);
            check("declaration-method-declaring-types", () => Array.TrueForAll(methods, method => method.DeclaringType == type && method.ReflectedType == type));
            check("declaration-method-base-definitions", () => Array.TrueForAll(methods, method => method.GetBaseDefinition() == method));
            check("declaration-base-parameters-warmed", () => warmedDefault == baseDefault && warmedInterfaceDefault == baseDefault);
            check("declaration-parameter-default-current", () => (int)methods[0].GetParameters()[0].DefaultValue == currentDefault);
            check("declaration-fresh-parameter-default-current", () => (int)fresh[0].GetParameters()[0].DefaultValue == currentDefault);
            check("declaration-interface-default-current", () => (int)operation.GetParameters()[0].DefaultValue == currentDefault);
            check("declaration-parameter-member-stable", () => methods[0].GetParameters()[0].Member == methods[0] && operation.GetParameters()[0].Member == operation);
            int value = (int)type.GetField("Value").GetValue(current);
            check("declaration-cached-explicit-invocation", () => (int)methods[0].Invoke(current, new object[] { 19 }) == value + 19);
            check("declaration-cached-default-invocation", () => (int)methods[0].Invoke(current, new object[] { Type.Missing }) == value + currentDefault);
            Console.WriteLine("DHE declaration parameter objects after requery: method=" + oldParameter.DefaultValue +
                "; interface=" + oldInterfaceParameter.DefaultValue + "; expectedCurrent=" + currentDefault);
        }
    }
}
