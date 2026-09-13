extern alias model;
using System;
using System.Collections;
using System.Collections.Generic;
using Payload = model::HybridCLR.Lab.ValueLayout.Payload;

namespace HybridCLR.Lab.ResourceCases
{
    public static class BoxedEnumeratorCases
    {
        public static void Run()
        {
            Console.WriteLine("DHE case begin: boxed-enumerator-current-storage");
            var marker = new object();
            var value = new Payload { Count = 17, Extra = 90000000001L, Reference = marker };
            IEnumerable source = new List<Payload> { value, value };
            IEnumerator iterator = source.GetEnumerator();
            if (!iterator.GetType().IsValueType) throw new InvalidOperationException("Expected boxed value enumerator.");
            try
            {
                RequireInvalidCurrent(iterator);
                if (!iterator.MoveNext()) throw new InvalidOperationException("Missing first value.");
                Verify((Payload)iterator.Current, marker);
                GC.Collect();
                Verify((Payload)iterator.Current, marker);
                iterator.Reset();
                RequireInvalidCurrent(iterator);
                if (!iterator.MoveNext()) throw new InvalidOperationException("Reset did not rewind.");
                Verify((Payload)iterator.Current, marker);
                if (!iterator.MoveNext()) throw new InvalidOperationException("Missing second value.");
                Verify((Payload)iterator.Current, marker);
                if (iterator.MoveNext()) throw new InvalidOperationException("Enumerator did not terminate.");
                RequireInvalidCurrent(iterator);
            }
            finally { ((IDisposable)iterator).Dispose(); }
        }

        private static void Verify(Payload value, object marker)
        {
            if (value.Count != 17 || value.Extra != 90000000001L || !ReferenceEquals(value.Reference, marker))
                throw new InvalidOperationException("Boxed enumerator copied the wrong Current layout.");
        }

        private static void RequireInvalidCurrent(IEnumerator iterator)
        {
            try { object value = iterator.Current; }
            catch (InvalidOperationException) { return; }
            throw new InvalidOperationException("Current must reject an invalid iterator position.");
        }
    }
}
