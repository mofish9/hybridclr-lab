using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace HybridCLR.Lab.Snapshot
{
    // Test Base linker roots, not a runtime callback. Later Current DLLs use
    // these existing ordinary Unity APIs without changing the engine assembly.
    internal static class UnitySerializationRoots
    {
        [Preserve]
        private static void Preserve(GameObject value, Type component, string json)
        {
            value.SetActive(false);
            JsonUtility.ToJson(value.GetComponent(component), false);
            JsonUtility.FromJsonOverwrite(json, value.GetComponent(component));
            GameObject copy = UnityEngine.Object.Instantiate(value);
            UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
