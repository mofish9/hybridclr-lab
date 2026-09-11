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
        [Serializable] private sealed class SerializedRecord { public string file, sha256; public bool typeTreeEnabled; }
        [Serializable] private sealed class BundleRecord { public string name, sha256; public SerializedRecord[] serializedFiles; }
        [Serializable] private sealed class BundleEvidence { public int assetRevision; public string options; public BundleRecord[] bundles; }
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
            // Each batch phase starts a fresh Editor; its active scene can be
            // untitled even though the preceding phase saved the startup scene.
            var startup = EditorSceneManager.OpenScene(EditorBuildSettings.scenes.First(row => row.enabled).path, OpenSceneMode.Single);
            Directory.CreateDirectory("Assets/Resources");
            var source = Object("saved-prefab");
            if (PrefabUtility.SaveAsPrefabAsset(source, prefab) == null) throw new InvalidOperationException("Prefab was not saved.");
            UnityEngine.Object.DestroyImmediate(source);
            EditorSceneManager.SaveScene(startup);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Object("saved-scene");
            if (!EditorSceneManager.SaveScene(scene, scenePath)) throw new InvalidOperationException("Scene was not saved.");
            const string bundledScene = "Assets/DheBundleScenes/DheBundledScene.unity";
            Directory.CreateDirectory("Assets/DheBundleScenes");
            if (!EditorSceneManager.SaveScene(scene, bundledScene, true)) throw new InvalidOperationException("Bundle-only scene was not saved.");
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
            string bundleOutput = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[index + 1])), "asset-bundles");
            if (Directory.Exists(bundleOutput)) throw new IOException("Bundle output must be new.");
            Directory.CreateDirectory(bundleOutput);
            var builds = new[] {
                new AssetBundleBuild { assetBundleName = "dhe-prefab", assetNames = new[] { prefab } },
                new AssetBundleBuild { assetBundleName = "dhe-scene", assetNames = new[] { bundledScene } },
            };
            const BuildAssetBundleOptions options = BuildAssetBundleOptions.UncompressedAssetBundle | BuildAssetBundleOptions.StrictMode;
            if (BuildPipeline.BuildAssetBundles(bundleOutput, builds, options, BuildTarget.StandaloneWindows64) == null)
                throw new InvalidOperationException("AssetBundle build failed.");
            var records = builds.Select(build => {
                string path = Path.Combine(bundleOutput, build.assetBundleName);
                var bundle = new UnityFS.BundleFileReader();
                using (var stream = File.OpenRead(path))
                using (var reader = new UnityFS.EndianBinaryReader(stream)) bundle.Load(reader);
                var serialized = bundle.CreateBundleFileInfo().files.Where(file =>
                    (file.file.StartsWith("CAB-", StringComparison.Ordinal) || file.file.StartsWith("BuildPlayer-", StringComparison.Ordinal)) &&
                    !file.file.EndsWith(".resS", StringComparison.Ordinal) && !file.file.EndsWith(".resource", StringComparison.Ordinal)).Select(file => {
                    var bytes = file.data;
                    if (bytes.Length < 64 || bytes[8] != 0 || bytes[9] != 0 || bytes[10] != 0 || bytes[11] != 22 || bytes[16] != 0)
                        throw new InvalidDataException("Expected Unity 2022 serialized-file header: " + file.file);
                    int end = 48; while (end < bytes.Length && bytes[end] != 0) ++end;
                    if (end > 304 || end + 5 >= bytes.Length) throw new InvalidDataException("Invalid serialized metadata.");
                    bool trees = bytes[end + 5] != 0;
                    using (var hash = SHA256.Create()) return new SerializedRecord { file = file.file, typeTreeEnabled = trees,
                        sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "") };
                }).ToArray();
                if (serialized.Length == 0 || serialized.Any(file => !file.typeTreeEnabled)) throw new InvalidDataException("Typed bundle required.");
                return new BundleRecord { name = build.assetBundleName, sha256 = Hash(path), serializedFiles = serialized };
            }).ToArray();
            File.WriteAllText(Path.Combine(bundleOutput, "bundle-evidence.json"), JsonUtility.ToJson(new BundleEvidence {
                assetRevision = current ? 2 : 1, options = options.ToString(), bundles = records }, true));
        }
    }
}
