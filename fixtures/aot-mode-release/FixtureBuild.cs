using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using HybridCLR.Editor;
using HybridCLR.Editor.Commands;
using HybridCLR.Editor.Installer;
using HybridCLR.Editor.Settings;

namespace StartupWindowsFixture
{
    public static class FixtureBuild
    {
        const string Scene = "Assets/Startup.unity";
        public static void Install()
        { Configure(); new InstallerController().InstallFromLocal(Argument("-startupRuntime")); }
        static void Configure()
        {
            var settings = HybridCLRSettings.Instance;
            settings.enable = true; settings.useGlobalIl2cpp = false;
            settings.hotUpdateAssemblyDefinitions = new UnityEditorInternal.AssemblyDefinitionAsset[0];
            settings.hotUpdateAssemblies = new[] { "StartupHotfix", "StartupUnityHotfix" };
            settings.preserveHotUpdateAssemblies = new string[0];
            settings.externalHotUpdateAssembliyDirs = new[] { Argument("-startupBaseRoot") };
            settings.patchAOTAssemblies = new string[0];
#if STARTUP_DHE_PROFILE
            settings.dheAotAssemblies = new[] { "StartupHotfix", "StartupUnityHotfix" };
#if STARTUP_MODE_SELECTION
            settings.enableAotModeSelection = true;
#else
            settings.enableAotModeSelection = false;
#endif
            settings.dhePreserveAotAssemblies = new[] { "mscorlib", "System", "System.Core", "StartupAotSupport" };
#endif
            HybridCLRSettings.Save();
            PlayerSettings.productName = "StartupPlayer";
            PlayerSettings.companyName = "HybridCLRStartupResearch";
            PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard_2_0);
            PlayerSettings.SetIl2CppCodeGeneration(UnityEditor.Build.NamedBuildTarget.Standalone, Il2CppCodeGeneration.OptimizeSize);
            PlayerSettings.SetManagedStrippingLevel(UnityEditor.Build.NamedBuildTarget.Standalone, ManagedStrippingLevel.Low);
            EditorUserBuildSettings.development = false;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), Scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Scene, true) };
        }
        public static void Build()
        {
            Configure();
#if STARTUP_MODE_SELECTION
            ValidateBaseAssetBoundary();
#endif
#if STARTUP_DHE_PROFILE
            string output = Argument("-startupOutput");
            DheProjectWorkflowRunner.BuildBase(new DheProjectWorkflowAdapter {
                ProjectRoot = Directory.GetParent(Application.dataPath).FullName,
                Workflow = "aot-mode-release",
                BuildIdentityAssetPath = "Assets/Runtime/DheBuildIdentity.cs",
                IdentityNamespace = "StartupWindowsFixture",
                RuntimeAssetRoot = "Assets/StreamingAssets/HybridCLR/DHE",
                EnableDispatchDiagnostics = false,
                GuardOrdinaryAotMethods = true,
                GetScenes = () => new[] { Scene },
                BuildPlayer = options => BuildPipeline.BuildPlayer(options),
                ResolvePlayerOutput = (target, root) => Path.Combine(root, "player", "StartupPlayer.exe")
            }, new DheProjectWorkflowOptions {
                Target = BuildTarget.StandaloneWindows64, OutputRoot = output,
                BaselineRoot = Path.Combine(output, "baseline"), CurrentRoot = Path.Combine(output, "current"),
                Bootstrap = true, EngineWorkflow = "Unity2022Fgs", Il2CppCodeGeneration = "OptimizeSize"
            });
#else
            CompileDllCommand.CompileDll(BuildTarget.StandaloneWindows64, false);
            string hotRoot = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(BuildTarget.StandaloneWindows64);
            Directory.CreateDirectory(hotRoot);
            File.Copy(Path.Combine(Argument("-startupBaseRoot"), "StartupHotfix.dll"), Path.Combine(hotRoot, "StartupHotfix.dll"), true);
            File.Copy(Path.Combine(Argument("-startupBaseRoot"), "StartupUnityHotfix.dll"), Path.Combine(hotRoot, "StartupUnityHotfix.dll"), true);
            Il2CppDefGeneratorCommand.GenerateIl2CppDef();
            LinkGeneratorCommand.GenerateLinkXml(BuildTarget.StandaloneWindows64);
            StripAOTDllCommand.GenerateStripedAOTDlls(BuildTarget.StandaloneWindows64);
            MethodBridgeGeneratorCommand.GenerateMethodBridgeAndReversePInvokeWrapper(BuildTarget.StandaloneWindows64);
            AOTReferenceGeneratorCommand.GenerateAOTGenericReference(BuildTarget.StandaloneWindows64);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { Scene }, target = BuildTarget.StandaloneWindows64,
                locationPathName = Path.Combine(Argument("-startupOutput"), "player", "StartupPlayer.exe")
            });
            if (report.summary.result != BuildResult.Succeeded) throw new BuildFailedException("Legacy IL2CPP Player failed.");
