using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    // Fixture-only, explicit command-line entry. Never shipped in the package.
    public static class DhePerformanceProbe
    {
        private interface IValue { int Read(int value); }
        private class Value : IValue
        {
            public int Field = 7;
            [MethodImpl(MethodImplOptions.NoInlining)] public virtual int Read(int value) => value + 1;
        }
        private sealed class Derived : Value
        {
            [MethodImpl(MethodImplOptions.NoInlining)] public override int Read(int value) => value + 2;
        }
        [Serializable] private sealed class Sample { public string name; public long ticks, checksum; public int iterations; }
        [Serializable] private sealed class Report
        {
            public bool passed, diagnostics, smokeIncluded;
            public int pid;
            public long frequency;
            public string phase, baseId;
            public Sample[] samples;
        }
        private static volatile int Sink;
        [MethodImpl(MethodImplOptions.NoInlining)] private static int Entry(int value) => value + 3;
        private static Sample Measure(string name, int iterations, Func<int, int> operation, int offset)
        {
            for (int i = 0; i < 2000; ++i) Sink = operation(i & 1023);
            long checksum = 0;
            long start = Stopwatch.GetTimestamp();
            for (int i = 0; i < iterations; ++i) checksum += operation(i & 1023);
            long ticks = Stopwatch.GetTimestamp() - start;
            long expected = (long)(iterations / 1024) * (1023 * 1024 / 2) +
                (long)(iterations % 1024) * (iterations % 1024 - 1) / 2 + (long)iterations * offset;
            if (checksum != expected) throw new InvalidDataException(name + " checksum " + checksum + " != " + expected);
            return new Sample { name = name, ticks = ticks, checksum = checksum, iterations = iterations };
        }
        public static void RunIfRequested(string phase)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-dhePerformanceResult");
            if (index < 0) return;
            Value receiver = new Derived(); IValue iface = receiver;
            FieldInfo field = typeof(Value).GetField("Field");
            var samples = new[] {
                Measure("ordinary-entry", 2 * 1024 * 1024, Entry, 3),
                Measure("ordinary-virtual", 2 * 1024 * 1024, value => receiver.Read(value), 2),
                Measure("ordinary-interface", 2 * 1024 * 1024, value => iface.Read(value), 2),
                Measure("ordinary-generic-allocation", 100 * 1024, value => { var list = new List<int>(4); list.Add(value); return list[0] + 4; }, 4),
                Measure("ordinary-reflection-field", 100 * 1024, value => value + (int)field.GetValue(receiver), 7),
            };
            MethodInfo diagnosticsQuery = typeof(RuntimeApi).GetMethod("AreDifferentialDispatchDiagnosticsEnabled");
            var report = new Report { passed = true, diagnostics = diagnosticsQuery == null || (bool)diagnosticsQuery.Invoke(null, null),
                smokeIncluded = typeof(DheRuntime).Assembly.GetType("HybridCLR.DheRuntimeSmoke") != null,
                pid = Process.GetCurrentProcess().Id, frequency = Stopwatch.Frequency, phase = phase,
                baseId = DheBuildIdentity.Create().BaseId, samples = samples };
            File.WriteAllText(args[index + 1] + "." + phase + ".json", JsonUtility.ToJson(report, true));
        }
    }
}
