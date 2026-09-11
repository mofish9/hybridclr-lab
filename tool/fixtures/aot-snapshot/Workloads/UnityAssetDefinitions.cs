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
#if UNITY_ASSET_ADDED_TYPES
    [Serializable, Preserve]
    public sealed class AssetExtension
    {
        public int Number;
        public string Text;
    }
    [Serializable, Preserve]
    public sealed class AssetAddedNode : IAssetNode
    {
        public int Value;
        public int Read() => Value + 200;
    }
#endif
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
#if UNITY_ASSET_ADDED_TYPES
        public AssetExtension Added;
        public AssetExtension[] AddedItems;
        [SerializeReference] public IAssetNode AddedNode;
#endif
#if UNITY_ASSET_CURRENT
        public long Extra;
        private const int Delta = 2;
#else
        private const int Delta = 1;
#endif
        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize()
        {
#if UNITY_ASSET_ADDED_TYPES
            // Empty Editor/default instances are allowed. The replay separately
            // requires successful data-bearing callbacks for both saved assets.
            if (Added != null || AddedItems != null || AddedNode != null)
            {
                if (Added == null || Added.Number != 101 || AddedItems == null || AddedItems.Length != 2 ||
                    AddedItems[0] == null || AddedItems[0].Number != 103 ||
                    AddedItems[1] == null || AddedItems[1].Number != 107 ||
                    AddedItems[0].Text != Added.Text || AddedItems[1].Text != Added.Text ||
                    AddedNode == null || AddedNode.GetType() != typeof(AssetAddedNode) || AddedNode.Read() != 309)
                    throw new InvalidOperationException("New serialized type data was not preserved.");
                Console.WriteLine("DHE added serialized types pass: " + Added.Text);
            }
#endif
            DeserializedStamp = Delta;
        }
        private void Awake() { Awakened = Delta; }
    }
}
