using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace HybridCLR.StartupPrototype
{
    public enum HybridExecutionMode { DHE = 1, LegacyInterpreter = 2 }

    // Windows prototype contract, shared by the pre-Unity launcher and both
    // AOT Players. It has no dependency on Unity, DHE, or hotfix assemblies.
    public sealed class StartupStore
    {
        readonly string path;
        readonly byte[] scopeHash;
        readonly string mutexName;
        public StartupStore(string path, string scope)
        {
            this.path = Path.GetFullPath(path);
            scopeHash = Digest(System.Text.Encoding.UTF8.GetBytes(scope));
            mutexName = "Local\\HclrStartup-" + Hex(Digest(System.Text.Encoding.UTF8.GetBytes(this.path.ToUpperInvariant())));
        }
        public HybridExecutionMode Read(out long generation)
        {
            using (var mutex = new WindowsMutex(mutexName))
            {
                Enter(mutex);
                try { return ReadLocked(out generation); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        HybridExecutionMode ReadLocked(out long generation)
        {
            byte[] bytes;
            try { bytes = File.ReadAllBytes(path); }
            catch (FileNotFoundException) { generation = 0; return HybridExecutionMode.DHE; }
            catch (DirectoryNotFoundException) { generation = 0; return HybridExecutionMode.DHE; }
            if (bytes.Length != 88 || System.Text.Encoding.ASCII.GetString(bytes, 0, 8) != "HCLRSTR1" ||
                BitConverter.ToInt32(bytes, 8) != 1)
                throw new InvalidDataException("Invalid startup selection record.");
            byte[] body = new byte[56]; Array.Copy(bytes, body, body.Length);
            byte[] hash = Digest(body);
            for (int index = 0; index < 32; index++)
                if (bytes[24 + index] != scopeHash[index] || bytes[56 + index] != hash[index])
                    throw new InvalidDataException("Startup selection scope or checksum mismatch.");
            generation = BitConverter.ToInt64(bytes, 16);
            if (generation < 1) throw new InvalidDataException("Invalid startup selection generation.");
            var mode = (HybridExecutionMode)BitConverter.ToInt32(bytes, 12);
            Validate(mode); return mode;
        }
        public void Write(HybridExecutionMode mode)
        {
            Validate(mode);
            using (var mutex = new WindowsMutex(mutexName))
            {
                Enter(mutex);
                try
                {
                    long generation; ReadLocked(out generation);
                    byte[] bytes = new byte[88];
                    Array.Copy(System.Text.Encoding.ASCII.GetBytes("HCLRSTR1"), bytes, 8);
                    Array.Copy(BitConverter.GetBytes(1), 0, bytes, 8, 4);
                    Array.Copy(BitConverter.GetBytes((int)mode), 0, bytes, 12, 4);
                    Array.Copy(BitConverter.GetBytes(checked(generation + 1)), 0, bytes, 16, 8);
                    Array.Copy(scopeHash, 0, bytes, 24, 32);
                    byte[] body = new byte[56]; Array.Copy(bytes, body, 56);
                    Array.Copy(Digest(body), 0, bytes, 56, 32);
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                    try
                    {
                        using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        { stream.Write(bytes, 0, bytes.Length); stream.Flush(true); }
                        if (File.Exists(path)) File.Replace(temporary, path, null);
                        else File.Move(temporary, path);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                }
                finally { mutex.ReleaseMutex(); }
            }
        }
        public void Clear()
        {
            using (var mutex = new WindowsMutex(mutexName))
            {
                Enter(mutex);
                try { File.Delete(path); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        static void Enter(WindowsMutex mutex)
        {
            mutex.Enter();
        }
        // IL2CPP does not implement the managed named-Mutex constructor.
        // Use one Windows adapter in both the pre-Unity host and IL2CPP Player.
        sealed class WindowsMutex : IDisposable
        {
            readonly IntPtr handle;
            public WindowsMutex(string name)
            {
                handle = CreateMutexW(IntPtr.Zero, false, name);
                if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            public void Enter()
            {
                uint result = WaitForSingleObject(handle, 10000);
                if (result == 0 || result == 0x80) return; // Acquired, including abandoned ownership.
                if (result == 0x102) throw new TimeoutException("Startup store is busy.");
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            public void ReleaseMutex()
            { if (!ReleaseMutexNative(handle)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error()); }
            public void Dispose() { CloseHandle(handle); }
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
            static extern IntPtr CreateMutexW(IntPtr attributes, [MarshalAs(UnmanagedType.Bool)] bool initiallyOwned, string name);
            [DllImport("kernel32.dll", SetLastError = true)] static extern uint WaitForSingleObject(IntPtr handle, uint timeout);
            [DllImport("kernel32.dll", EntryPoint = "ReleaseMutex", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)] static extern bool ReleaseMutexNative(IntPtr handle);
            [DllImport("kernel32.dll")][return: MarshalAs(UnmanagedType.Bool)] static extern bool CloseHandle(IntPtr handle);
        }
        public static void Validate(HybridExecutionMode mode)
        {
            if (mode != HybridExecutionMode.DHE && mode != HybridExecutionMode.LegacyInterpreter)
                throw new ArgumentOutOfRangeException("mode");
        }
        public static byte[] Digest(byte[] bytes) { using (var sha = SHA256.Create()) return sha.ComputeHash(bytes); }
        public static string Hex(byte[] bytes) { return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant(); }
    }

    public static class HybridStartup
    {
        static readonly object gate = new object();
        static StartupStore store;
        static HybridExecutionMode effective;
        public static void BindCompiledProfile(HybridExecutionMode compiledProfile, StartupStore selectedStore)
        {
            if (selectedStore == null) throw new ArgumentNullException("selectedStore");
            StartupStore.Validate(compiledProfile);
            lock (gate)
            {
                if (store != null) throw new InvalidOperationException("This process already selected its profile.");
                effective = compiledProfile; store = selectedStore;
            }
        }
        public static HybridExecutionMode EffectiveMode
        { get { lock (gate) { if (store == null) throw new InvalidOperationException("Not selected."); return effective; } } }
        public static Task<bool> RequestNextStartupModeAsync(HybridExecutionMode mode)
        {
            lock (gate)
            {
                if (store == null) throw new InvalidOperationException("Not selected.");
                store.Write(mode); return Task.FromResult(mode != effective);
            }
        }
        public static Task<bool> ClearNextStartupModeAsync()
        {
            lock (gate)
            {
                if (store == null) throw new InvalidOperationException("Not selected.");
                store.Clear(); return Task.FromResult(effective != HybridExecutionMode.DHE);
            }
        }
    }
}
