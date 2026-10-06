using UnityEngine;
#if COMPONENT
public class Parent : MonoBehaviour { }
public class Child : Parent { }
#elif SCRIPTABLE
public class Data : ScriptableObject { }
#elif CALLBACK
public static class Startup
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Run() { }
}
#endif
