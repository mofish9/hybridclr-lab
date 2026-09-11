using UnityEditor;

namespace HybridCLR.Lab.Editor
{
    internal static class UnityAssetAuthorBuild
    {
        public static void Build()
        {
            AssetDatabase.Refresh();
            UnityAssetBuild.PrepareIfPresent();
        }
    }
}
