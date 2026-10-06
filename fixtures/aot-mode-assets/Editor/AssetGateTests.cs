using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using HybridCLR.Editor.Settings;

public static class AssetGateTests
{
    [Serializable] sealed class Result { public bool passed; public string error; public string[] cases; }
    static readonly List<string> cases = new List<string>();
    static Type validatorType;
    static IPreprocessBuildWithReport validator;
    public static void Run()
    {
        var result = new Result();
        var previous = PlayerSettings.GetPreloadedAssets();
        try
        {
            var settings = HybridCLRSettings.Instance;
            settings.enable = true; settings.enableAotModeSelection = true;
            settings.hotUpdateAssemblies = new[] { "ReviewHotfix" };
            settings.dheAotAssemblies = new[] { "ReviewHotfix" };
            settings.hotUpdateAssemblyDefinitions = new UnityEditorInternal.AssemblyDefinitionAsset[0];
            settings.preserveHotUpdateAssemblies = new string[0];
            settings.externalHotUpdateAssembliyDirs = new string[0];
            HybridCLRSettings.Save();
            validatorType = typeof(HybridCLRSettings).Assembly.GetType("HybridCLR.Editor.BuildProcessors.AotModeAssetValidator", true);
            validator = (IPreprocessBuildWithReport)Activator.CreateInstance(validatorType, true);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("Deferred"); go.AddComponent<ReviewComponent>();
            EditorSceneManager.SaveScene(scene, "Assets/HotfixScene.unity");
            ExpectRejected("base-scene", () => validatorType.GetMethod("ValidateScene", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { scene }));
            // Unity also processes scenes on entry to Play mode, without a build report.
            ((IProcessSceneWithReport)validator).OnProcessScene(scene, null);
            cases.Add("editor-play-scene");

            Directory.CreateDirectory("Assets/Editor/Resources"); Directory.CreateDirectory("Assets/Resources"); AssetDatabase.Refresh();
            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Editor/Resources/Deferred.prefab");
            validator.OnPreprocessBuild(null); cases.Add("editor-resources-excluded");
            PrefabUtility.SaveAsPrefabAsset(go, "Assets/Resources/Deferred.prefab");
            ExpectRejected("player-resources-rejected", () => validator.OnPreprocessBuild(null));
            AssetDatabase.DeleteAsset("Assets/Resources/Deferred.prefab");

            var host = ScriptableObject.CreateInstance<ReviewHost>();
            AssetDatabase.CreateAsset(host, "Assets/Host.asset");
            PlayerSettings.SetPreloadedAssets(new UnityEngine.Object[] { host });
            host.root = new ReviewHost.Node(); host.root.next = host.root;
            validator.OnPreprocessBuild(null); cases.Add("self-cycle");
            host.root.next = new ReviewHost.Node { next = host.root };
            host.shared = host.root;
            validator.OnPreprocessBuild(null); cases.Add("mutual-cycle-shared-reference");
            host.root.next.payload = new ReviewPayload();
            ExpectRejected("nested-deferred-reference", () => validator.OnPreprocessBuild(null));
            host.root.next.payload = null; host.tail = new ReviewPayload();
            ExpectRejected("deferred-after-shared-reference", () => validator.OnPreprocessBuild(null));
            host.tail = null;
            PlayerSettings.SetPreloadedAssets(previous); AssetDatabase.DeleteAsset("Assets/Host.asset");

            Directory.CreateDirectory("Bundles");
            var bundles = new[] { new AssetBundleBuild { assetBundleName = "hotfix-scene", assetNames = new[] { "Assets/HotfixScene.unity" } } };
            if (BuildPipeline.BuildAssetBundles("Bundles", bundles, BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64) == null)
                throw new Exception("Hotfix scene bundle build failed");
            cases.Add("hotfix-scene-bundle");
            result.passed = true;
        }
        catch (Exception error) { result.error = error.ToString(); }
        finally
        {
            PlayerSettings.SetPreloadedAssets(previous);
            result.cases = cases.ToArray();
            var args = Environment.GetCommandLineArgs();
            var path = args[Array.IndexOf(args, "-assetGateReport") + 1];
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
        }
        if (!result.passed) throw new Exception(result.error);
        Debug.Log("AOT_ASSET_GATE_PASSED");
    }
    static void ExpectRejected(string name, Action action)
    {
        try { action(); }
        catch (BuildFailedException) { cases.Add(name); return; }
        catch (TargetInvocationException error) when (error.InnerException is BuildFailedException) { cases.Add(name); return; }
        throw new Exception("Expected asset rejection: " + name);
    }
}
