using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.Lab.ManagedCases;

namespace HybridCLR.Lab.BenchmarkReference
{
    internal static class Program
    {
        private const string SuiteId = "managed-performance-v1";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static int Main(string[] args)
        {
            RunnerOptions options = RunnerOptions.Parse(args);
            if (options.List)
            {
                foreach (PerformanceWorkloadDefinition definition in PerformanceWorkload.All)
                {
                    Console.WriteLine(definition.Id);
                }

                return 0;
            }

            string policyPath = Path.GetFullPath(options.PolicyPath);
            string outputPath = Path.GetFullPath(options.OutputPath);
            BenchmarkPolicy policy = LoadPolicy(policyPath);
            IReadOnlyList<PerformanceWorkloadDefinition> definitions = SelectDefinitions(options);
            string goldenPath = Path.GetFullPath(options.GoldenPath);
            BenchmarkGolden golden = LoadGolden(goldenPath);
            Dictionary<string, GoldenWorkload> goldenById = ValidateGolden(definitions, golden, options.Mode);
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
            List<WorkloadResult> workloads = definitions
                .Select(definition => Execute(definition, options.Mode, policy, goldenById[definition.Id]))
                .ToList();
            string managedAssemblyPath = typeof(PerformanceWorkload).Assembly.Location;
            BenchmarkResult result = new BenchmarkResult
            {
                SchemaVersion = 1,
                SuiteId = SuiteId,
                BenchmarkMode = options.Mode,
                Runner = "dotnet-reference-benchmark-v1",
                Runtime = RuntimeInformation.FrameworkDescription,
                Platform = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                StartedAtUtc = startedAtUtc,
                ProcessId = Process.GetCurrentProcess().Id,
                StopwatchFrequency = Stopwatch.Frequency,
                PolicySha256 = ComputeSha256(policyPath),
                GoldenContractSha256 = ComputeSha256(goldenPath),
                ManagedAssemblySha256 = ComputeSha256(managedAssemblyPath),
                Startup = new StartupTimings(),
                Workloads = workloads
            };

            string? outputDirectory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            File.WriteAllText(outputPath, JsonSerializer.Serialize(result, JsonOptions));
            Console.WriteLine($"Reference benchmark ({options.Mode}): {workloads.Count} workloads");
            Console.WriteLine($"Result: {outputPath}");
            return 0;
        }

        private static BenchmarkPolicy LoadPolicy(string path)
        {
            BenchmarkPolicy? policy = JsonSerializer.Deserialize<BenchmarkPolicy>(File.ReadAllText(path), JsonOptions);
            if (policy == null || policy.SchemaVersion != 1 || policy.WarmupBatches < 0 || policy.MeasurementBatches < 1)
            {
                throw new InvalidDataException("Invalid benchmark policy: " + path);
            }

            return policy;
        }

