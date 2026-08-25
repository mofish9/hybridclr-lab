using System;
using System.Collections.Generic;
using System.Globalization;
using HybridCLR.Lab.BoundaryContracts;

#if HYBRIDCLR_AOT_BENCHMARK
namespace HybridCLR.Lab.ManagedCasesAot
#else
namespace HybridCLR.Lab.ManagedCases
#endif
{
    public sealed class PerformanceWorkloadDefinition
    {
        public PerformanceWorkloadDefinition(
            string id,
            string category,
            int coldIterations,
            int iterations,
            params string[] features)
        {
            Id = id;
            Category = category;
            ColdIterations = coldIterations;
            Iterations = iterations;
            Features = features;
        }

        public string Id { get; }

        public string Category { get; }

        public int ColdIterations { get; }

        public int Iterations { get; }

        public IReadOnlyList<string> Features { get; }
    }

    public static class PerformanceWorkload
    {
        private static readonly PerformanceWorkloadDefinition[] Definitions =
        {
            new PerformanceWorkloadDefinition("interp_arithmetic", "interpreter", 1, 1000000, "arithmetic", "loop"),
            new PerformanceWorkloadDefinition("interp_branch", "interpreter", 16, 1000000, "branch", "switch"),
            new PerformanceWorkloadDefinition("interp_array", "interpreter", 64, 1000000, "array", "ldelem", "stelem"),
            new PerformanceWorkloadDefinition("interp_call", "interpreter", 1, 1000000, "call", "interp-to-interp"),
            new PerformanceWorkloadDefinition("interp_field", "interpreter", 1, 1000000, "field", "instance-call"),
            new PerformanceWorkloadDefinition("interp_float", "interpreter", 1, 2000000, "floating-point", "loop"),
            new PerformanceWorkloadDefinition("interp_struct", "interpreter", 1, 500000, "valuetype", "field"),
            new PerformanceWorkloadDefinition("interp_generic", "interpreter", 1, 2000000, "generic", "valuetype"),
            new PerformanceWorkloadDefinition("interp_virtual", "interpreter", 1, 1000000, "virtual", "interface"),
            new PerformanceWorkloadDefinition("interp_delegate", "interpreter", 1, 1000000, "delegate", "dispatch"),
            new PerformanceWorkloadDefinition("interp_exception", "interpreter", 8, 100000, "exception", "finally"),
            new PerformanceWorkloadDefinition("interp_to_aot_boundary", "boundary", 1, 2000000, "interp-to-aot", "static-call"),
            new PerformanceWorkloadDefinition("aot_to_interp_boundary", "boundary", 1, 1000000, "aot-to-interp", "delegate"),
            new PerformanceWorkloadDefinition("fgs_static_bridge", "full-generic-sharing", 1, 200000, "interp-to-aot", "static-call", "struct"),
            new PerformanceWorkloadDefinition("fgs_virtual_bridge", "full-generic-sharing", 1, 200000, "interp-to-aot", "interface", "struct"),
            new PerformanceWorkloadDefinition("fgs_delegate_bridge", "full-generic-sharing", 1, 200000, "interp-to-aot", "delegate", "struct"),
            new PerformanceWorkloadDefinition("fgs_calli_bridge", "full-generic-sharing", 1, 200000, "interp-to-aot", "calli", "struct"),
            new PerformanceWorkloadDefinition("interp_string_allocation", "allocation", 1, 300000, "string", "allocation"),
            new PerformanceWorkloadDefinition("interp_boxing", "optimizer-pattern", 1, 500000, "boxing", "int32", "non-escaping", "elidable"),
            new PerformanceWorkloadDefinition("interp_boxing_escape", "allocation", 1, 500000, "boxing", "int32", "escaping", "object-array"),
            new PerformanceWorkloadDefinition("interp_boxing_mixed", "allocation", 1, 500000, "boxing", "int32", "mixed-escape"),
        };

        public static IReadOnlyList<PerformanceWorkloadDefinition> All => Definitions;

        public static long Execute(string id, int iterations)
        {
            if (iterations < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(iterations));
            }

            switch (id)
            {
                case "interp_arithmetic": return Arithmetic(iterations);
                case "interp_branch": return Branch(iterations);
                case "interp_array": return Array(iterations);
                case "interp_call": return Call(iterations);
                case "interp_field": return Field(iterations);
                case "interp_float": return Float(iterations);
                case "interp_struct": return Struct(iterations);
                case "interp_generic": return Generic(iterations);
                case "interp_virtual": return Virtual(iterations);
                case "interp_delegate": return Delegate(iterations);
                case "interp_exception": return Exception(iterations);
                case "interp_to_aot_boundary": return Boundary(iterations);
                case "aot_to_interp_boundary": return ReverseBoundary(iterations);
                case "fgs_static_bridge": return FgsStaticBridge(iterations);
                case "fgs_virtual_bridge": return FgsVirtualBridge(iterations);
                case "fgs_delegate_bridge": return FgsDelegateBridge(iterations);
                case "fgs_calli_bridge": return FgsCalliBridge(iterations);
            case "interp_string_allocation": return StringAllocation(iterations);
            case "interp_boxing": return Boxing(iterations);
            case "interp_boxing_escape": return BoxingEscape(iterations);
            case "interp_boxing_mixed": return BoxingMixed(iterations);
            default: throw new ArgumentException("Unknown performance workload: " + id, nameof(id));
            }
        }

        private static long Arithmetic(int iterations)
        {
            int value = 17;
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                value = unchecked(value * 1664525 + 1013904223);
                total += value ^ (value >> 13);
            }

            return total;
        }

        private static long Branch(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                switch ((i * 7) & 15)
                {
                    case 0:
                    case 1:
                        total += i + 3;
                        break;
                    case 2:
                    case 3:
                    case 4:
                        total -= i - 5;
                        break;
                    case 8:
                    case 12:
                        total ^= i;
                        break;
                    default:
                        total += (i & 1) == 0 ? 11 : -7;
                        break;
                }
            }

            return total;
        }

        private static long Array(int iterations)
        {
            int[] values = new int[64];
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                int writeIndex = i & 63;
                int readIndex = (i * 13) & 63;
                values[writeIndex] = i + 3;
                total += values[readIndex];
            }

            return total + values[0];
        }

        private static long Call(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += LeafCall(i);
            }

            return total;
        }

        private static long Field(int iterations)
        {
            FieldState state = new FieldState(7);
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += state.Advance(i);
            }

            return total + state.Value;
        }

        private static long Float(int iterations)
        {
            double value = 1.25;
            for (int i = 0; i < iterations; i++)
            {
                value = value * 1.000001 + (i & 7) * 0.125;
            }

            return (long)(value * 1000.0);
        }

        private static long Struct(int iterations)
        {
            WorkloadValue value = new WorkloadValue(3, 5);
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                value = value.Advance(i);
                total += value.Score;
            }

            return total;
        }

        private static long Generic(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += GenericIdentity(i) + 1;
            }

            return total;
        }

        private static long FgsStaticBridge(int iterations)
        {
            BridgeValue value = new BridgeValue(3, 5, 7.5, 11);
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                value = AotBoundaryHost.Echo(value.Advance(i));
                total = unchecked(total + value.Checksum);
            }
            return total;
        }

        private static long FgsVirtualBridge(int iterations)
        {
            IGenericBoundary<BridgeValue> service = new AotGenericBoundary<BridgeValue>();
            BridgeValue value = new BridgeValue(3, 5, 7.5, 11);
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                value = service.Convert(value.Advance(i));
                total = unchecked(total + value.Checksum);
            }
            return total;
        }

        private static long FgsDelegateBridge(int iterations)
        {
            Func<BridgeValue, BridgeValue> echo = AotBoundaryHost.Echo<BridgeValue>;
            BridgeValue value = new BridgeValue(3, 5, 7.5, 11);
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                value = echo(value.Advance(i));
                total = unchecked(total + value.Checksum);
            }
            return total;
        }

        private static unsafe long FgsCalliBridge(int iterations)
        {
            delegate* managed<BridgeValue, BridgeValue> echo = &AotBoundaryHost.Echo<BridgeValue>;
            BridgeValue value = new BridgeValue(3, 5, 7.5, 11);
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                value = echo(value.Advance(i));
                total = unchecked(total + value.Checksum);
            }
            return total;
        }

        private static long Virtual(int iterations)
        {
            IWorkloadOperation operation = new MultiplyOperation();
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += operation.Apply(i);
            }

            return total;
        }

        private static long Delegate(int iterations)
        {
            Func<int, int> callback = AddOne;
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += callback(i);
            }

            return total;
        }

        private static long Exception(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    if ((i & 7) == 0)
                    {
                        throw new InvalidOperationException();
                    }

                    total += i;
                }
                catch (InvalidOperationException)
                {
                    total -= i;
                }
                finally
                {
                    total += 1;
                }
            }

            return total;
        }

        private static long Boundary(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += AotBoundaryHost.Add(i, 3);
            }

            return total;
        }

        private static long ReverseBoundary(int iterations)
        {
            Func<int, int> callback = AddOne;
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                total += AotBoundaryHost.CallFunc(callback, i);
            }

            return total;
        }

        private static long StringAllocation(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                string value = (i * 17).ToString(CultureInfo.InvariantCulture);
                total += value.Length;
            }

            return total;
        }

        private static long Boxing(int iterations)
        {
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                object boxed = i;
                total += (int)boxed;
            }

            return total;
        }

        private static long BoxingEscape(int iterations)
        {
            object[] sink = new object[64];
            long total = 0;
            for (int i = 0; i < iterations; i++)
            {
                int slot = i & (sink.Length - 1);
                sink[slot] = i;
                total += (int)sink[slot];
            }

            return total;
        }

        private static long BoxingMixed(int iterations)
        {
            int escapingIterations = Math.Max(1, iterations / 8);
            return Boxing(iterations - escapingIterations) + BoxingEscape(escapingIterations);
        }

        private static int AddOne(int value)
        {
            return value + 1;
        }

        private static int LeafCall(int value)
        {
            return unchecked(value * 3 + 7);
        }

        private sealed class FieldState
        {
            public FieldState(int value)
            {
                Value = value;
            }

            public int Value;

            public int Advance(int delta)
            {
                Value = unchecked(Value * 33 + delta);
                return Value;
            }
        }

        private readonly struct WorkloadValue
        {
            public WorkloadValue(int left, int right)
            {
                Left = left;
                Right = right;
            }

            public int Left { get; }

            public int Right { get; }

            public int Score => Left ^ Right;

            public WorkloadValue Advance(int value)
            {
                return new WorkloadValue(unchecked(Left + value + 1), unchecked(Right * 3 + value));
            }
        }

        private interface IWorkloadOperation
        {
            int Apply(int value);
        }

        private sealed class MultiplyOperation : IWorkloadOperation
        {
            public int Apply(int value)
            {
                return value * 3 + 1;
            }
        }

        private static T GenericIdentity<T>(T value) where T : struct
        {
            return value;
        }

        private readonly struct BridgeValue
        {
            public BridgeValue(int first, long wide, double floating, short last)
            {
                First = first;
                Wide = wide;
                Floating = floating;
                Last = last;
            }

            public int First { get; }
            public long Wide { get; }
            public double Floating { get; }
            public short Last { get; }

            public long Checksum => unchecked(First + Wide + (long)Floating + Last);

            public BridgeValue Advance(int value)
            {
                return new BridgeValue(
                    unchecked(First + (value & 3) + 1),
                    unchecked(Wide + (value & 7) + 1),
                    Floating + (value & 1) + 0.25,
                    unchecked((short)(Last + (value & 1))));
            }
        }
    }
}
