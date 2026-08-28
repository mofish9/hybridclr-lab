using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;

namespace HybridCLR.Lab
{
    internal static class HybridCLRLabPlayerRunner
    {
        private const string ManagedAssemblyFile = "HybridCLRLab/HybridCLR.ManagedCases.dll.bytes";
        private const string MetadataStressAssemblyFile = "HybridCLRLab/HybridCLR.MetadataStress.dll.bytes";
        private const string MetadataStressPrewarmManifestFile = "HybridCLRLab/metadata-stress-prewarm.json";
        private const string CrossAssemblyDerivedFile = "HybridCLRLab/HybridCLR.CrossAssemblyDerived.dll.bytes";
        private const string ManifestFile = "HybridCLRLab/test-manifest.json";
        private const string ManifestIndexFile = "HybridCLRLab/test-manifest.ids";
        private const string ManifestContractsFile = "HybridCLRLab/test-manifest.contracts";
        private const string GoldenContractFile = "HybridCLRLab/test-golden.json";
        private const string BuildIdentityFile = "HybridCLRLab/build-identity.json";
        private const string AotMetadataIndexFile = "HybridCLRLab/AotMetadata/aot-metadata.ids";
        private const string BenchmarkPolicyFile = "HybridCLRLab/benchmark-policy.json";
        private const string BenchmarkGoldenFile = "HybridCLRLab/benchmark-golden.json";
        private const string DefaultResultFile = "hybridclr-lab-player-result.json";
        private const string DefaultBenchmarkResultFile = "hybridclr-lab-player-benchmark.json";
        private const string DefaultMetadataBenchmarkResultFile = "hybridclr-lab-metadata-benchmark.json";
        private const int CaseTimeoutMilliseconds = 10000;
        private const int MetadataStressOuterTypeCount = 1024;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Run()
        {
            if (Application.isEditor)
            {
                return;
            }

            int exitCode = 0;
            try
            {
                string labMode = GetArgument("-labMode");
                if (string.Equals(labMode, "benchmark", StringComparison.Ordinal))
                {
                    BenchmarkRun benchmarkRun = ExecuteBenchmark();
                    WriteBenchmarkResult(benchmarkRun);
                    Debug.Log($"[HybridCLR Lab] Player benchmark: {benchmarkRun.Workloads.Count} workloads");
                }
                else if (string.Equals(labMode, "metadata", StringComparison.Ordinal))
                {
                    if (GetOptionalBooleanArgument("-labMetadataWarmupAcrossFrames", false))
                    {
                        StartMetadataBenchmarkAcrossFrames();
                        return;
                    }
                    MetadataBenchmarkRun metadataRun = ExecuteMetadataBenchmark();
                    WriteMetadataBenchmarkResult(metadataRun);
                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HYBRIDCLR_METADATA_PROFILE")))
                    {
                        Instrumentation.FlushMetadataProfile();
                    }
                    Debug.Log($"[HybridCLR Lab] Metadata benchmark: {metadataRun.MetadataMode}");
                }
                else
                {
                    TestRun run = Execute();
                    WriteResult(run);
                    WriteInstrumentationSnapshotIfRequested();
                    exitCode = run.Failed == 0 ? 0 : 1;
                    Debug.Log($"[HybridCLR Lab] Player suite: {run.Passed}/{run.Total} passed");
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                exitCode = 1;
            }

            Application.Quit(exitCode);
        }

        private static TestRun Execute()
        {
            byte[] assemblyBytes = ReadStreamingAssetBytes(ManagedAssemblyFile);
            byte[] crossAssemblyBytes = ReadStreamingAssetBytes(CrossAssemblyDerivedFile);
            byte[] manifestBytes = ReadStreamingAssetBytes(ManifestFile);
            string[] manifestLines = ReadStreamingAssetLines(ManifestIndexFile);
            GoldenContractFileData golden = LoadGoldenContract();
            BuildIdentity buildIdentity = LoadBuildIdentity();
            string aotMetadataMode = GetAotMetadataMode();
            ValidateBuildIdentity(buildIdentity, assemblyBytes, aotMetadataMode, crossAssemblyBytes);
            long metadataStarted = Stopwatch.GetTimestamp();
            AotMetadataLoadSummary aotMetadata = LoadAotMetadataIfEnabled();
            long aotMetadataLoadNanoseconds = aotMetadataMode == "none"
                ? 0
                : ToNanoseconds(Stopwatch.GetTimestamp() - metadataStarted);
            Assembly assembly = Assembly.Load(assemblyBytes);
            ExecuteCrossAssemblyLazyVTableProbe(assembly, crossAssemblyBytes);
            bool lazyMetadataConcurrentFirstTouchPassed = true;
            Type registryType = assembly.GetType("HybridCLR.Lab.ManagedCases.CaseRegistry", true)!;
            FieldInfo runtimeTargetField = registryType.GetField("RuntimeTarget", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingFieldException(registryType.FullName, "RuntimeTarget");
            object definitions = registryType.GetProperty("All", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            runtimeTargetField.SetValue(null, buildIdentity.target);
            if (!string.Equals((string)runtimeTargetField.GetValue(null)!, buildIdentity.target, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unable to configure the managed test runtime target.");
            }
            string suiteLine = manifestLines.Length == 0 ? string.Empty : manifestLines[0].TrimStart('\uFEFF');
            if (manifestLines.Length < 2 || !suiteLine.StartsWith("suiteId=", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid test manifest index.");
            }
            string suiteId = suiteLine.Substring("suiteId=".Length);
            Dictionary<string, ManifestContract> manifestContracts = LoadManifestContracts(ManifestContractsFile, suiteId);
            Dictionary<string, GoldenContractCase> goldenById = LoadGoldenCases(golden, suiteId);
            if (golden.cases.Length != manifestLines.Length - 1)
            {
                throw new InvalidDataException("Manifest and golden case counts differ.");
            }
            for (int goldenIndex = 0; goldenIndex < golden.cases.Length; goldenIndex++)
            {
                if (!string.Equals(golden.cases[goldenIndex].id, manifestLines[goldenIndex + 1].Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Manifest and golden case order differs at index " + goldenIndex);
                }
            }
            Dictionary<string, object> definitionsById = new Dictionary<string, object>(StringComparer.Ordinal);
            foreach (object definition in (IEnumerable)definitions)
            {
                string id = (string)definition.GetType().GetProperty("Id")!.GetValue(definition)!;
                if (!definitionsById.TryAdd(id, definition)) throw new InvalidDataException("Duplicate registered case: " + id);
            }

            TestRun run = new TestRun
            {
                StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                AssemblySha256 = Sha256(assemblyBytes),
                ManifestSha256 = Sha256(manifestBytes),
                GoldenContractSha256 = Sha256(ReadStreamingAssetBytes(GoldenContractFile)),
                SuiteId = suiteId,
                ProcessId = Process.GetCurrentProcess().Id,
                AotMetadataMode = aotMetadataMode,
                AotMetadataFileCount = aotMetadata.FileCount,
                AotMetadataBytes = aotMetadata.TotalBytes,
                AotMetadataLoadNanoseconds = aotMetadataLoadNanoseconds,
                LazyMetadataConcurrentFirstTouchPassed = lazyMetadataConcurrentFirstTouchPassed,
                FullGenericSharingDiagnosticsEnabled = buildIdentity.fullGenericSharingDiagnostics,
                BuildIdentity = buildIdentity,
            };
            if (run.FullGenericSharingDiagnosticsEnabled)
            {
                Instrumentation.ResetFullGenericSharing();
            }
            if (!string.IsNullOrWhiteSpace(GetArgument("-labInstrumentationResult")))
            {
                Instrumentation.Reset();
            }
            string caseFilter = GetArgument("-labCaseFilter");
            string rawCaseLimit = GetArgument("-labCaseLimit");
            int caseLimit = string.IsNullOrWhiteSpace(rawCaseLimit)
                ? int.MaxValue
                : GetPositiveArgument("-labCaseLimit");
            HashSet<string> manifestIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 1; i < manifestLines.Length; i++)
            {
                string id = manifestLines[i].Trim();
                if (id.Length == 0 || !manifestIds.Add(id))
                {
                    throw new InvalidDataException("Duplicate or empty manifest case: " + id);
                }

                if (!definitionsById.TryGetValue(id, out object definition))
                {
                    throw new InvalidDataException("Manifest case is not registered: " + id);
                }

                if (!manifestContracts.TryGetValue(id, out ManifestContract manifestContract))
                {
                    throw new InvalidDataException("Index case is missing from manifest contracts: " + id);
                }
                if (!goldenById.TryGetValue(id, out GoldenContractCase goldenCase))
                {
                    throw new InvalidDataException("Index case is missing from golden contract: " + id);
                }

                Type definitionType = definition.GetType();
                string definitionCategory = (string)definitionType.GetProperty("Category")!.GetValue(definition)!;
                string definitionLayer = (string)definitionType.GetProperty("Layer")!.GetValue(definition)!;
                string[] definitionFeatures = ((IEnumerable)definitionType.GetProperty("Features")!.GetValue(definition)!)
                    .Cast<object>()
                    .Select(value => (string)value)
                    .ToArray();
                if (!string.Equals(definitionCategory, manifestContract.Category, StringComparison.Ordinal) ||
                    !string.Equals(definitionLayer, manifestContract.Layer, StringComparison.Ordinal) ||
                    !manifestContract.Features.SequenceEqual(definitionFeatures, StringComparer.Ordinal))
                {
                    throw new InvalidDataException("Manifest metadata mismatch: " + id);
                }
                if (!string.Equals(goldenCase.category, manifestContract.Category, StringComparison.Ordinal) ||
                    !string.Equals(goldenCase.layer, manifestContract.Layer, StringComparison.Ordinal) ||
                    !string.Equals(string.Join(",", goldenCase.features ?? Array.Empty<string>()), string.Join(",", manifestContract.Features), StringComparison.Ordinal))
                {
                    throw new InvalidDataException("Golden metadata mismatch: " + id);
                }

                bool matchesFilter = string.IsNullOrWhiteSpace(caseFilter) ||
                    string.Equals(id, caseFilter, StringComparison.Ordinal);
                if (matchesFilter && run.Cases.Count < caseLimit)
                {
                    run.Cases.Add(ExecuteCase(definition, goldenCase, run.FullGenericSharingDiagnosticsEnabled));
                }
            }
            if (manifestIds.Count != definitionsById.Count || manifestContracts.Count != definitionsById.Count || goldenById.Count != definitionsById.Count)
            {
                throw new InvalidDataException("Manifest and registered case counts differ.");
            }
            if (!string.IsNullOrWhiteSpace(caseFilter) && run.Cases.Count == 0)
            {
                throw new ArgumentException("Unknown -labCaseFilter: " + caseFilter);
            }

            run.Total = run.Cases.Count;
            run.Passed = run.Cases.Count(result => result.Status == "passed");
            run.Failed = run.Total - run.Passed;
            if (run.FullGenericSharingDiagnosticsEnabled)
            {
                run.FullGenericSharingDispatchCount = Instrumentation.GetFullGenericSharingDispatchCount();
                run.FullGenericSharingInterpreterInvokerCount = Instrumentation.GetFullGenericSharingInterpreterInvokerCount();
            }
            return run;
        }

        private static BenchmarkRun ExecuteBenchmark()
        {
            string benchmarkRuntime = GetArgument("-labBenchmarkRuntime");
            if (string.IsNullOrWhiteSpace(benchmarkRuntime))
            {
                benchmarkRuntime = "hybridclr";
            }
            if (benchmarkRuntime == "aot")
            {
                return ExecuteAotBenchmark();
            }
            if (benchmarkRuntime != "hybridclr")
            {
                throw new ArgumentException("-labBenchmarkRuntime must be 'hybridclr' or 'aot'.");
            }

            string mode = GetArgument("-labBenchmarkMode");
            if (mode != "cold" && mode != "steady")
            {
                throw new ArgumentException("-labBenchmarkMode must be 'cold' or 'steady'.");
            }

            string requestedWorkload = GetArgument("-labBenchmarkWorkload");
            if (mode == "cold" && string.IsNullOrWhiteSpace(requestedWorkload))
            {
                throw new ArgumentException("Cold benchmark mode requires -labBenchmarkWorkload.");
            }

            int warmupBatches = mode == "cold" ? 0 : GetPositiveOrZeroArgument("-labWarmupBatches");
            int measurementBatches = mode == "cold" ? 1 : GetPositiveArgument("-labMeasurementBatches");
            int repetitions = mode == "cold" ? 1 : GetPositiveOrZeroArgument("-labBenchmarkRepeat");
            if (repetitions < 1) repetitions = 1;
            byte[] assemblyBytes = ReadStreamingAssetBytes(ManagedAssemblyFile);
            byte[] policyBytes = ReadStreamingAssetBytes(BenchmarkPolicyFile);
            string aotMetadataMode = GetAotMetadataMode();
            BuildIdentity buildIdentity = LoadBuildIdentity();
            ValidateBuildIdentity(buildIdentity, assemblyBytes, aotMetadataMode);

            long started = Stopwatch.GetTimestamp();
            LoadAotMetadataIfEnabled();
            long aotMetadataLoaded = Stopwatch.GetTimestamp();
            Assembly assembly = Assembly.Load(assemblyBytes);
            long assemblyLoaded = Stopwatch.GetTimestamp();
            Type workloadType = assembly.GetType("HybridCLR.Lab.ManagedCases.PerformanceWorkload", true)!;
            object rawDefinitions = workloadType.GetProperty("All", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
            MethodInfo executeMethod = workloadType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static)!;
            Func<string, int, long> execute = (Func<string, int, long>)Delegate.CreateDelegate(
                typeof(Func<string, int, long>),
                executeMethod);
            List<BenchmarkDefinition> definitions = ReadBenchmarkDefinitions(rawDefinitions, requestedWorkload);
            Dictionary<string, BenchmarkGoldenWorkload> goldenById = LoadBenchmarkGolden(definitions, mode);
            long workloadDiscovered = Stopwatch.GetTimestamp();

            BenchmarkRun run = new BenchmarkRun
            {
                ExecutionRuntime = "hybridclr",
                BenchmarkMode = mode,
                AotMetadataMode = aotMetadataMode,
                StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                ProcessId = Process.GetCurrentProcess().Id,
                StopwatchFrequency = Stopwatch.Frequency,
                PolicySha256 = Sha256(policyBytes),
                GoldenContractSha256 = Sha256(ReadStreamingAssetBytes(BenchmarkGoldenFile)),
                AssemblySha256 = Sha256(assemblyBytes),
                AotMetadataLoadNanoseconds = aotMetadataMode == "none"
                    ? 0
                    : ToNanoseconds(aotMetadataLoaded - started),
                HotUpdateAssemblyLoadNanoseconds = ToNanoseconds(assemblyLoaded - aotMetadataLoaded),
                WorkloadDiscoveryNanoseconds = ToNanoseconds(workloadDiscovered - assemblyLoaded),
                BuildIdentity = buildIdentity,
            };
            foreach (BenchmarkDefinition definition in definitions)
            {
                run.Workloads.Add(ExecuteBenchmarkWorkload(execute, definition, mode, warmupBatches, measurementBatches, repetitions, goldenById[definition.Id]));
            }

            WriteInstrumentationSnapshotIfRequested();

            return run;
        }

        private static MetadataBenchmarkRun ExecuteMetadataBenchmark()
        {
            MetadataBenchmarkContext context = BeginMetadataBenchmark();
            if (context.WarmupMode != "none")
            {
                long warmupStarted = Stopwatch.GetTimestamp();
                MetadataWarmupExecution execution = ExecuteMetadataWarmup(context.Assembly, context.WarmupMode,
                    context.Run.StressAssemblySha256, context.ReflectionProfile);
                context.Run.MetadataWarmupNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - warmupStarted);
                context.Run.MetadataWarmupAcrossFrames = execution.AcrossFrames;
                context.Run.MetadataWarmupFrameCount = execution.FrameCount;
                context.Run.MetadataWarmupBatchCount = execution.BatchCount;
                context.Run.MetadataWarmupProcessingNanoseconds = execution.ProcessingNanoseconds;
                context.Run.MetadataWarmupMaxFrameNanoseconds = execution.MaxFrameNanoseconds;
                ForceFullCollection(context.SettleMilliseconds);
                context.Run.Snapshots.Add(CaptureMemorySnapshot("metadata-warmed"));
            }
            CompleteMetadataBenchmark(context);
            return context.Run;
        }

        private static void StartMetadataBenchmarkAcrossFrames()
        {
            GameObject gameObject = new GameObject("HybridCLR Lab Metadata Runner");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
            HybridCLRLabCoroutineHost host = gameObject.AddComponent<HybridCLRLabCoroutineHost>();
            host.StartCoroutine(ExecuteMetadataBenchmarkAcrossFrames(host));
        }

        private static IEnumerator ExecuteMetadataBenchmarkAcrossFrames(HybridCLRLabCoroutineHost host)
        {
            MetadataBenchmarkContext context = null;
            Exception failure = null;
            try
            {
                context = BeginMetadataBenchmark();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
            if (failure != null)
            {
                FinishMetadataBenchmarkAcrossFrames(host, failure);
                yield break;
            }

            if (context.WarmupMode != "none")
            {
                var execution = new MetadataWarmupExecution { AcrossFrames = true };
                long warmupStarted = Stopwatch.GetTimestamp();
                IEnumerator warmup = ExecuteMetadataWarmupIterator(context.Assembly, context.WarmupMode,
                    context.Run.StressAssemblySha256, context.ReflectionProfile, execution, true);
                while (true)
                {
                    bool hasNext = false;
                    object current = null;
                    try
                    {
                        hasNext = warmup.MoveNext();
                        if (hasNext)
                            current = warmup.Current;
                    }
                    catch (Exception exception)
                    {
                        failure = exception;
                    }
                    if (failure != null || !hasNext)
                        break;
                    yield return current;
                }
                if (failure != null)
                {
                    FinishMetadataBenchmarkAcrossFrames(host, failure);
                    yield break;
                }

                context.Run.MetadataWarmupNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - warmupStarted);
                context.Run.MetadataWarmupAcrossFrames = execution.AcrossFrames;
                context.Run.MetadataWarmupFrameCount = execution.FrameCount;
                context.Run.MetadataWarmupBatchCount = execution.BatchCount;
                context.Run.MetadataWarmupProcessingNanoseconds = execution.ProcessingNanoseconds;
                context.Run.MetadataWarmupMaxFrameNanoseconds = execution.MaxFrameNanoseconds;
                try
                {
                    ForceFullCollection(context.SettleMilliseconds);
                    context.Run.Snapshots.Add(CaptureMemorySnapshot("metadata-warmed"));
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure == null)
            {
                try
                {
                    CompleteMetadataBenchmark(context);
                    WriteMetadataBenchmarkResult(context.Run);
                    if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HYBRIDCLR_METADATA_PROFILE")))
                        Instrumentation.FlushMetadataProfile();
                    Debug.Log("[HybridCLR Lab] Metadata benchmark: " + context.Run.MetadataMode);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }
            FinishMetadataBenchmarkAcrossFrames(host, failure);
        }

        private static void FinishMetadataBenchmarkAcrossFrames(HybridCLRLabCoroutineHost host, Exception failure)
        {
            if (failure != null)
                Debug.LogException(failure);
            UnityEngine.Object.Destroy(host.gameObject);
            Application.Quit(failure == null ? 0 : 1);
        }

        private static MetadataBenchmarkContext BeginMetadataBenchmark()
        {
            string metadataMode = GetAotMetadataMode();
            BuildIdentity buildIdentity = LoadBuildIdentity();
            ValidateBuildIdentity(buildIdentity, null, metadataMode);
            int settleMilliseconds = GetOptionalNonNegativeArgument("-labSettleMilliseconds", 50);
            string reflectionProfile = GetArgument("-labReflectionProfile");
            if (string.IsNullOrWhiteSpace(reflectionProfile))
            {
                reflectionProfile = "exhaustive";
            }
            if (reflectionProfile != "exhaustive" && reflectionProfile != "selective")
            {
                throw new ArgumentException("-labReflectionProfile must be 'exhaustive' or 'selective'.");
            }
            string rawReflectionTypeLimit = GetArgument("-labReflectionTypeLimit");
            int reflectionTypeLimit = 0;
            if (reflectionProfile == "selective")
            {
                reflectionTypeLimit = GetPositiveArgument("-labReflectionTypeLimit");
                if (reflectionTypeLimit > MetadataStressOuterTypeCount)
                {
                    throw new ArgumentException("-labReflectionTypeLimit exceeds the generated stress type count.");
                }
            }
            else if (!string.IsNullOrWhiteSpace(rawReflectionTypeLimit))
            {
                throw new ArgumentException("-labReflectionTypeLimit is only valid for the selective reflection profile.");
            }
            string metadataScenario = GetArgument("-labMetadataScenario");
            if (string.IsNullOrWhiteSpace(metadataScenario))
            {
                metadataScenario = "entry-first";
            }
            if (metadataScenario != "entry-first" && metadataScenario != "reflection-first")
            {
                throw new ArgumentException("-labMetadataScenario must be 'entry-first' or 'reflection-first'.");
            }
            MetadataBenchmarkRun run = new MetadataBenchmarkRun
            {
                MetadataMode = metadataMode,
                MetadataScenario = metadataScenario,
                ReflectionProfile = reflectionProfile,
                ReflectionRequestedTypeCount = reflectionTypeLimit,
                StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                ProcessId = Process.GetCurrentProcess().Id,
                BuildIdentity = buildIdentity,
            };

            ForceFullCollection(settleMilliseconds);
            run.Snapshots.Add(CaptureMemorySnapshot("baseline"));

            long phaseStarted = Stopwatch.GetTimestamp();
            AotMetadataLoadSummary aotMetadata = LoadAotMetadataIfEnabled();
            run.AotMetadataBytes = aotMetadata.TotalBytes;
            run.AotMetadataFileCount = aotMetadata.FileCount;
            run.AotMetadataLoadNanoseconds = metadataMode == "none"
                ? 0
                : ToNanoseconds(Stopwatch.GetTimestamp() - phaseStarted);
            ForceFullCollection(settleMilliseconds);
            run.Snapshots.Add(CaptureMemorySnapshot("aot-metadata-loaded"));

            byte[] assemblyBytes = ReadStreamingAssetBytes(MetadataStressAssemblyFile);
            run.StressAssemblyName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(MetadataStressAssemblyFile));
            run.StressAssemblyBytes = assemblyBytes.LongLength;
            run.StressAssemblySha256 = Sha256(assemblyBytes);
            run.Snapshots.Add(CaptureMemorySnapshot("hot-update-bytes-read"));

            phaseStarted = Stopwatch.GetTimestamp();
            Assembly assembly = Assembly.Load(assemblyBytes);
            run.AssemblyLoadNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - phaseStarted);
            run.Snapshots.Add(CaptureMemorySnapshot("hot-update-assembly-loaded"));

            assemblyBytes = null!;
            ForceFullCollection(settleMilliseconds);
            run.Snapshots.Add(CaptureMemorySnapshot("hot-update-bytes-released"));

            string warmupMode = GetArgument("-labMetadataWarmup");
            if (string.IsNullOrWhiteSpace(warmupMode))
            {
                warmupMode = "none";
            }
            run.MetadataWarmupMode = warmupMode;

            return new MetadataBenchmarkContext
            {
                Run = run,
                Assembly = assembly,
                MetadataScenario = metadataScenario,
                ReflectionProfile = reflectionProfile,
                ReflectionTypeLimit = reflectionTypeLimit,
                SettleMilliseconds = settleMilliseconds,
                WarmupMode = warmupMode,
            };
        }

        private static void CompleteMetadataBenchmark(MetadataBenchmarkContext context)
        {
            if (context.MetadataScenario == "entry-first")
            {
                ExecuteMetadataEntry(context.Assembly, context.Run, context.SettleMilliseconds);
                ExecuteMetadataReflection(context.Assembly, context.Run, context.ReflectionProfile,
                    context.ReflectionTypeLimit, context.SettleMilliseconds);
            }
            else
            {
                ExecuteMetadataReflection(context.Assembly, context.Run, context.ReflectionProfile,
                    context.ReflectionTypeLimit, context.SettleMilliseconds);
                ExecuteMetadataEntry(context.Assembly, context.Run, context.SettleMilliseconds);
            }
        }

        private static void ExecuteMetadataEntry(Assembly assembly, MetadataBenchmarkRun run, int settleMilliseconds)
        {
            long phaseStarted = Stopwatch.GetTimestamp();
            Type entryType = assembly.GetType("HybridCLR.Lab.MetadataStress.MetadataStressEntry", true)!;
            MethodInfo entryMethod = entryType.GetMethod("Touch", BindingFlags.Public | BindingFlags.Static)!;
            run.EntryResolveNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - phaseStarted);

            phaseStarted = Stopwatch.GetTimestamp();
            run.EntryChecksum = Convert.ToInt64(entryMethod.Invoke(null, null), CultureInfo.InvariantCulture);
            run.EntryExecuteNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - phaseStarted);
            ForceFullCollection(settleMilliseconds);
            run.Snapshots.Add(CaptureMemorySnapshot("entry-executed"));
        }

        private static void ExecuteMetadataReflection(Assembly assembly, MetadataBenchmarkRun run, string reflectionProfile, int reflectionTypeLimit, int settleMilliseconds)
        {
            long phaseStarted = Stopwatch.GetTimestamp();
            Type[] types;
            if (reflectionProfile == "exhaustive")
            {
                types = assembly.GetTypes();
            }
            else
            {
                types = new Type[reflectionTypeLimit];
                for (int index = 0; index < reflectionTypeLimit; ++index)
                {
                    int typeIndex = (index * 641 + 1) & (MetadataStressOuterTypeCount - 1);
                    string typeName = "HybridCLR.Lab.MetadataStress.StressType" + typeIndex.ToString("D4", CultureInfo.InvariantCulture);
                    types[index] = assembly.GetType(typeName, true)!;
                }
            }
            run.ReflectionTypeDiscoveryNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - phaseStarted);
            int memberCount = 0;
            int attributeCount = 0;
            const BindingFlags allMembers = BindingFlags.Public | BindingFlags.NonPublic |
                BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            bool detailedProfile = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("HYBRIDCLR_METADATA_PROFILE"));
            if (detailedProfile)
            {
                foreach (Type type in types)
                {
                    long detailStarted = Stopwatch.GetTimestamp();
                    attributeCount += type.GetCustomAttributes(false).Length;
                    run.ReflectionTypeAttributesNanoseconds += ToNanoseconds(Stopwatch.GetTimestamp() - detailStarted);

                    detailStarted = Stopwatch.GetTimestamp();
                    MemberInfo[] members = type.GetMembers(allMembers);
                    run.ReflectionMemberDiscoveryNanoseconds += ToNanoseconds(Stopwatch.GetTimestamp() - detailStarted);
                    memberCount += members.Length;

                    detailStarted = Stopwatch.GetTimestamp();
                    foreach (MemberInfo member in members)
                    {
                        attributeCount += member.GetCustomAttributes(false).Length;
                    }
                    run.ReflectionMemberAttributesNanoseconds += ToNanoseconds(Stopwatch.GetTimestamp() - detailStarted);
                }
            }
            else
            {
                foreach (Type type in types)
                {
                    attributeCount += type.GetCustomAttributes(false).Length;
                    MemberInfo[] members = type.GetMembers(allMembers);
                    memberCount += members.Length;
                    foreach (MemberInfo member in members)
                    {
                        attributeCount += member.GetCustomAttributes(false).Length;
                    }
                }
            }
            run.ReflectionTouchNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - phaseStarted);
            run.TypeCount = types.Length;
            run.MemberCount = memberCount;
            run.AttributeCount = attributeCount;
            ForceFullCollection(settleMilliseconds);
            run.Snapshots.Add(CaptureMemorySnapshot("reflection-touched"));
        }

