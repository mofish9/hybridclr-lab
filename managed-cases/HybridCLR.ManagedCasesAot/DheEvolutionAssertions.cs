#if DHE_EVOLUTION_CURRENT
using System;
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
            MethodInfo method = type.GetMethod(nameof(DheDemoCalculator.AddedAttributedMethod)) ??
                throw new MissingMethodException(type.FullName, nameof(DheDemoCalculator.AddedAttributedMethod));
            Require(method.DeclaringType == type, "added method identity");
            Require(instance.AddedAttributedMethod(5) == 2205 &&
                (int)method.Invoke(instance, new object[] { 5 }) == 2205, "added method calls");
            ValidateMarker(method, 2201, "added-method");
            ValidateParameter(method, 2203, "added-parameter");

            MethodInfo generic = type.GetMethod(nameof(DheDemoCalculator.AddedAttributedGenericMethod));
            ValidateMarker(generic, 2202, "added-generic-method");
            MethodInfo inflated = generic.MakeGenericMethod(typeof(string));
            ValidateMarker(inflated, 2202, "added-generic-method");
            ValidateParameter(generic, 2204, "generic-parameter");
            ValidateParameter(inflated, 2204, "generic-parameter");
            Require(instance.AddedAttributedGenericMethod("direct") == "direct" &&
                (string)inflated.Invoke(instance, new object[] { "reflection" }) == "reflection",
                "added generic method calls");

            MethodInfo asyncMethod = type.GetMethod(nameof(DheDemoCalculator.AddedAsyncMethod));
            var asyncAttribute = asyncMethod.GetCustomAttribute<AsyncStateMachineAttribute>();
            Require(asyncAttribute != null && asyncAttribute.StateMachineType.DeclaringType == type,
                "added async state machine attribute");
            Require(instance.AddedAsyncMethod(5).GetAwaiter().GetResult() == 2305 &&
                ((Task<int>)asyncMethod.Invoke(instance, new object[] { 6 })).GetAwaiter().GetResult() == 2306,
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
}
#endif
