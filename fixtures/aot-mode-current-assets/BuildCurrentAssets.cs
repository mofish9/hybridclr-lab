using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class BuildCurrentAssets
{
    public static void Run()
    {
        var assembly=Assembly.Load("StartupUnityHotfix");
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        var go=new GameObject("Existing and Current-only components");
        go.AddComponent(assembly.GetType("StartupUnityHotfix.Worker",true));
        go.AddComponent(assembly.GetType("StartupUnityHotfix.AddedWorker",true));
        PrefabUtility.SaveAsPrefabAsset(go,"Assets/HotfixPrefab.prefab");
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(assembly.GetType("StartupUnityHotfix.Data",true)),"Assets/HotfixData.asset");
        AssetDatabase.CreateAsset(ScriptableObject.CreateInstance(assembly.GetType("StartupUnityHotfix.AddedData",true)),"Assets/AddedData.asset");
        EditorSceneManager.SaveScene(scene,"Assets/HotfixBundle.unity");
        var args=Environment.GetCommandLineArgs();
        string output=args[Array.IndexOf(args,"-bundleOutput")+1];
        Directory.CreateDirectory(output);
        var bundles=new[]{
            new AssetBundleBuild{assetBundleName="hotfix-assets",assetNames=new[]{"Assets/HotfixPrefab.prefab","Assets/HotfixData.asset","Assets/AddedData.asset"}},
            new AssetBundleBuild{assetBundleName="hotfix-scene",assetNames=new[]{"Assets/HotfixBundle.unity"}}
        };
        if(BuildPipeline.BuildAssetBundles(output,bundles,BuildAssetBundleOptions.ForceRebuildAssetBundle,BuildTarget.StandaloneWindows64)==null)
            throw new Exception("Current-only bundle build failed");
        File.WriteAllText(Path.Combine(output,"current-assets.json"),"{\"currentOnlyComponents\":true,\"currentOnlyScriptableObjects\":true,\"scene\":true}");
    }
}
