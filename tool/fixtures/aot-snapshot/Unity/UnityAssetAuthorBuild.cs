using UnityEditor;
using System;
using System.IO;
using System.Collections.Generic;
using HybridCLR.Editor.Commands;

namespace HybridCLR.Lab.Editor
{
    internal static class UnityAssetAuthorBuild
    {
        public static void Build()
        {
            AssetDatabase.Refresh();
            var args = Environment.GetCommandLineArgs();
            string source = args[Array.IndexOf(args, "-dheAssetCurrent") + 1];
            string output = args[Array.IndexOf(args, "-dheAssetProvenanceOutput") + 1];
            DheAssetBuild.Build(Directory.GetFiles(source, "*.dll"), BuildTarget.StandaloneWindows64, "Unity2022Fgs", output, root => {
                UnityAssetBuild.PrepareIfPresent();
                return new Dictionary<string, string> {
                    ["prefab"] = Path.Combine(root, "asset-bundles/dhe-prefab"),
                    ["scene"] = Path.Combine(root, "asset-bundles/dhe-scene") };
            });
        }
    }
}
