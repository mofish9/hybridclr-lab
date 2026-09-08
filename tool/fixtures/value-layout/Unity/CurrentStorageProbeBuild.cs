using System;
using System.IO;
using System.Security.Cryptography;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    public static class CurrentStorageProbeBuild
    {
        private static readonly string[] Hotfix = { "HybridCLR.ValueLayoutModel", "HybridCLR.ValueLayoutOther", "HybridCLR.ValueLayoutConsumer" };
        private static string Argument(string name)
        {
            var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name);
            if (index < 0 || index + 1 >= args.Length) throw new ArgumentException(name);
            return Path.GetFullPath(args[index + 1]);
        }
        public static void Prepare()
        {
            var settings = HybridCLRSettings.Instance;
            settings.enable = true; settings.useGlobalIl2cpp = false;
            settings.hotUpdateAssemblyDefinitions = Array.Empty<UnityEditorInternal.AssemblyDefinitionAsset>();
            settings.hotUpdateAssemblies = Hotfix; settings.dheAotAssemblies = Hotfix;
            settings.preserveHotUpdateAssemblies = Array.Empty<string>();
            settings.externalHotUpdateAssembliyDirs = new[] { "Assets/Plugins/ValueLayout" };
            settings.patchAOTAssemblies = new[] { "mscorlib", "System", "System.Core" };
            HybridCLRSettings.Save();
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Standalone, Il2CppCodeGeneration.OptimizeSize);
            var installer = new InstallerController(); installer.InstallFromLocal(Argument("-probeRuntime"));
            if (!installer.HasInstalledHybridCLR()) throw new BuildFailedException("Runtime installation failed.");
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, "Assets/Scenes/CurrentStorage.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/CurrentStorage.unity", true) };
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            string output = Argument("-probeOutput");
            DheBuildPipeline.PrepareProjectArtifacts(new DheProjectPrepareOptions
            {
                Target = BuildTarget.StandaloneWindows64, Bootstrap = true,
                BaselineOutputRoot = Path.Combine(output, "baseline"), CurrentOutputRoot = Path.Combine(output, "generated-current"),
                RequireDheEqualsHotUpdate = true,
            });
        }
        public static void Build()
        {
            string output = Argument("-probeOutput");
            string player = Path.Combine(Argument("-probePlayerRoot"), "CurrentStorage.exe");
            if (Directory.Exists(Path.GetDirectoryName(player))) throw new IOException("Player output must be new.");
            // Stripped-AOT generation enables Visual Studio export. Restore
            // an executable build explicitly before validating runtime behavior.
            EditorUserBuildSettings.SetPlatformSettings("Standalone", "CreateSolution", "false");
            EditorUserBuildSettings.selectedStandaloneTarget = BuildTarget.StandaloneWindows64;
            EditorUserBuildSettings.standaloneBuildSubtarget = StandaloneBuildSubtarget.Player;
            BuildReport report = DheBuildPipeline.BuildPlayer(new DhePlayerBuildOptions
            {
                Target = BuildTarget.StandaloneWindows64, BaselineAotRoot = Path.Combine(output, "baseline"),
                OutputPath = player, Scenes = new[] { "Assets/Scenes/CurrentStorage.unity" }, CleanBuild = true,
                // Deliberately isolates image/layout behavior. The static lab
                // entry chooses Current metadata; native guards/caller coverage
                // must still be tested with the full DHE workflow afterwards.
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException(report.summary.result.ToString());
            string gameAssembly = Path.Combine(Path.GetDirectoryName(player), "GameAssembly.dll");
            if (!File.Exists(player) || !File.Exists(gameAssembly))
                throw new BuildFailedException("Build did not produce the requested native Player executable.");
            string stripped = SettingsUtil.GetAssembliesPostIl2CppStripDir(BuildTarget.StandaloneWindows64);
            foreach (string name in Hotfix)
                File.Copy(Path.Combine(stripped, name + ".dll"), Path.Combine(output, "baseline", name + ".dll"), true);
            string nativeSource = Path.Combine(stripped, "HybridCLR.ValueLayoutNative.dll");
            string nativeBaseline = Path.Combine(output, "baseline", "HybridCLR.ValueLayoutNative.dll");
            if (File.Exists(nativeBaseline))
            {
                if (Hash(nativeSource) != Hash(nativeBaseline)) throw new BuildFailedException("Ordinary AOT fixture changed during Base construction.");
            }
            else File.Copy(nativeSource, nativeBaseline);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(player), "probe-build.json"), JsonUtility.ToJson(new BuildReceipt
            { passed = true, unityVersion = Application.unityVersion, bytes = report.summary.totalSize.ToString(),
                playerSha256 = Hash(player), gameAssemblySha256 = Hash(gameAssembly) }, true));
        }
        private static string Hash(string file)
        {
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(file))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
        [Serializable] private class BuildReceipt
        {
            public bool passed; public string unityVersion; public string bytes;
            public string playerSha256; public string gameAssemblySha256;
            public string scope = "Current storage probe; no native entry guard qualification";
        }
    }
}
