using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;

namespace HybridCLR.Lab.Editor
{
    internal static class UnityAssetControlBuild
    {
        public static void Build()
        {
            var args = Environment.GetCommandLineArgs();
            string output = args[Array.IndexOf(args, "-controlPlayer") + 1];
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetManagedStrippingLevel(BuildTargetGroup.Standalone, ManagedStrippingLevel.Minimal);
            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            const string path = "Assets/Scenes/Control.unity";
            if (!EditorSceneManager.SaveScene(scene, path)) throw new IOException("Control scene was not saved.");
            var result = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { path }, locationPathName = output,
                target = BuildTarget.StandaloneWindows64, options = BuildOptions.StrictMode });
            if (result.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Control build failed.");
        }
    }
}
