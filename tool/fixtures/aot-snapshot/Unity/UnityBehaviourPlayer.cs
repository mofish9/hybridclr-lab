using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace HybridCLR.Lab.Snapshot
{
    public sealed class UnityBehaviourPlayer : MonoBehaviour
    {
        private readonly List<string> checks = new List<string>();
        private Action<string[], string> completed;
        private Type componentType, stateType;
        private Behaviour component;
        private GameObject target, addedTarget;
        private WeakReference marker;
        private int delta, phase, firstFrame;
        private float deadline;
        private bool expectedAwakeChanged, expectedReaderChanged;

        internal static void Begin(int expectedDelta, bool[] expectedSelections, Action<string[], string> completed)
        {
            if (expectedSelections == null || expectedSelections.Length != 2)
                throw new ArgumentException("Lifecycle requires two bound method selections.");
            var driver = new GameObject("DHE Unity validation driver").AddComponent<UnityBehaviourPlayer>();
            driver.delta = expectedDelta;
            driver.expectedAwakeChanged = expectedSelections[0]; driver.expectedReaderChanged = expectedSelections[1];
            Console.WriteLine("DHE Unity expected selection: " + expectedSelections[0] + "," + expectedSelections[1]);
            driver.completed = completed; driver.firstFrame = Time.frameCount; driver.deadline = Time.realtimeSinceStartup + 20;
        }
        private void Require(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("Unity DHE component: " + name);
            checks.Add(name); Console.WriteLine("DHE Unity check: " + name);
        }
        private int Read(string name) => (int)componentType.GetField(name).GetValue(component);
        private int State(string name) => (int)stateType.GetField(name).GetValue(null);
        private void Update()
        {
            try
            {
                if (Time.frameCount - firstFrame > 600 || Time.realtimeSinceStartup > deadline) throw new TimeoutException("Unity callback/coroutine validation timed out.");
                if (phase == 0)
                {
                    var model = typeof(ValueLayout.Factory).Assembly;
                    componentType = model.GetType("HybridCLR.Lab.UnityCases.EvolvingBehaviour", true);
                    stateType = model.GetType("HybridCLR.Lab.UnityCases.UnityCaseState", true);
                    target = new GameObject("DHE evolving component"); component = (Behaviour)target.AddComponent(componentType);
                    Require(Read("Value") == 1 + 10 * delta, "awake-current-value");
                    Require(Read("Enabled") == delta, "on-enable-current-value");
                    var awake = componentType.GetMethod("Awake", BindingFlags.NonPublic | BindingFlags.Instance);
                    Require(RuntimeApi.IsDifferentialMethodChanged(awake) == expectedAwakeChanged, "awake-diff-selection");
                    if (delta == 2)
                    {
                        Require((long)componentType.GetField("Extra").GetValue(component) == 91000000019L, "added-instance-field");
                        marker = new WeakReference(componentType.GetField("Marker").GetValue(component));
                        Type added = model.GetType("HybridCLR.Lab.UnityCases.AddedBehaviour", true);
                        addedTarget = new GameObject("DHE added component"); var instance = addedTarget.AddComponent(added);
                        Require((int)added.GetField("Value").GetValue(instance) == 77, "added-component-awake");
                    }
                    phase = 1; return;
                }
                if (phase == 1)
                {
                    if (Read("Updates") < 3 || Read("CoroutineSteps") < 2 * delta || Read("LateUpdates") < delta) return;
                    component.enabled = false;
                    Require(Read("Started") == delta, "start-current-value");
                    Require(Read("Value") == 1 + 10 * delta + Read("Updates") * delta, "update-current-value");
                    Require(Read("LateUpdates") >= delta && Read("LateUpdates") % delta == 0, "late-update-current-value");
                    Require(Read("CoroutineSteps") == 2 * delta, "coroutine-resumed-across-frames");
                    Require(State("Disabled") == delta, "on-disable-current-value");
                    MethodInfo reader = componentType.GetMethod("ReadUnchanged");
                    // The bound plan includes field and interface dependencies,
                    // even when the fixture's numerical Delta did not change.
                    Require(RuntimeApi.IsDifferentialMethodChanged(reader) == expectedReaderChanged, "layout-dependent-reader-selection");
                    reader.Invoke(component, null); RuntimeApi.ResetDifferentialDispatchCounters();
                    int actual = (int)reader.Invoke(component, null);
                    int aot = RuntimeApi.GetDifferentialAotEntryCount(), interpreted = RuntimeApi.GetDifferentialInterpreterEntryCount();
                    Require(actual == Read("Value") + 1 && (expectedReaderChanged ? interpreted >= 1 : aot >= 1 && interpreted == 0), "layout-dependent-reader-execution");
                    MethodInfo unaffected = typeof(ValueLayout.Factory).GetMethod("UnchangedRevision");
                    unaffected.Invoke(null, null); RuntimeApi.ResetDifferentialDispatchCounters();
                    int sentinel = (int)unaffected.Invoke(null, null);
                    aot = RuntimeApi.GetDifferentialAotEntryCount(); interpreted = RuntimeApi.GetDifferentialInterpreterEntryCount();
                    Require(sentinel == 5 && !RuntimeApi.IsDifferentialMethodChanged(unaffected) && aot >= 1 && interpreted == 0,
                        "unaffected-method-stays-aot");
                    phase = 2; return;
                }
                if (phase == 2)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    if (delta == 2) Require(marker.IsAlive && ReferenceEquals(marker.Target, componentType.GetField("Marker").GetValue(component)), "added-reference-survives-gc");
                    Destroy(target); if (addedTarget != null) Destroy(addedTarget);
                    phase = 3; return;
                }
                if (State("Destroyed") != delta) return;
                Require(State("Disabled") == delta, "disable-not-repeated-on-destroy");
                Require(State("Destroyed") == delta, "on-destroy-current-value");
                Require(Time.frameCount - firstFrame >= 3, "real-multiple-frames");
                Finish(null);
            }
            catch (Exception error) { Finish(error.ToString()); }
        }
        private void Finish(string error)
        {
            enabled = false;
            if (error == null) Console.WriteLine("DHE Unity component pass: " + delta + ":" + checks.Count);
            completed(checks.ToArray(), error);
        }
    }
}
