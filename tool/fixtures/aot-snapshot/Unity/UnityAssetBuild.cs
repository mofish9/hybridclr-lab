using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HybridCLR.Lab.Editor
{
    internal static class UnityAssetBuild
    {
        [Serializable] private sealed class Evidence
        {
            public int assetRevision;
            public string scene, prefab, sceneSha256, prefabSha256;
        }
        internal static void PrepareIfPresent()
        {
            var assembly = typeof(ValueLayout.Factory).Assembly;
            var componentType = assembly.GetType("HybridCLR.Lab.UnityAssets.AssetBehaviour");
            if (componentType == null) return;
            var stateType = assembly.GetType("HybridCLR.Lab.UnityAssets.AssetState", true);
            var nodeType = assembly.GetType("HybridCLR.Lab.UnityAssets.AssetNode", true);
            bool current = componentType.GetField("Extra") != null;
            object State(int number)
            {
                var value = Activator.CreateInstance(stateType);
                stateType.GetField(current ? "RenamedNumber" : "Number").SetValue(value, number);
                stateType.GetField("Text").SetValue(value, "archived-state");
                if (current) stateType.GetField("Extra").SetValue(value, 90000000031L);
                return value;
            }
            GameObject Object(string name)
            {
                var root = new GameObject(name); root.SetActive(false);
                var child = new GameObject("saved-child"); child.transform.SetParent(root.transform, false);
                var component = root.AddComponent(componentType);
                componentType.GetField("AssetRevision").SetValue(component, current ? 2 : 1);
                componentType.GetField("Count").SetValue(component, 17);
                componentType.GetField("State").SetValue(component, State(23));
                var list = (IList)Activator.CreateInstance(componentType.GetField("Items").FieldType);
                list.Add(State(31)); list.Add(State(37)); componentType.GetField("Items").SetValue(component, list);
                var node = Activator.CreateInstance(nodeType); nodeType.GetField("Value").SetValue(node, 29);
                if (current) nodeType.GetField("Extra").SetValue(node, 90000000043L);
                componentType.GetField("Node").SetValue(component, node);
                componentType.GetField("Target").SetValue(component, child);
                if (current) componentType.GetField("Extra").SetValue(component, 90000000017L);
                return root;
            }
            const string prefab = "Assets/Resources/DheAssetPrefab.prefab", scenePath = "Assets/Scenes/DheAssetScene.unity";
            Directory.CreateDirectory("Assets/Resources");
            var source = Object("saved-prefab");
            if (PrefabUtility.SaveAsPrefabAsset(source, prefab) == null) throw new InvalidOperationException("Prefab was not saved.");
            UnityEngine.Object.DestroyImmediate(source);
            var startup = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Object("saved-scene");
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Scene was not saved.");
            EditorSceneManager.CloseScene(scene, true); SceneManager.SetActiveScene(startup);
            EditorBuildSettings.scenes = EditorBuildSettings.scenes.Where(row => row.path != scenePath)
                .Concat(new[] { new EditorBuildSettingsScene(scenePath, true) }).ToArray();
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
            string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
            string[] args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, "-dheOutputRoot");
            if (index < 0) throw new ArgumentException("Missing output root.");
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[index + 1])), "unity-assets.json"),
                JsonUtility.ToJson(new Evidence { assetRevision = current ? 2 : 1, scene = scenePath, prefab = prefab,
                    sceneSha256 = Hash(scenePath), prefabSha256 = Hash(prefab) }, true));
        }
    }
}