        private static BenchmarkGolden LoadGolden(string path)
        {
            BenchmarkGolden? golden = JsonSerializer.Deserialize<BenchmarkGolden>(File.ReadAllText(path), JsonOptions);
            if (golden == null || golden.SchemaVersion != 1 || golden.Workloads == null ||
                !string.Equals(golden.SuiteId, SuiteId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid benchmark golden contract: " + path);
            }
            return golden;
        }

        private static Dictionary<string, GoldenWorkload> ValidateGolden(
            IReadOnlyList<PerformanceWorkloadDefinition> definitions,
            BenchmarkGolden golden,
            string mode)
        {
            Dictionary<string, GoldenWorkload> byId = golden.Workloads.ToDictionary(item => item.Id, StringComparer.Ordinal);
            if (byId.Count != golden.Workloads.Count || byId.Count != PerformanceWorkload.All.Count)
            {
                throw new InvalidDataException("Benchmark golden workload set is incomplete or contains duplicates.");
            }

            foreach (PerformanceWorkloadDefinition definition in definitions)
            {
                if (!byId.TryGetValue(definition.Id, out GoldenWorkload? goldenWorkload) ||
                    !string.Equals(definition.Category, goldenWorkload.Category, StringComparison.Ordinal) ||
                    !definition.Features.SequenceEqual(goldenWorkload.Features, StringComparer.Ordinal) ||
                    (mode == "cold" && definition.ColdIterations != goldenWorkload.ColdIterations) ||
                    (mode == "steady" && definition.Iterations != goldenWorkload.SteadyIterations))
                {
                    throw new InvalidDataException("Benchmark golden metadata mismatch: " + definition.Id);
                }
            }
            return byId;
        }

        private static IReadOnlyList<PerformanceWorkloadDefinition> SelectDefinitions(RunnerOptions options)
        {
            if (string.IsNullOrEmpty(options.Workload))
            {
                if (options.Mode == "cold")
                {
                    throw new ArgumentException("Cold mode requires --workload so each first-use sample runs in a fresh process.");
                }

                return PerformanceWorkload.All;
            }

            PerformanceWorkloadDefinition? definition = PerformanceWorkload.All
                .FirstOrDefault(item => string.Equals(item.Id, options.Workload, StringComparison.Ordinal));
            if (definition == null)
            {
                throw new ArgumentException("Unknown workload: " + options.Workload);
            }

            return new[] { definition };
        }

        private static WorkloadResult Execute(
            PerformanceWorkloadDefinition definition,
            string mode,
            BenchmarkPolicy policy,
            GoldenWorkload golden)
        {
            int iterations = mode == "cold" ? definition.ColdIterations : definition.Iterations;
            int warmupBatches = mode == "cold" ? 0 : policy.WarmupBatches;
            int measurementBatches = mode == "cold" ? 1 : policy.MeasurementBatches;
            long expectedChecksum = ParseChecksum(mode == "cold" ? golden.ColdChecksum : golden.SteadyChecksum, definition.Id);
            for (int i = 0; i < warmupBatches; i++)
            {
                VerifyChecksum(definition.Id, PerformanceWorkload.Execute(definition.Id, iterations), expectedChecksum);
            }

            if (mode == "steady")
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            List<long> batchNanoseconds = new List<long>(measurementBatches);
            List<double> nanosecondsPerIteration = new List<double>(measurementBatches);
            for (int i = 0; i < measurementBatches; i++)
            {
                long started = Stopwatch.GetTimestamp();
                long checksum = PerformanceWorkload.Execute(definition.Id, iterations);
                long elapsed = Stopwatch.GetTimestamp() - started;
                VerifyChecksum(definition.Id, checksum, expectedChecksum);
                long nanoseconds = ToNanoseconds(elapsed);
                batchNanoseconds.Add(nanoseconds);
                nanosecondsPerIteration.Add(nanoseconds / (double)iterations);
            }

            return new WorkloadResult
            {
                Id = definition.Id,
                Category = definition.Category,
                Features = definition.Features.ToList(),
                Iterations = iterations,
                WarmupBatches = warmupBatches,
                MeasurementBatches = measurementBatches,
                Checksum = expectedChecksum.ToString(CultureInfo.InvariantCulture),
                BatchNanoseconds = batchNanoseconds,
                NanosecondsPerIteration = nanosecondsPerIteration,
                GcCollections = new GcCollections
                {
                    Generation0 = GC.CollectionCount(0) - gen0Before,
                    Generation1 = GC.CollectionCount(1) - gen1Before,
                    Generation2 = GC.CollectionCount(2) - gen2Before
                }
            };
        }

        private static long ParseChecksum(string value, string id)
        {
            if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long checksum))
            {
                throw new InvalidDataException("Invalid benchmark checksum for " + id + ".");
            }
            return checksum;
        }

        private static void VerifyChecksum(string id, long actual, long expected)
        {
            if (expected != actual)
            {
                throw new InvalidDataException(
                    string.Format(CultureInfo.InvariantCulture, "Checksum mismatch for {0}: expected {1}, got {2}.", id, expected, actual));
            }
        }

