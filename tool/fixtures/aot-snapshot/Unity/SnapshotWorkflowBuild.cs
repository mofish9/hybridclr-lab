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
        [Serializable] private sealed class JsonCase { public string name; public bool passed; public string error; }
        [Serializable] private sealed class JsonReport { public bool passed; public JsonCase[] checks; }
        public static void CheckPlanJson()
        {
            var type = typeof(DheRuntime).GetNestedType("DheAssemblyMode", System.Reflection.BindingFlags.NonPublic);
            var canonical = typeof(DheRuntime).GetMethod("CanonicalAssemblyModes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            string hash = new string('a', 64);
            string valid = JsonUtility.ToJson(new DheExecutionPlan { schemaVersion = 1, assemblyName = "Example",
                baseMetaVersionSha256 = hash, currentMetaVersionSha256 = hash,
                currentStorageTypeTokens = new uint[] { 0x02000002 }, currentExecutionMethodTokens = new uint[] { 0x06000001 },
                currentStorageTypeTokenCount = 1, currentExecutionMethodTokenCount = 1 });
            var cases = new System.Collections.Generic.List<JsonCase>();
            void Check(string name, string suffix, bool expected)
            {
                var modes = Array.CreateInstance(type, 1);
                modes.SetValue(JsonUtility.FromJson("{\"assemblyName\":\"Example\",\"executionMode\":\"dhe-differential\"" + suffix + "}", type), 0);
                bool accepted = true; string error = null;
                try { canonical.Invoke(null, new object[] { modes }); }
                catch (System.Reflection.TargetInvocationException exception) { accepted = false; error = exception.InnerException.Message; }
                cases.Add(new JsonCase { name = name, passed = accepted == expected, error = error });
            }
            Check("absent-plan", "", true);
            Check("null-plan-array", ",\"executionPlans\":null", true);
            Check("empty-plan-array", ",\"executionPlans\":[]", true);
            Check("valid-plan", ",\"executionPlans\":[" + valid + "]", true);
            Check("empty-plan-object-rejected", ",\"executionPlans\":[{}]", false);
            Check("null-array-element-rejected", ",\"executionPlans\":[null]", false);
            Check("multiple-plans-rejected", ",\"executionPlans\":[" + valid + "," + valid + "]", false);
            Check("missing-selection-rejected", ",\"executionPlans\":[" + valid.Replace("\"currentStorageTypeTokens\":[33554434],", "") + "]", false);
            Check("wrong-count-rejected", ",\"executionPlans\":[" + valid.Replace("\"currentStorageTypeTokenCount\":1", "\"currentStorageTypeTokenCount\":2") + "]", false);
            var report = new JsonReport { passed = cases.All(row => row.passed), checks = cases.ToArray() };
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-snapshotJsonResult");
            File.WriteAllText(args[index + 1], JsonUtility.ToJson(report, true));
            if (!report.passed) throw new InvalidOperationException("Unity execution-plan JSON checks failed.");
        }
        public static void Prepare()
        {
            UnityAssetBuild.PrepareIfPresent();
            DheProjectWorkflowRunner.Prepare(Adapter());
        }
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
            AdditionalGuardMvJsonPaths = OrdinaryGuardMvJsonPaths(),
            GuardOrdinaryAotMethods = GuardAllOrdinaryAot(),
        };

        private static string[] OrdinaryGuardMvJsonPaths()
        {
            if (GuardAllOrdinaryAot()) return Array.Empty<string>();
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-dheOrdinaryGuardMvRoot");
            if (index < 0 || index + 1 >= args.Length) return Array.Empty<string>();
            string root = Path.GetFullPath(args[index + 1]);
            return Directory.Exists(root) ? Directory.GetFiles(root, "*.mv.json") : Array.Empty<string>();
        }
        private static bool GuardAllOrdinaryAot()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-dheGuardAllOrdinaryAot");
            return index >= 0 && index + 1 < args.Length && args[index + 1] == "true";
        }
    }
}
