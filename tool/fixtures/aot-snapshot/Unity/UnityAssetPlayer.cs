using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridCLR.Lab.Snapshot
{
    public sealed class UnityAssetPlayer : MonoBehaviour
    {
        private readonly List<string> checks = new List<string>();
        private Action<string[], string> completed;
        private Type componentType, stateType;
        private bool current;
        private int assetRevision, phase;
        private float deadline;
        private AsyncOperation operation;
        private string bundleRoot, bundleScenePath;
        private AssetBundle prefabBundle, sceneBundle;
        internal static void Begin(bool current, int assetRevision, Action<string[], string> completed)
        {
            var driver = new GameObject("DHE asset validation").AddComponent<UnityAssetPlayer>();
            DontDestroyOnLoad(driver.gameObject);
            driver.current = current; driver.assetRevision = assetRevision; driver.completed = completed;
            driver.deadline = Time.realtimeSinceStartup + 30;
            string[] args = Environment.GetCommandLineArgs(); int bundleIndex = Array.IndexOf(args, "-unityAssetBundleRoot");
            if (bundleIndex >= 0) driver.bundleRoot = args[bundleIndex + 1];
        }
        private void Check(string name, bool value)
        {
            if (!value) throw new InvalidOperationException("Unity asset failed: " + name);
            checks.Add(name); Console.WriteLine("DHE Unity asset check: " + name);
        }
        private void Validate(GameObject root, string prefix)
        {
            var component = root.GetComponent(componentType);
            object Field(object instance, string name) => instance.GetType().GetField(name).GetValue(instance);
            Check(prefix + ":type", component != null && component.GetType() == componentType);
            Check(prefix + ":authored-revision", (int)Field(component, "AssetRevision") == assetRevision);
            Check(prefix + ":inactive", !root.activeSelf && (int)Field(component, "Awakened") == 0);
            Check(prefix + ":existing-field", (int)Field(component, "Count") == 17);
            var state = Field(component, "State");
            Check(prefix + ":nested-type", state.GetType() == stateType);
            Check(prefix + ":nested-number", (int)Field(state, current ? "RenamedNumber" : "Number") == 23);
            Check(prefix + ":nested-text", (string)Field(state, "Text") == "archived-state");
            var items = (IList)Field(component, "Items");
            Check(prefix + ":list", items.Count == 2 && (int)Field(items[0], current ? "RenamedNumber" : "Number") == 31 &&
                (int)Field(items[1], current ? "RenamedNumber" : "Number") == 37);
            var node = Field(component, "Node");
            Check(prefix + ":managed-reference", node != null && node.GetType().FullName == "HybridCLR.Lab.UnityAssets.AssetNode" && (int)Field(node, "Value") == 29);
            Check(prefix + ":managed-reference-method", (int)node.GetType().GetMethod("Read").Invoke(node, null) == (current ? 129 : 29));
            Check(prefix + ":unity-reference", (GameObject)Field(component, "Target") == root.transform.GetChild(0).gameObject);
            Check(prefix + ":deserialize-callback", (int)Field(component, "DeserializedStamp") == (current ? 2 : 1));
            if (current)
            {
                Check(prefix + ":added-component-field", (long)Field(component, "Extra") == (assetRevision == 2 ? 90000000017L : 0L));
                Check(prefix + ":added-nested-field", (long)Field(state, "Extra") == (assetRevision == 2 ? 90000000031L : 0L));
                Check(prefix + ":added-node-field", (long)Field(node, "Extra") == (assetRevision == 2 ? 90000000043L : 0L));
                componentType.GetField("Extra").SetValue(component, 90000000059L);
            }
            var clone = Instantiate(root); var copied = clone.GetComponent(componentType);
            Check(prefix + ":clone-fields", (int)Field(copied, "Count") == 17 && (!current || (long)Field(copied, "Extra") == 90000000059L));
            Check(prefix + ":clone-reference", (GameObject)Field(copied, "Target") == clone.transform.GetChild(0).gameObject &&
                (GameObject)Field(copied, "Target") != (GameObject)Field(component, "Target"));
            componentType.GetField("Count").SetValue(copied, 53);
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            Check(prefix + ":independent-storage-gc", (int)Field(component, "Count") == 17 && (int)Field(copied, "Count") == 53 &&
                (int)Field(Field(copied, "Node"), "Value") == 29);
            root.SetActive(true);
            Check(prefix + ":awake", (int)Field(component, "Awakened") == (current ? 2 : 1) && (int)Field(component, "Count") == 17);
            Destroy(clone);
        }
        private void Update()
        {
            try
            {
                if (Time.realtimeSinceStartup > deadline) throw new TimeoutException("Unity asset probe timed out.");
                if (phase == 0)
                {
                    var assembly = typeof(ValueLayout.Factory).Assembly;
                    componentType = assembly.GetType("HybridCLR.Lab.UnityAssets.AssetBehaviour", true);
                    stateType = assembly.GetType("HybridCLR.Lab.UnityAssets.AssetState", true);
                    GameObject prefab;
                    if (bundleRoot != null)
                    {
                        prefabBundle = AssetBundle.LoadFromFile(Path.Combine(bundleRoot, "dhe-prefab"));
                        Check("bundle-prefab-loaded", prefabBundle != null && !prefabBundle.isStreamedSceneAssetBundle);
                        sceneBundle = AssetBundle.LoadFromFile(Path.Combine(bundleRoot, "dhe-scene"));
                        Check("bundle-scene-loaded", sceneBundle != null && sceneBundle.isStreamedSceneAssetBundle);
                        bundleScenePath = sceneBundle.GetAllScenePaths().Single();
                        prefab = prefabBundle.LoadAsset<GameObject>("Assets/Resources/DheAssetPrefab.prefab");
                    }
                    else prefab = Resources.Load<GameObject>("DheAssetPrefab");
                    if (prefab == null) throw new InvalidOperationException("Archived Prefab is missing.");
                    var instance = Instantiate(prefab); Validate(instance, "prefab"); Destroy(instance);
                    operation = SceneManager.LoadSceneAsync(bundleScenePath ?? "DheAssetScene", LoadSceneMode.Additive);
                    phase = 1; return;
                }
                if (!operation.isDone) return;
                if (phase == 1)
                {
                    var scene = bundleScenePath == null ? SceneManager.GetSceneByName("DheAssetScene") : SceneManager.GetSceneByPath(bundleScenePath);
                    if (bundleScenePath != null) Check("bundle-scene-path", scene.path == bundleScenePath && scene.name == "DheBundledScene");
                    Validate(scene.GetRootGameObjects().Single(root => root.name == "saved-scene"), "scene");
                    operation = SceneManager.UnloadSceneAsync(scene); phase = 2; return;
                }
                Check("scene-unloaded", !(bundleScenePath == null ? SceneManager.GetSceneByName("DheAssetScene") : SceneManager.GetSceneByPath(bundleScenePath)).isLoaded);
                if (prefabBundle != null) prefabBundle.Unload(true);
                if (sceneBundle != null) sceneBundle.Unload(true);
                Finish(null);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private void Finish(string error)
        {
            enabled = false;
            if (error == null) Console.WriteLine("DHE Unity asset pass: " + checks.Count);
            completed(checks.ToArray(), error);
        }
    }
}
