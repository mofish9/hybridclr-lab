extern alias model;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Payload = model::HybridCLR.Lab.ValueLayout.Payload;

namespace HybridCLR.Lab.Review
{
    public static class BoxedReflectionCases
    {
        public static void Run()
        {
            Console.WriteLine("DHE review begin: boxed-reflection");
            object marker = new object();
            var move = typeof(IEnumerator).GetMethod("MoveNext");
            IEnumerator iterator = NewIterator(marker);
            var concrete = iterator.GetType().GetMethod("MoveNext");
            VerifyMove(move, iterator, marker);
            Console.WriteLine("DHE review passed: boxed-interface-reflection");
            typeof(IEnumerator).GetMethod("Reset").Invoke(iterator, null);
            VerifyMove(concrete, iterator, marker);
            Console.WriteLine("DHE review passed: boxed-concrete-reflection");
            ((IDisposable)iterator).Dispose();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly.GetName().Name != "Assembly-CSharp") continue;
                var old = assembly.GetType("HybridCLR.Lab.Snapshot.SnapshotPlayer")
                    .GetField("OldBoxedEnumerator").GetValue(null);
                if (old == null) throw new InvalidOperationException("Missing old-box negative control.");
                Console.WriteLine("DHE review old-box selection: interface=" + HybridCLR.RuntimeApi.IsDifferentialMethodChanged(move) +
                    ", current-concrete=" + HybridCLR.RuntimeApi.IsDifferentialMethodChanged(concrete) +
                    ", old-concrete=" + HybridCLR.RuntimeApi.IsDifferentialMethodChanged(old.GetType().GetMethod("MoveNext")));
                RequireRejected(move, old);
                RequireRejected(concrete, old);
                Console.WriteLine("DHE review passed: old-box-interface-and-concrete-rejection");
            }

            var start = new ManualResetEventSlim(false);
            var threads = new Thread[8];
            var errors = new Exception[threads.Length];
            for (int index = 0; index < threads.Length; ++index)
            {
                int worker = index;
                threads[index] = new Thread(() => {
                    try
                    {
                        start.Wait();
                        for (int sample = 0; sample < 64; ++sample)
                        {
                            var current = NewIterator(marker);
                            VerifyMove(move, current, marker);
                            ((IDisposable)current).Dispose();
                        }
                    }
                    catch (Exception error) { errors[worker] = error; }
                });
                threads[index].Start();
            }
            start.Set();
            foreach (var thread in threads) thread.Join();
            foreach (var error in errors) if (error != null) throw new InvalidOperationException("Concurrent boxed reflection failed.", error);
            start.Dispose();
            Console.WriteLine("DHE review passed: boxed-reflection-and-concurrent-generic-touch");
        }

        private static IEnumerator NewIterator(object marker) =>
            ((IEnumerable)new List<Payload> { new Payload { Count = 17, Extra = 90000000001L, Reference = marker } }).GetEnumerator();

        private static void VerifyMove(MethodInfo move, IEnumerator iterator, object marker)
        {
            if (!(bool)move.Invoke(iterator, null)) throw new InvalidOperationException("Missing reflected value.");
            var value = (Payload)typeof(IEnumerator).GetProperty("Current").GetValue(iterator, null);
            if (value.Count != 17 || value.Extra != 90000000001L || !ReferenceEquals(value.Reference, marker))
                throw new InvalidOperationException("Reflection copied the wrong physical layout.");
            if ((bool)move.Invoke(iterator, null)) throw new InvalidOperationException("Reflected iterator did not terminate.");
            try { typeof(IEnumerator).GetProperty("Current").GetValue(iterator, null); }
            catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException) { return; }
            throw new InvalidOperationException("Reflected invalid Current did not preserve its exception.");
        }

        private static void RequireRejected(MethodInfo method, object receiver)
        {
            try { method.Invoke(receiver, null); }
            catch (TargetException) { return; }
            catch (TargetInvocationException error) when (error.InnerException is ExecutionEngineException) { return; }
            throw new InvalidOperationException("Reflection admitted an old physical box.");
        }
    }
}