        private static MetadataWarmupExecution ExecuteMetadataWarmup(Assembly assembly, string warmupMode,
            string assemblySha256, string reflectionProfile)
        {
            var execution = new MetadataWarmupExecution();
            IEnumerator iterator = ExecuteMetadataWarmupIterator(assembly, warmupMode, assemblySha256,
                reflectionProfile, execution, false);
            while (iterator.MoveNext())
            {
            }
            return execution;
        }

        private static IEnumerator ExecuteMetadataWarmupIterator(Assembly assembly, string warmupMode,
            string assemblySha256, string reflectionProfile, MetadataWarmupExecution execution, bool acrossFrames)
        {
            if (!string.Equals(warmupMode, "entry", StringComparison.Ordinal) &&
                !string.Equals(warmupMode, "entry-method", StringComparison.Ordinal) &&
                !string.Equals(warmupMode, "entry-graph", StringComparison.Ordinal) &&
                !string.Equals(warmupMode, "entry-method-graph", StringComparison.Ordinal))
            {
                throw new ArgumentException("-labMetadataWarmup supports 'entry', 'entry-method', 'entry-graph', or 'entry-method-graph'.");
            }
            execution.AcrossFrames = acrossFrames;
            long frameStarted = Stopwatch.GetTimestamp();

            if (string.Equals(warmupMode, "entry-method-graph", StringComparison.Ordinal))
            {
                PrewarmManifestData methodManifest = JsonUtility.FromJson<PrewarmManifestData>(
                    Encoding.UTF8.GetString(ReadStreamingAssetBytes(MetadataStressPrewarmManifestFile)));
                if (methodManifest == null || methodManifest.schemaVersion != 1 ||
                    !string.Equals(methodManifest.assemblyName, assembly.GetName().Name, StringComparison.Ordinal) ||
                    !string.Equals(methodManifest.assemblySha256, assemblySha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(methodManifest.rootType, "HybridCLR.Lab.MetadataStress.MetadataStressEntry", StringComparison.Ordinal) ||
                    !string.Equals(methodManifest.rootMethod, "Touch", StringComparison.Ordinal) ||
                    methodManifest.rootParameterCount != 0 || methodManifest.reachableMethodCount < 1 ||
                    !string.Equals(methodManifest.graphCoverage, "static-il-candidates", StringComparison.Ordinal) ||
                    methodManifest.dynamicEdgesIncluded ||
                    methodManifest.methods == null || methodManifest.methods.Length == 0 ||
                    methodManifest.methodCount != methodManifest.methods.Length)
                {
                    throw new InvalidDataException("Metadata stress prewarm manifest has no valid static IL method candidate graph.");
                }

                var tokenDescriptors = new List<global::HybridCLR.RuntimePrewarmMethodDescriptor>(methodManifest.methods.Length);
                var reflectionDescriptors = new List<global::HybridCLR.RuntimePrewarmMethodDescriptor>();
                // Token lookup intentionally leaves the declaring class's method
                // table partially materialized on Tuanjie 2022. It is the fastest
                // option when the contract is entry-first and reflection is
                // selective. Exhaustive reflection must pay the complete method
                // table cost during warmup, otherwise the deferred fill creates a
                // much larger tail at Type.GetMembers time.
                bool allowTokenWarmup = !string.Equals(reflectionProfile, "exhaustive", StringComparison.Ordinal);
                foreach (PrewarmMethodData method in methodManifest.methods)
                {
                    if (method == null || string.IsNullOrWhiteSpace(method.declaringType) ||
                        string.IsNullOrWhiteSpace(method.name) || method.parameterCount < 0 ||
                        method.genericParameterCount < 0 || method.metadataToken < 0)
                    {
                        throw new InvalidDataException("Metadata stress prewarm method graph contains an invalid descriptor.");
                    }
                    var descriptor = new global::HybridCLR.RuntimePrewarmMethodDescriptor(
                        method.declaringType,
                        method.name,
                        method.parameterCount,
                        method.genericParameterCount,
                        method.metadataToken,
                        method.parameterTypes,
                        method.returnType);
                    // Token lookup is only valid for non-generic declaring types;
                    // generic/legacy descriptors retain the reflection resolver.
                    if (allowTokenWarmup && method.metadataToken > 0 && method.declaringType.IndexOf('<') < 0 &&
                        method.genericParameterCount == 0)
                        tokenDescriptors.Add(descriptor);
                    else
                        reflectionDescriptors.Add(descriptor);
                }

                float methodBudgetMilliseconds = GetOptionalNonNegativeFloatArgument("-labMetadataWarmupBudgetMilliseconds", 1.0f);
                int maxMethods = GetOptionalNonNegativeArgument("-labMetadataWarmupMaxMethods", 8);
                if (maxMethods < 1)
                {
                    throw new ArgumentException("-labMetadataWarmupMaxMethods must be at least 1.");
                }

                int methodBatchCount = 0;
                int methodFailedCount = 0;
                RuntimePrewarmMethodTokenManifestQueue tokenQueue =
                    RuntimePrewarmManifest.CreateIncrementalMethodTokenQueue(assembly, tokenDescriptors);
                while (!tokenQueue.IsComplete)
                {
                    RuntimePrewarmMethodBatchResult batch = tokenQueue.Process(methodBudgetMilliseconds, maxMethods);
                    if (batch.ProcessedCount == 0)
                        throw new InvalidOperationException("Metadata token prewarm queue made no progress.");
                    methodBatchCount++;
                    execution.BatchCount++;
                    methodFailedCount += batch.FailedCount;
                    if (acrossFrames && (!tokenQueue.IsComplete || reflectionDescriptors.Count > 0))
                    {
                        RecordMetadataWarmupFrame(execution, frameStarted);
                        yield return null;
                        frameStarted = Stopwatch.GetTimestamp();
                    }
                }
                RuntimePrewarmMethodManifestQueue methodQueue =
                    RuntimePrewarmManifest.CreateIncrementalMethodBaseQueue(assembly, reflectionDescriptors);
                while (!methodQueue.IsComplete)
                {
                    RuntimePrewarmMethodBatchResult batch = methodQueue.Process(methodBudgetMilliseconds, maxMethods);
                    if (batch.ProcessedCount == 0)
                        throw new InvalidOperationException("Metadata method prewarm queue made no progress.");
                    methodBatchCount++;
                    execution.BatchCount++;
                    methodFailedCount += batch.FailedCount;
                    if (acrossFrames && !methodQueue.IsComplete)
                    {
                        RecordMetadataWarmupFrame(execution, frameStarted);
                        yield return null;
                        frameStarted = Stopwatch.GetTimestamp();
                    }
                }
                if (methodFailedCount != 0)
                {
                    throw new InvalidOperationException("Metadata method prewarm graph failed for " + methodFailedCount + " method(s).");
                }
                Debug.Log("[HybridCLR Lab] Metadata method prewarm graph completed: " + methodManifest.methods.Length + " method(s) in " + methodBatchCount + " batch(es).");
                RecordMetadataWarmupFrame(execution, frameStarted);
                yield break;
            }

            if (string.Equals(warmupMode, "entry-graph", StringComparison.Ordinal))
            {
                PrewarmManifestData manifest = JsonUtility.FromJson<PrewarmManifestData>(
                    Encoding.UTF8.GetString(ReadStreamingAssetBytes(MetadataStressPrewarmManifestFile)));
                if (manifest == null || manifest.schemaVersion != 1 ||
                    !string.Equals(manifest.assemblyName, assembly.GetName().Name, StringComparison.Ordinal) ||
                    !string.Equals(manifest.assemblySha256, assemblySha256, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(manifest.rootType, "HybridCLR.Lab.MetadataStress.MetadataStressEntry", StringComparison.Ordinal) ||
                    !string.Equals(manifest.rootMethod, "Touch", StringComparison.Ordinal) ||
                    manifest.rootParameterCount != 0 || manifest.reachableMethodCount < 1 ||
                    !string.Equals(manifest.graphCoverage, "static-il-candidates", StringComparison.Ordinal) ||
                    manifest.dynamicEdgesIncluded ||
                    manifest.types == null || manifest.types.Length == 0)
                {
                    throw new InvalidDataException("Metadata stress prewarm manifest does not match the loaded assembly.");
                }

                HashSet<string> manifestTypes = new HashSet<string>(StringComparer.Ordinal);
                foreach (string typeName in manifest.types)
                {
                    if (string.IsNullOrWhiteSpace(typeName) || !manifestTypes.Add(typeName))
                    {
                        throw new InvalidDataException("Metadata stress prewarm manifest contains duplicate or empty types.");
                    }
                }

                float budgetMilliseconds = GetOptionalNonNegativeFloatArgument("-labMetadataWarmupBudgetMilliseconds", 1.0f);
                int maxTypes = GetOptionalNonNegativeArgument("-labMetadataWarmupMaxTypes", 8);
                if (maxTypes < 1)
                {
                    throw new ArgumentException("-labMetadataWarmupMaxTypes must be at least 1.");
                }

                int batchCount = 0;
                int failedCount = 0;
                RuntimePrewarmManifestQueue queue = RuntimePrewarmManifest.CreateIncrementalQueue(
                    assembly, manifest.types);
                while (!queue.IsComplete)
                {
                    RuntimePrewarmBatchResult batch = queue.Process(budgetMilliseconds, maxTypes);
                    if (batch.ProcessedCount == 0)
                        throw new InvalidOperationException("Metadata prewarm queue made no progress.");
                    batchCount++;
                    execution.BatchCount++;
                    failedCount += batch.FailedCount;
                    if (acrossFrames && !queue.IsComplete)
                    {
                        RecordMetadataWarmupFrame(execution, frameStarted);
                        yield return null;
                        frameStarted = Stopwatch.GetTimestamp();
                    }
                }
                if (failedCount != 0)
                {
                    throw new InvalidOperationException("Metadata prewarm graph failed for " + failedCount + " type(s).");
                }
                Debug.Log("[HybridCLR Lab] Metadata prewarm graph completed: " + manifest.types.Length + " type(s) in " + batchCount + " batch(es).");
                RecordMetadataWarmupFrame(execution, frameStarted);
                yield break;
            }

            Type entryType = assembly.GetType("HybridCLR.Lab.MetadataStress.MetadataStressEntry", true)!;
            if (string.Equals(warmupMode, "entry", StringComparison.Ordinal))
            {
                if (!global::HybridCLR.RuntimeApi.PrewarmClass(entryType))
                    throw new InvalidOperationException("Metadata warmup could not prepare the entry class.");
                if (!global::HybridCLR.RuntimeApi.PrewarmClass(entryType))
                    throw new InvalidOperationException("Metadata warmup class preparation was not idempotent.");
                execution.BatchCount = 1;
                RecordMetadataWarmupFrame(execution, frameStarted);
                yield break;
            }

            MethodInfo entryMethod = entryType.GetMethod("Touch", BindingFlags.Public | BindingFlags.Static)!;
            if (!global::HybridCLR.RuntimeApi.PrewarmMethod(entryMethod))
                throw new InvalidOperationException("Metadata warmup could not prepare the entry method.");
            if (!global::HybridCLR.RuntimeApi.PrewarmMethod(entryMethod))
                throw new InvalidOperationException("Metadata warmup method preparation was not idempotent.");
            execution.BatchCount = 1;
            RecordMetadataWarmupFrame(execution, frameStarted);
        }

        private static void RecordMetadataWarmupFrame(MetadataWarmupExecution execution, long frameStarted)
        {
            long elapsed = ToNanoseconds(Stopwatch.GetTimestamp() - frameStarted);
            execution.FrameCount++;
            execution.ProcessingNanoseconds += elapsed;
            if (elapsed > execution.MaxFrameNanoseconds)
                execution.MaxFrameNanoseconds = elapsed;
        }

        private static void ForceFullCollection(int settleMilliseconds)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            if (settleMilliseconds > 0)
            {
                Thread.Sleep(settleMilliseconds);
            }
        }

        private static MemorySnapshot CaptureMemorySnapshot(string name)
        {
            long privateBytes = -1;
            long workingSetBytes = -1;
            long peakPrivateBytes = -1;
            long peakWorkingSetBytes = -1;
#if UNITY_STANDALONE_WIN
            ProcessMemoryCounters counters = new ProcessMemoryCounters
            {
                Size = (uint)Marshal.SizeOf<ProcessMemoryCounters>(),
            };
            if (GetProcessMemoryInfo(GetCurrentProcess(), ref counters, counters.Size))
            {
                privateBytes = checked((long)counters.PagefileUsage.ToUInt64());
                workingSetBytes = checked((long)counters.WorkingSetSize.ToUInt64());
                peakPrivateBytes = checked((long)counters.PeakPagefileUsage.ToUInt64());
                peakWorkingSetBytes = checked((long)counters.PeakWorkingSetSize.ToUInt64());
            }
#endif
            try
            {
                if (privateBytes < 0 || workingSetBytes < 0)
                {
                    using Process process = Process.GetCurrentProcess();
                    process.Refresh();
                    privateBytes = process.PrivateMemorySize64 > 0 ? process.PrivateMemorySize64 : -1;
                    workingSetBytes = process.WorkingSet64 > 0 ? process.WorkingSet64 : -1;
                    peakPrivateBytes = process.PeakPagedMemorySize64 > 0 ? process.PeakPagedMemorySize64 : -1;
                    peakWorkingSetBytes = process.PeakWorkingSet64 > 0 ? process.PeakWorkingSet64 : -1;
                }
            }
            catch
            {
                // Some IL2CPP platforms do not expose all Process memory counters.
            }

            long unityAllocatedBytes = -1;
            long unityReservedBytes = -1;
            long androidPssBytes = -1;
            try
            {
                unityAllocatedBytes = Profiler.GetTotalAllocatedMemoryLong();
                unityReservedBytes = Profiler.GetTotalReservedMemoryLong();
            }
            catch
            {
                // Profiler memory counters are optional on stripped release Players.
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using AndroidJavaClass debug = new AndroidJavaClass("android.os.Debug");
                androidPssBytes = checked(debug.CallStatic<long>("getPss") * 1024L);
            }
            catch
            {
                // Android PSS is optional on vendor-modified runtimes.
            }
#endif

            return new MemorySnapshot
            {
                Name = name,
                PrivateBytes = privateBytes,
                WorkingSetBytes = workingSetBytes,
                PeakPrivateBytes = peakPrivateBytes,
                PeakWorkingSetBytes = peakWorkingSetBytes,
                ManagedHeapBytes = GC.GetTotalMemory(false),
                UnityAllocatedBytes = unityAllocatedBytes,
                UnityReservedBytes = unityReservedBytes,
                AndroidPssBytes = androidPssBytes,
            };
        }

        private static BuildIdentity LoadBuildIdentity()
        {
            byte[] bytes = ReadStreamingAssetBytes(BuildIdentityFile);
            BuildIdentity identity = UnityEngine.JsonUtility.FromJson<BuildIdentity>(Encoding.UTF8.GetString(bytes));
            if (identity == null || identity.schemaVersion != 1 ||
                string.IsNullOrWhiteSpace(identity.profile) ||
                string.IsNullOrWhiteSpace(identity.target) ||
                string.IsNullOrWhiteSpace(identity.architecture) ||
                string.IsNullOrWhiteSpace(identity.il2cppCodeGeneration) ||
                string.IsNullOrWhiteSpace(identity.hybridclrUnityTreeSha256) ||
                string.IsNullOrWhiteSpace(identity.stagedRuntimeSha256) ||
                string.IsNullOrWhiteSpace(identity.managedAssemblySha256) ||
                string.IsNullOrWhiteSpace(identity.crossAssemblyDerivedSha256) ||
                (identity.aotMetadataPackaging != "include" && identity.aotMetadataPackaging != "exclude"))
            {
                throw new InvalidDataException("Invalid build identity.");
            }
            identity.Sha256 = Sha256(bytes);
            return identity;
        }

        private static void ValidateBuildIdentity(BuildIdentity identity, byte[] managedAssemblyBytes, string metadataMode, byte[] crossAssemblyBytes = null)
        {
            if (managedAssemblyBytes != null &&
                !string.Equals(identity.managedAssemblySha256, Sha256(managedAssemblyBytes), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Build identity managed assembly hash does not match the staged assembly.");
            }
            if (crossAssemblyBytes != null &&
                !string.Equals(identity.crossAssemblyDerivedSha256, Sha256(crossAssemblyBytes), StringComparison.Ordinal))
            {
                throw new InvalidDataException("Build identity cross-assembly probe hash does not match the staged assembly.");
            }
            if (identity.aotMetadataPackaging == "exclude" && metadataMode != "none")
            {
                throw new InvalidOperationException("A no-metadata build can only run with -labAotMetadataMode none.");
            }
        }

        private static void ExecuteCrossAssemblyLazyVTableProbe(Assembly parentAssembly, byte[] assemblyBytes)
        {
            Assembly assembly = Assembly.Load(assemblyBytes);
            ExecuteLazyMetadataConcurrentFirstTouchProbe(assembly);
            Type[] parentTypes = parentAssembly.GetTypes();
            if (parentTypes.Length == 0)
            {
                throw new InvalidOperationException("Cross-assembly lazy VTable probe requires materialized parent types.");
            }
            Type probeType = assembly.GetType("HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe", true)!;
            MethodInfo runMethod = probeType.GetMethod("Run", BindingFlags.Public | BindingFlags.Static)
                ?? throw new MissingMethodException(probeType.FullName, "Run");
            string result = (string)runMethod.Invoke(null, null)!;
            if (!string.Equals(result, "derived:26:34", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cross-assembly lazy VTable probe failed: " + result);
            }
        }

        private static void ExecuteLazyMetadataConcurrentFirstTouchProbe(Assembly assembly)
        {
            const int workerCount = 8;
            Thread[] workers = new Thread[workerCount];
            Exception[] errors = new Exception[workerCount];
            int[] checksums = new int[workerCount];
            using CountdownEvent ready = new CountdownEvent(workerCount);
            using ManualResetEventSlim start = new ManualResetEventSlim(false);

            for (int workerIndex = 0; workerIndex < workerCount; ++workerIndex)
            {
                int capturedIndex = workerIndex;
                workers[workerIndex] = new Thread(() =>
                {
                    ready.Signal();
                    start.Wait();
                    try
                    {
                        checksums[capturedIndex] = TouchLazyMetadataConcurrencyTarget(assembly, capturedIndex);
                    }
                    catch (Exception exception)
                    {
                        errors[capturedIndex] = exception;
                    }
                });
                workers[workerIndex].Start();
            }

            if (!ready.Wait(TimeSpan.FromSeconds(30)))
            {
                start.Set();
                throw new TimeoutException("Lazy metadata workers did not reach the start barrier.");
            }
            start.Set();

            int checksum = 0;
            for (int workerIndex = 0; workerIndex < workerCount; ++workerIndex)
            {
                if (!workers[workerIndex].Join(TimeSpan.FromSeconds(30)))
                {
                    throw new TimeoutException("Lazy metadata worker did not complete.");
                }
                if (errors[workerIndex] != null)
                {
                    throw new InvalidOperationException(
                        "Lazy metadata worker " + workerIndex + " failed.",
                        errors[workerIndex]);
                }
                checksum += checksums[workerIndex];
            }
            if (checksum != 60048)
            {
                throw new InvalidOperationException("Lazy metadata concurrency checksum mismatch: " + checksum);
            }
        }

        private static int TouchLazyMetadataConcurrencyTarget(Assembly assembly, int workerIndex)
        {
            Type targetType = assembly.GetType(
                "HybridCLR.Lab.CrossAssemblyDerived.LazyMetadataConcurrencyTarget",
                true)!;
            const BindingFlags declaredPublic =
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            MethodInfo compute = targetType.GetMethod("Compute", declaredPublic)
                ?? throw new MissingMethodException(targetType.FullName, "Compute");
            MethodInfo raise = targetType.GetMethod("Raise", declaredPublic)
                ?? throw new MissingMethodException(targetType.FullName, "Raise");
            MethodInfo setThreadValue = targetType.GetMethod("SetAndGetThreadValue", declaredPublic)
                ?? throw new MissingMethodException(targetType.FullName, "SetAndGetThreadValue");
            PropertyInfo property = targetType.GetProperty("Value", declaredPublic)
                ?? throw new MissingMemberException(targetType.FullName, "Value");
            EventInfo changed = targetType.GetEvent("Changed", declaredPublic)
                ?? throw new MissingMemberException(targetType.FullName, "Changed");
            FieldInfo staticField = targetType.GetField("StaticValue", declaredPublic)
                ?? throw new MissingFieldException(targetType.FullName, "StaticValue");
            FieldInfo threadField = targetType.GetField("ThreadValue", declaredPublic)
                ?? throw new MissingFieldException(targetType.FullName, "ThreadValue");
            ParameterInfo[] parameters = compute.GetParameters();
            if (parameters.Length != 1)
            {
                throw new InvalidOperationException("Lazy metadata target has an unexpected method signature.");
            }

            object[][] attributeGroups =
            {
                targetType.GetCustomAttributes(false),
                compute.GetCustomAttributes(false),
                parameters[0].GetCustomAttributes(false),
                property.GetCustomAttributes(false),
                changed.GetCustomAttributes(false),
                staticField.GetCustomAttributes(false),
                threadField.GetCustomAttributes(false),
            };
            int markerCount = 0;
            bool hasThreadStatic = false;
            foreach (object[] attributes in attributeGroups)
            {
                foreach (object attribute in attributes)
                {
                    string attributeType = attribute.GetType().FullName;
                    if (string.Equals(
                        attributeType,
                        "HybridCLR.Lab.CrossAssemblyDerived.LazyMetadataMarkerAttribute",
                        StringComparison.Ordinal))
                    {
                        ++markerCount;
                    }
                    else if (attribute is ThreadStaticAttribute)
                    {
                        hasThreadStatic = true;
                    }
                }
            }
            if (markerCount != 7 || !hasThreadStatic)
            {
                throw new InvalidOperationException(
                    "Lazy metadata attribute contract mismatch: markers=" + markerCount +
                    ", threadStatic=" + hasThreadStatic);
            }

            object instance = Activator.CreateInstance(targetType)!;
            property.SetValue(instance, workerIndex);
            int threadValue = (int)setThreadValue.Invoke(null, new object[] { workerIndex + 1 })!;
            if (threadValue != workerIndex + 1 || (int)threadField.GetValue(null)! != threadValue)
            {
                throw new InvalidOperationException("Lazy metadata ThreadStatic value mismatch.");
            }
            int callbackCount = 0;
            Action callback = () => Interlocked.Increment(ref callbackCount);
            changed.AddEventHandler(instance, callback);
            int computeResult = (int)compute.Invoke(instance, new object[] { workerIndex })!;
            raise.Invoke(instance, null);
            changed.RemoveEventHandler(instance, callback);
            return markerCount * 1000 +
                (hasThreadStatic ? 100 : 0) +
                (int)property.GetValue(instance)! * 100 +
                threadValue * 10 +
                computeResult +
                callbackCount;
        }

#if UNITY_STANDALONE_WIN
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("psapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetProcessMemoryInfo(
            IntPtr process,
            ref ProcessMemoryCounters counters,
            uint size);
#endif

        private static void WriteInstrumentationSnapshotIfRequested()
        {
            string output = GetArgument("-labInstrumentationResult");
            if (string.IsNullOrWhiteSpace(output))
            {
                return;
            }

            string snapshot = Instrumentation.Snapshot();
            if (string.IsNullOrWhiteSpace(snapshot))
            {
                throw new InvalidDataException("Instrumented runtime returned an empty profile snapshot.");
            }

            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            WriteAllTextAtomic(output, snapshot);
        }

        private static BenchmarkRun ExecuteAotBenchmark()
        {
            string mode = GetArgument("-labBenchmarkMode");
            if (mode != "cold" && mode != "steady")
            {
                throw new ArgumentException("-labBenchmarkMode must be 'cold' or 'steady'.");
            }

            string requestedWorkload = GetArgument("-labBenchmarkWorkload");
            if (mode == "cold" && string.IsNullOrWhiteSpace(requestedWorkload))
            {
                throw new ArgumentException("Cold benchmark mode requires -labBenchmarkWorkload.");
            }

            int warmupBatches = mode == "cold" ? 0 : GetPositiveOrZeroArgument("-labWarmupBatches");
            int measurementBatches = mode == "cold" ? 1 : GetPositiveArgument("-labMeasurementBatches");
            int repetitions = mode == "cold" ? 1 : GetPositiveOrZeroArgument("-labBenchmarkRepeat");
            if (repetitions < 1) repetitions = 1;
            string aotMetadataMode = GetAotMetadataMode();
            BuildIdentity buildIdentity = LoadBuildIdentity();
            ValidateBuildIdentity(buildIdentity, null, aotMetadataMode);
            long started = Stopwatch.GetTimestamp();
            Func<string, int, long> execute = global::HybridCLR.Lab.ManagedCasesAot.PerformanceWorkload.Execute;
            List<BenchmarkDefinition> definitions = new List<BenchmarkDefinition>();
            foreach (global::HybridCLR.Lab.ManagedCasesAot.PerformanceWorkloadDefinition definition in
                     global::HybridCLR.Lab.ManagedCasesAot.PerformanceWorkload.All)
            {
                if (!string.IsNullOrWhiteSpace(requestedWorkload) &&
                    !string.Equals(requestedWorkload, definition.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                definitions.Add(new BenchmarkDefinition
                {
                    Id = definition.Id,
                    Category = definition.Category,
                    ColdIterations = definition.ColdIterations,
                    Iterations = definition.Iterations,
                    Features = definition.Features.ToArray(),
                });
            }
            if (definitions.Count == 0)
            {
                throw new ArgumentException("Unknown benchmark workload: " + requestedWorkload);
            }
            Dictionary<string, BenchmarkGoldenWorkload> goldenById = LoadBenchmarkGolden(definitions, mode);

            string assemblyHash = GetArgument("-labAotAssemblySha256");
            if (assemblyHash.Length != 64)
            {
                throw new InvalidDataException("Missing -labAotAssemblySha256 for AOT benchmark.");
            }

            BenchmarkRun run = new BenchmarkRun
            {
                ExecutionRuntime = "aot",
                BenchmarkMode = mode,
                AotMetadataMode = aotMetadataMode,
                StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                ProcessId = Process.GetCurrentProcess().Id,
                StopwatchFrequency = Stopwatch.Frequency,
                PolicySha256 = Sha256(ReadStreamingAssetBytes(BenchmarkPolicyFile)),
                GoldenContractSha256 = Sha256(ReadStreamingAssetBytes(BenchmarkGoldenFile)),
                AssemblySha256 = assemblyHash,
                AotMetadataLoadNanoseconds = 0,
                HotUpdateAssemblyLoadNanoseconds = 0,
                WorkloadDiscoveryNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - started),
                BuildIdentity = buildIdentity,
            };
            foreach (BenchmarkDefinition definition in definitions)
            {
                run.Workloads.Add(ExecuteBenchmarkWorkload(execute, definition, mode, warmupBatches, measurementBatches, repetitions, goldenById[definition.Id]));
            }

            return run;
        }

        private static List<BenchmarkDefinition> ReadBenchmarkDefinitions(object rawDefinitions, string requestedWorkload)
        {
            List<BenchmarkDefinition> definitions = new List<BenchmarkDefinition>();
            foreach (object rawDefinition in (IEnumerable)rawDefinitions)
            {
                Type type = rawDefinition.GetType();
                string id = (string)type.GetProperty("Id")!.GetValue(rawDefinition)!;
                if (!string.IsNullOrWhiteSpace(requestedWorkload) &&
                    !string.Equals(requestedWorkload, id, StringComparison.Ordinal))
                {
                    continue;
                }

                definitions.Add(new BenchmarkDefinition
                {
                    Id = id,
                    Category = (string)type.GetProperty("Category")!.GetValue(rawDefinition)!,
                    ColdIterations = (int)type.GetProperty("ColdIterations")!.GetValue(rawDefinition)!,
                    Iterations = (int)type.GetProperty("Iterations")!.GetValue(rawDefinition)!,
                    Features = ((IEnumerable)type.GetProperty("Features")!.GetValue(rawDefinition)!)
                        .Cast<object>()
                        .Select(value => (string)value)
                        .ToArray(),
                });
            }

            if (definitions.Count == 0)
            {
                throw new ArgumentException("Unknown benchmark workload: " + requestedWorkload);
            }

            return definitions;
        }

        private static BenchmarkWorkloadResult ExecuteBenchmarkWorkload(
            Func<string, int, long> execute,
            BenchmarkDefinition definition,
            string mode,
            int warmupBatches,
            int measurementBatches,
            int repetitions,
            BenchmarkGoldenWorkload golden)
        {
            int iterations = mode == "cold" ? definition.ColdIterations : definition.Iterations;
            long expectedChecksum = ParseChecksum(mode == "cold" ? golden.coldChecksum : golden.steadyChecksum, definition.Id);

            if (mode == "steady" && !string.IsNullOrWhiteSpace(GetArgument("-labInstrumentationResult")))
            {
                Instrumentation.Reset();
            }

            for (int i = 0; i < warmupBatches; i++)
            {
                for (int repeat = 0; repeat < repetitions; repeat++)
                {
                    VerifyChecksum(definition.Id, execute(definition.Id, iterations), expectedChecksum);
                }
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
            BenchmarkWorkloadResult result = new BenchmarkWorkloadResult
            {
                Id = definition.Id,
                Category = definition.Category,
                Features = definition.Features,
                Iterations = iterations,
                WarmupBatches = warmupBatches,
                MeasurementBatches = measurementBatches,
                Repetitions = repetitions,
            };

            for (int i = 0; i < measurementBatches; i++)
            {
                long batchStarted = Stopwatch.GetTimestamp();
                long checksum = 0;
                for (int repeat = 0; repeat < repetitions; repeat++)
                {
                    checksum = execute(definition.Id, iterations);
                    VerifyChecksum(definition.Id, checksum, expectedChecksum);
                }
                long elapsedNanoseconds = ToNanoseconds(Stopwatch.GetTimestamp() - batchStarted);
                result.BatchNanoseconds.Add(elapsedNanoseconds / repetitions);
                result.NanosecondsPerIteration.Add(elapsedNanoseconds / (double)repetitions / iterations);
            }

            result.Checksum = expectedChecksum.ToString(CultureInfo.InvariantCulture);
            result.Generation0Collections = GC.CollectionCount(0) - gen0Before;
            result.Generation1Collections = GC.CollectionCount(1) - gen1Before;
            result.Generation2Collections = GC.CollectionCount(2) - gen2Before;
            return result;
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

        private static Dictionary<string, BenchmarkGoldenWorkload> LoadBenchmarkGolden(
            List<BenchmarkDefinition> definitions,
            string mode)
        {
            string json = Encoding.UTF8.GetString(ReadStreamingAssetBytes(BenchmarkGoldenFile));
            BenchmarkGoldenFileData golden = JsonUtility.FromJson<BenchmarkGoldenFileData>(json);
            if (golden == null || golden.schemaVersion != 1 || !string.Equals(golden.suiteId, "managed-performance-v1", StringComparison.Ordinal) || golden.workloads == null)
            {
                throw new InvalidDataException("Invalid benchmark golden contract.");
            }

            Dictionary<string, BenchmarkGoldenWorkload> byId = new Dictionary<string, BenchmarkGoldenWorkload>(StringComparer.Ordinal);
            foreach (BenchmarkGoldenWorkload workload in golden.workloads)
            {
                if (workload == null || string.IsNullOrWhiteSpace(workload.id) || !byId.TryAdd(workload.id, workload))
                {
                    throw new InvalidDataException("Duplicate or empty benchmark golden workload ID.");
                }
            }
            foreach (BenchmarkDefinition definition in definitions)
            {
                if (!byId.TryGetValue(definition.Id, out BenchmarkGoldenWorkload workload) ||
                    !string.Equals(definition.Category, workload.category, StringComparison.Ordinal) ||
                    !definition.Features.SequenceEqual(workload.features ?? Array.Empty<string>(), StringComparer.Ordinal) ||
                    (mode == "cold" && definition.ColdIterations != workload.coldIterations) ||
                    (mode == "steady" && definition.Iterations != workload.steadyIterations))
                {
                    throw new InvalidDataException("Benchmark golden metadata mismatch: " + definition.Id);
                }
            }
            return byId;
        }

        private static long ToNanoseconds(long ticks)
        {
            return Math.Max(0, (long)Math.Round(ticks * (1_000_000_000.0 / Stopwatch.Frequency)));
        }

        private static Dictionary<string, ManifestContract> LoadManifestContracts(string relativePath, string expectedSuiteId)
        {
            string[] lines = ReadStreamingAssetLines(relativePath);
            string suiteLine = lines.Length == 0 ? string.Empty : lines[0].TrimStart('\uFEFF');
            if (lines.Length < 2 || !suiteLine.StartsWith("suiteId=", StringComparison.Ordinal) ||
                !string.Equals(suiteLine.Substring("suiteId=".Length), expectedSuiteId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Invalid manifest contracts index.");
            }

            Dictionary<string, ManifestContract> contracts = new Dictionary<string, ManifestContract>(StringComparer.Ordinal);
            for (int i = 1; i < lines.Length; i++)
            {
                string[] fields = lines[i].Split('\t');
                if (fields.Length != 4 || fields[0].Length == 0 || !contracts.TryAdd(fields[0], new ManifestContract
                    {
                        Category = fields[1],
                        Layer = fields[2],
                        Features = fields[3].Length == 0 ? Array.Empty<string>() : fields[3].Split(',')
                    }))
                {
                    throw new InvalidDataException("Duplicate or malformed manifest contract at line " + (i + 1));
                }
            }

            return contracts;
        }

        private static AotMetadataLoadSummary LoadAotMetadataIfEnabled()
        {
            if (GetAotMetadataMode() == "none")
            {
                return new AotMetadataLoadSummary();
            }

            return LoadAotMetadata();
        }

        private static string GetAotMetadataMode()
        {
            string mode = GetArgument("-labAotMetadataMode");
            if (string.IsNullOrWhiteSpace(mode))
            {
                return "supplemental";
            }
            if (mode != "supplemental" && mode != "none")
            {
                throw new ArgumentException("-labAotMetadataMode must be 'supplemental' or 'none'.");
            }
            return mode;
        }

        private static AotMetadataLoadSummary LoadAotMetadata()
        {
            AotMetadataLoadSummary summary = new AotMetadataLoadSummary();
            foreach (string rawAssemblyName in ReadStreamingAssetLines(AotMetadataIndexFile))
            {
                string assemblyName = rawAssemblyName.Trim().TrimStart('\uFEFF');
                if (assemblyName.Length == 0)
                {
                    continue;
                }

                string metadataPath = "HybridCLRLab/AotMetadata/" + assemblyName + ".dll.bytes";
                byte[] metadataBytes = ReadStreamingAssetBytes(metadataPath);
                summary.TotalBytes += metadataBytes.LongLength;
                summary.FileCount++;
                global::HybridCLR.LoadImageErrorCode result = global::HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(
                    metadataBytes,
                    global::HybridCLR.HomologousImageMode.SuperSet);
                if (result != global::HybridCLR.LoadImageErrorCode.OK)
                {
                    throw new InvalidDataException($"Failed to load AOT metadata '{assemblyName}': {result}");
                }
            }
            return summary;
        }

        private static CaseResult ExecuteCase(object definition, GoldenContractCase golden, bool fullGenericSharingDiagnosticsEnabled)
        {
            long dispatchCountBefore = fullGenericSharingDiagnosticsEnabled
                ? Instrumentation.GetFullGenericSharingDispatchCount()
                : 0;
            long interpreterInvokerCountBefore = fullGenericSharingDiagnosticsEnabled
                ? Instrumentation.GetFullGenericSharingInterpreterInvokerCount()
                : 0;
            Task<CaseResult> task = Task.Run(() => ExecuteCaseCore(definition, golden));
            CaseResult result;
            if (!task.Wait(CaseTimeoutMilliseconds))
            {
                Type type = definition.GetType();
                result = new CaseResult
                {
                    Id = (string)type.GetProperty("Id")!.GetValue(definition)!,
                    Category = (string)type.GetProperty("Category")!.GetValue(definition)!,
                    Layer = (string)type.GetProperty("Layer")!.GetValue(definition)!,
                    Features = ((IEnumerable)type.GetProperty("Features")!.GetValue(definition)!)
                        .Cast<object>()
                        .Select(value => (string)value)
                        .ToArray(),
                    Status = "failed",
                    Message = $"case timed out after {CaseTimeoutMilliseconds} ms"
                };
            }
            else
            {
                result = task.GetAwaiter().GetResult();
            }
            if (fullGenericSharingDiagnosticsEnabled)
            {
                result.FullGenericSharingAotBridgeCount = Instrumentation.GetFullGenericSharingDispatchCount() - dispatchCountBefore;
                result.FullGenericSharingInterpreterInvokerCount = Instrumentation.GetFullGenericSharingInterpreterInvokerCount() - interpreterInvokerCountBefore;
            }
            return result;
        }

        private static CaseResult ExecuteCaseCore(object definition, GoldenContractCase golden)
        {
            Type type = definition.GetType();
            string id = (string)type.GetProperty("Id")!.GetValue(definition)!;
            string category = (string)type.GetProperty("Category")!.GetValue(definition)!;
            string layer = (string)type.GetProperty("Layer")!.GetValue(definition)!;
            string[] features = ((IEnumerable)type.GetProperty("Features")!.GetValue(definition)!)
                .Cast<object>()
                .Select(value => (string)value)
                .ToArray();
            Stopwatch stopwatch = Stopwatch.StartNew();
            string returnValue = null;
            string sideEffect = null;
            string exceptionType = null;
            string exceptionMessage = null;
            try
            {
                Delegate execute = (Delegate)type.GetProperty("Execute")!.GetValue(definition)!;
                object observation = execute.DynamicInvoke();
                if (observation != null)
                {
                    Type observationType = observation.GetType();
                    returnValue = (string)observationType.GetProperty("ReturnValue")!.GetValue(observation)!;
                    sideEffect = (string)observationType.GetProperty("SideEffect")!.GetValue(observation)!;
                }
            }
            catch (TargetInvocationException exception)
            {
                Exception actualException = exception.InnerException ?? exception;
                exceptionType = actualException.GetType().FullName;
                exceptionMessage = actualException.Message;
            }
            catch (Exception exception)
            {
                exceptionType = exception.GetType().FullName;
                exceptionMessage = exception.Message;
            }
            stopwatch.Stop();

            string message = null;
            if (!string.Equals(golden.exceptionType, exceptionType, StringComparison.Ordinal))
            {
                message = "exception mismatch" + (string.IsNullOrEmpty(exceptionMessage) ? string.Empty : ": " + exceptionMessage);
            }
            else if (exceptionType == null && (!string.Equals(golden.returnValue, returnValue, StringComparison.Ordinal) ||
                                               !string.Equals(golden.sideEffect, sideEffect, StringComparison.Ordinal)))
            {
                message = "observation mismatch";
            }

            return new CaseResult
            {
                Id = id,
                Category = category,
                Layer = layer,
                Features = features,
                Status = message == null ? "passed" : "failed",
                ReturnValue = returnValue,
                SideEffect = sideEffect,
                ExceptionType = exceptionType,
                DurationNanoseconds = (long)(stopwatch.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency)),
                Message = message,
            };
        }

        private static GoldenContractFileData LoadGoldenContract()
        {
            string json = Encoding.UTF8.GetString(ReadStreamingAssetBytes(GoldenContractFile));
            GoldenContractFileData golden = JsonUtility.FromJson<GoldenContractFileData>(json);
            if (golden == null || golden.schemaVersion != 1 || string.IsNullOrWhiteSpace(golden.suiteId) || golden.cases == null)
            {
                throw new InvalidDataException("Invalid golden contract.");
            }
            return golden;
        }

        private static Dictionary<string, GoldenContractCase> LoadGoldenCases(GoldenContractFileData golden, string suiteId)
        {
            if (!string.Equals(golden.suiteId, suiteId, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Golden contract suite ID differs from manifest.");
            }

            Dictionary<string, GoldenContractCase> cases = new Dictionary<string, GoldenContractCase>(StringComparer.Ordinal);
            foreach (GoldenContractCase goldenCase in golden.cases)
            {
                if (goldenCase == null || string.IsNullOrWhiteSpace(goldenCase.id) || !cases.TryAdd(goldenCase.id, goldenCase))
                {
                    throw new InvalidDataException("Duplicate or empty golden case ID.");
                }
            }
            return cases;
        }

        private static void WriteResult(TestRun run)
        {
            string output = GetArgument("-labResult");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.Combine(Application.persistentDataPath, DefaultResultFile);
            }

            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            StringBuilder json = new StringBuilder();
            json.Append("{\"schemaVersion\":2,\"suiteId\":\"").Append(Escape(run.SuiteId)).Append("\",\"runner\":\"hybridclr-player-v3\",");
            json.Append("\"runtime\":\"").Append(Escape(Application.unityVersion + " IL2CPP")).Append("\",");
            json.Append("\"platform\":\"").Append(Escape(SystemInfo.operatingSystem)).Append("\",");
            json.Append("\"architecture\":\"").Append(GetProcessArchitecture()).Append("\",");
            json.Append("\"startedAtUtc\":\"").Append(Escape(run.StartedAtUtc)).Append("\",");
            json.Append("\"processId\":").Append(run.ProcessId).Append(',');
            json.Append("\"aotMetadataMode\":\"").Append(run.AotMetadataMode).Append("\",");
            json.Append("\"aotMetadata\":{\"fileCount\":").Append(run.AotMetadataFileCount)
                .Append(",\"totalBytes\":").Append(run.AotMetadataBytes)
                .Append(",\"loadNanoseconds\":").Append(run.AotMetadataLoadNanoseconds).Append("},");
            json.Append("\"correctnessProbes\":{\"crossAssemblyLazyVTable\":true,\"lazyMetadataConcurrentFirstTouch\":")
                .Append(run.LazyMetadataConcurrentFirstTouchPassed ? "true" : "false").Append("},");
            json.Append("\"fullGenericSharingDiagnostics\":{\"enabled\":")
                .Append(run.FullGenericSharingDiagnosticsEnabled ? "true" : "false")
                .Append(",\"dispatchCount\":").Append(run.FullGenericSharingDispatchCount)
                .Append(",\"interpreterInvokerCount\":").Append(run.FullGenericSharingInterpreterInvokerCount).Append("},");
            AppendBuildIdentity(json, run.BuildIdentity);
            json.Append("\"manifestSha256\":\"").Append(run.ManifestSha256).Append("\",");
            json.Append("\"goldenContractSha256\":\"").Append(run.GoldenContractSha256).Append("\",");
            json.Append("\"managedAssemblySha256\":\"").Append(run.AssemblySha256).Append("\",");
            json.Append("\"summary\":{\"total\":").Append(run.Total).Append(",\"passed\":").Append(run.Passed).Append(",\"failed\":").Append(run.Failed).Append("},\"cases\":[");
            for (int i = 0; i < run.Cases.Count; i++)
            {
                if (i > 0) json.Append(',');
                CaseResult result = run.Cases[i];
                json.Append("{\"id\":\"").Append(Escape(result.Id)).Append("\",\"category\":\"").Append(Escape(result.Category)).Append("\",\"layer\":\"").Append(Escape(result.Layer)).Append("\",\"features\":[");
                for (int featureIndex = 0; featureIndex < result.Features.Length; featureIndex++)
                {
                    if (featureIndex > 0) json.Append(',');
                    json.Append('\"').Append(Escape(result.Features[featureIndex])).Append('\"');
                }
                json.Append("],\"status\":\"").Append(result.Status).Append("\",");
                AppendNullable(json, "returnValue", result.ReturnValue);
                json.Append(',');
                AppendNullable(json, "sideEffect", result.SideEffect);
                json.Append(',');
                AppendNullable(json, "exceptionType", result.ExceptionType);
                json.Append(",\"durationNanoseconds\":").Append(result.DurationNanoseconds)
                    .Append(",\"fullGenericSharingAotBridgeCount\":").Append(result.FullGenericSharingAotBridgeCount)
                    .Append(",\"fullGenericSharingInterpreterInvokerCount\":").Append(result.FullGenericSharingInterpreterInvokerCount).Append(',');
                AppendNullable(json, "message", result.Message);
                json.Append('}');
            }
            json.Append("]}");
            WriteAllTextAtomic(output, json.ToString());
            Debug.Log("[HybridCLR Lab] Player result: " + output);
        }

        private static void WriteBenchmarkResult(BenchmarkRun run)
        {
            string output = GetArgument("-labBenchmarkResult");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.Combine(Application.persistentDataPath, DefaultBenchmarkResultFile);
            }

            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            StringBuilder json = new StringBuilder();
            json.Append("{\"schemaVersion\":1,\"suiteId\":\"managed-performance-v1\",\"benchmarkMode\":\"")
                .Append(run.BenchmarkMode).Append("\",\"runner\":\"hybridclr-player-benchmark-")
                .Append(run.ExecutionRuntime).Append("-v1\",");
            if (!string.IsNullOrWhiteSpace(run.AotMetadataMode))
            {
                json.Append("\"aotMetadataMode\":\"").Append(run.AotMetadataMode).Append("\",");
            }
            json.Append("\"runtime\":\"").Append(Escape(Application.unityVersion + " IL2CPP")).Append("\",");
            json.Append("\"platform\":\"").Append(Escape(SystemInfo.operatingSystem)).Append("\",");
            json.Append("\"architecture\":\"").Append(GetProcessArchitecture()).Append("\",");
            json.Append("\"startedAtUtc\":\"").Append(Escape(run.StartedAtUtc)).Append("\",");
            json.Append("\"processId\":").Append(run.ProcessId).Append(',');
            AppendBuildIdentity(json, run.BuildIdentity);
            json.Append("\"stopwatchFrequency\":").Append(run.StopwatchFrequency).Append(',');
            json.Append("\"policySha256\":\"").Append(run.PolicySha256).Append("\",");
            json.Append("\"goldenContractSha256\":\"").Append(run.GoldenContractSha256).Append("\",");
            json.Append("\"managedAssemblySha256\":\"").Append(run.AssemblySha256).Append("\",");
            json.Append("\"startup\":{");
            json.Append("\"aotMetadataLoadNanoseconds\":").Append(run.AotMetadataLoadNanoseconds).Append(',');
            json.Append("\"hotUpdateAssemblyLoadNanoseconds\":").Append(run.HotUpdateAssemblyLoadNanoseconds).Append(',');
            json.Append("\"workloadDiscoveryNanoseconds\":").Append(run.WorkloadDiscoveryNanoseconds).Append("},");
            json.Append("\"workloads\":[");
            for (int i = 0; i < run.Workloads.Count; i++)
            {
                if (i > 0) json.Append(',');
                BenchmarkWorkloadResult result = run.Workloads[i];
                json.Append("{\"id\":\"").Append(Escape(result.Id)).Append("\",\"category\":\"")
                    .Append(Escape(result.Category)).Append("\",\"features\":[");
                for (int featureIndex = 0; featureIndex < result.Features.Length; featureIndex++)
                {
                    if (featureIndex > 0) json.Append(',');
                    json.Append('\"').Append(Escape(result.Features[featureIndex])).Append('\"');
                }
                json.Append("],\"iterations\":").Append(result.Iterations);
                json.Append(",\"warmupBatches\":").Append(result.WarmupBatches);
                json.Append(",\"measurementBatches\":").Append(result.MeasurementBatches);
                json.Append(",\"repetitions\":").Append(result.Repetitions);
                json.Append(",\"checksum\":\"").Append(result.Checksum).Append("\",\"batchNanoseconds\":[");
                for (int batchIndex = 0; batchIndex < result.BatchNanoseconds.Count; batchIndex++)
                {
                    if (batchIndex > 0) json.Append(',');
                    json.Append(result.BatchNanoseconds[batchIndex]);
                }
                json.Append("],\"nanosecondsPerIteration\":[");
                for (int batchIndex = 0; batchIndex < result.NanosecondsPerIteration.Count; batchIndex++)
                {
                    if (batchIndex > 0) json.Append(',');
                    json.Append(result.NanosecondsPerIteration[batchIndex].ToString("R", CultureInfo.InvariantCulture));
                }
                json.Append("],\"gcCollections\":{");
                json.Append("\"generation0\":").Append(result.Generation0Collections).Append(',');
                json.Append("\"generation1\":").Append(result.Generation1Collections).Append(',');
                json.Append("\"generation2\":").Append(result.Generation2Collections).Append("}}");
            }
            json.Append("]}");
            WriteAllTextAtomic(output, json.ToString());
            Debug.Log("[HybridCLR Lab] Player benchmark result: " + output);
        }

        private static void WriteMetadataBenchmarkResult(MetadataBenchmarkRun run)
        {
            string output = GetArgument("-labMetadataResult");
            if (string.IsNullOrWhiteSpace(output))
            {
                output = Path.Combine(Application.persistentDataPath, DefaultMetadataBenchmarkResultFile);
            }

            output = Path.GetFullPath(output);
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            StringBuilder json = new StringBuilder();
            json.Append("{\"schemaVersion\":1,\"suiteId\":\"hybridclr-metadata-load-v2\",\"runner\":\"hybridclr-player-metadata-v2\",");
            json.Append("\"metadataMode\":\"").Append(run.MetadataMode).Append("\",");
            json.Append("\"runtime\":\"").Append(Escape(Application.unityVersion + " IL2CPP")).Append("\",");
            json.Append("\"platform\":\"").Append(Escape(SystemInfo.operatingSystem)).Append("\",");
            json.Append("\"architecture\":\"").Append(GetProcessArchitecture()).Append("\",");
            json.Append("\"startedAtUtc\":\"").Append(Escape(run.StartedAtUtc)).Append("\",");
            json.Append("\"processId\":").Append(run.ProcessId).Append(',');
            AppendBuildIdentity(json, run.BuildIdentity);
            json.Append("\"metadataScenario\":\"").Append(run.MetadataScenario).Append("\",");
            json.Append("\"reflectionContract\":{\"profile\":\"").Append(run.ReflectionProfile)
                .Append("\",\"requestedTypeCount\":").Append(run.ReflectionRequestedTypeCount).Append("},");
            json.Append("\"stressAssembly\":{\"name\":\"").Append(Escape(run.StressAssemblyName))
                .Append("\",\"bytes\":").Append(run.StressAssemblyBytes)
                .Append(",\"sha256\":\"").Append(run.StressAssemblySha256).Append("\"},");
            json.Append("\"aotMetadata\":{\"totalBytes\":").Append(run.AotMetadataBytes)
                .Append(",\"fileCount\":").Append(run.AotMetadataFileCount).Append("},");
            json.Append("\"metadataWarmup\":{\"mode\":\"").Append(Escape(run.MetadataWarmupMode ?? "none"))
                .Append("\",\"nanoseconds\":").Append(run.MetadataWarmupNanoseconds)
                .Append(",\"acrossFrames\":").Append(run.MetadataWarmupAcrossFrames ? "true" : "false")
                .Append(",\"frameCount\":").Append(run.MetadataWarmupFrameCount)
                .Append(",\"batchCount\":").Append(run.MetadataWarmupBatchCount)
                .Append(",\"processingNanoseconds\":").Append(run.MetadataWarmupProcessingNanoseconds)
                .Append(",\"maxFrameNanoseconds\":").Append(run.MetadataWarmupMaxFrameNanoseconds).Append("},");
            json.Append("\"durationsNanoseconds\":{");
            json.Append("\"aotMetadataLoad\":").Append(run.AotMetadataLoadNanoseconds).Append(',');
            json.Append("\"assemblyLoad\":").Append(run.AssemblyLoadNanoseconds).Append(',');
            json.Append("\"entryResolve\":").Append(run.EntryResolveNanoseconds).Append(',');
            json.Append("\"reflectionTouch\":").Append(run.ReflectionTouchNanoseconds).Append(',');
            json.Append("\"entryExecute\":").Append(run.EntryExecuteNanoseconds).Append("},");
            json.Append("\"reflectionPhasesNanoseconds\":{");
            json.Append("\"typeDiscovery\":").Append(run.ReflectionTypeDiscoveryNanoseconds).Append(',');
            json.Append("\"typeAttributes\":").Append(run.ReflectionTypeAttributesNanoseconds).Append(',');
            json.Append("\"memberDiscovery\":").Append(run.ReflectionMemberDiscoveryNanoseconds).Append(',');
            json.Append("\"memberAttributes\":").Append(run.ReflectionMemberAttributesNanoseconds).Append("},");
            json.Append("\"touchCounts\":{");
            json.Append("\"types\":").Append(run.TypeCount).Append(',');
            json.Append("\"members\":").Append(run.MemberCount).Append(',');
            json.Append("\"attributes\":").Append(run.AttributeCount).Append(',');
            json.Append("\"entryChecksum\":").Append(run.EntryChecksum).Append("},");
            json.Append("\"snapshots\":[");
            for (int i = 0; i < run.Snapshots.Count; i++)
            {
                if (i > 0) json.Append(',');
                MemorySnapshot snapshot = run.Snapshots[i];
                json.Append("{\"name\":\"").Append(snapshot.Name).Append("\",");
                json.Append("\"privateBytes\":").Append(snapshot.PrivateBytes).Append(',');
                json.Append("\"workingSetBytes\":").Append(snapshot.WorkingSetBytes).Append(',');
                json.Append("\"peakPrivateBytes\":").Append(snapshot.PeakPrivateBytes).Append(',');
                json.Append("\"peakWorkingSetBytes\":").Append(snapshot.PeakWorkingSetBytes).Append(',');
                json.Append("\"managedHeapBytes\":").Append(snapshot.ManagedHeapBytes).Append(',');
                json.Append("\"unityAllocatedBytes\":").Append(snapshot.UnityAllocatedBytes).Append(',');
                json.Append("\"unityReservedBytes\":").Append(snapshot.UnityReservedBytes).Append(',');
                json.Append("\"androidPssBytes\":").Append(snapshot.AndroidPssBytes).Append('}');
            }
            json.Append("]}");
            WriteAllTextAtomic(output, json.ToString());
            Debug.Log("[HybridCLR Lab] Metadata benchmark result: " + output);
        }

        private static void AppendNullable(StringBuilder json, string name, string value)
        {
            json.Append("\"").Append(name).Append("\":");
            if (value == null)
            {
                json.Append("null");
            }
            else
            {
                json.Append("\"").Append(Escape(value)).Append("\"");
            }
        }

        private static void AppendBuildIdentity(StringBuilder json, BuildIdentity identity)
        {
            json.Append("\"buildIdentity\":{");
            json.Append("\"sha256\":\"").Append(identity.Sha256).Append("\",");
            json.Append("\"profile\":\"").Append(Escape(identity.profile)).Append("\",");
            json.Append("\"target\":\"").Append(Escape(identity.target)).Append("\",");
            json.Append("\"architecture\":\"").Append(Escape(identity.architecture)).Append("\",");
            json.Append("\"il2cppCodeGeneration\":\"").Append(Escape(identity.il2cppCodeGeneration)).Append("\",");
            json.Append("\"aotMetadataPackaging\":\"").Append(Escape(identity.aotMetadataPackaging)).Append("\",");
            json.Append("\"fullGenericSharingDiagnostics\":")
                .Append(identity.fullGenericSharingDiagnostics ? "true" : "false").Append(',');
            json.Append("\"hybridclrUnityTreeSha256\":\"").Append(identity.hybridclrUnityTreeSha256).Append("\",");
            json.Append("\"stagedRuntimeSha256\":\"").Append(identity.stagedRuntimeSha256).Append("\",");
            json.Append("\"managedAssemblySha256\":\"").Append(identity.managedAssemblySha256).Append("\"},");
        }

        private static string Escape(string value)
        {
            StringBuilder escaped = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '\\': escaped.Append("\\\\"); break;
                    case '"': escaped.Append("\\\""); break;
                    case '\n': escaped.Append("\\n"); break;
                    case '\r': escaped.Append("\\r"); break;
                    case '\t': escaped.Append("\\t"); break;
                    default: escaped.Append(character); break;
                }
            }

            return escaped.ToString();
        }

        private static string Sha256(byte[] bytes)
        {
            using System.Security.Cryptography.SHA256 sha256 = System.Security.Cryptography.SHA256.Create();
            StringBuilder result = new StringBuilder();
            foreach (byte value in sha256.ComputeHash(bytes)) result.Append(value.ToString("X2"));
            return result.ToString();
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.Ordinal)) return args[i + 1];
            }
            return string.Empty;
        }

