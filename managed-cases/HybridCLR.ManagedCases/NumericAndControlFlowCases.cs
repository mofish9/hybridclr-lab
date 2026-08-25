using System;
using System.Collections.Generic;
using System.Globalization;

namespace HybridCLR.Lab.ManagedCases
{
    public static partial class CaseRegistry
    {
        private static void RegisterNumericAndControlFlowCases(List<CaseDefinition> cases)
        {
            RegisterCase(cases, "signed_division_remainder", "numeric", SignedDivisionRemainder, "-3,-2", features: new[] { "div", "rem", "int32" });
            RegisterCase(cases, "unsigned_wide_arithmetic", "numeric", UnsignedWideArithmetic, "18446744073709551612", features: new[] { "uint64", "sub", "add" });
            RegisterCase(cases, "signed_conversion_chain", "numeric", SignedConversionChain, "254,-2,65534", features: new[] { "conv", "sign-extension" });
            RegisterCase(cases, "checked_multiply_overflow", "numeric", CheckedMultiplyOverflow, null, expectedExceptionType: "System.OverflowException", features: new[] { "mul.ovf" });
            RegisterCase(cases, "float_nan_comparison", "numeric", FloatNaNComparison, "False,True,True", features: new[] { "float32", "nan", "comparison" });
            RegisterCase(cases, "float_negative_zero", "numeric", FloatNegativeZero, "80000000", features: new[] { "float32", "bit-pattern" });
            RegisterCase(cases, "double_infinity", "numeric", DoubleInfinity, "True,True", features: new[] { "float64", "infinity" });
            RegisterCase(cases, "decimal_arithmetic", "numeric", DecimalArithmetic, "37.0", features: new[] { "decimal", "value-type-call" });
            RegisterCase(cases, "numeric_truncating_cast", "numeric", NumericTruncatingCast, "3,-3", features: new[] { "conv.i4", "float64" });
            RegisterCase(cases, "long_shift_masking", "numeric", LongShiftMasking, "2,-4", features: new[] { "shl", "shr", "int64" });
            RegisterCase(cases, "unsigned_comparison", "numeric", UnsignedComparison, "True,True", features: new[] { "comparison.un", "uint32" });
            RegisterCase(cases, "dense_integer_switch", "control-flow", DenseIntegerSwitch, "39", features: new[] { "switch", "dense" });
            RegisterCase(cases, "sparse_integer_switch", "control-flow", SparseIntegerSwitch, "49", features: new[] { "branch", "sparse-switch" });
            RegisterCase(cases, "nested_break_continue", "control-flow", NestedBreakContinue, "91", features: new[] { "loop", "break", "continue" });
            RegisterCase(cases, "short_circuit_side_effect", "control-flow", ShortCircuitSideEffect, "False,True", "1", features: new[] { "brfalse", "brtrue" });
            RegisterCase(cases, "null_coalescing_chain", "control-flow", NullCoalescingChain, "fallback", features: new[] { "null", "coalesce" });
            RegisterCase(cases, "conditional_expression", "control-flow", ConditionalExpression, "odd:9", features: new[] { "conditional", "branch" });
            RegisterCase(cases, "mutual_recursion", "control-flow", MutualRecursion, "True,True", features: new[] { "call", "recursion" });
            RegisterCase(cases, "character_arithmetic", "numeric", CharacterArithmetic, "937,939", features: new[] { "char", "conv.u2" });
            RegisterCase(cases, "do_while_accumulation", "control-flow", DoWhileAccumulation, "55", features: new[] { "loop", "backward-branch" });
            RegisterCase(cases, "performance_arithmetic_loop_regression", "control-flow", PerformanceArithmeticLoopRegression, "1073851942304517", features: new[] { "loop", "backward-branch", "ldloc-copy-propagation" });
            RegisterCase(cases, "interface_branch_join_dispatch", "dispatch", InterfaceBranchJoinDispatch, "16,15", features: new[] { "interface", "branch", "control-flow-join" });
            RegisterCase(cases, "interface_loop_type_change", "dispatch", InterfaceLoopTypeChange, "14", features: new[] { "interface", "loop", "backward-branch" });
            RegisterCase(cases, "interface_eval_stack_branch_join", "dispatch", InterfaceEvalStackBranchJoin, "16,15", features: new[] { "interface", "branch", "evaluation-stack" });
            RegisterCase(cases, "delegate_eval_stack_branch_join", "dispatch", DelegateEvalStackBranchJoin, "16,15", features: new[] { "delegate", "branch", "evaluation-stack" });
        }

