using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Scripting;

namespace HybridCLR.Lab.UnityAssets
{
    [Serializable, Preserve]
    public sealed class AssetState
    {
#if UNITY_ASSET_CURRENT
        [FormerlySerializedAs("Number")] public int RenamedNumber;
        public long Extra;
#else
        public int Number;
#endif
        public string Text;
    }
    public interface IAssetNode { int Read(); }
    [Serializable, Preserve]
    public sealed class AssetNode : IAssetNode
    {
        public int Value;
#if UNITY_ASSET_CURRENT
        public long Extra;
        public int Read() => Value + 100;
#else
        public int Read() => Value;
#endif
    }
    [Preserve]
    public sealed class AssetBehaviour : MonoBehaviour, ISerializationCallbackReceiver
    {
        public int AssetRevision, Count;
        public AssetState State;
        public List<AssetState> Items;
        [SerializeReference] public IAssetNode Node;
        public GameObject Target;
        [NonSerialized] public int DeserializedStamp, Awakened;
#if UNITY_ASSET_CURRENT
        public long Extra;
        private const int Delta = 2;
#else
        private const int Delta = 1;
#endif
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { DeserializedStamp = Delta; }
        private void Awake() { Awakened = Delta; }
    }
}
