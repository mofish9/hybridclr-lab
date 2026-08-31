using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace __DHE_NAMESPACE__
{
    /// <summary>
    /// Project adapter for the HybridCLR DHE C# host. This default embeds DHE
    /// payloads in StreamingAssets. Replace the resource evidence and player
    /// callback when the project uses YooAsset, Addressables, signing, or a
    /// target-specific device runner.
    /// </summary>
    public static class DheWorkflowBuild
    {
        private const string RuntimeAssetRoot = "Assets/StreamingAssets/HybridCLR/DHE";

        public static void Prepare()
        {
            BuildTarget target = Target();
            string outputRoot = FullArgument("-dheOutputRoot");
            string baselineRoot = FullArgument("-dheBaselineRoot");
            string currentRoot = FullArgument("-dheCurrentRoot");
            string mode = OptionalArgument("-dheMode") ?? "Exploratory";
            string baselineSource = Environment.GetEnvironmentVariable("DHE_BASELINE_ROOT");
            DheProjectPrepareResult result = DheBuildPipeline.PrepareProjectArtifacts(
                new DheProjectPrepareOptions
                {
                    Target = target,
                    Mode = mode,
                    BaselineSourceRoot = string.IsNullOrWhiteSpace(baselineSource)
                        ? null : Path.GetFullPath(baselineSource),
                    BaselineOutputRoot = baselineRoot,
                    CurrentOutputRoot = currentRoot,
                    RequireDheEqualsHotUpdate = true,
                });

            WriteJson(Path.Combine(outputRoot, "adapter", "prepare.json"), new PrepareReport
            {
                schemaVersion = 1,
                format = "hybridclr.dhe-project-adapter-prepare.json",
                generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                passed = true,
                toolchainContractVersion = 1,
                target = target.ToString(),
                mode = result.Mode,
                pathSemantics = "workspace-absolute-v1",
                projectPath = ProjectRoot(),
                settingsFile = Path.GetFullPath("ProjectSettings/HybridCLRSettings.asset"),
                baselineRoot = result.BaselineOutputRoot,
                currentRoot = result.CurrentOutputRoot,
                baselineSourceRoot = result.BaselineSourceRoot,
                currentSourceRoot = result.CurrentSourceRoot,
                runtimeAssemblySourceRoot = result.CurrentSourceRoot,
                baselineGeneratedFromCurrent = result.BaselineGeneratedFromCurrent,
                aotAssemblies = result.DheAotAssemblyNames,
                hotUpdateAssemblies = result.HotUpdateAssemblyNames,
            });
        }

        public static void StageRuntimePlan()
        {
            BuildTarget target = Target();
            EnsureTarget(target);
            string projectRoot = ProjectRoot();
            string outputRoot = FullArgument("-dheOutputRoot");
            DheRuntimePlanResult result = DheBuildPipeline.StageRuntimePlan(
                new DheRuntimePlanOptions
                {
                    Target = target,
                    ProjectRoot = projectRoot,
                    ProjectPlanPath = FullArgument("-dheProjectPlan"),
                    RuntimeAssetRoot = Path.Combine(projectRoot,
                        RuntimeAssetRoot.Replace('/', Path.DirectorySeparatorChar)),
                    OutputRoot = outputRoot,
                    StrippedAotRoot = Path.GetFullPath(
                        SettingsUtil.GetAssembliesPostIl2CppStripDir(target)),
                    AotMetadataAssemblyNames = SettingsUtil.AOTAssemblyNames.ToArray(),
                    HotfixAssemblyNames = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved.ToArray(),
                    HotfixLoadOrderResolver = names => names.OrderBy(name => name,
                        StringComparer.OrdinalIgnoreCase).ToArray(),
                });
            AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate |
                ImportAssetOptions.ForceSynchronousImport);
            WriteJson(Path.Combine(outputRoot, "adapter", "stage-runtime-plan.json"),
                new StageReport
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

        public static void BuildDheYooAsset()
        {
            WriteJson(Path.Combine(FullArgument("-dheOutputRoot"), "adapter",
                "resource-evidence.json"), new ResourceEvidenceReport
                {
                    schemaVersion = 1,
                    format = "hybridclr.dhe-resource-evidence.json",
                    generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    passed = true,
                    target = Argument("-dheTarget"),
                    strategy = "player-embedded-streaming-assets",
                    pathSemantics = "workspace-absolute-v1",
                    details = RuntimeAssetRoot,
                });
        }

        public static void BuildScriptsOnly()
        {
            BuildPlayer(true);
        }

        public static void BuildFinalPlayer()
        {
            BuildPlayer(false);
        }

        private static void BuildPlayer(bool scriptsOnly)
        {
            BuildTarget target = Target();
            EnsureTarget(target);
            string outputRoot = FullArgument("-dheOutputRoot");
            string playerPath = PlayerPath(outputRoot, target);
            string[] scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled)
                .Select(scene => scene.path).ToArray();
            if (scenes.Length == 0)
                throw new BuildFailedException("DHE Player requires at least one enabled scene.");

            DheNativeFinalizeResult finalization = null;
            BuildReport report = DheBuildPipeline.BuildPlayer(new DhePlayerBuildOptions
            {
                OutputPath = playerPath,
                BaselineAotRoot = FullArgument("-dheBaselineRoot"),
                Target = target,
                BuildOptions = scriptsOnly ? BuildOptions.BuildScriptsOnly : BuildOptions.None,
                CleanBuild = scriptsOnly,
                Scenes = scenes,
                BuildPlayerCallback = options => BuildPipeline.BuildPlayer(options),
                NativeFinalizeOptions = scriptsOnly ? null : NativeOptions(outputRoot, true),
                NativeFinalizeResultCallback = result => finalization = result,
            });
            if (scriptsOnly)
                finalization = DheBuildPipeline.FinalizeProjectNativeCode(
                    NativeOptions(outputRoot, false));
            WriteNativeEvidence(outputRoot, target, finalization, !scriptsOnly);
            WriteJson(Path.Combine(outputRoot, "adapter", scriptsOnly
                ? "build-scripts-only.json" : "build-final-player.json"),
                new PlayerBuildReport
                {
                    schemaVersion = 1,
                    format = "hybridclr.dhe-adapter-player-build.json",
                    generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    passed = report != null && report.summary.result == BuildResult.Succeeded,
                    scriptsOnly = scriptsOnly,
                    target = target.ToString(),
                    playerPath = playerPath,
                });
        }

        private static DheNativeFinalizeOptions NativeOptions(string outputRoot, bool rebuildPlayer)
        {
            return new DheNativeFinalizeOptions
            {
                ProjectRoot = ProjectRoot(),
                ProjectPlanPath = FullArgument("-dheProjectPlan"),
                OutputManifestPath = Path.Combine(outputRoot, "native",
                    "dhe-native-manifest.json"),
                BeeLogPath = Path.Combine(outputRoot, "native", "bee-rebuild.log"),
                RequireCompleteCoverage = true,
                RebuildPlayer = rebuildPlayer,
                BeeMaxAttempts = 3,
                BeeTimeoutSeconds = 600,
            };
        }

        private static void WriteNativeEvidence(string outputRoot, BuildTarget target,
            DheNativeFinalizeResult result, bool final)
        {
            if (result == null || result.GuardResult == null)
                throw new BuildFailedException("DHE native finalization did not return evidence.");
            DheNativeGuardResult guard = result.GuardResult;
            bool guardPassed = guard.UnsupportedMethodCount == 0 &&
                (guard.RequestedMethodCount == 0 || guard.NativeEntryCount > 0);
            WriteJson(Path.Combine(outputRoot, "adapter", "native-guards.json"),
                new NativeGuardReport
                {
                    schemaVersion = 1,
                    format = "hybridclr.dhe-adapter-native-guards.json",
                    generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    passed = guardPassed,
                    target = target.ToString(),
                    generatedCppRoot = result.GeneratedCppRoot,
                    manifestPath = guard.ManifestPath,
                    requestedMethodCount = guard.RequestedMethodCount,
                    transformedMethodCount = guard.TransformedMethodCount,
                    nativeEntryCount = guard.NativeEntryCount,
                    unsupportedMethodCount = guard.UnsupportedMethodCount,
                });
            if (!final)
                return;
            DheBeeRebuildResult rebuild = result.BeeRebuildResult;
            WriteJson(Path.Combine(outputRoot, "adapter", "native-finalize.json"),
                new NativeFinalizeReport
                {
                    schemaVersion = 1,
                    format = "hybridclr.dhe-adapter-native-finalize.json",
                    generatedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
                    passed = guardPassed && rebuild != null && rebuild.ExitCode == 0,
                    target = target.ToString(),
                    generatedCppRoot = result.GeneratedCppRoot,
                    manifestPath = guard.ManifestPath,
                    beeBackendPath = rebuild == null ? null : rebuild.BeeBackendPath,
                    dagPath = rebuild == null ? null : rebuild.DagPath,
                    logPath = rebuild == null ? null : rebuild.LogPath,
                    attempts = rebuild == null ? 0 : rebuild.Attempts,
                    exitCode = rebuild == null ? -1 : rebuild.ExitCode,
                });
        }

        private static string PlayerPath(string outputRoot, BuildTarget target)
        {
            string playerRoot = Path.Combine(outputRoot, "player");
            switch (target)
            {
                case BuildTarget.Android:
                    return Path.Combine(playerRoot, PlayerSettings.productName + ".apk");
                case BuildTarget.iOS:
                    return Path.Combine(playerRoot, PlayerSettings.productName + "-iOS");
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(playerRoot, PlayerSettings.productName + ".exe");
                default:
                    return Path.Combine(playerRoot, PlayerSettings.productName);
            }
        }

        private static void EnsureTarget(BuildTarget target)
        {
            BuildTargetGroup group = BuildPipeline.GetBuildTargetGroup(target);
            if (target == BuildTarget.NoTarget || group == BuildTargetGroup.Unknown)
                throw new BuildFailedException("Unsupported DHE build target: " + target);
            if (EditorUserBuildSettings.activeBuildTarget != target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(group, target))
                throw new BuildFailedException("Unable to switch build target to " + target);
        }

        private static BuildTarget Target()
        {
            if (Enum.TryParse(Argument("-dheTarget"), true, out BuildTarget target))
                return target;
            throw new BuildFailedException("Unsupported DHE build target.");
        }

        private static string ProjectRoot()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }

        private static string FullArgument(string name)
        {
            return Path.GetFullPath(Argument(name));
        }

        private static string Argument(string name)
        {
            return OptionalArgument(name) ??
                throw new BuildFailedException("Missing Unity argument: " + name);
        }

        private static string OptionalArgument(string name)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(arguments, name);
            return index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : null;
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true),
                new System.Text.UTF8Encoding(false));
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
        private sealed class ResourceEvidenceReport
        {
            public int schemaVersion;
            public string format;
            public string generatedAtUtc;
            public bool passed;
            public string target;
            public string strategy;
            public string pathSemantics;
            public string details;
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
    }
}
