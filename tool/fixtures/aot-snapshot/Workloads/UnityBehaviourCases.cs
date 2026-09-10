using System.Collections;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.Scripting;

namespace HybridCLR.Lab.UnityCases
{
    public static class UnityCaseState
    {
        public static int Disabled, Destroyed;
    }

    [Preserve]
    public sealed class EvolvingBehaviour : MonoBehaviour
    {
        public int Value, Enabled, Started, Updates, LateUpdates, CoroutineSteps;
#if UNITY_CASE_CURRENT
        public long Extra;
        public object Marker;
        private const int Delta = 2;
#else
        private const int Delta = 1;
#endif
        private void Awake()
        {
            Value = 1 + Delta * 10;
#if UNITY_CASE_CURRENT
            Extra = 91000000019L;
            Marker = new object();
#endif
        }
        private void OnEnable() { Enabled += Delta; }
        private void Start() { Started += Delta; StartCoroutine(Steps()); }
        private void Update() { Updates++; Value += Delta; }
        private void LateUpdate() { LateUpdates += Delta; }
        private void OnDisable() { UnityCaseState.Disabled += Delta; }
        private void OnDestroy() { UnityCaseState.Destroyed += Delta; }
        private IEnumerator Steps()
        {
            CoroutineSteps += Delta;
            yield return null;
            CoroutineSteps += Delta;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadUnchanged() { return Value + 1; }
    }

#if UNITY_CASE_CURRENT
    [Preserve]
    public sealed class AddedBehaviour : MonoBehaviour
    {
        public int Value;
        private void Awake() { Value = 77; }
    }
#endif
}
