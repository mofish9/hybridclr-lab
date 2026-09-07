#if DHE_EVOLUTION_CURRENT
using System;
using System.Collections.Generic;
using System.IO;
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
                try
                {
                    action();
                    string evidencePath = Environment.GetEnvironmentVariable("HYBRIDCLR_DHE_EVOLUTION_EVIDENCE");
                    if (!string.IsNullOrEmpty(evidencePath)) File.AppendAllText(evidencePath, name + "\n");
                }
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
                Type[] expected = { typeof(DheDemoCalculator), typeof(List<DheDemoCalculator[]>), typeof(DheDemoCalculator[,]) };
                Type[] actual = typeof(DheEvolutionCarrier).GetCustomAttributes<DheEvolutionTypeMarkerAttribute>()
                    .Select(marker => marker.Value).ToArray();
                Require(actual.Length == expected.Length && expected.All(actual.Contains), "new attribute Base type arguments");
                Type[] raw = typeof(DheEvolutionCarrier).GetCustomAttributesData()
                    .Where(attribute => attribute.AttributeType == typeof(DheEvolutionTypeMarkerAttribute))
                    .Select(attribute => (Type)attribute.ConstructorArguments[0].Value).ToArray();
                Require(raw.Length == expected.Length && expected.All(raw.Contains), "new raw attribute Base type arguments");
            });
            Check("array-declarations", () =>
            {
                var carrier = new DheEvolutionCarrier();
                FieldInfo vector = typeof(DheEvolutionCarrier).GetField(nameof(DheEvolutionCarrier.Vector));
                FieldInfo matrix = typeof(DheEvolutionCarrier).GetField(nameof(DheEvolutionCarrier.Matrix));
                Require(vector.FieldType == typeof(DheDemoCalculator[]) &&
                    matrix.FieldType == typeof(DheDemoCalculator[,]), "new array field type identities");
                vector.SetValue(carrier, new[] { instance });
                matrix.SetValue(carrier, new[,] { { instance } });
                Require(ReferenceEquals(carrier.Vector[0], instance) && ReferenceEquals(carrier.Matrix[0, 0], instance),
                    "new array field assignments");
            });
            Check("ref-out-signature", () =>
            {
                MethodInfo method = typeof(DheEvolutionCarrier).GetMethod(nameof(DheEvolutionCarrier.Move));
                Require(method.GetParameters().All(parameter => parameter.ParameterType == typeof(DheDemoCalculator).MakeByRefType()),
                    "new ref/out parameter type identities");
                object[] arguments = { instance, null! };
                method.Invoke(null, arguments);
                Require(arguments[0] == null && ReferenceEquals(arguments[1], instance), "new ref/out reflection invocation");
                var callback = (DheEvolutionMove)Delegate.CreateDelegate(typeof(DheEvolutionMove), method);
                DheDemoCalculator source = instance;
                callback(ref source, out DheDemoCalculator target);
                Require(source == null && ReferenceEquals(target, instance), "new ref/out delegate invocation");
            });
            Check("interface-and-constrained-call", () =>
            {
                Require(typeof(DheEvolutionOperation).GetInterfaces().Contains(typeof(IIntOperation)), "new type Base interface identity");
                object operation = new DheEvolutionOperation();
                Require(operation is IIntOperation && ((IIntOperation)operation).Apply(3) == 3003 &&
                    DheCapabilityCases.GenericConstrained(new DheEvolutionOperation(), 3) == 3103,
                    "new implementation Base interface and constrained calls");
            });
            Check("generic-constraint", () =>
            {
                Type parameter = typeof(DheEvolutionConstrained<>).GetGenericArguments().Single();
                Require(parameter.GetGenericParameterConstraints().Single() == typeof(DheDemoCalculator), "new generic type Base constraint");
                MethodInfo method = typeof(DheEvolutionCarrier).GetMethod(nameof(DheEvolutionCarrier.GenericEcho));
                Require(method.GetGenericArguments().Single().GetGenericParameterConstraints().Single() == typeof(DheDemoCalculator),
                    "new generic method Base constraint");
                Require(ReferenceEquals(method.MakeGenericMethod(typeof(DheDemoCalculator)).Invoke(null, new object[] { instance }), instance) &&
                    ReferenceEquals(new DheEvolutionConstrained<DheDemoCalculator>().Echo(instance), instance), "new constrained generic calls");
            });
            Check("generic-inheritance", () =>
            {
                Require(typeof(DheEvolutionGenericDerived).BaseType == typeof(GenericVirtualOperation<IntOperationStruct>),
                    "new type generic Base parent identity");
                object derived = new DheEvolutionGenericDerived();
                Require(derived is GenericVirtualOperation<IntOperationStruct> operation &&
                    operation.Apply(new IntOperationStruct(), 3) == 106, "new generic parent cast and virtual call");
            });
            Check("repeated-structural-evolution", () =>
            {
                var carrier = new DheEvolutionCarrier();
                FieldInfo revision = typeof(DheEvolutionCarrier).GetField(nameof(DheEvolutionCarrier.Revision));
                PropertyInfo property = typeof(DheEvolutionCarrier).GetProperty(nameof(DheEvolutionCarrier.RevisionProperty));
                MethodInfo method = typeof(DheEvolutionCarrier).GetMethod(nameof(DheEvolutionCarrier.ReadRevision));
                Require(revision.DeclaringType == typeof(DheEvolutionCarrier) &&
                    property.DeclaringType == typeof(DheEvolutionCarrier) && method.DeclaringType == typeof(DheEvolutionCarrier),
                    "second-generation member identities");
                Require(carrier.Revision == 0 && (int)revision.GetValue(carrier) == 0,
                    "second-generation field default");
                revision.SetValue(carrier, 41);
                Require(carrier.ReadRevision() == 41 && (int)method.Invoke(carrier, null) == 41,
                    "second-generation field and method calls");
                property.SetValue(carrier, 7);
                Func<int> callback = carrier.ReadRevision;
                var reflected = (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), carrier, method);
                Require(callback() == 4007 && reflected() == 4007 &&
                    (int)property.GetValue(carrier) == 4007, "second-generation property and delegates");
                WeakReference payload = StoreRepeatedPayload(carrier);
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Require(payload.IsAlive && ReferenceEquals(payload.Target, carrier.RevisionPayload) &&
                    ((byte[])carrier.RevisionPayload)[0] == 73, "second-generation sidecar GC retention");
            });
            Require(errors.Count == 0, "new type declarations: " + string.Join("; ", errors));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static WeakReference StoreRepeatedPayload(DheEvolutionCarrier carrier)
        {
            var payload = new byte[37];
            payload[0] = 73;
            carrier.RevisionPayload = payload;
            return new WeakReference(payload);
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

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed class DheEvolutionTypeMarkerAttribute : Attribute
    {
        public DheEvolutionTypeMarkerAttribute(Type value) { Value = value; }
        public Type Value { get; }
    }

    [DheEvolutionTypeMarker(typeof(DheDemoCalculator))]
    [DheEvolutionTypeMarker(typeof(List<DheDemoCalculator[]>))]
    [DheEvolutionTypeMarker(typeof(DheDemoCalculator[,]))]
    public sealed class DheEvolutionCarrier
    {
        public int Revision;
        public object RevisionPayload = null!;
        public int RevisionProperty { get => Revision; set => Revision = value + 4000; }
        public int ReadRevision() => Revision;
        public DheDemoCalculator Value = null!;
        public List<DheDemoCalculator> Values = null!;
        public DheDemoCalculator[] Vector = null!;
        public DheDemoCalculator[,] Matrix = null!;
        public static DheDemoCalculator Echo(DheDemoCalculator value) => value;
        public static T GenericEcho<T>(T value) where T : DheDemoCalculator => value;
        public static void Move(ref DheDemoCalculator source, out DheDemoCalculator target)
        {
            target = source;
            source = null!;
        }
    }

    public sealed class DheEvolutionDerived : DheDemoCalculator { }
    public sealed class DheEvolutionGenericDerived : GenericVirtualOperation<IntOperationStruct> { }
    public sealed class DheEvolutionConstrained<T> where T : DheDemoCalculator
    {
        public T Echo(T value) => value;
    }
    public sealed class DheEvolutionOperation : IIntOperation
    {
        public int Apply(int value) => value + 3000;
    }
    public delegate void DheEvolutionMove(ref DheDemoCalculator source, out DheDemoCalculator target);
}
#endif
