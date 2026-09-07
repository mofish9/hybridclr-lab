#if DHE_EVOLUTION_CURRENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCasesAot
{
    public static class DheEvolutionAssertions
    {
        public static void Validate(DheDemoCalculator instance)
        {
            Type type = typeof(DheDemoCalculator);
            Require(type == instance.GetType(), "new type resolves existing Base type");
            MethodInfo method = type.GetMethod(nameof(DheDemoCalculator.AddedAttributedMethod)) ??
                throw new MissingMethodException(type.FullName, nameof(DheDemoCalculator.AddedAttributedMethod));
            Require(method.DeclaringType == type, "added method identity");
            Require(instance.AddedAttributedMethod(5) == 2205 &&
                (int)Invoke(method, instance, new object[] { 5 }) == 2205, "added method calls");
            ValidateMarker(method, 2201, "added-method");
            ValidateParameter(method, 2203, "added-parameter");

            MethodInfo generic = type.GetMethod(nameof(DheDemoCalculator.AddedAttributedGenericMethod));
            ValidateMarker(generic, 2202, "added-generic-method");
            MethodInfo inflated = generic.MakeGenericMethod(typeof(string));
            Require(inflated.DeclaringType == type && inflated.GetGenericMethodDefinition() == generic,
                "inflated supplemental method identity");
            ValidateMarker(inflated, 2202, "added-generic-method");
            ValidateParameter(generic, 2204, "generic-parameter");
            ValidateParameter(inflated, 2204, "generic-parameter");
            Require(instance.AddedAttributedGenericMethod("direct") == "direct" &&
                (string)Invoke(inflated, instance, new object[] { "reflection" }) == "reflection",
                "added generic method calls");

            MethodInfo asyncMethod = type.GetMethod(nameof(DheDemoCalculator.AddedAsyncMethod));
            var asyncAttribute = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
            Require(asyncAttribute != null && asyncAttribute.StateMachineType.DeclaringType == type,
                "added async state machine attribute");
            Require(instance.AddedAsyncMethod(5).GetAwaiter().GetResult() == 2305 &&
                ((Task<int>)Invoke(asyncMethod, instance, new object[] { 6 })).GetAwaiter().GetResult() == 2306,
                "added async completion");

            MethodInfo iterator = type.GetMethod(nameof(DheDemoCalculator.AddedIteratorMethod));
            var iteratorAttribute = iterator.GetCustomAttribute<IteratorStateMachineAttribute>();
            Require(iteratorAttribute != null && iteratorAttribute.StateMachineType.DeclaringType == type,
                "added iterator state machine attribute");
            Require(instance.AddedIteratorMethod(5).SequenceEqual(new[] { 2405, 2406 }),
                "added iterator execution");
            Require(type.Assembly.GetReferencedAssemblies().Any(assembly => assembly.Name == "System.Core") ||
                type.Assembly.GetReferencedAssemblies().Any(assembly => assembly.Name == "netstandard"),
                "current assembly reference reflection");
            ValidateDeclarations(instance);
        }

        private static void ValidateDeclarations(DheDemoCalculator instance)
        {
            var errors = new List<string>();
            void Check(string name, Action action)
            {
                try { action(); }
                catch (Exception exception) { errors.Add(name + ": " + exception.Message); }
            }
            Check("method-signature", () =>
            {
                MethodInfo method = typeof(DheEvolutionCarrier).GetMethod(nameof(DheEvolutionCarrier.Echo));
                Require(method.ReturnType == typeof(DheDemoCalculator) &&
                    method.GetParameters()[0].ParameterType == typeof(DheDemoCalculator), "new method signature identity");
                Require(ReferenceEquals(method.Invoke(null, new object[] { instance }), instance), "new method Base argument invocation");
                var callback = (Func<DheDemoCalculator, DheDemoCalculator>)Delegate.CreateDelegate(
                    typeof(Func<DheDemoCalculator, DheDemoCalculator>), method);
                Require(ReferenceEquals(callback(instance), instance), "new method Base argument delegate");
            });
            Check("field-type", () =>
            {
                var carrier = new DheEvolutionCarrier();
                FieldInfo field = typeof(DheEvolutionCarrier).GetField(nameof(DheEvolutionCarrier.Value));
                Require(field.FieldType == typeof(DheDemoCalculator), "new field type identity");
                field.SetValue(carrier, instance);
                Require(ReferenceEquals(carrier.Value, instance), "new field Base object assignment");
            });
            Check("generic-field-type", () =>
            {
                FieldInfo field = typeof(DheEvolutionCarrier).GetField(nameof(DheEvolutionCarrier.Values));
                Require(field.FieldType == typeof(List<DheDemoCalculator>), "new generic field type identity");
                var carrier = new DheEvolutionCarrier();
                field.SetValue(carrier, new List<DheDemoCalculator> { instance });
                Require(ReferenceEquals(carrier.Values[0], instance), "new generic field assignment");
            });
            Check("inheritance", () =>
            {
                Require(typeof(DheEvolutionDerived).BaseType == typeof(DheDemoCalculator), "new type Base parent identity");
                object derived = new DheEvolutionDerived();
                Require(derived is DheDemoCalculator && ((DheDemoCalculator)derived).InstanceStable(3) == 9,
                    "new derived type Base cast and call");
            });
            Check("attribute-type-argument", () =>
            {
                var marker = typeof(DheEvolutionCarrier).GetCustomAttribute<DheEvolutionTypeMarkerAttribute>();
                Require(marker.Value == typeof(DheDemoCalculator), "new attribute Base type argument");
            });
            Require(errors.Count == 0, "new type declarations: " + string.Join("; ", errors));
        }

        private static object Invoke(MethodInfo method, object instance, object[] arguments)
        {
            try { return method.Invoke(instance, arguments); }
            catch (Exception exception)
            {
                throw new InvalidOperationException("DHE evolution invocation failed: " + method.Name +
                    "; declaring type matches target=" + (method.DeclaringType == instance.GetType()), exception);
            }
        }

        private static void ValidateParameter(MethodInfo method, int value, string label)
        {
            ParameterInfo parameter = method.GetParameters().Single();
            Require(parameter.Name == "value" && parameter.Position == 0, "added method parameter identity");
            var marker = parameter.GetCustomAttribute<DheMetadataMarkerAttribute>();
            Require(marker != null && marker.Value == value && marker.Label == label &&
                parameter.IsDefined(typeof(DheMetadataMarkerAttribute), false), "added method parameter attribute");
            CustomAttributeData data = parameter.GetCustomAttributesData().Single(attribute =>
                attribute.AttributeType == typeof(DheMetadataMarkerAttribute));
            Require((int)data.ConstructorArguments[0].Value == value &&
                (string)data.ConstructorArguments[1].Value == label, "added method parameter attribute data");
        }

        private static void ValidateMarker(MethodInfo method, int value, string label)
        {
            var marker = method.GetCustomAttribute<DheMetadataMarkerAttribute>();
            Require(marker != null && marker.Value == value && marker.Label == label,
                "added method custom attribute instance");
            Require(method.IsDefined(typeof(DheMetadataMarkerAttribute), false),
                "added method IsDefined");
            CustomAttributeData data = method.GetCustomAttributesData().Single(attribute =>
                attribute.AttributeType == typeof(DheMetadataMarkerAttribute));
            Require((int)data.ConstructorArguments[0].Value == value &&
                (string)data.ConstructorArguments[1].Value == label,
                "added method custom attribute data");
        }

        private static void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("DHE evolution failed: " + name);
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class DheEvolutionTypeMarkerAttribute : Attribute
    {
        public DheEvolutionTypeMarkerAttribute(Type value) { Value = value; }
        public Type Value { get; }
    }

    [DheEvolutionTypeMarker(typeof(DheDemoCalculator))]
    public sealed class DheEvolutionCarrier
    {
        public DheDemoCalculator Value = null!;
        public List<DheDemoCalculator> Values = null!;
        public static DheDemoCalculator Echo(DheDemoCalculator value) => value;
    }

    public sealed class DheEvolutionDerived : DheDemoCalculator { }
}
#endif