        private static string GetProcessArchitecture()
        {
            if (Application.platform == RuntimePlatform.Android)
            {
                return IntPtr.Size == 8 ? "arm64" : "armv7";
            }

            return IntPtr.Size == 8 ? "x64" : "x86";
        }

        private static byte[] ReadStreamingAssetBytes(string relativePath)
        {
            relativePath = relativePath.Replace('\\', '/').TrimStart('/');
#if UNITY_ANDROID && !UNITY_EDITOR
            string streamingAssetsPath = Application.streamingAssetsPath.Replace('\\', '/');
            int separator = streamingAssetsPath.IndexOf("!/", StringComparison.Ordinal);
            if (separator < 0)
            {
                throw new InvalidDataException("Unexpected Android StreamingAssets path: " + streamingAssetsPath);
            }

            string entryRoot = streamingAssetsPath.Substring(separator + 2).Trim('/');
            string entryName = entryRoot.Length == 0 ? relativePath : entryRoot + "/" + relativePath;
            using FileStream apk = File.OpenRead(Application.dataPath);
            using ZipArchive archive = new ZipArchive(apk, ZipArchiveMode.Read, false);
            ZipArchiveEntry entry = archive.GetEntry(entryName);
            if (entry == null)
            {
                throw new FileNotFoundException("StreamingAssets entry was not found in APK.", entryName);
            }

            using Stream input = entry.Open();
            using MemoryStream output = new MemoryStream(checked((int)entry.Length));
            input.CopyTo(output);
            return output.ToArray();
#else
            return File.ReadAllBytes(Path.Combine(Application.streamingAssetsPath, relativePath));
#endif
        }

