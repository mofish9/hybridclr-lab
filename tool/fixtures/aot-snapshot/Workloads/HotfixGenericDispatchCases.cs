extern alias model;
using System;
using System.Reflection;
using Payload = model::HybridCLR.Lab.ValueLayout.Payload;
using Cases = model::HybridCLR.Lab.ResourceCases.FrozenResourceCases;

namespace HybridCLR.Lab.GenericDispatch
{
    public static class ConditionalCases
    {
        public static void RunIfRequested()
        {
            string[] args = Environment.GetCommandLineArgs();
            int index = Array.IndexOf(args, "-hotfixGenericDispatchAffected");
            if (index < 0) return;
            bool affected;
            if (index + 1 >= args.Length || !bool.TryParse(args[index + 1], out affected))
                throw new InvalidOperationException("Missing bound generic dispatch expectation.");
            object marker = new object();
            var value = new Payload { Count = 17, Extra = 90000000018L, Reference = marker };
            MethodInfo definition = typeof(Cases).GetMethod("Identity", BindingFlags.NonPublic | BindingFlags.Static);
            object result = Invoke(definition.MakeGenericMethod(typeof(Payload)), value, affected, "payload");
            Verify((Payload)result, marker);
            var envelope = new Cases.Envelope<Payload> { Value = value, Marker = 151 };
            result = Invoke(definition.MakeGenericMethod(typeof(Cases.Envelope<Payload>)), envelope, affected, "envelope");
            var copied = (Cases.Envelope<Payload>)result;
            Verify(copied.Value, marker);
            if (copied.Marker != 151) throw new InvalidOperationException("Generic dispatch lost neighboring storage.");
            Console.WriteLine("DHE hotfix generic dispatch pass: 2");
        }

        private static object Invoke(MethodInfo method, object input, bool affected, string name)
        {
            object[] args = { input };
            method.Invoke(null, args);
            bool selected = RuntimeApi.IsDifferentialMethodChanged(method);
            RuntimeApi.ResetDifferentialDispatchCounters();
            object result = method.Invoke(null, args);
            int interpreted = RuntimeApi.GetDifferentialInterpreterEntryCount();
            int aot = RuntimeApi.GetDifferentialAotEntryCount();
            Console.WriteLine("DHE hotfix generic dispatch " + name + ": selected=" + selected +
                " expected=" + affected + " interpreter=" + interpreted + " aot=" + aot);
            if (selected != affected || interpreted != (affected ? 1 : 0) || !affected && aot <= 0)
                throw new InvalidOperationException("Generic dispatch did not follow the bound Base plan: " + name);
            return result;
        }

        private static void Verify(Payload value, object marker)
        {
            if (value.Count != 17 || value.Extra != 90000000018L || !ReferenceEquals(value.Reference, marker))
                throw new InvalidOperationException("Generic dispatch changed the value/reference copy.");
        }
    }
}
