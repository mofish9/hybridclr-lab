using System;
using System.IO;
using System.Linq;
using HybridCLR.Editor.Commands;
using UnityEditor;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    // Fixture callbacks only. All Base build phases use the package runner.
    public static class SnapshotWorkflowBuild
    {
        [Serializable] private sealed class Mode { public string assemblyName; public string executionMode; public DheExecutionPlan executionPlan; }
        public static void InspectJson()
        {
            foreach (string source in new[] { "{\"assemblyName\":\"Example\",\"executionPlan\":null}", "{\"assemblyName\":\"Example\"}",
                "{\"assemblyName\":\"Example\",\"executionPlan\":{}}" })
            {
                Mode mode = JsonUtility.FromJson<Mode>(source);
                Debug.Log("SNAPSHOT_JSON " + source + " planIsNull=" + (mode.executionPlan == null) + " parsed=" + JsonUtility.ToJson(mode));
            }
        }
        public static void Prepare() => DheProjectWorkflowRunner.Prepare(Adapter());
        public static void StageRuntimePlan() => DheProjectWorkflowRunner.StageRuntimePlan(Adapter());
        public static void BuildScriptsOnly() => DheProjectWorkflowRunner.BuildScriptsOnly(Adapter());
        public static void BuildFinalPlayer() => DheProjectWorkflowRunner.BuildFinalPlayer(Adapter());
        private static DheProjectWorkflowAdapter Adapter() => new DheProjectWorkflowAdapter
        {
            ProjectRoot = Directory.GetParent(Application.dataPath).FullName,
            Workflow = "aot-snapshot-package-workflow",
            BuildIdentityAssetPath = "Assets/SnapshotIdentity.cs",
            IdentityNamespace = "HybridCLR.Lab.Snapshot",
            IdentityClassName = "DheBuildIdentity",
            RuntimeAssetRoot = "Assets/StreamingAssets/SnapshotDHE",
            GetScenes = () => EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
            BuildPlayer = options =>
            {
                EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolution", "false");
                EditorUserBuildSettings.selectedStandaloneTarget = BuildTarget.StandaloneWindows64;
                EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
                return BuildPipeline.BuildPlayer(options);
            },
            ResolvePlayerOutput = (target, output) => Path.Combine(output, "player", "Snapshot.exe"),
        };
    }
}
