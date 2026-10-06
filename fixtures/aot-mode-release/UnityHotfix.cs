using UnityEngine;
namespace StartupUnityHotfix
{
    public sealed class Worker : MonoBehaviour
    {
        public int Value;
        void Awake()
        {
#if CURRENT_PAYLOAD
            Value=207;
#else
            Value=107;
#endif
        }
    }
    public sealed class Data : ScriptableObject { public int Value=29; }
    public static class Entry
    {
        public static int Run()
        {
            var gameObject=new GameObject("Hotfix dynamic component");
            var worker=(Worker)gameObject.AddComponent(typeof(Worker));
            var data=(Data)ScriptableObject.CreateInstance(typeof(Data));
            int result=worker.Value+data.Value;
            Object.Destroy(gameObject); Object.Destroy(data);
            return result;
        }
    }
}
