extern alias model;
using System;
using System.Reflection;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using State = model::HybridCLR.Lab.UnityCases.UnityCaseState;

namespace HybridCLR.Lab.Declarations
{
    public static class DeclarationValues
    {
#if DECLARATION_VIRTUAL_7
        public const int Default = 7;
        public const bool HasInterface = true;
        public const string Mode = "declaration-virtual-7";
#elif DECLARATION_NONVIRTUAL_11
        public const int Default = 11;
        public const bool HasInterface = false;
        public const string Mode = "declaration-nonvirtual-11";
#else
        public const int Default = 3;
        public const bool HasInterface = false;
        public const string Mode = "declaration-nonvirtual-3";
#endif
    }

    public interface IOperation
    {
        int Measure(int value = DeclarationValues.Default);
    }

    // Transfer these exact compiler declarations to the existing component.
    public sealed class DeclarationTemplate
#if DECLARATION_VIRTUAL_7
        : IOperation, ISerializationCallbackReceiver
#endif
    {
        public int Measure(int value = DeclarationValues.Default) { return ((Evolving)(object)this).Value + value; }
        public void OnBeforeSerialize() { model::HybridCLR.Lab.UnityReference.SerializationCallbackCases.Before((Evolving)(object)this); }
        public void OnAfterDeserialize() { model::HybridCLR.Lab.UnityReference.SerializationCallbackCases.After((Evolving)(object)this); }
    }

    public static class DeclarationCases
    {
        private const MethodAttributes SlotFlags = MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unityDeclarationProbe") >= 0) Run();
        }
        private static bool Supports(object value) { return value is IOperation; }
        // The fixture relocates the template cast along with its methods.
        private static int Direct(object value, int argument) { return ((DeclarationTemplate)value).Measure(argument); }
        private static int DirectDefault(object value) { return ((DeclarationTemplate)value).Measure(); }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run()
        {
            Console.WriteLine("DHE declaration mode: " + DeclarationValues.Mode);
            int passed = 0, failed = 0, disabled = State.Disabled, destroyed = State.Destroyed;
            void Check(string name, Func<bool> assertion)
            {
                try
                {
                    if (!assertion()) throw new InvalidOperationException("assertion returned false");
                    passed++; Console.WriteLine("DHE Unity declaration check: " + name);
                }
                catch (Exception error)
                {
                    failed++; Console.WriteLine("DHE Unity declaration failure: " + name + ": " + error);
                }
            }
            var owner = new GameObject("DHE declaration component"); owner.SetActive(false);
            try
            {
                Type type = typeof(model::HybridCLR.Lab.ValueLayout.Factory).Assembly.GetType(typeof(Evolving).FullName, true);
                var component = (Evolving)owner.AddComponent(type); component.Value = 37;
                string[] names = { "Measure", "OnBeforeSerialize", "OnAfterDeserialize" };
                var methods = Array.ConvertAll(names, name => type.GetMethod(name));
                MethodInfo measure = methods[0];
                MethodAttributes expectedFlags = DeclarationValues.HasInterface ? SlotFlags : 0;
                Check("current-method-flags", () => Array.TrueForAll(methods, method => method != null && (method.Attributes & SlotFlags) == expectedFlags));
                Check("current-method-base-definitions", () => Array.TrueForAll(methods, method => method.GetBaseDefinition() == method));
                Check("current-method-declaring-types", () => Array.TrueForAll(methods, method => method.DeclaringType == type && method.ReflectedType == type));
                Check("current-method-enumeration", () => {
                    foreach (string name in names)
                    {
                        var declared = Array.FindAll(type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly), method => method.Name == name);
                        var inherited = Array.FindAll(type.GetMethods(BindingFlags.Public | BindingFlags.Instance), method => method.Name == name);
                        if (declared.Length != 1 || inherited.Length != 1 || declared[0] != inherited[0]) return false;
                    }
                    return true;
                });
                Check("current-parameter-default", () => {
                    var parameter = measure.GetParameters()[0];
                    return parameter.HasDefaultValue && parameter.IsOptional && (int)parameter.DefaultValue == DeclarationValues.Default;
                });
                Check("interface-parameter-default", () => (int)typeof(IOperation).GetMethod("Measure").GetParameters()[0].DefaultValue == DeclarationValues.Default);
                Check("direct-explicit-argument", () => Direct(component, 19) == 56);
                Check("direct-default-argument", () => DirectDefault(component) == 37 + DeclarationValues.Default);
                Check("reflected-explicit-argument", () => (int)measure.Invoke(component, new object[] { 23 }) == 60);
                Check("reflected-default-argument", () => (int)measure.Invoke(component, new object[] { Type.Missing }) == 37 + DeclarationValues.Default);
                Check("interface-public-queries", () => Supports(component) == DeclarationValues.HasInterface &&
                    typeof(IOperation).IsAssignableFrom(type) == DeclarationValues.HasInterface &&
                    (Array.IndexOf(type.GetInterfaces(), typeof(IOperation)) >= 0) == DeclarationValues.HasInterface);
                Check("interface-explicit-call-or-absence", () => DeclarationValues.HasInterface
                    ? ((IOperation)(object)component).Measure(29) == 66 : !Supports(component));
                Check("interface-default-call-or-absence", () => DeclarationValues.HasInterface
                    ? ((IOperation)(object)component).Measure() == 37 + DeclarationValues.Default : !Supports(component));
                Check("interface-map-or-rejection", () => {
                    if (!DeclarationValues.HasInterface)
                    {
                        try { type.GetInterfaceMap(typeof(IOperation)); return false; }
                        catch (ArgumentException) { return true; }
                    }
                    var map = type.GetInterfaceMap(typeof(IOperation));
                    return map.TargetType == type && map.InterfaceType == typeof(IOperation) && map.TargetMethods.Length == 1 &&
                        map.InterfaceMethods.Length == 1 && map.TargetMethods[0] == measure &&
                        (int)map.TargetMethods[0].Invoke(component, new object[] { 31 }) == 68;
                });
                Type counters = typeof(model::HybridCLR.Lab.UnityReference.SerializationCallbackCases);
                const BindingFlags hidden = BindingFlags.NonPublic | BindingFlags.Static;
                FieldInfo before = counters.GetField("beforeCount", hidden), after = counters.GetField("afterCount", hidden);
                before.SetValue(null, 0); after.SetValue(null, 0);
                Check("native-before-callback-selection", () => {
                    string json = JsonUtility.ToJson(component, false);
                    return json.Contains("\"Value\":37") && (int)before.GetValue(null) == (DeclarationValues.HasInterface ? 1 : 0);
                });
                Check("native-after-callback-selection", () => {
                    JsonUtility.FromJsonOverwrite("{\"Value\":41}", component);
                    return component.Value == 41 && (int)after.GetValue(null) == (DeclarationValues.HasInterface ? 1 : 0);
                });
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
            Check("fixture-preserves-lifecycle-state", () => State.Disabled == disabled && State.Destroyed == destroyed);
            if (failed != 0) throw new InvalidOperationException("DHE declaration failures: " + failed);
            Console.WriteLine("DHE Unity declaration pass: " + passed);
        }
    }
}