        private static string[] ReadStreamingAssetLines(string relativePath)
        {
            string text = Encoding.UTF8.GetString(ReadStreamingAssetBytes(relativePath));
            List<string> lines = new List<string>();
            using StringReader reader = new StringReader(text);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                lines.Add(line);
            }
            return lines.ToArray();
        }

        private static void WriteAllTextAtomic(string output, string contents)
        {
            string temporary = output + ".tmp";
            File.WriteAllText(temporary, contents, Encoding.UTF8);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
            File.Move(temporary, output);
        }

        private static int GetPositiveArgument(string name)
        {
            int value = GetPositiveOrZeroArgument(name);
            if (value < 1) throw new ArgumentException(name + " must be at least 1.");
            return value;
        }

        private static int GetPositiveOrZeroArgument(string name)
        {
            string rawValue = GetArgument(name);
            if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0)
            {
                throw new ArgumentException(name + " must be a non-negative integer.");
            }

            return value;
        }

        private static int GetOptionalNonNegativeArgument(string name, int defaultValue)
        {
            string rawValue = GetArgument(name);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return defaultValue;
            }
            if (!int.TryParse(rawValue, NumberStyles.None, CultureInfo.InvariantCulture, out int value) || value < 0)
            {
                throw new ArgumentException(name + " must be a non-negative integer.");
            }
            return value;
        }

        private static float GetOptionalNonNegativeFloatArgument(string name, float defaultValue)
        {
            string rawValue = GetArgument(name);
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return defaultValue;
            }
            if (!float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentException(name + " must be a finite non-negative number.");
            }
            return value;
        }

        private static bool GetOptionalBooleanArgument(string name, bool defaultValue)
        {
            string rawValue = GetArgument(name);
            if (string.IsNullOrWhiteSpace(rawValue))
                return defaultValue;
            bool value;
            if (!bool.TryParse(rawValue, out value))
                throw new ArgumentException(name + " must be 'true' or 'false'.");
            return value;
        }

        private sealed class TestRun
        {
            public string SuiteId;
            public string StartedAtUtc;
            public string AssemblySha256;
            public string ManifestSha256;
            public string GoldenContractSha256;
            public int ProcessId;
            public string AotMetadataMode;
            public int AotMetadataFileCount;
            public long AotMetadataBytes;
            public long AotMetadataLoadNanoseconds;
            public bool LazyMetadataConcurrentFirstTouchPassed;
            public bool FullGenericSharingDiagnosticsEnabled;
            public long FullGenericSharingDispatchCount;
            public long FullGenericSharingInterpreterInvokerCount;
            public BuildIdentity BuildIdentity;
            public int Total;
            public int Passed;
            public int Failed;
            public readonly System.Collections.Generic.List<CaseResult> Cases = new System.Collections.Generic.List<CaseResult>();
        }

        private sealed class ManifestContract
        {
            public string Category;
            public string Layer;
            public string[] Features;
        }

        [Serializable]
        private sealed class GoldenContractFileData
        {
            public int schemaVersion;
            public string suiteId;
            public GoldenContractCase[] cases;
        }

        [Serializable]
        private sealed class GoldenContractCase
        {
            public string id;
            public string category;
            public string layer;
            public string[] features;
            public string returnValue;
            public string sideEffect;
            public string exceptionType;
        }

        private sealed class CaseResult
        {
            public string Id;
            public string Category;
            public string Layer;
            public string[] Features;
            public string Status;
            public string ReturnValue;
            public string SideEffect;
            public string ExceptionType;
            public long DurationNanoseconds;
            public long FullGenericSharingAotBridgeCount;
            public long FullGenericSharingInterpreterInvokerCount;
            public string Message;
        }

        private sealed class BenchmarkRun
        {
            public string ExecutionRuntime;
            public string BenchmarkMode;
            public string AotMetadataMode;
            public string StartedAtUtc;
            public int ProcessId;
            public long StopwatchFrequency;
            public string PolicySha256;
            public string GoldenContractSha256;
            public string AssemblySha256;
            public long AotMetadataLoadNanoseconds;
            public long HotUpdateAssemblyLoadNanoseconds;
            public long WorkloadDiscoveryNanoseconds;
            public BuildIdentity BuildIdentity;
            public readonly List<BenchmarkWorkloadResult> Workloads = new List<BenchmarkWorkloadResult>();
        }

        private sealed class BenchmarkDefinition
        {
            public string Id;
            public string Category;
            public int ColdIterations;
            public int Iterations;
            public string[] Features;
        }

        [Serializable]
        private sealed class BenchmarkGoldenFileData
        {
            public int schemaVersion;
            public string suiteId;
            public BenchmarkGoldenWorkload[] workloads;
        }

        [Serializable]
        private sealed class BenchmarkGoldenWorkload
        {
            public string id;
            public string category;
            public string[] features;
            public int coldIterations;
            public string coldChecksum;
            public int steadyIterations;
            public string steadyChecksum;
        }

        private sealed class BenchmarkWorkloadResult
        {
            public string Id;
            public string Category;
            public string[] Features;
            public int Iterations;
            public int WarmupBatches;
            public int MeasurementBatches;
            public int Repetitions;
            public string Checksum;
            public readonly List<long> BatchNanoseconds = new List<long>();
            public readonly List<double> NanosecondsPerIteration = new List<double>();
            public int Generation0Collections;
            public int Generation1Collections;
            public int Generation2Collections;
        }

        private sealed class AotMetadataLoadSummary
        {
            public long TotalBytes;
            public int FileCount;
        }

        [Serializable]
        private sealed class PrewarmManifestData
        {
            public int schemaVersion;
            public string assemblyName;
            public string assemblySha256;
            public string rootType;
            public string rootMethod;
            public int rootParameterCount;
            public int reachableMethodCount;
            public int methodCount;
            public string graphCoverage;
            public bool dynamicEdgesIncluded;
            public PrewarmMethodData[] methods;
            public string[] types;
        }

        [Serializable]
        private sealed class PrewarmMethodData
        {
            public string declaringType;
            public string name;
            public int parameterCount;
            public int genericParameterCount;
            public int metadataToken;
            public string[] parameterTypes;
            public string returnType;
        }

        private sealed class MetadataBenchmarkRun
        {
            public string MetadataMode;
            public string MetadataScenario;
            public string ReflectionProfile;
            public int ReflectionRequestedTypeCount;
            public string StartedAtUtc;
            public int ProcessId;
            public BuildIdentity BuildIdentity;
            public string StressAssemblyName;
            public long StressAssemblyBytes;
            public string StressAssemblySha256;
            public long AotMetadataBytes;
            public int AotMetadataFileCount;
            public long AotMetadataLoadNanoseconds;
            public long AssemblyLoadNanoseconds;
            public string MetadataWarmupMode;
            public long MetadataWarmupNanoseconds;
            public bool MetadataWarmupAcrossFrames;
            public int MetadataWarmupFrameCount;
            public int MetadataWarmupBatchCount;
            public long MetadataWarmupProcessingNanoseconds;
            public long MetadataWarmupMaxFrameNanoseconds;
            public long EntryResolveNanoseconds;
            public long ReflectionTouchNanoseconds;
            public long EntryExecuteNanoseconds;
            public long ReflectionTypeDiscoveryNanoseconds;
            public long ReflectionTypeAttributesNanoseconds;
            public long ReflectionMemberDiscoveryNanoseconds;
            public long ReflectionMemberAttributesNanoseconds;
            public int TypeCount;
            public int MemberCount;
            public int AttributeCount;
            public long EntryChecksum;
            public readonly List<MemorySnapshot> Snapshots = new List<MemorySnapshot>();
        }

        private sealed class MetadataBenchmarkContext
        {
            public MetadataBenchmarkRun Run;
            public Assembly Assembly;
            public string MetadataScenario;
            public string ReflectionProfile;
            public int ReflectionTypeLimit;
            public int SettleMilliseconds;
            public string WarmupMode;
        }

        private sealed class MetadataWarmupExecution
        {
            public bool AcrossFrames;
            public int FrameCount;
            public int BatchCount;
            public long ProcessingNanoseconds;
            public long MaxFrameNanoseconds;
        }

        private sealed class MemorySnapshot
        {
            public string Name;
            public long PrivateBytes;
            public long WorkingSetBytes;
            public long PeakPrivateBytes;
            public long PeakWorkingSetBytes;
            public long ManagedHeapBytes;
            public long UnityAllocatedBytes;
            public long UnityReservedBytes;
            public long AndroidPssBytes;
        }

        [Serializable]
        private sealed class BuildIdentity
        {
            public int schemaVersion;
            public string profile;
            public string target;
            public string architecture;
            public string il2cppCodeGeneration;
            public string aotMetadataPackaging;
            public bool fullGenericSharingDiagnostics;
            public string hybridclrUnityTreeSha256;
            public string stagedRuntimeSha256;
            public string managedAssemblySha256;
            public string crossAssemblyDerivedSha256;
            [NonSerialized] public string Sha256;
        }

#if UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint Size;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }
#endif

    }
}
