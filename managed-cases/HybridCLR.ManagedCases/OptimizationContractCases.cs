using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterOptimizationContractCases(List<CaseDefinition> cases)
        {
            RegisterCase(
                cases,
                "optimizer_array_loop_range",
                "array",
                OptimizerArrayLoopRange,
                "1832",
                "63",
                features: new[] { "array", "loop", "range-analysis", "ldelem", "stelem" });
            RegisterCase(
                cases,
                "optimizer_array_branch_join",
                "array",
                OptimizerArrayBranchJoin,
                "80",
                "0",
                features: new[] { "array", "branch", "control-flow-join", "range-analysis" });
            RegisterCase(
                cases,
                "optimizer_array_exception_edges",
                "array",
                OptimizerArrayExceptionEdges,
                "null,index,negative",
                features: new[] { "array", "null", "bounds", "exception" });
            RegisterCase(
                cases,
                "optimizer_array_repeated_store_range",
                "array",
                OptimizerArrayRepeatedStoreRange,
                "2016",
                "27",
                features: new[] { "array", "range", "repeated-store", "loop" });
            RegisterCase(
                cases,
                "optimizer_array_repeated_store_negative",
                "array",
                OptimizerArrayRepeatedStoreNegative,
                "0",
                "1",
                features: new[] { "array", "range", "repeated-store", "negative", "exception" });
            RegisterCase(
                cases,
                "optimizer_array_conditional_store_default",
                "array",
                OptimizerArrayConditionalStoreDefault,
                "0",
                "1",
                features: new[] { "array", "range", "conditional-store", "initlocals", "exception" });
            RegisterCase(
                cases,
                "optimizer_array_backedge_bounds",
                "array",
                OptimizerArrayBackedgeBounds,
                "IndexOutOfRangeException",
                features: new[] { "array", "bounds", "loop", "backward-branch", "exception" });
            RegisterCase(
                cases,
                "optimizer_array_direct_store_load",
                "array",
                OptimizerArrayDirectStoreLoad,
                "120",
                "92",
                features: new[] { "array", "loop", "range-analysis", "ldelem", "stelem" });
            RegisterCase(
                cases,
                "optimizer_scalar_struct_layout",
                "value-type",
                OptimizerScalarStructLayout,
                "18,72623859790382856,3.5,-7,72623859790382870",
                features: new[] { "struct", "scalar-fields", "layout", "constructor" });
            RegisterCase(
                cases,
                "optimizer_explicit_struct_layout",
                "value-type",
                OptimizerExplicitStructLayout,
                "22,22,44,44",
                features: new[] { "struct", "explicit-layout", "overlap", "constructor" });
            RegisterCase(
                cases,
                "optimizer_four_scalar_struct_ctor",
                "value-type",
                OptimizerFourScalarStructCtor,
                "11,22,33,44,110",
                "0B00000016000000210000002C000000",
                features: new[] { "struct", "scalar-fields", "sequential-layout", "constructor" });
        }

        private static CaseObservation OptimizerArrayLoopRange()
        {
            int[] values = new int[8];
            int total = 0;
            for (int i = 0; i < 64; i++)
            {
                int writeIndex = i & 7;
                values[writeIndex] = i;
                int readIndex = (i * 3) & 7;
                total += values[readIndex];
            }

            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                values[7].ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation OptimizerArrayBranchJoin()
        {
            int[] values = { 10, 20, 30, 40 };
            int total = 0;
            int index = 0;
            for (int i = 0; i < 4; i++)
            {
                if ((i & 1) == 0)
                {
                    index = i & 3;
                }
                else
                {
                    index = (i + 1) & 3;
                }

                total += values[index];
            }

            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                index.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation OptimizerArrayExceptionEdges()
        {
            string result = string.Empty;
            int[]? nullArray = null;
            try
            {
                _ = nullArray![0];
            }
            catch (NullReferenceException)
            {
                result = "null";
            }

            int[] empty = new int[0];
            try
            {
                _ = empty[0];
            }
            catch (IndexOutOfRangeException)
            {
                result += ",index";
            }

            int[] values = new int[4];
            int negative = -1;
            try
            {
                _ = values[negative];
            }
            catch (IndexOutOfRangeException)
            {
                result += ",negative";
            }

            return new CaseObservation(result);
        }

        private static CaseObservation OptimizerArrayRepeatedStoreRange()
        {
            int[] values = new int[32];
            int total = 0;
            int index = 0;
            for (int i = 0; i < 64; i++)
            {
                index = (i * 5) & 31;
                values[index] = i;
                total += values[index];
            }

            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                index.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation OptimizerArrayRepeatedStoreNegative()
        {
            int[] values = new int[4];
            int index = 0;
            int total = 0;
            int errors = 0;
            for (int i = 0; i < 2; i++)
            {
                index = i == 0 ? 0 : -1;
                try
                {
                    total += values[index];
                }
                catch (IndexOutOfRangeException)
                {
                    errors++;
                }
            }

            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                errors.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation OptimizerArrayConditionalStoreDefault()
        {
            int[] values = new int[4];
            int index = 0;
            int total = 0;
            int errors = 0;
            for (int i = 0; i < 2; i++)
            {
                if (i == 1)
                {
                    index = 5;
                }

                try
                {
                    total += values[index];
                }
                catch (IndexOutOfRangeException)
                {
                    errors++;
                }
            }

            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                errors.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation OptimizerArrayBackedgeBounds()
        {
            try
            {
                return new CaseObservation(OptimizerArrayBackedgeBoundsCore().ToString(CultureInfo.InvariantCulture));
            }
            catch (IndexOutOfRangeException)
            {
                return new CaseObservation(nameof(IndexOutOfRangeException));
            }
        }

        private static CaseObservation OptimizerArrayDirectStoreLoad()
        {
            int[] values = new int[8];
            int total = 0;
            for (int i = 0; i < 16; i++)
            {
                values[i & 7] = i;
                total += values[i & 7];
            }

            int finalSum = 0;
            for (int i = 0; i < values.Length; i++)
            {
                finalSum += values[i];
            }
            return new CaseObservation(
                total.ToString(CultureInfo.InvariantCulture),
                finalSum.ToString(CultureInfo.InvariantCulture));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int OptimizerArrayBackedgeBoundsCore()
        {
            int[] values = new int[4];
            int index = 0;
            int total = 0;
            int iteration = 0;
            while (iteration < 2)
            {
                total += values[index];
                index = iteration == 0 ? 5 : 0;
                iteration++;
            }
            return total;
        }

        private static CaseObservation OptimizerScalarStructLayout()
        {
            ScalarLayoutValue value = new ScalarLayoutValue(18, 72623859790382856L, 3.5, -7);
            int size = Marshal.SizeOf<ScalarLayoutValue>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, buffer, false);
                byte[] raw = new byte[size];
                Marshal.Copy(buffer, raw, 0, size);
                return new CaseObservation(
                    value.Format() + "|size=" + size.ToString(CultureInfo.InvariantCulture) +
                    "|bytes=" + BitConverter.ToString(raw).Replace("-", string.Empty));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        private static CaseObservation OptimizerExplicitStructLayout()
        {
            ExplicitLayoutValue value = new ExplicitLayoutValue(11, 22, 33, 44);
            return new CaseObservation(value.Format());
        }

        private static CaseObservation OptimizerFourScalarStructCtor()
        {
            FourScalarValue value = new FourScalarValue(11, 22, 33, 44);
            int size = Marshal.SizeOf<FourScalarValue>();
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(value, buffer, false);
                byte[] raw = new byte[size];
                Marshal.Copy(buffer, raw, 0, size);
                return new CaseObservation(
                    value.Format(),
                    BitConverter.ToString(raw).Replace("-", string.Empty));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 32)]
        private readonly struct ScalarLayoutValue
        {
            public ScalarLayoutValue(byte first, long wide, double floating, short last)
            {
                _first = first;
                _wide = wide;
                _floating = floating;
                _last = last;
            }

            [FieldOffset(0)]
            private readonly byte _first;

            [FieldOffset(8)]
            private readonly long _wide;

            [FieldOffset(16)]
            private readonly double _floating;

            [FieldOffset(24)]
            private readonly short _last;

            public byte First => _first;

            public long Wide => _wide;

            public double Floating => _floating;

            public short Last => _last;

            public string Format()
            {
                long checksum = First + Wide + (long)Floating + Last;
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4}",
                    First,
                    Wide,
                    Floating,
                    Last,
                    checksum);
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 8)]
        private struct ExplicitLayoutValue
        {
            [FieldOffset(0)]
            public int First;

            [FieldOffset(0)]
            public int Overlap;

            [FieldOffset(4)]
            public int Second;

            [FieldOffset(4)]
            public int Tail;

            public ExplicitLayoutValue(int first, int overlap, int second, int tail)
            {
                First = first;
                Overlap = overlap;
                Second = second;
                Tail = tail;
            }

            public string Format()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3}",
                    First,
                    Overlap,
                    Second,
                    Tail);
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 4)]
        private readonly struct FourScalarValue
        {
            private readonly int _first;
            private readonly int _second;
            private readonly int _third;
            private readonly int _fourth;

            public FourScalarValue(int first, int second, int third, int fourth)
            {
                _first = first;
                _second = second;
                _third = third;
                _fourth = fourth;
            }

            public string Format()
            {
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "{0},{1},{2},{3},{4}",
                    _first,
                    _second,
                    _third,
                    _fourth,
                    _first + _second + _third + _fourth);
            }
        }
    }
}