        private static long ToNanoseconds(long ticks)
        {
            return Math.Max(0, (long)Math.Round(ticks * (1_000_000_000.0 / Stopwatch.Frequency)));
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using SHA256 sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream));
        }

        private sealed class RunnerOptions
        {
            public string PolicyPath { get; private set; } = "manifests/benchmark-policy.json";

            public string GoldenPath { get; private set; } = "manifests/benchmark-golden.json";

            public string OutputPath { get; private set; } = "reports/raw/reference-benchmark.json";

            public string Mode { get; private set; } = "steady";

            public string? Workload { get; private set; }

            public bool List { get; private set; }

            public static RunnerOptions Parse(string[] args)
            {
                RunnerOptions options = new RunnerOptions();
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--policy" when i + 1 < args.Length:
                            options.PolicyPath = args[++i];
                            break;
                        case "--golden" when i + 1 < args.Length:
                            options.GoldenPath = args[++i];
                            break;
                        case "--output" when i + 1 < args.Length:
                            options.OutputPath = args[++i];
                            break;
                        case "--mode" when i + 1 < args.Length:
                            options.Mode = args[++i];
                            break;
                        case "--workload" when i + 1 < args.Length:
                            options.Workload = args[++i];
                            break;
                        case "--list":
                            options.List = true;
                            break;
                        default:
                            throw new ArgumentException("Unknown or incomplete argument: " + args[i]);
                    }
                }

                if (options.Mode != "cold" && options.Mode != "steady")
                {
                    throw new ArgumentException("Benchmark mode must be 'cold' or 'steady'.");
                }

                return options;
            }
        }

        private sealed class BenchmarkPolicy
        {
            public int SchemaVersion { get; set; }

            public int WarmupBatches { get; set; }

            public int MeasurementBatches { get; set; }
        }

        private sealed class BenchmarkGolden
        {
            public int SchemaVersion { get; set; }
            public string SuiteId { get; set; } = "";
            public List<GoldenWorkload> Workloads { get; set; } = new List<GoldenWorkload>();
        }

        private sealed class GoldenWorkload
        {
            public string Id { get; set; } = "";
            public string Category { get; set; } = "";
            public List<string> Features { get; set; } = new List<string>();
            public int ColdIterations { get; set; }
            public string ColdChecksum { get; set; } = "";
            public int SteadyIterations { get; set; }
            public string SteadyChecksum { get; set; } = "";
        }

        private sealed class BenchmarkResult
        {
            public int SchemaVersion { get; set; }

            public string SuiteId { get; set; } = "";

            public string BenchmarkMode { get; set; } = "";

            public string Runner { get; set; } = "";

            public string Runtime { get; set; } = "";

            public string Platform { get; set; } = "";

            public string Architecture { get; set; } = "";

            public DateTimeOffset StartedAtUtc { get; set; }

            public int ProcessId { get; set; }

            public long StopwatchFrequency { get; set; }

            public string PolicySha256 { get; set; } = "";

            public string GoldenContractSha256 { get; set; } = "";

            public string ManagedAssemblySha256 { get; set; } = "";

            public StartupTimings Startup { get; set; } = new StartupTimings();

            public List<WorkloadResult> Workloads { get; set; } = new List<WorkloadResult>();
        }

        private sealed class StartupTimings
        {
            public long AotMetadataLoadNanoseconds { get; set; }

            public long HotUpdateAssemblyLoadNanoseconds { get; set; }

            public long WorkloadDiscoveryNanoseconds { get; set; }
        }

        private sealed class WorkloadResult
        {
            public string Id { get; set; } = "";

            public string Category { get; set; } = "";

            public List<string> Features { get; set; } = new List<string>();

            public int Iterations { get; set; }

            public int WarmupBatches { get; set; }

            public int MeasurementBatches { get; set; }

            public string Checksum { get; set; } = "";

            public List<long> BatchNanoseconds { get; set; } = new List<long>();

            public List<double> NanosecondsPerIteration { get; set; } = new List<double>();

            public GcCollections GcCollections { get; set; } = new GcCollections();
        }

        private sealed class GcCollections
        {
            public int Generation0 { get; set; }

            public int Generation1 { get; set; }

            public int Generation2 { get; set; }
        }
    }
}
