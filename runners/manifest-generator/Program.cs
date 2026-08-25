using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using HybridCLR.Lab.ManagedCases;

namespace HybridCLR.Lab.ManifestGenerator
{
    internal static class Program
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        };

        private static int Main(string[] args)
        {
            string output = args.Length == 0 ? "manifests/test-manifest.json" : args[0];
            List<CaseDefinition> orderedCases = CaseRegistry.All
                .OrderBy(testCase => testCase.Layer, StringComparer.Ordinal)
                .ThenBy(testCase => testCase.Category, StringComparer.Ordinal)
                .ThenBy(testCase => testCase.Id, StringComparer.Ordinal)
                .ToList();
            TestManifest manifest = new TestManifest
            {
                SchemaVersion = 2,
                SuiteId = "managed-differential-v2",
                Cases = orderedCases
                    .Select(testCase => new ManifestCase
                    {
                        Id = testCase.Id,
                        Category = testCase.Category,
                        Layer = testCase.Layer,
                        Features = testCase.Features.ToArray(),
                        Required = true,
                    })
                    .ToList(),
            };

            string path = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(manifest, JsonOptions).Replace("\r\n", "\n") + "\n";
            File.WriteAllText(path, json, new UTF8Encoding(false));
            Console.WriteLine($"Generated {manifest.Cases.Count} cases: {path}");
            return 0;
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

            public string[] Features { get; set; } = Array.Empty<string>();

            public bool Required { get; set; }
        }

    }
}
