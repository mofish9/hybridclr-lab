using System;
using System.Collections.Generic;
using System.Globalization;

namespace HybridCLR.Lab.ManagedCases
{
    public static class CaseRegistry
    {
        private static readonly CaseDefinition[] Cases =
        {
            new CaseDefinition(
                "integer_sum_of_squares",
                "numeric",
                IntegerSumOfSquares,
                "333833500"),
            new CaseDefinition(
                "checked_overflow",
                "numeric",
                CheckedOverflow,
                null,
                null,
                "System.OverflowException"),
            new CaseDefinition(
                "float_bit_pattern",
                "numeric",
                FloatBitPattern,
                "40980000"),
            new CaseDefinition(
                "struct_ref_out",
                "value-type",
                StructRefOut,
                "8,2",
                "10"),
            new CaseDefinition(
                "array_and_string",
                "array",
                ArrayAndString,
                "1,2,3,4,5",
                "15"),
            new CaseDefinition(
                "generic_value_type",
                "generic",
                GenericValueType,
                "11,7",
                "18"),
            new CaseDefinition(
                "virtual_interface_call",
                "dispatch",
                VirtualInterfaceCall,
                "39"),
            new CaseDefinition(
                "delegate_call",
                "dispatch",
                DelegateCall,
                "42"),
            new CaseDefinition(
                "boxing_unboxing",
                "value-type",
                BoxingUnboxing,
                "32"),
            new CaseDefinition(
                "exception_finally",
                "exception",
                ExceptionFinally,
                "16")
        };

        public static IReadOnlyList<CaseDefinition> All => Cases;

        private static CaseObservation IntegerSumOfSquares()
        {
            long sum = 0;
            for (int i = 1; i <= 1000; i++)
            {
                sum += (long)i * i;
            }

            return new CaseObservation(sum.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation CheckedOverflow()
        {
            int one = GetOne();
            int value = checked(int.MaxValue + one);
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static int GetOne()
        {
            return 1;
        }

        private static CaseObservation FloatBitPattern()
        {
            float value = 1.5f * 3.25f - 0.125f;
            int bits = BitConverter.SingleToInt32Bits(value);
            return new CaseObservation(bits.ToString("X8", CultureInfo.InvariantCulture));
        }

        private static CaseObservation StructRefOut()
        {
            Point point = new Point(3, 4);
            Move(ref point, 5, -2, out int sum);
            return new CaseObservation(
                point.X.ToString(CultureInfo.InvariantCulture) + "," + point.Y.ToString(CultureInfo.InvariantCulture),
                sum.ToString(CultureInfo.InvariantCulture));
        }

        private static void Move(ref Point point, int deltaX, int deltaY, out int sum)
        {
            point.X += deltaX;
            point.Y += deltaY;
            sum = point.X + point.Y;
        }

        private static CaseObservation ArrayAndString()
        {
            int[] values = { 5, 1, 4, 2, 3 };
            Array.Sort(values);
            int sum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                sum += values[i];
            }

            return new CaseObservation(string.Join(",", values), sum.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation GenericValueType()
        {
            Pair<int> pair = new Pair<int>(7, 11);
            Swap(ref pair.First, ref pair.Second);
            return new CaseObservation(
                pair.First.ToString(CultureInfo.InvariantCulture) + "," + pair.Second.ToString(CultureInfo.InvariantCulture),
                (pair.First + pair.Second).ToString(CultureInfo.InvariantCulture));
        }

        private static void Swap<T>(ref T left, ref T right)
        {
            T temporary = left;
            left = right;
            right = temporary;
        }

        private static CaseObservation VirtualInterfaceCall()
        {
            ICalculator calculator = new SquareCalculator();
            return new CaseObservation(calculator.Calculate(6).ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation DelegateCall()
        {
            Func<int, int, int> operation = Add;
            return new CaseObservation(operation(12, 30).ToString(CultureInfo.InvariantCulture));
        }

        private static int Add(int left, int right)
        {
            return left + right;
        }

        private static CaseObservation BoxingUnboxing()
        {
            object boxed = new TinyValue(27);
            TinyValue value = (TinyValue)boxed;
            return new CaseObservation((value.Value + 5).ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation ExceptionFinally()
        {
            int state = 1;
            try
            {
                state *= 3;
                throw new InvalidOperationException("expected");
            }
            catch (InvalidOperationException)
            {
                state += 5;
            }
            finally
            {
                state *= 2;
            }

            return new CaseObservation(state.ToString(CultureInfo.InvariantCulture));
        }

        private struct Point
        {
            public Point(int x, int y)
            {
                X = x;
                Y = y;
            }

            public int X;

            public int Y;
        }

        private struct Pair<T>
        {
            public Pair(T first, T second)
            {
                First = first;
                Second = second;
            }

            public T First;

            public T Second;
        }

        private readonly struct TinyValue
        {
            public TinyValue(int value)
            {
                Value = value;
            }

            public int Value { get; }
        }

        private interface ICalculator
        {
            int Calculate(int value);
        }

        private abstract class CalculatorBase
        {
            public abstract int Calculate(int value);
        }

        private sealed class SquareCalculator : CalculatorBase, ICalculator
        {
            public override int Calculate(int value)
            {
                return value * value + 3;
            }
        }
    }
}