#endif
            Debug.Log("STARTUP_BUILD_SUCCEEDED");
        }
        static string Argument(string name)
        { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); if (index < 0 || index + 1 >= args.Length) throw new Exception("Missing " + name); return args[index + 1]; }

#if STARTUP_MODE_SELECTION
        static void ValidateBaseAssetBoundary()
        {
            var validatorType=typeof(HybridCLRSettings).Assembly.GetType("HybridCLR.Editor.BuildProcessors.AotModeAssetValidator",true);
            var validator=Activator.CreateInstance(validatorType,true);
            var sceneGate=(IProcessSceneWithReport)validator;
            var buildGate=(IPreprocessBuildWithReport)validator;
            var scene=EditorSceneManager.GetActiveScene();
            var unityAssembly=System.Reflection.Assembly.Load("StartupUnityHotfix");
            var go=new GameObject("Deferred asset gate");
            go.AddComponent(unityAssembly.GetType("StartupUnityHotfix.Worker",true));
            bool sceneRejected=false,preloadedRejected=false,resourcesRejected=false;
            var previous=PlayerSettings.GetPreloadedAssets();
            string dataPath="Assets/DeferredGateData.asset",resourcePath="Assets/Resources/DeferredGate.prefab";
            try {
                try {sceneGate.OnProcessScene(scene,null);}catch(BuildFailedException){sceneRejected=true;}
                var data=ScriptableObject.CreateInstance(unityAssembly.GetType("StartupUnityHotfix.Data",true));
                AssetDatabase.CreateAsset(data,dataPath);PlayerSettings.SetPreloadedAssets(new UnityEngine.Object[]{data});
                try {buildGate.OnPreprocessBuild(null);}catch(BuildFailedException){preloadedRejected=true;}
                PlayerSettings.SetPreloadedAssets(previous);
                Directory.CreateDirectory("Assets/Resources");AssetDatabase.Refresh();
                PrefabUtility.SaveAsPrefabAsset(go,resourcePath);
                try {buildGate.OnPreprocessBuild(null);}catch(BuildFailedException){resourcesRejected=true;}
            } finally {
                PlayerSettings.SetPreloadedAssets(previous);UnityEngine.Object.DestroyImmediate(go);
                AssetDatabase.DeleteAsset(dataPath);AssetDatabase.DeleteAsset(resourcePath);
                EditorSceneManager.SaveScene(scene, Scene);
            }
            sceneGate.OnProcessScene(scene,null);buildGate.OnPreprocessBuild(null);
            if(!sceneRejected || !preloadedRejected || !resourcesRejected)throw new Exception("Base asset boundary gate failed");
            Directory.CreateDirectory(Argument("-startupOutput"));
            File.WriteAllText(Path.Combine(Argument("-startupOutput"),"asset-gate.json"),"{\"sceneRejected\":true,\"preloadedRejected\":true,\"resourcesRejected\":true,\"ordinaryAssetsAccepted\":true}");
        }
#endif
    }
}