        private static CaseObservation SignedDivisionRemainder()
        {
            int value = -17;
            return Observation(value / 5, value % 5);
        }

        private static CaseObservation UnsignedWideArithmetic()
        {
            ulong value = ulong.MaxValue - 10UL;
            value += 7UL;
            return new CaseObservation(value.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation SignedConversionChain()
        {
            int source = -2;
            byte unsignedByte = unchecked((byte)source);
            sbyte signedByte = unchecked((sbyte)unsignedByte);
            ushort unsignedShort = unchecked((ushort)signedByte);
            return new CaseObservation(FormattableString.Invariant($"{unsignedByte},{signedByte},{unsignedShort}"));
        }

        private static CaseObservation CheckedMultiplyOverflow()
        {
            int value = GetLargeValue();
            return new CaseObservation(checked(value * value).ToString(CultureInfo.InvariantCulture));
        }

        private static int GetLargeValue()
        {
            return 50000;
        }

        private static CaseObservation FloatNaNComparison()
        {
            float nan = float.NaN;
            float other = BitConverter.Int32BitsToSingle(unchecked((int)0x7FC00000));
            return new CaseObservation(FormattableString.Invariant($"{nan == other},{nan != other},{float.IsNaN(nan)}"));
        }

        private static CaseObservation FloatNegativeZero()
        {
            int bits = BitConverter.SingleToInt32Bits(-0.0f);
            return new CaseObservation(bits.ToString("X8", CultureInfo.InvariantCulture));
        }

        private static CaseObservation DoubleInfinity()
        {
            double positive = 1.0d / GetPositiveZero();
            double negative = -1.0d / GetPositiveZero();
            return new CaseObservation(FormattableString.Invariant($"{double.IsPositiveInfinity(positive)},{double.IsNegativeInfinity(negative)}"));
        }

        private static double GetPositiveZero()
        {
            return 0.0d;
        }

        private static CaseObservation DecimalArithmetic()
        {
            decimal value = 12.5m * 3m - 0.5m;
            return new CaseObservation(value.ToString("0.0", CultureInfo.InvariantCulture));
        }

        private static CaseObservation NumericTruncatingCast()
        {
            return Observation((int)3.9d, (int)-3.9d);
        }

        private static CaseObservation LongShiftMasking()
        {
            long left = 1L << 65;
            long right = -8L >> 1;
            return Observation(left, right);
        }

        private static CaseObservation UnsignedComparison()
        {
            uint high = uint.MaxValue;
            int signed = unchecked((int)high);
            return new CaseObservation(FormattableString.Invariant($"{high > 1U},{signed < 1}"));
        }

        private static CaseObservation DenseIntegerSwitch()
        {
            int total = 0;
            for (int i = -1; i <= 6; i++)
            {
                total += DenseScore(i);
            }

            return Observation(total);
        }

        private static int DenseScore(int value)
        {
            switch (value)
            {
                case 0: return 2;
                case 1: return 3;
                case 2: return 5;
                case 3: return 7;
                case 4: return 11;
                case 5: return 13;
                default: return -1;
            }
        }

        private static CaseObservation SparseIntegerSwitch()
        {
            return Observation(SparseScore(-100) + SparseScore(7) + SparseScore(1000));
        }

        private static int SparseScore(int value)
        {
            switch (value)
            {
                case -100: return 11;
                case 7: return 17;
                case 1000: return 21;
                default: return -9;
            }
        }

        private static CaseObservation NestedBreakContinue()
        {
            int total = 0;
            for (int row = 0; row < 5; row++)
            {
                for (int column = 0; column < 6; column++)
                {
                    if (column == 1) continue;
                    if (row == 3) break;
                    total += row + column;
                }
            }

            return Observation(total);
        }

        private static CaseObservation ShortCircuitSideEffect()
        {
            int calls = 0;
            bool first = false && IncrementAndTrue(ref calls);
            bool second = true && IncrementAndTrue(ref calls);
            return new CaseObservation(FormattableString.Invariant($"{first},{second}"), calls.ToString(CultureInfo.InvariantCulture));
        }

        private static bool IncrementAndTrue(ref int value)
        {
            value++;
            return true;
        }

        private static CaseObservation NullCoalescingChain()
        {
            string? first = null;
            string? second = null;
            return new CaseObservation(first ?? second ?? "fallback");
        }

        private static CaseObservation ConditionalExpression()
        {
            int value = 9;
            string label = value % 2 == 0 ? "even" : "odd";
            return new CaseObservation(label + ":" + value.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation MutualRecursion()
        {
            return new CaseObservation(FormattableString.Invariant($"{IsEven(20)},{IsOdd(17)}"));
        }

        private static bool IsEven(int value)
        {
            return value == 0 || IsOdd(value - 1);
        }

        private static bool IsOdd(int value)
        {
            return value != 0 && IsEven(value - 1);
        }

        private static CaseObservation CharacterArithmetic()
        {
            char first = '\u03A9';
            char second = (char)(first + 2);
            return Observation((int)first, (int)second);
        }

        private static CaseObservation DoWhileAccumulation()
        {
            int value = 1;
            int total = 0;
            do
            {
                total += value++;
            }
            while (value <= 10);

            return Observation(total);
        }

        private static CaseObservation PerformanceArithmeticLoopRegression()
        {
            long checksum = PerformanceWorkload.Execute("interp_arithmetic", 1000000);
            return new CaseObservation(checksum.ToString(CultureInfo.InvariantCulture));
        }

        private static CaseObservation InterfaceBranchJoinDispatch()
        {
            return Observation(InvokeBranchSelectedOperation(true, 5), InvokeBranchSelectedOperation(false, 5));
        }

        private static int InvokeBranchSelectedOperation(bool useAdd, int value)
        {
            IControlFlowOperation operation;
            if (useAdd)
            {
                operation = new AddControlFlowOperation();
            }
            else
            {
                operation = new MultiplyControlFlowOperation();
            }

            return operation.Apply(value);
        }

        private static CaseObservation InterfaceLoopTypeChange()
        {
            IControlFlowOperation operation = new AddControlFlowOperation();
            int total = 0;
            for (int i = 0; i < 2; i++)
            {
                total += operation.Apply(i);
                operation = new MultiplyControlFlowOperation();
            }

            return Observation(total);
        }

        private static CaseObservation InterfaceEvalStackBranchJoin()
        {
            return Observation(InvokeEvalStackOperation(true, 5), InvokeEvalStackOperation(false, 5));
        }

        private static int InvokeEvalStackOperation(bool useAdd, int value)
        {
            return (useAdd
                ? (IControlFlowOperation)(object)new AddControlFlowOperation()
                : (IControlFlowOperation)(object)new MultiplyControlFlowOperation()).Apply(value);
        }

        private static CaseObservation DelegateEvalStackBranchJoin()
        {
            return Observation(InvokeEvalStackDelegate(true, 5), InvokeEvalStackDelegate(false, 5));
        }

        private static int InvokeEvalStackDelegate(bool useAdd, int value)
        {
            return (useAdd
                ? new Func<int, int>(AddDelegateOperation)
                : new Func<int, int>(MultiplyDelegateOperation))(value);
        }

        private static int AddDelegateOperation(int value)
        {
            return value + 11;
        }

        private static int MultiplyDelegateOperation(int value)
        {
            return value * 3;
        }

        private interface IControlFlowOperation
        {
            int Apply(int value);
        }

        private sealed class AddControlFlowOperation : IControlFlowOperation
        {
            public int Apply(int value)
            {
                return value + 11;
            }
        }

        private sealed class MultiplyControlFlowOperation : IControlFlowOperation
        {
            public int Apply(int value)
            {
                return value * 3;
            }
        }

        private static CaseObservation Observation<TFirst, TSecond>(TFirst first, TSecond second)
        {
            return new CaseObservation(FormattableString.Invariant($"{first},{second}"));
        }

        private static CaseObservation Observation<T>(T value)
        {
            return new CaseObservation(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty);
        }
    }
}
