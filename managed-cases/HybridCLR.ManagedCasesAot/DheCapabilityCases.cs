using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace HybridCLR.Lab.ManagedCasesAot
{
    // These types keep their layout and signatures identical in baseline and
    // current builds. Only method bodies are changed with DHE_CURRENT.
    public static class DheCapabilityCases
    {
        public static int InterfaceCall(IIntOperation operation, int value)
        {
#if DHE_CURRENT
            return operation.Apply(value) + 100;
#else
            return operation.Apply(value) + 1;
#endif
        }

        public static int DelegateCall(IntOperation operation, int value)
        {
#if DHE_CURRENT
            return operation(value) + 100;
#else
            return operation(value) + 1;
#endif
        }

        public static T GenericSelect<T>(T value, T fallback)
        {
#if DHE_CURRENT
            return value;
#else
            return fallback;
#endif
        }

        public static int GenericConstrained<T>(T operation, int value) where T : IIntOperation
        {
#if DHE_CURRENT
            return operation.Apply(value) + 100;
#else
            return operation.Apply(value) + 1;
#endif
        }

        public static SmallValue MutateValue(SmallValue value)
        {
#if DHE_CURRENT
            value.Number += 100;
            value.Wide += 1000;
#else
            value.Number += 1;
            value.Wide += 1;
#endif
            return value;
        }

        public static void RefOutValue(ref SmallValue value, out int result)
        {
#if DHE_CURRENT
            value.Number += 100;
            result = value.Number + 100;
#else
            value.Number += 1;
            result = value.Number + 1;
#endif
        }

        public static object BoxValue(SmallValue value)
        {
#if DHE_CURRENT
            SmallValue copy = value;
            copy.Number += 100;
            return copy;
#else
            return value;
#endif
        }

        public static async Task<int> AsyncValue(int value)
        {
            // Keep the probe deterministic when invoked synchronously by the
            // Unity Player runner; the compiler still emits an async state
            // machine and the method body remains a DHE candidate.
#if DHE_CURRENT
            if (value == int.MinValue)
            {
                value++;
            }
#else
            if (value == int.MinValue)
            {
                value--;
            }
#endif
            await Task.CompletedTask;
#if DHE_CURRENT
            return value + 100;
#else
            return value + 1;
#endif
        }

        public static IEnumerable<int> IterateValue(int value)
        {
#if DHE_CURRENT
            if (value == int.MinValue)
            {
                value++;
            }
#else
            if (value == int.MinValue)
            {
                value--;
            }
#endif
#if DHE_CURRENT
            yield return value + 100;
#else
            yield return value + 1;
#endif
            yield return value + 2;
        }

        public static int GenericContainerValue(int value)
        {
#if DHE_STRUCTURE_CURRENT
            var added = new DheAddedReferenceType(300);
            var generic = new DheAddedGenericType<int>(added.Apply(value));
            var calculator = new DheDemoCalculator();
            var nested = new DheAddedNestedType(600);
            return generic.Value + AddedStaticMethod(value) +
                calculator.AddedInstanceMethod(value) + nested.Apply(value);
#else
            var values = new List<int> { value, value + 1 };
#if DHE_CURRENT
            return values[0] + values[1] + 100;
#else
            return values[0] + values[1] + 1;
#endif
#endif
        }

        public static int NullableValue(SmallValue? value)
        {
            SmallValue unwrapped = value ?? new SmallValue(0, 0);
#if DHE_CURRENT
            return unwrapped.Number + (unwrapped.Wide > 0 ? 100 : 0);
#else
            return unwrapped.Number + (unwrapped.Wide > 0 ? 1 : 0);
#endif
        }

        public static int DelegateClosedInstance(int value)
        {
            IntOperation operation = new DelegateTarget(5).Apply;
#if DHE_CURRENT
            return operation(value) + 100;
#else
            return operation(value) + 1;
#endif
        }

        public static int DelegateOpenInstance(int value)
        {
            OpenIntOperation operation = (target, input) => target.Apply(input);
#if DHE_CURRENT
            return operation(new DelegateTarget(5), value) + 100;
#else
            return operation(new DelegateTarget(5), value) + 1;
#endif
        }

        public static int DelegateMulticast(int value)
        {
            int calls = 0;
            IntOperation operation = input => { calls += input; return calls; };
            operation += input => { calls += input * 2; return calls; };
#if DHE_CURRENT
            return operation(value) + calls + 100;
#else
            return operation(value) + calls + 1;
#endif
        }

        public static int ExceptionFinally(int value)
        {
            int marker = 0;
            try
            {
                if (value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }
                marker = value;
#if DHE_CURRENT
                return marker + 100;
#else
                return marker + 1;
#endif
            }
            finally
            {
                FinallyMarker = marker + 1;
            }
        }

#if DHE_STRUCTURE_CURRENT
        public sealed class DheAddedNestedType
        {
            private readonly int offset;

            public DheAddedNestedType(int offset)
            {
                this.offset = offset;
            }

            public int Apply(int value)
            {
                return value + offset;
            }
        }

        public static int AddedStaticMethod(int value)
        {
            return value + 400;
        }

#endif

        public static int FinallyMarker;
    }

    public sealed class DelegateTarget
    {
        private readonly int offset;

        public DelegateTarget(int offset)
        {
            this.offset = offset;
        }

        public int Apply(int value)
        {
            return value + offset;
        }
    }

    public delegate int OpenIntOperation(DelegateTarget target, int value);

    public interface IIntOperation
    {
        int Apply(int value);
    }

    public readonly struct IntOperationStruct : IIntOperation
    {
        public int Apply(int value)
        {
            return value * 2;
        }
    }

    public class VirtualOperationBase
    {
        public virtual int Apply(int value)
        {
#if DHE_CURRENT
            return value + 100;
#else
            return value + 1;
#endif
        }
    }

    public sealed class VirtualOperationDerived : VirtualOperationBase
    {
        public override int Apply(int value)
        {
            return value * 3;
        }
    }

    public class GenericVirtualOperation<T> where T : IIntOperation
    {
        public virtual int Apply(T operation, int value)
        {
#if DHE_CURRENT
            return operation.Apply(value) + 100;
#else
            return operation.Apply(value) + 1;
#endif
        }
    }

    public struct SmallValue
    {
        public int Number;
        public long Wide;

        public SmallValue(int number, long wide)
        {
            Number = number;
            Wide = wide;
        }
    }

    public delegate int IntOperation(int value);
}
