using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    /// <summary>
    /// C# project adapter consumed by HybridCLR.DheTool. Project-specific
    /// paths and the smoke assertion stay here; MV generation, runtime-plan
    /// binding, and baseline/current phase isolation stay in the package.
    /// </summary>
    public static class HybridCLRDheWorkflowBuild
    {
        public static void Prepare()
        {
            BuildTarget target = ParseTarget(Argument("-dheTarget"));
            string outputRoot = Path.GetFullPath(Argument("-dheOutputRoot"));
            string baselineRoot = Path.GetFullPath(Argument("-dheBaselineRoot"));
            string currentRoot = Path.GetFullPath(Argument("-dheCurrentRoot"));
            string mode = OptionalArgument("-dheMode") ?? "Exploratory";
            string currentInputRoot = OptionalArgument("-dheCurrentInputRoot");
            string configuredBaseline = Environment.GetEnvironmentVariable("DHE_BASELINE_ROOT");
            DheProjectPrepareResult prepared = DheBuildPipeline.PrepareProjectArtifacts(
                new DheProjectPrepareOptions
                {
                    Target = target,
                    Mode = mode,
                    BaselineSourceRoot = !string.IsNullOrWhiteSpace(configuredBaseline) &&
                        Directory.Exists(configuredBaseline) ? Path.GetFullPath(configuredBaseline) : null,
                    BaselineOutputRoot = baselineRoot,
                    CurrentOutputRoot = currentRoot,
                    RequireDheEqualsHotUpdate = true,
                    BeforeCurrentGeneration = names =>
                    {
                        if (!string.IsNullOrWhiteSpace(currentInputRoot))
                            StageCurrentAssemblyInputs(Path.GetFullPath(currentInputRoot), names);
                    },
                });
            var report = new PrepareReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-project-adapter-prepare.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = true,
                toolchainContractVersion = 1,
                target = target.ToString(),
                mode = mode,
                pathSemantics = "workspace-absolute-v1",
                projectPath = Directory.GetParent(Application.dataPath).FullName,
                settingsFile = Path.GetFullPath("ProjectSettings/HybridCLRSettings.asset"),
                baselineRoot = baselineRoot,
                currentRoot = currentRoot,
                baselineSourceRoot = prepared.BaselineSourceRoot,
                currentSourceRoot = prepared.CurrentSourceRoot,
                runtimeAssemblySourceRoot = prepared.CurrentSourceRoot,
                baselineGeneratedFromCurrent = prepared.BaselineGeneratedFromCurrent,
                aotAssemblies = prepared.DheAotAssemblyNames,
                hotUpdateAssemblies = prepared.HotUpdateAssemblyNames,
            };
            WriteJson(Path.Combine(outputRoot, "adapter", "prepare.json"), report);
        }

        public static void StageRuntimePlan()
        {
            BuildTarget target = ParseTarget(Argument("-dheTarget"));
            string outputRoot = Path.GetFullPath(Argument("-dheOutputRoot"));
            string planPath = Path.GetFullPath(Argument("-dheProjectPlan"));
            string projectRoot = ProjectRoot();
            EnsureTarget(target);
            string runtimeAssetRoot = Path.Combine(projectRoot, "Assets", "StreamingAssets", "HybridCLRLab", "DheDemo");
            string strippedAotRoot = Path.GetFullPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(target));
            DheRuntimePlanResult result = DheBuildPipeline.StageRuntimePlan(new DheRuntimePlanOptions
            {
                Target = target,
                ProjectRoot = projectRoot,
                ProjectPlanPath = planPath,
                RuntimeAssetRoot = runtimeAssetRoot,
                OutputRoot = outputRoot,
                StrippedAotRoot = strippedAotRoot,
                AotMetadataAssemblyNames = SettingsUtil.AOTAssemblyNames.ToArray(),
                HotfixAssemblyNames = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.ToArray(),
                HotfixLoadOrderResolver = names => names.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray(),
            });
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            WriteJson(Path.Combine(outputRoot, "adapter", "stage-runtime-plan.json"), new StageReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-adapter-stage.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = true,
                target = target.ToString(),
                runtimePlanPath = result.RuntimePlanPath,
                handoffPlanPath = result.HandoffPlanPath,
                assemblyCount = result.AssemblyNames.Length,
                assemblyNames = result.AssemblyNames,
            });
        }

        /// <summary>
        /// The demo has no YooAsset catalog. Keep the workflow phase explicit
        /// and emit the same structured evidence a resource-owning project
        /// would produce, so the host never needs a shell-specific fallback.
        /// </summary>
        public static void BuildDheYooAsset()
        {
            string outputRoot = Path.GetFullPath(Argument("-dheOutputRoot"));
            string target = Argument("-dheTarget");
            string path = Path.Combine(outputRoot, "adapter", "resource-evidence.json");
            WriteJson(path, new ResourceEvidenceReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-resource-evidence.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = true,
                policy = "skip",
                strategy = "demo-no-resource-catalog",
                target = target,
                pathSemantics = "workspace-absolute-v1",
            });
        }

        public static void BuildScriptsOnly() => BuildPlayer(true);

        public static void BuildFinalPlayer()
        {
            BuildPlayer(false);
        }

        private static void BuildPlayer(bool scriptsOnly)
        {
            BuildTarget target = ParseTarget(Argument("-dheTarget"));
            string outputRoot = Path.GetFullPath(Argument("-dheOutputRoot"));
            string baselineRoot = Path.GetFullPath(Argument("-dheBaselineRoot"));
            EnsureTarget(target);
            EnsureBuildScene();
            ConfigurePlayerSettings(target);
            string playerRoot = Path.Combine(outputRoot, "player");
            string playerPath = target == BuildTarget.Android
                ? Path.Combine(playerRoot, "HybridCLRLab.apk")
                : target == BuildTarget.iOS
                    ? Path.Combine(playerRoot, "HybridCLRLab-iOS")
                    : Path.Combine(playerRoot, "HybridCLRLab.exe");
            DheNativeFinalizeResult nativeResult = null;
            BuildReport report = DheBuildPipeline.BuildPlayer(new DhePlayerBuildOptions
            {
                OutputPath = playerPath,
                BaselineAotRoot = baselineRoot,
                Target = target,
                BuildOptions = scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None,
                // scripts-only owns the clean generation pass. After that
                // pass the package patches the generated C++ guards; the
                // final build must reuse those exact Bee inputs.
                CleanBuild = scriptsOnly,
                Scenes = new[] { "Assets/Scenes/HybridCLRLab.unity" },
                BuildPlayerCallback = options => BuildPipeline.BuildPlayer(options),
                NativeFinalizeOptions = scriptsOnly ? null : CreateNativeFinalizeOptions(outputRoot, true),
                NativeFinalizeResultCallback = scriptsOnly ? null : result => nativeResult = result,
            });
            if (scriptsOnly)
            {
                nativeResult = DheBuildPipeline.FinalizeProjectNativeCode(
                    CreateNativeFinalizeOptions(outputRoot, false));
                StageBuildIdentity(target, baselineRoot, outputRoot, nativeResult);
            }
            else ValidateFinalNativeIdentity(outputRoot, nativeResult);
            WriteNativeReports(target, outputRoot, nativeResult, !scriptsOnly);
            WriteJson(Path.Combine(outputRoot, "adapter", scriptsOnly ? "build-scripts-only.json" : "build-final-player.json"), new PlayerBuildReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-adapter-player-build.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = report != null && report.summary.result == BuildResult.Succeeded,
                scriptsOnly = scriptsOnly,
                target = target.ToString(),
                playerPath = playerPath,
            });
            if (!scriptsOnly && target == BuildTarget.StandaloneWindows64)
            {
                RunStandaloneSmoke(playerPath, outputRoot);
            }
        }

        private static DheNativeFinalizeOptions CreateNativeFinalizeOptions(
            string outputRoot, bool rebuildPlayer)
        {
            return new DheNativeFinalizeOptions
            {
                ProjectRoot = ProjectRoot(),
                ProjectPlanPath = Path.GetFullPath(Argument("-dheProjectPlan")),
                OutputManifestPath = Path.Combine(outputRoot, "native", "dhe-native-manifest.json"),
                BeeLogPath = Path.Combine(outputRoot, "native", "bee-rebuild.log"),
                RequireCompleteCoverage = true,
                RebuildPlayer = rebuildPlayer,
                BeeMaxAttempts = 3,
                BeeTimeoutSeconds = 600,
            };
        }

        private static void EnsureBuildScene()
        {
            const string scenePath = "Assets/Scenes/HybridCLRLab.unity";
            if (!File.Exists(Path.Combine(ProjectRoot(), scenePath.Replace('/', Path.DirectorySeparatorChar))))
                throw new FileNotFoundException("DHE demo scene was not found", scenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(scenePath, true) };
        }

        private static void StageCurrentAssemblyInputs(string inputRoot, IEnumerable<string> assemblyNames)
        {
            if (!Directory.Exists(inputRoot))
                throw new DirectoryNotFoundException("DHE current assembly input root was not found: " + inputRoot);
            string projectRoot = ProjectRoot();
            string destinationRoot = Path.Combine(projectRoot, "Assets", "Plugins", "HybridCLRLab");
            Directory.CreateDirectory(destinationRoot);
            foreach (string assemblyName in assemblyNames)
            {
                string sourcePath = Path.Combine(inputRoot, assemblyName + ".dll");
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException("DHE current assembly input was not found", sourcePath);
                string destinationPath = Path.Combine(destinationRoot, assemblyName + ".dll");
                File.Copy(sourcePath, destinationPath, true);
                AssetDatabase.ImportAsset(
                    "Assets/Plugins/HybridCLRLab/" + assemblyName + ".dll",
                    ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }
        }

        private static void WriteNativeReports(BuildTarget target, string outputRoot,
            DheNativeFinalizeResult nativeResult, bool final)
        {
            DheNativeGuardResult result = nativeResult.GuardResult;
            var report = new NativeGuardReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-adapter-native-guards.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = result.UnsupportedMethodCount == 0 &&
                    (result.RequestedMethodCount == 0 || result.NativeEntryCount > 0),
                generatedCppRoot = nativeResult.GeneratedCppRoot,
                manifestPath = result.ManifestPath,
                requestedMethodCount = result.RequestedMethodCount,
                transformedMethodCount = result.TransformedMethodCount,
                nativeEntryCount = result.NativeEntryCount,
                unsupportedMethodCount = result.UnsupportedMethodCount,
                target = target.ToString(),
            };
            WriteJson(Path.Combine(outputRoot, "adapter", "native-guards.json"), report);
            if (!final) return;
            DheBeeRebuildResult rebuild = nativeResult.BeeRebuildResult;
            WriteJson(Path.Combine(outputRoot, "adapter", "native-finalize.json"),
                new NativeFinalizeReport
                {
                    schemaVersion = 1,
                    format = "hybridclr.dhe-adapter-native-finalize.json",
                    generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    passed = rebuild != null && rebuild.ExitCode == 0 && report.passed,
                    target = target.ToString(),
                    generatedCppRoot = nativeResult.GeneratedCppRoot,
                    manifestPath = result.ManifestPath,
                    beeBackendPath = rebuild == null ? null : rebuild.BeeBackendPath,
                    dagPath = rebuild == null ? null : rebuild.DagPath,
                    logPath = rebuild == null ? null : rebuild.LogPath,
                    attempts = rebuild == null ? 0 : rebuild.Attempts,
                    exitCode = rebuild == null ? -1 : rebuild.ExitCode,
                });
        }

        private static void ConfigurePlayerSettings(BuildTarget target)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            PlayerSettings.SetScriptingBackend(group, ScriptingImplementation.IL2CPP);
            if (target == BuildTarget.Android)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                EditorUserBuildSettings.buildAppBundle = false;
            }
        }

        private static void EnsureTarget(BuildTarget target)
        {
            if (target == BuildTarget.NoTarget)
                throw new InvalidOperationException("DHE requires an explicit Unity build target.");
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (group == BuildTargetGroup.Unknown)
                throw new InvalidOperationException("Unity has no build target group for " + target + ".");
            if (EditorUserBuildSettings.activeBuildTarget != target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new BuildFailedException("Unable to switch Unity active build target to " + target + ".");
        }

        private static void StageBuildIdentity(BuildTarget target, string baselineRoot, string outputRoot,
            DheNativeFinalizeResult nativeResult)
        {
            if (nativeResult == null || nativeResult.GuardResult == null)
                throw new BuildFailedException("DHE build identity requires native guard evidence.");
            string[] assemblyNames = (nativeResult.AssemblyNames ?? Array.Empty<string>())
                .OrderBy(name => name, StringComparer.Ordinal).ToArray();
            if (assemblyNames.Length == 0) throw new BuildFailedException("DHE build identity assembly set is empty.");
            var baselineRecords = new List<KeyValuePair<string, byte[]>>();
            var snapshotRecords = new List<KeyValuePair<string, byte[]>>();
            var assemblies = new List<DheBuildIdentityAssembly>();
            string mainHash = null;
            foreach (string assemblyName in assemblyNames)
            {
                string baselinePath = Path.Combine(baselineRoot, assemblyName + ".dll");
                if (!File.Exists(baselinePath)) throw new FileNotFoundException("DHE baseline assembly was not found", baselinePath);
                byte[] baselineBytes = File.ReadAllBytes(baselinePath);
                byte[] snapshotBytes = Sha256(baselineBytes);
                string hash = ToHex(snapshotBytes);
                if (assemblyName == "HybridCLR.ManagedCasesAot") mainHash = hash;
                baselineRecords.Add(new KeyValuePair<string, byte[]>(assemblyName, baselineBytes));
                snapshotRecords.Add(new KeyValuePair<string, byte[]>(assemblyName, snapshotBytes));
                assemblies.Add(new DheBuildIdentityAssembly
                {
                    assemblyName = assemblyName,
                    baselinePath = Path.GetFullPath(baselinePath),
                    baselineSha256 = hash,
                    snapshotSha256 = hash,
                });
            }
            if (string.IsNullOrWhiteSpace(mainHash)) throw new BuildFailedException("DHE main assembly is missing from build identity.");
            string baselineSetHash = Sha256NamedByteSet(baselineRecords);
            string snapshotSetHash = Sha256NamedByteSet(snapshotRecords);
            DheNativeGuardResult guard = nativeResult.GuardResult;
            string source = "namespace HybridCLR.Lab\n{\n" +
                "    internal static class HybridCLRDheBuildIdentity\n    {\n" +
                "        public const int IdentityVersion = 2;\n" +
                "        public const string Target = \"" + target + "\";\n" +
                "        public const string BaselineAssemblySha256 = \"" + mainHash + "\";\n" +
                "        public const string AotSnapshotSha256 = \"" + mainHash + "\";\n" +
                "        public const string AotSnapshotKind = \"managed-assembly-plus-generated-cpp-v1\";\n" +
                "        public const string NativeGuardSourceSha256 = \"" + guard.NativeGuardSourceSha256 + "\";\n" +
                "        public const string NativeManifestSha256 = \"" + guard.NativeManifestSha256 + "\";\n" +
                "    }\n}\n";
            string sourcePath = Path.Combine(ProjectRoot(), "Assets", "Runtime", "HybridCLRDheBuildIdentity.cs");
            File.WriteAllText(sourcePath, source, new System.Text.UTF8Encoding(false));
            string streamingRoot = Path.Combine(ProjectRoot(), "Assets", "StreamingAssets", "HybridCLRLab");
            Directory.CreateDirectory(streamingRoot);
            var identity = new DheBuildIdentityReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-build-identity.json",
                workflow = "unity2021-dhe-demo",
                target = target.ToString(),
                identityVersion = 2,
                pathSemantics = "workspace-absolute-v1",
                baselineAssemblySha256 = baselineSetHash,
                aotSnapshotSha256 = snapshotSetHash,
                mainBaselineAssemblySha256 = mainHash,
                mainSnapshotSha256 = mainHash,
                aotSnapshotKind = "managed-assembly-plus-generated-cpp-v1",
                nativeGuardSourceSha256 = guard.NativeGuardSourceSha256,
                nativeManifestSha256 = guard.NativeManifestSha256,
                generatedCppRoot = nativeResult.GeneratedCppRoot,
                generatedCppPaths = guard.GeneratedCppPaths ?? Array.Empty<string>(),
                nativeManifestPath = guard.ManifestPath,
                assemblies = assemblies.ToArray(),
            };
            WriteJson(Path.Combine(streamingRoot, "build-identity.json"), identity);
            WriteJson(Path.Combine(outputRoot, "build-identity.json"), identity);
            AssetDatabase.ImportAsset("Assets/Runtime/HybridCLRDheBuildIdentity.cs", ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        private static void ValidateFinalNativeIdentity(string outputRoot, DheNativeFinalizeResult nativeResult)
        {
            if (nativeResult == null || nativeResult.GuardResult == null)
                throw new BuildFailedException("DHE final native identity is missing.");
            DheBuildIdentityReport identity = JsonUtility.FromJson<DheBuildIdentityReport>(
                File.ReadAllText(Path.Combine(outputRoot, "build-identity.json")));
            DheNativeGuardResult guard = nativeResult.GuardResult;
            if (identity == null || identity.nativeManifestSha256 != guard.NativeManifestSha256 ||
                identity.nativeGuardSourceSha256 != guard.NativeGuardSourceSha256)
                throw new BuildFailedException("DHE final native identity changed after the Player was compiled.");
        }

        private static string Sha256NamedByteSet(IEnumerable<KeyValuePair<string, byte[]>> records)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
            {
                foreach (KeyValuePair<string, byte[]> record in records.OrderBy(item => item.Key, StringComparer.Ordinal))
                {
                    byte[] name = System.Text.Encoding.UTF8.GetBytes(record.Key + "\n");
                    sha.TransformBlock(name, 0, name.Length, name, 0);
                    byte[] bytes = record.Value ?? Array.Empty<byte>();
                    sha.TransformBlock(bytes, 0, bytes.Length, bytes, 0);
                    byte[] separator = { (byte)'\n' };
                    sha.TransformBlock(separator, 0, separator.Length, separator, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHex(sha.Hash);
            }
        }

        private static byte[] Sha256(byte[] value)
        {
            using (System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create())
                return sha.ComputeHash(value ?? Array.Empty<byte>());
        }

        private static string ToHex(byte[] value) =>
            BitConverter.ToString(value ?? Array.Empty<byte>()).Replace("-", string.Empty).ToLowerInvariant();

        private static void RunStandaloneSmoke(string playerPath, string outputRoot)
        {
            if (!File.Exists(playerPath)) throw new FileNotFoundException("DHE Player executable was not produced", playerPath);
            string resultPath = Path.Combine(outputRoot, "dhe-player-result.json");
            var start = new System.Diagnostics.ProcessStartInfo(playerPath)
            {
                WorkingDirectory = Path.GetDirectoryName(playerPath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            start.ArgumentList.Add("-batchmode");
            start.ArgumentList.Add("-nographics");
            start.ArgumentList.Add("-labMode");
            start.ArgumentList.Add("dhe");
            start.ArgumentList.Add("-labTarget");
            start.ArgumentList.Add("StandaloneWindows64");
            start.ArgumentList.Add("-labAotMetadataMode");
            start.ArgumentList.Add("supplemental");
            start.ArgumentList.Add("-labResult");
            start.ArgumentList.Add(resultPath);
            start.ArgumentList.Add("-logFile");
            start.ArgumentList.Add(Path.Combine(outputRoot, "dhe-player.log"));
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(start);
            if (process == null) throw new InvalidOperationException("Unable to start DHE Player.");
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            bool exited = process.WaitForExit(180000);
            if (!exited)
            {
                try
                {
                    process.Kill();
                    process.WaitForExit();
                }
                catch { }
            }
            Task.WaitAll(stdout, stderr);
            File.WriteAllText(Path.Combine(outputRoot, "dhe-player-process.log"), stdout.Result + Environment.NewLine + stderr.Result, new System.Text.UTF8Encoding(false));
            if (!exited)
                throw new TimeoutException("DHE Player smoke timed out.");
            if (process.ExitCode != 0) throw new BuildFailedException("DHE Player smoke exited with code " + process.ExitCode);
            if (!File.Exists(resultPath)) throw new FileNotFoundException("DHE Player smoke result was not produced", resultPath);
            string result = File.ReadAllText(resultPath);
            if (!result.Contains("\"passed\": true", StringComparison.OrdinalIgnoreCase) && !result.Contains("\"passed\":true", StringComparison.OrdinalIgnoreCase))
                throw new BuildFailedException("DHE Player smoke reported failure: " + resultPath);
        }

        private static void CopyAssemblies(string sourceRoot, string destinationRoot, IEnumerable<string> names)
        {
            Directory.CreateDirectory(destinationRoot);
            foreach (string name in names)
            {
                string source = Path.Combine(sourceRoot, name + ".dll");
                if (!File.Exists(source)) throw new FileNotFoundException("DHE stripped assembly was not found", source);
                File.Copy(source, Path.Combine(destinationRoot, name + ".dll"), true);
            }
        }

        private static string ProjectRoot() => Directory.GetParent(Application.dataPath).FullName;

        private static string Sha256File(string path)
        {
            using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static string Argument(string name) => OptionalArgument(name) ?? throw new InvalidOperationException("Missing Unity argument: " + name);

        private static string OptionalArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        private static BuildTarget ParseTarget(string value)
        {
            if (Enum.TryParse(value, true, out BuildTarget target)) return target;
            throw new InvalidOperationException("Unsupported DHE target: " + value);
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }

        [Serializable]
        private sealed class PrepareReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public int toolchainContractVersion;
            public string target;
            public string mode;
            public string pathSemantics;
            public string projectPath;
            public string settingsFile;
            public string baselineRoot;
            public string currentRoot;
            public string baselineSourceRoot;
            public string currentSourceRoot;
            public string runtimeAssemblySourceRoot;
            public bool baselineGeneratedFromCurrent;
            public string[] aotAssemblies;
            public string[] hotUpdateAssemblies;
        }

        [Serializable]
        private sealed class StageReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public string target;
            public string runtimePlanPath;
            public string handoffPlanPath;
            public int assemblyCount;
            public string[] assemblyNames;
        }

        [Serializable]
        private sealed class NativeGuardReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public string target;
            public string generatedCppRoot;
            public string manifestPath;
            public int requestedMethodCount;
            public int transformedMethodCount;
            public int nativeEntryCount;
            public int unsupportedMethodCount;
        }

        [Serializable]
        private sealed class NativeFinalizeReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public string target;
            public string generatedCppRoot;
            public string manifestPath;
            public string beeBackendPath;
            public string dagPath;
            public string logPath;
            public int attempts;
            public int exitCode;
        }

        [Serializable]
        private sealed class PlayerBuildReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public bool scriptsOnly;
            public string target;
            public string playerPath;
        }

        [Serializable]
        private sealed class ResourceEvidenceReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public string policy;
            public string strategy;
            public string target;
            public string pathSemantics;
        }

        [Serializable]
        private sealed class DheBuildIdentityReport
        {
            public int schemaVersion;
            public string format;
            public string workflow;
            public string target;
            public int identityVersion;
            public string pathSemantics;
            public string baselineAssemblySha256;
            public string aotSnapshotSha256;
            public string mainBaselineAssemblySha256;
            public string mainSnapshotSha256;
            public string aotSnapshotKind;
            public string nativeGuardSourceSha256;
            public string nativeManifestSha256;
            public string generatedCppRoot;
            public string[] generatedCppPaths;
            public string nativeManifestPath;
            public DheBuildIdentityAssembly[] assemblies;
        }

        [Serializable]
        private sealed class DheBuildIdentityAssembly
        {
            public string assemblyName;
            public string baselinePath;
            public string baselineSha256;
            public string snapshotSha256;
        }
    }
}
