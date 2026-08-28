using UnityEditor;
using UnityEngine;

namespace HybridCLR.Lab.Editor
{
    [InitializeOnLoad]
    internal static class HybridCLRLabEditorSmoke
    {
        static HybridCLRLabEditorSmoke()
        {
            Debug.Log("[HybridCLR Lab] Tuanjie editor project loaded.");
        }

        [MenuItem("HybridCLR Lab/Validate Project")]
        private static void ValidateProject()
        {
            Debug.Log("[HybridCLR Lab] Project validation entry point is available.");
        }
    }
}

