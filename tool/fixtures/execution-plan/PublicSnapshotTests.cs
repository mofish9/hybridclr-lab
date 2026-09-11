using HybridCLR;

internal static class PublicSnapshotTests
{
    internal static void Run(Dictionary<string, bool> cases, Provider provider,
        DheRuntimeIdentity identity, string manifestPath, string assets, string pausePath)
    {
        string[] expected = identity.AssemblyNames.OrderBy(name => name,
            StringComparer.OrdinalIgnoreCase).ToArray();
        Func<string[]>[] properties = { () => DheRuntime.PlannedAssemblyNames,
            () => DheRuntime.LoadedAssemblyNames, () => DheRuntime.DifferentialAssemblyNames,
            () => DheRuntime.InterpreterOnlyAssemblyNames };
        foreach (bool fail in new[] { false, true })
        {
            RuntimeApi.SimulateNewProcess();
            using var gate = new PausedProvider(provider, pausePath, fail);
            using var readersReady = new CountdownEvent(8);
            int stop = 0, readErrors = 0;
            var readers = Enumerable.Range(0, 8).Select(index => new Thread(() =>
            {
                bool first = true;
                while (Volatile.Read(ref stop) == 0)
                {
                    try
                    {
                        for (int property = 0; property < properties.Length; ++property)
                        {
                            string[] names = properties[property]();
                            // Independent reads may straddle a publication, but
                            // each individual array must be empty or complete.
                            if (names.Length != 0 &&
                                (property == 1 || property == 3 || fail || !names.SequenceEqual(expected)))
                                Interlocked.Increment(ref readErrors);
                        }
                    }
                    catch { Interlocked.Increment(ref readErrors); }
                    finally { if (first) { first = false; readersReady.Signal(); } }
                    Thread.Yield();
                }
            }) { IsBackground = true }).ToArray();
            var initializer = Task.Run(() => DheRuntime.InitializeFromResourceUpdate(gate,
                identity, manifestPath, out _, assets));
            bool started = false, completed = false, initialized = false, joined = true;
            string prefix = fail ? "snapshot-rejected-plan:" : "snapshot-valid-plan:";
            try
            {
                gate.WaitUntilPaused();
                foreach (Thread reader in readers) reader.Start();
                started = true;
                if (!readersReady.Wait(10000)) throw new TimeoutException("Snapshot readers did not start.");
                cases[prefix + "partial-plan-hidden"] = properties.All(read => read().Length == 0);
                gate.Release();
                completed = initializer.Wait(10000);
                if (completed) initialized = initializer.Result;
            }
            finally
            {
                gate.Release();
                Volatile.Write(ref stop, 1);
                if (started) foreach (Thread reader in readers) joined &= reader.Join(5000);
                if (!initializer.IsCompleted && !initializer.Wait(10000))
                    throw new TimeoutException("Plan initialization did not finish.");
            }
            cases[prefix + "concurrent-readers"] = completed && joined && readErrors == 0;
            cases[prefix + "final-state"] = completed && initialized == !fail && RuntimeApi.Calls == 0 &&
                (fail ? properties.All(read => read().Length == 0) :
                    DheRuntime.PlannedAssemblyNames.SequenceEqual(expected) &&
                    DheRuntime.DifferentialAssemblyNames.SequenceEqual(expected) &&
                    DheRuntime.LoadedAssemblyNames.Length == 0 && DheRuntime.InterpreterOnlyAssemblyNames.Length == 0);
            if (!fail)
            {
                string[] retained = DheRuntime.PlannedAssemblyNames;
                bool isolated = true;
                foreach (Func<string[]> read in properties)
                {
                    string[] original = read(), copy = read();
                    if (copy.Length != 0) copy[0] = "caller-mutated-status";
                    isolated &= read().SequenceEqual(original);
                }
                cases["snapshot-returned-arrays-are-copies"] = isolated;
                DheRuntime.Reset();
                cases["snapshot-reset-clears-status-and-preserves-retained-array"] =
                    properties.All(read => read().Length == 0) && retained.SequenceEqual(expected);
            }
        }
        RuntimeApi.SimulateNewProcess();
    }

    private sealed class PausedProvider : IDheRuntimeAssetProvider, IDisposable
    {
        private readonly Provider inner;
        private readonly string pausePath;
        private readonly bool fail;
        private readonly ManualResetEventSlim paused = new(false), released = new(false);
        internal PausedProvider(Provider inner, string pausePath, bool fail)
        { this.inner = inner; this.pausePath = pausePath; this.fail = fail; }
        internal void WaitUntilPaused()
        { if (!paused.Wait(10000)) throw new TimeoutException("Plan did not reach the second assembly."); }
        internal void Release() => released.Set();
        public bool Exists(string path) => inner.Exists(path);
        public string LoadText(string path) => inner.LoadText(path);
        public byte[] LoadBytes(string path)
        {
            if (path == pausePath)
            {
                paused.Set();
                if (!released.Wait(10000)) throw new TimeoutException("Plan provider was not released.");
                if (fail) throw new InvalidDataException("Deliberate failure after the first plan assembly.");
            }
            return inner.LoadBytes(path);
        }
        public void Dispose() { paused.Dispose(); released.Dispose(); }
    }
}
