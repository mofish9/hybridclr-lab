extern alias model;
using System;
using UnityEngine;
using Evolving = model::HybridCLR.Lab.UnityCases.EvolvingBehaviour;
using Factory = model::HybridCLR.Lab.ValueLayout.Factory;

namespace HybridCLR.Lab.UnityReference
{
    public static class DispatchCases
    {
        public static void RunIfRequested()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-unityReferenceDispatchProbe") >= 0) Run();
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void Run()
        {
            var owner = new GameObject("DHE reflected dispatch diagnostic"); owner.SetActive(false);
            try
            {
                Type type = typeof(Factory).Assembly.GetType(typeof(Evolving).FullName, true);
                var component = (Evolving)owner.AddComponent(type); component.Value = 37;
                var reader = type.GetMethod("ReadUnchanged"); reader.Invoke(component, null);
                RuntimeApi.ResetDifferentialDispatchCounters();
                int actual = (int)reader.Invoke(component, null);
                int aot = RuntimeApi.GetDifferentialAotEntryCount(), interpreted = RuntimeApi.GetDifferentialInterpreterEntryCount();
                Console.WriteLine("DHE reflected dispatch reader: selected=" + RuntimeApi.IsDifferentialMethodChanged(reader) +
                    " actual=" + actual + " expected=38 aot=" + aot + " interpreter=" + interpreted);
                if (actual != 38) throw new InvalidOperationException("Reflected reader value mismatch.");
                Console.WriteLine("DHE Unity reference dispatch check: reader-value");
                var unchanged = typeof(Factory).GetMethod("UnchangedRevision"); unchanged.Invoke(null, null);
                RuntimeApi.ResetDifferentialDispatchCounters(); actual = (int)unchanged.Invoke(null, null);
                aot = RuntimeApi.GetDifferentialAotEntryCount(); interpreted = RuntimeApi.GetDifferentialInterpreterEntryCount();
                Console.WriteLine("DHE reflected dispatch unaffected: selected=" + RuntimeApi.IsDifferentialMethodChanged(unchanged) +
                    " actual=" + actual + " expected=5 aot=" + aot + " interpreter=" + interpreted);
                if (actual != 5) throw new InvalidOperationException("Unaffected value mismatch.");
                Console.WriteLine("DHE Unity reference dispatch check: unaffected-value");
            }
            finally { UnityEngine.Object.DestroyImmediate(owner); }
            Console.WriteLine("DHE Unity reference dispatch pass: 2");
        }
    }
}
