using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HybridCLR.Lab.ManagedCases;

namespace HybridCLR.Lab.ReferenceRunner
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        private static int Main(string[] args)
        {
            RunnerOptions options = RunnerOptions.Parse(args);
            string manifestPath = Path.GetFullPath(options.ManifestPath);
            string outputPath = Path.GetFullPath(options.OutputPath);
            TestManifest manifest = LoadManifest(manifestPath);
            string goldenPath = Path.GetFullPath(options.GoldenPath);
            GoldenContract golden = LoadGolden(goldenPath);
            ValidateRegistry(manifest, golden);

            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
            List<CaseResult> caseResults = ExecuteCases(manifest, golden);
            int failed = caseResults.Count(result => result.Status == "failed");
            string managedAssemblyPath = typeof(CaseRegistry).Assembly.Location;

            TestRunResult result = new TestRunResult
            {
                SchemaVersion = 2,
                SuiteId = manifest.SuiteId,
                Runner = "dotnet-reference-v1",
                Runtime = RuntimeInformation.FrameworkDescription,
                Platform = RuntimeInformation.OSDescription,
                Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
                StartedAtUtc = startedAtUtc,
                ManifestSha256 = ComputeSha256(manifestPath),
                GoldenContractSha256 = ComputeSha256(goldenPath),
                ManagedAssemblySha256 = ComputeSha256(managedAssemblyPath),
                Summary = new TestSummary
                {
                    Total = caseResults.Count,
                    Passed = caseResults.Count - failed,
                    Failed = failed
                },
                Cases = caseResults
            };

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            string json = JsonSerializer.Serialize(result, JsonOptions).Replace("\r\n", "\n") + "\n";
            File.WriteAllText(outputPath, json, new UTF8Encoding(false));
            Console.WriteLine($"Reference suite: {result.Summary.Passed}/{result.Summary.Total} passed");
            Console.WriteLine($"Result: {outputPath}");
            return failed == 0 ? 0 : 1;
        }

        private static TestManifest LoadManifest(string manifestPath)
        {
            string json = File.ReadAllText(manifestPath);
            TestManifest? manifest = JsonSerializer.Deserialize<TestManifest>(json, JsonOptions);
            if (manifest == null || manifest.SchemaVersion != 2 || string.IsNullOrWhiteSpace(manifest.SuiteId))
            {
                throw new InvalidDataException($"Invalid test manifest: {manifestPath}");
            }

            return manifest;
        }

        private static GoldenContract LoadGolden(string goldenPath)
        {
            GoldenContract? golden = JsonSerializer.Deserialize<GoldenContract>(File.ReadAllText(goldenPath), JsonOptions);
            if (golden == null || golden.SchemaVersion != 1 || string.IsNullOrWhiteSpace(golden.SuiteId))
            {
                throw new InvalidDataException($"Invalid golden contract: {goldenPath}");
            }
            return golden;
        }

        private static void ValidateRegistry(TestManifest manifest, GoldenContract golden)
        {
            if (!string.Equals(manifest.SuiteId, golden.SuiteId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Manifest and golden suite IDs differ.");
            }
            Dictionary<string, CaseDefinition> registered = CaseRegistry.All.ToDictionary(testCase => testCase.Id);
            HashSet<string> manifestIds = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<string, GoldenCase> goldenById = golden.Cases.ToDictionary(testCase => testCase.Id);
            if (goldenById.Count != golden.Cases.Count || golden.Cases.Count != manifest.Cases.Count)
            {
                throw new InvalidDataException("Manifest and golden case counts differ or golden IDs are duplicated.");
            }
            for (int index = 0; index < manifest.Cases.Count; index++)
            {
                if (!string.Equals(manifest.Cases[index].Id, golden.Cases[index].Id, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Manifest and golden case order differs at index {index}.");
                }
            }
            foreach (ManifestCase manifestCase in manifest.Cases)
            {
                if (!manifestIds.Add(manifestCase.Id))
                {
                    throw new InvalidDataException($"Duplicate manifest case id: {manifestCase.Id}");
                }

                if (!registered.TryGetValue(manifestCase.Id, out CaseDefinition? definition))
                {
                    throw new InvalidDataException($"Manifest case is not registered: {manifestCase.Id}");
                }

                if (!string.Equals(definition.Category, manifestCase.Category, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Category mismatch for case: {manifestCase.Id}");
                }

                if (!string.Equals(definition.Layer, manifestCase.Layer, StringComparison.Ordinal))
                {
                    throw new InvalidDataException($"Layer mismatch for case: {manifestCase.Id}");
                }

                if (!definition.Features.SequenceEqual(manifestCase.Features, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"Feature mismatch for case: {manifestCase.Id}");
                }

                if (!goldenById.TryGetValue(manifestCase.Id, out GoldenCase? goldenCase) ||
                    !string.Equals(goldenCase.Category, manifestCase.Category, StringComparison.Ordinal) ||
                    !string.Equals(goldenCase.Layer, manifestCase.Layer, StringComparison.Ordinal) ||
                    !goldenCase.Features.SequenceEqual(manifestCase.Features, StringComparer.Ordinal))
                {
                    throw new InvalidDataException($"Golden metadata mismatch for case: {manifestCase.Id}");
                }
            }

            string[] unlistedCases = registered.Keys.Where(id => !manifestIds.Contains(id)).OrderBy(id => id).ToArray();
            if (unlistedCases.Length > 0)
            {
                throw new InvalidDataException("Registered cases missing from manifest: " + string.Join(", ", unlistedCases));
            }
        }

        private static List<CaseResult> ExecuteCases(TestManifest manifest, GoldenContract golden)
        {
            Dictionary<string, CaseDefinition> registered = CaseRegistry.All.ToDictionary(testCase => testCase.Id);
            Dictionary<string, GoldenCase> goldenById = golden.Cases.ToDictionary(testCase => testCase.Id);
            List<CaseResult> results = new List<CaseResult>(manifest.Cases.Count);
            foreach (ManifestCase manifestCase in manifest.Cases)
            {
                CaseDefinition definition = registered[manifestCase.Id];
                results.Add(ExecuteCase(definition, goldenById[manifestCase.Id]));
            }

            return results;
        }

        private static CaseResult ExecuteCase(CaseDefinition definition, GoldenCase golden)
        {
            CaseObservation? observation = null;
            string? exceptionType = null;
            long started = Stopwatch.GetTimestamp();
            try
            {
                observation = definition.Execute();
            }
            catch (Exception exception)
            {
                exceptionType = exception.GetType().FullName;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - started;
            long durationNanoseconds = (long)(elapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency));
            string? message = Compare(golden, observation, exceptionType);
            return new CaseResult
            {
                Id = definition.Id,
                Category = definition.Category,
                Layer = definition.Layer,
                Features = definition.Features.ToList(),
                Status = message == null ? "passed" : "failed",
                ReturnValue = observation?.ReturnValue,
                SideEffect = observation?.SideEffect,
                ExceptionType = exceptionType,
                DurationNanoseconds = Math.Max(0, durationNanoseconds),
                Message = message
            };
        }

        private static string? Compare(
            GoldenCase golden,
            CaseObservation? observation,
            string? exceptionType)
        {
            if (!string.Equals(golden.ExceptionType, exceptionType, StringComparison.Ordinal))
            {
                return $"Expected exception '{golden.ExceptionType ?? "<none>"}', got '{exceptionType ?? "<none>"}'.";
            }

            if (exceptionType != null)
            {
                return null;
            }

            if (observation == null)
            {
                return "Case returned no observation.";
            }

            if (!string.Equals(golden.ReturnValue, observation.ReturnValue, StringComparison.Ordinal))
            {
                return $"Expected return '{golden.ReturnValue}', got '{observation.ReturnValue}'.";
            }

            if (!string.Equals(golden.SideEffect, observation.SideEffect, StringComparison.Ordinal))
            {
                return $"Expected side effect '{golden.SideEffect}', got '{observation.SideEffect}'.";
            }

            return null;
        }

        private static string ComputeSha256(string path)
        {
            using FileStream stream = File.OpenRead(path);
            using SHA256 sha256 = SHA256.Create();
            return Convert.ToHexString(sha256.ComputeHash(stream));
        }

        private sealed class RunnerOptions
        {
            public string ManifestPath { get; private set; } = "manifests/test-manifest.json";

            public string GoldenPath { get; private set; } = "manifests/test-golden.json";

            public string OutputPath { get; private set; } = "reports/reference-result.json";

            public static RunnerOptions Parse(string[] args)
            {
                RunnerOptions options = new RunnerOptions();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] == "--manifest" && i + 1 < args.Length)
                    {
                        options.ManifestPath = args[++i];
                    }
                    else if (args[i] == "--golden" && i + 1 < args.Length)
                    {
                        options.GoldenPath = args[++i];
                    }
                    else if (args[i] == "--output" && i + 1 < args.Length)
                    {
                        options.OutputPath = args[++i];
                    }
                    else
                    {
                        throw new ArgumentException($"Unknown or incomplete argument: {args[i]}");
                    }
                }

                return options;
            }
        }

        private sealed class TestManifest
        {
            public int SchemaVersion { get; set; }

            public string SuiteId { get; set; } = "";

            public List<ManifestCase> Cases { get; set; } = new List<ManifestCase>();
        }

        private sealed class ManifestCase
        {
            public string Id { get; set; } = "";

            public string Category { get; set; } = "";

            public string Layer { get; set; } = "";

            public List<string> Features { get; set; } = new List<string>();

            public bool Required { get; set; }
        }

        private sealed class GoldenContract
        {
            public int SchemaVersion { get; set; }
            public string SuiteId { get; set; } = "";
            public List<GoldenCase> Cases { get; set; } = new List<GoldenCase>();
        }

        private sealed class GoldenCase
        {
            public string Id { get; set; } = "";
            public string Category { get; set; } = "";
            public string Layer { get; set; } = "";
            public List<string> Features { get; set; } = new List<string>();
            public string? ReturnValue { get; set; }
            public string? SideEffect { get; set; }
            public string? ExceptionType { get; set; }
        }

        private sealed class TestRunResult
        {
            public int SchemaVersion { get; set; }

            public string SuiteId { get; set; } = "";

            public string Runner { get; set; } = "";

            public string Runtime { get; set; } = "";

            public string Platform { get; set; } = "";

            public string Architecture { get; set; } = "";

            public DateTimeOffset StartedAtUtc { get; set; }

            public string ManifestSha256 { get; set; } = "";

            public string GoldenContractSha256 { get; set; } = "";

            public string ManagedAssemblySha256 { get; set; } = "";

            public TestSummary Summary { get; set; } = new TestSummary();

            public List<CaseResult> Cases { get; set; } = new List<CaseResult>();
        }

        private sealed class TestSummary
        {
            public int Total { get; set; }

            public int Passed { get; set; }

            public int Failed { get; set; }
        }

        private sealed class CaseResult
        {
            public string Id { get; set; } = "";

            public string Category { get; set; } = "";

            public string Layer { get; set; } = "";

            public List<string> Features { get; set; } = new List<string>();

            public string Status { get; set; } = "";

            public string? ReturnValue { get; set; }

            public string? SideEffect { get; set; }

            public string? ExceptionType { get; set; }

            public long DurationNanoseconds { get; set; }

            public string? Message { get; set; }
        }
    }
}
