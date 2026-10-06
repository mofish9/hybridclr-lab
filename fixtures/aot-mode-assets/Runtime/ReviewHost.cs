using System;
using UnityEngine;
public sealed class ReviewHost : ScriptableObject
{
    [Serializable] public sealed class Node { [SerializeReference] public Node next; [SerializeReference] public object payload; }
    [SerializeReference] public Node root, shared;
    [SerializeReference] public object tail;
}
