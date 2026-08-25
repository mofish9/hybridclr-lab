using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        public static string RuntimeTarget = string.Empty;

        private static readonly CaseDefinition[] Cases = CreateCases();

        private static CaseDefinition[] CreateCases()
        {
            List<CaseDefinition> cases = new List<CaseDefinition>
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
                "delegate_loop_reassignment",
                "dispatch",
                DelegateLoopReassignment,
                "13"),
            new CaseDefinition(
                "delegate_covariant_return",
                "dispatch",
                DelegateCovariantReturn,
                "hybrid"),
            new CaseDefinition(
                "delegate_result_branch",
                "dispatch",
                DelegateResultBranch,
                "positive"),
            new CaseDefinition(
                "boxing_unboxing",
                "value-type",
                BoxingUnboxing,
                "32"),
            new CaseDefinition(
                "boxing_unboxing_safepoint",
                "value-type",
                BoxingUnboxingAcrossSafepoint,
                "42",
                features: new[] { "boxing", "unboxing", "gc-safepoint" }),
            new CaseDefinition(
                "exception_finally",
                "exception",
                ExceptionFinally,
                "16"),
            new CaseDefinition(
                "math_divrem",
                "numeric",
                MathDivRem,
                "184",
                "17"),
            new CaseDefinition(
                "bitwise_shift",
                "numeric",
                BitwiseShift,
                "105"),
            new CaseDefinition(
                "string_formatting",
                "string",
                StringFormatting,
                "hybrid:07:3"),
            new CaseDefinition(
                "nullable_value_type",
                "value-type",
                NullableValueType,
                "20",
                "True,False"),
            new CaseDefinition(
                "enum_switch",
                "control-flow",
                EnumSwitch,
                "4",
                "Screen"),
            new CaseDefinition(
                "array_copy_resize",
                "array",
                ArrayCopyResize,
                "1,2,3,0,0",
                "1,2,3,4"),
            new CaseDefinition(
                "jagged_array",
                "array",
                JaggedArray,
                "2,4;1,3,5",
                "15"),
            new CaseDefinition(
                "multidimensional_array",
                "array",
                MultidimensionalArray,
                "21",
                "6"),
            new CaseDefinition(
                "generic_reference_type",
                "generic",
                GenericReferenceType,
                "right,left",
                "9"),
            new CaseDefinition(
                "generic_interface_constraint",
                "generic",
                GenericInterfaceConstraint,
                "21",
                "15"),
            new CaseDefinition(
                "generic_method_roundtrip",
                "generic",
                GenericMethodRoundtrip,
                "lab,41",
                "2,5"),
            new CaseDefinition(
                "base_virtual_chain",
                "dispatch",
                BaseVirtualChain,
                "18",
                "TriplingStage"),
            new CaseDefinition(
                "delegate_multicast",
                "dispatch",
                DelegateMulticast,
                "12",
                "AB"),
            new CaseDefinition(
                "lambda_capture",
                "dispatch",
                LambdaCapture,
                "13,17"),
            new CaseDefinition(
                "nested_try_catch",
                "exception",
                NestedTryCatch,
                "31"),
            new CaseDefinition(
                "catch_filter",
                "exception",
                CatchFilter,
                "ArgumentException",
                "filtered"),
            new CaseDefinition(
                "throw_rethrow",
                "exception",
                ThrowRethrow,
                "24",
                "rethrow"),
            new CaseDefinition(
                "recursive_factorial",
                "control-flow",
                RecursiveFactorial,
                "720"),
            new CaseDefinition(
                "dictionary_lookup",
                "collection",
                DictionaryLookup,
                "15",
                "3"),
            new CaseDefinition(
                "list_foreach",
                "collection",
                ListForeach,
                "1,2,3,4",
                "10"),
            new CaseDefinition(
                "double_precision_math",
                "numeric",
                DoublePrecisionMath,
                "3FE3333333333334"),
            new CaseDefinition(
                "unchecked_wraparound",
                "numeric",
                UncheckedWraparound,
                "-2147483647"),
            new CaseDefinition(
                "struct_interface_dispatch",
                "dispatch",
                StructInterfaceDispatch,
                "20",
                "15"),
            new CaseDefinition(
                "reflection_invoke",
                "reflection",
                ReflectionInvoke,
                "21",
                "ReflectionTarget"),
            new CaseDefinition(
                "static_constructor_once",
                "static",
                StaticConstructorOnce,
                "1",
                "1"),
            new CaseDefinition(
                "iterator_enumeration",
                "iterator",
                IteratorEnumeration,
                "1,2,3,4",
                "10"),
            new CaseDefinition(
                "array_covariance",
                "array",
                ArrayCovariance,
                "alpha,gamma",
                "String[]"),
            new CaseDefinition(
                "generic_nested_combo",
                "generic",
                GenericNestedCombo,
                "2,5",
                "3,7"),
            new CaseDefinition(
                "string_switch",
                "control-flow",
                StringSwitch,
                "2",
                "beta"),
            new CaseDefinition(
                "instance_delegate",
                "dispatch",
                InstanceDelegate,
                "22",
                "Accumulator"),
            new CaseDefinition(
                "thread_join",
                "thread",
                ThreadJoin,
                "23"),
                new CaseDefinition(
                "async_completed_task",
                "async",
                AsyncCompletedTask,
                "34")
            };

            RegisterNumericAndControlFlowCases(cases);
            RegisterArrayCollectionAndStringCases(cases);
            RegisterGenericAndValueTypeCases(cases);
            RegisterExceptionAndRuntimeCases(cases);
            RegisterReflectionAsyncAndThreadCases(cases);
            RegisterInteropConcurrencyCases(cases);
            RegisterBoundaryCases(cases);
            RegisterOptimizationContractCases(cases);
            return cases.ToArray();
        }

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

        private static CaseObservation DelegateLoopReassignment()
        {
            Func<int, int> operation = Increment;
            int total = 0;
            for (int i = 0; i < 4; i++)
            {
                total += operation(i);
                operation = Double;
            }
            return new CaseObservation(total.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation DelegateCovariantReturn()
        {
            Func<string> stringFactory = CreateHybridString;
            Func<object> objectFactory = stringFactory;
            return new CaseObservation(objectFactory().ToString());
        }

        private static CaseObservation DelegateResultBranch()
        {
            Func<int, bool> predicate = IsPositive;
            return new CaseObservation(predicate(3) ? "positive" : "negative");
        }

        private static int Increment(int value) => value + 1;

        private static int Double(int value) => value * 2;

        private static string CreateHybridString() => "hybrid";

        private static bool IsPositive(int value) => value > 0;

        private static CaseObservation BoxingUnboxing()
        {
            object boxed = new TinyValue(27);
            TinyValue value = (TinyValue)boxed;
            return new CaseObservation((value.Value + 5).ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation BoxingUnboxingAcrossSafepoint()
        {
            object boxed = 42;
            GC.Collect();
            return new CaseObservation(((int)boxed).ToString(CultureInfo.InvariantCulture));
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

        private static CaseObservation MathDivRem()
        {
            int quotient = Math.DivRem(12345, 67, out int remainder);
            return new CaseObservation(
                quotient.ToString(CultureInfo.InvariantCulture),
                remainder.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation BitwiseShift()
        {
            int value = ((0x5A & 0x3C) << 2) | (0x12 >> 1);
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation StringFormatting()
        {
            string formatted = string.Format(CultureInfo.InvariantCulture, "{0}:{1:D2}:{2}", "hybrid", 7, 3);
            return new CaseObservation(formatted);
        }

        private static CaseObservation NullableValueType()
        {
            int? present = 12;
            int? missing = null;
            int total = present.GetValueOrDefault() + missing.GetValueOrDefault(8);
            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                present.HasValue + "," + missing.HasValue);
        }

        private static CaseObservation EnumSwitch()
        {
            BlendMode mode = BlendMode.Screen;
            int score = Score(mode) + Score(BlendMode.Add);
            return new CaseObservation(score.ToString(CultureInfo.InvariantCulture), mode.ToString());
        }

        private static int Score(BlendMode mode)
        {
            switch (mode)
            {
                case BlendMode.Add:
                    return 1;
                case BlendMode.Multiply:
                    return 2;
                case BlendMode.Screen:
                    return 3;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode));
            }
        }

        private static CaseObservation ArrayCopyResize()
        {
            int[] source = { 1, 2, 3 };
            int[] destination = new int[5];
            Array.Copy(source, destination, source.Length);
            Array.Resize(ref source, 4);
            source[3] = destination[0] + destination[2];
            return new CaseObservation(JoinInts(destination), JoinInts(source));
        }

        private static CaseObservation JaggedArray()
        {
            int[][] rows =
            {
                new[] { 2, 4 },
                new[] { 1, 3, 5 },
            };

            int total = 0;
            for (int rowIndex = 0; rowIndex < rows.Length; rowIndex++)
            {
                int[] row = rows[rowIndex];
                for (int columnIndex = 0; columnIndex < row.Length; columnIndex++)
                {
                    total += row[columnIndex];
                }
            }

            return new CaseObservation(JoinInts(rows[0]) + ";" + JoinInts(rows[1]), total.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation MultidimensionalArray()
        {
            int[,] grid =
            {
                { 1, 2, 3 },
                { 4, 5, 6 },
            };

            int total = 0;
            int diagonal = 0;
            for (int row = 0; row < grid.GetLength(0); row++)
            {
                for (int column = 0; column < grid.GetLength(1); column++)
                {
                    int value = grid[row, column];
                    total += value;
                    if (row == column)
                    {
                        diagonal += value;
                    }
                }
            }

            return new CaseObservation(total.ToString(CultureInfo.InvariantCulture), diagonal.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation GenericReferenceType()
        {
            Pair<string> pair = new Pair<string>("left", "right");
            Swap(ref pair.First, ref pair.Second);
            return new CaseObservation(
                pair.First + "," + pair.Second,
                (pair.First.Length + pair.Second.Length).ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation GenericInterfaceConstraint()
        {
            TripleTransformer transformer = new TripleTransformer();
            int first = ApplyTransform(transformer, 7);
            int second = ApplyTransform(transformer, 5);
            return new CaseObservation(
                first.ToString(CultureInfo.InvariantCulture),
                second.ToString(CultureInfo.InvariantCulture));
        }

        private static int ApplyTransform<T>(T transformer, int value) where T : struct, IIntTransformer
        {
            return transformer.Transform(value);
        }

        private static CaseObservation GenericMethodRoundtrip()
        {
            string text = Echo("lab");
            int number = Echo(41);
            Point point = Echo(new Point(2, 5));
            return new CaseObservation(
                text + "," + number.ToString(CultureInfo.InvariantCulture),
                point.X.ToString(CultureInfo.InvariantCulture) + "," + point.Y.ToString(CultureInfo.InvariantCulture));
        }

        private static T Echo<T>(T value)
        {
            return value;
        }

        private static CaseObservation BaseVirtualChain()
        {
            ArithmeticStage stage = new TriplingStage();
            return new CaseObservation(stage.Transform(4).ToString(CultureInfo.InvariantCulture), stage.GetType().Name);
        }

        private static CaseObservation DelegateMulticast()
        {
            int state = 1;
            string trace = string.Empty;

            Action first = () =>
            {
                state += 2;
                trace += "A";
            };

            Action second = () =>
            {
                state *= 4;
                trace += "B";
            };

            first += second;
            first();
            return new CaseObservation(state.ToString(CultureInfo.InvariantCulture), trace);
        }

        private static CaseObservation LambdaCapture()
        {
            int offset = 3;
            Func<int, int> addOffset = value => value + offset;
            int first = addOffset(10);
            offset = 7;
            int second = addOffset(10);
            return new CaseObservation(
                first.ToString(CultureInfo.InvariantCulture) + "," + second.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation NestedTryCatch()
        {
            int state = 0;
            try
            {
                state += 1;
                try
                {
                    state += 2;
                    throw new InvalidOperationException("inner");
                }
                catch (InvalidOperationException)
                {
                    state += 4;
                }
                finally
                {
                    state += 8;
                }
            }
            finally
            {
                state += 16;
            }

            return new CaseObservation(state.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation CatchFilter()
        {
            string marker = string.Empty;
            try
            {
                throw new ArgumentException("bad");
            }
            catch (Exception exception) when (exception is ArgumentException)
            {
                marker = exception.GetType().Name;
            }

            return new CaseObservation(marker, "filtered");
        }

        private static CaseObservation ThrowRethrow()
        {
            int state = 0;
            try
            {
                try
                {
                    state = 7;
                    throw new InvalidOperationException("rethrow");
                }
                catch
                {
                    state += 5;
                    throw;
                }
            }
            catch (InvalidOperationException)
            {
                state *= 2;
            }

            return new CaseObservation(state.ToString(CultureInfo.InvariantCulture), "rethrow");
        }

        private static CaseObservation RecursiveFactorial()
        {
            int value = Factorial(6);
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static int Factorial(int value)
        {
            if (value <= 1)
            {
                return 1;
            }

            return value * Factorial(value - 1);
        }

        private static CaseObservation DictionaryLookup()
        {
            Dictionary<string, int> scores = new Dictionary<string, int>
            {
                ["alpha"] = 3,
                ["beta"] = 5,
                ["gamma"] = 7,
            };

            int total = scores["alpha"] + scores["beta"] + scores["gamma"];
            return new CaseObservation(total.ToString(CultureInfo.InvariantCulture), scores.Count.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation ListForeach()
        {
            List<int> values = new List<int> { 4, 1, 3, 2 };
            values.Sort();
            int sum = 0;
            foreach (int value in values)
            {
                sum += value;
            }

            return new CaseObservation(JoinInts(values), sum.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation DoublePrecisionMath()
        {
            double value = 0.1d + 0.2d + 0.3d;
            long bits = BitConverter.DoubleToInt64Bits(value);
            return new CaseObservation(bits.ToString("X16", CultureInfo.InvariantCulture));
        }

        private static CaseObservation UncheckedWraparound()
        {
            int value = unchecked(int.MaxValue + 2);
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation StructInterfaceDispatch()
        {
            OffsetTransformer transformer = new OffsetTransformer(11);
            int generic = ApplyTransform(transformer, 9);
            int viaInterface = ApplyInterfaceTransform(transformer, 4);
            return new CaseObservation(
                generic.ToString(CultureInfo.InvariantCulture),
                viaInterface.ToString(CultureInfo.InvariantCulture));
        }

        private static int ApplyInterfaceTransform(IIntTransformer transformer, int value)
        {
            return transformer.Transform(value);
        }

        private static CaseObservation ReflectionInvoke()
        {
            Type targetType = typeof(ReflectionTarget);
            object instance = Activator.CreateInstance(targetType)!;
            int sum = (int)targetType
                .GetMethod(nameof(ReflectionTarget.Add), BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(instance, new object[] { 8, 13 })!;
            string name = (string)targetType
                .GetMethod(nameof(ReflectionTarget.Describe), BindingFlags.Public | BindingFlags.Instance)!
                .Invoke(instance, Array.Empty<object>())!;
            return new CaseObservation(sum.ToString(CultureInfo.InvariantCulture), name);
        }

        private static CaseObservation StaticConstructorOnce()
        {
            int first = StaticCtorProbe.Touch();
            int second = StaticCtorProbe.Touch();
            return new CaseObservation(
                first.ToString(CultureInfo.InvariantCulture),
                second.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation IteratorEnumeration()
        {
            StringBuilder builder = new StringBuilder();
            int sum = 0;
            bool first = true;
            foreach (int value in YieldSequence())
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(value.ToString(CultureInfo.InvariantCulture));
                sum += value;
                first = false;
            }

            return new CaseObservation(builder.ToString(), sum.ToString(CultureInfo.InvariantCulture));
        }

        private static IEnumerable<int> YieldSequence()
        {
            yield return 1;
            yield return 2;
            yield return 3;
            yield return 4;
        }

        private static CaseObservation ArrayCovariance()
        {
            string[] values = { "alpha", "beta" };
            object[] alias = values;
            alias[1] = "gamma";
            return new CaseObservation(string.Join(",", values), values.GetType().Name);
        }

        private static CaseObservation GenericNestedCombo()
        {
            Pair<Pair<int>> outer = new Pair<Pair<int>>(new Pair<int>(2, 3), new Pair<int>(5, 7));
            Swap(ref outer.First.Second, ref outer.Second.First);
            return new CaseObservation(
                outer.First.First.ToString(CultureInfo.InvariantCulture) + "," + outer.First.Second.ToString(CultureInfo.InvariantCulture),
                outer.Second.First.ToString(CultureInfo.InvariantCulture) + "," + outer.Second.Second.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation StringSwitch()
        {
            string tag = "beta";
            int score = tag switch
            {
                "alpha" => 1,
                "beta" => 2,
                "gamma" => 3,
                _ => 0,
            };

            return new CaseObservation(score.ToString(CultureInfo.InvariantCulture), tag);
        }

        private static CaseObservation InstanceDelegate()
        {
            Accumulator accumulator = new Accumulator(9);
            Func<int, int> add = accumulator.Add;
            int value = add(13);
            string targetType = add.Target?.GetType().Name ?? string.Empty;
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture), targetType);
        }

        private static CaseObservation ThreadJoin()
        {
            int value = 0;
            Thread thread = new Thread(() => { value = 23; });
            thread.Start();
            thread.Join();
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation AsyncCompletedTask()
        {
            int value = RunAsync().GetAwaiter().GetResult();
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static async Task<int> RunAsync()
        {
            await Task.CompletedTask;
            return 34;
        }

        private static string JoinInts(IEnumerable<int> values)
        {
            StringBuilder builder = new StringBuilder();
            bool first = true;
            foreach (int value in values)
            {
                if (!first)
                {
                    builder.Append(',');
                }

                builder.Append(value.ToString(CultureInfo.InvariantCulture));
                first = false;
            }

            return builder.ToString();
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

        private interface IIntTransformer
        {
            int Transform(int value);
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

        private enum BlendMode
        {
            Add,
            Multiply,
            Screen,
        }

        private readonly struct TripleTransformer : IIntTransformer
        {
            public int Transform(int value)
            {
                return value * 3;
            }
        }

        private readonly struct OffsetTransformer : IIntTransformer
        {
            public OffsetTransformer(int offset)
            {
                Offset = offset;
            }

            public int Offset { get; }

            public int Transform(int value)
            {
                return value + Offset;
            }
        }

        private abstract class ArithmeticStage
        {
            public virtual int Transform(int value)
            {
                return value + 2;
            }
        }

        private sealed class TriplingStage : ArithmeticStage
        {
            public override int Transform(int value)
            {
                return base.Transform(value) * 3;
            }
        }

        private sealed class ReflectionTarget
        {
            public int Add(int left, int right)
            {
                return left + right;
            }

            public string Describe()
            {
                return GetType().Name;
            }
        }

        private sealed class StaticCtorProbe
        {
            private static int TouchCount;

            static StaticCtorProbe()
            {
                TouchCount++;
            }

            public static int Touch()
            {
                return TouchCount;
            }
        }

        private sealed class Accumulator
        {
            public Accumulator(int baseValue)
            {
                BaseValue = baseValue;
            }

            public int BaseValue { get; }

            public int Add(int value)
            {
                return BaseValue + value;
            }
        }
    }
}
