using System;

namespace HybridCLR.Lab.BoundaryContracts
{
    public enum BoundaryKind : long
    {
        None = 0,
        Alpha = 3,
        Beta = 7,
    }

    public readonly struct BoundaryPayload : IEquatable<BoundaryPayload>
    {
        public BoundaryPayload(int number, long wide, BoundaryKind kind)
        {
            Number = number;
            Wide = wide;
            Kind = kind;
        }

        public int Number { get; }

        public long Wide { get; }

        public BoundaryKind Kind { get; }

        public int Score => Number + (int)Wide + (int)Kind;

        public bool Equals(BoundaryPayload other)
        {
            return Number == other.Number && Wide == other.Wide && Kind == other.Kind;
        }

        public override bool Equals(object? obj)
        {
            return obj is BoundaryPayload other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Number;
                hash = (hash * 397) ^ Wide.GetHashCode();
                return (hash * 397) ^ Kind.GetHashCode();
            }
        }
    }

    public delegate int BoundaryCallback(int left, int right);

    public interface IBoundaryService
    {
        int Calculate(int value);

        BoundaryPayload Transform(BoundaryPayload value);

        string Describe(string prefix);
    }

    public interface IBoundaryValue
    {
        int GetValue();
    }

    public interface IGenericBoundary<T>
    {
        T Convert(T value);
    }

    public interface IGenericSink<T>
    {
        void Store(T value);
    }

    public sealed class AotGenericBoundary<T> : IGenericBoundary<T>, IGenericSink<T>
    {
        public T Convert(T value)
        {
            return value;
        }

        public void Store(T value)
        {
            AotBoundaryHost.Sink(value);
        }
    }

    public class AotGenericVirtualBase<T>
    {
        public virtual T Transform(T value)
        {
            return value;
        }

        public virtual void Store(T value)
        {
            AotBoundaryHost.Sink(value);
        }
    }

    public sealed class AotGenericVirtualDerived<T> : AotGenericVirtualBase<T>
    {
        public override T Transform(T value)
        {
            return value;
        }

        public override void Store(T value)
        {
            AotBoundaryHost.Sink(value);
        }
    }

    public sealed class AotGenericBox<T>
    {
        public AotGenericBox(T value)
        {
            Value = value;
        }

        public T Value { get; set; }
    }

    public abstract class BoundaryBase
    {
        protected BoundaryBase(int offset)
        {
            Offset = offset;
        }

        protected int Offset { get; }

        public virtual int Process(int value)
        {
            return value + Offset;
        }

        public abstract string Name { get; }

        public virtual T ProcessGeneric<T>(T value)
        {
            return value;
        }
    }

    public static class AotBoundaryHost
    {
        private static int s_genericSinkCount;

        public static int Add(int left, int right)
        {
            return left + right;
        }

        public static int CallInterface(IBoundaryService service, int value)
        {
            return service.Calculate(value);
        }

        public static BoundaryPayload CallInterfaceWithStruct(IBoundaryService service, BoundaryPayload value)
        {
            return service.Transform(value);
        }

        public static string CallInterfaceWithString(IBoundaryService service, string prefix)
        {
            return service.Describe(prefix);
        }

        public static int CallVirtual(BoundaryBase service, int value)
        {
            return service.Process(value);
        }

        public static string ReadVirtualProperty(BoundaryBase service)
        {
            return service.Name;
        }

        public static int CallDelegate(BoundaryCallback callback, int left, int right)
        {
            return callback(left, right);
        }

        public static int CallFunc(Func<int, int> callback, int value)
        {
            return callback(value);
        }

        public static T Echo<T>(T value)
        {
            return value;
        }

        public static void Sink<T>(T value)
        {
            s_genericSinkCount++;
            GC.KeepAlive(value);
        }

        public static void ResetGenericSinkCount()
        {
            s_genericSinkCount = 0;
        }

        public static int GetGenericSinkCount()
        {
            return s_genericSinkCount;
        }

        public static int CallConstrained<T>(T value) where T : struct, IBoundaryValue
        {
            return value.GetValue();
        }

        public static void Swap<T>(ref T left, ref T right)
        {
            T temporary = left;
            left = right;
            right = temporary;
        }

        public static T CallGenericDelegate<T>(Func<T, T> callback, T value)
        {
            return callback(value);
        }

        public static T CallGenericInterface<T>(IGenericBoundary<T> service, T value)
        {
            return service.Convert(value);
        }

        public static T CallGenericVirtual<T>(BoundaryBase service, T value)
        {
            return service.ProcessGeneric(value);
        }

        public static int SumArray(int[] values)
        {
            int sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return sum;
        }

        public static void Mutate(ref BoundaryPayload value, out int score)
        {
            value = new BoundaryPayload(value.Number + 2, value.Wide + 4, BoundaryKind.Beta);
            score = value.Score;
        }

        public static int CatchInterpreterException(Func<int> callback)
        {
            try
            {
                return callback();
            }
            catch (InvalidOperationException)
            {
                return 91;
            }
        }

        public static int ThrowAotException()
        {
            throw new ArgumentException("aot-boundary");
        }

        public static object BoxPayload(BoundaryPayload value)
        {
            return value;
        }
    }
}
