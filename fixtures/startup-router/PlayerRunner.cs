using System;
using System.IO;
using System.Reflection;
using System.Linq;
using UnityEngine;
using HybridCLR.Startup;
using System.Security.Cryptography;
using System.Runtime.InteropServices;

namespace StartupWindowsFixture
{
    public static class PlayerRunner
    {
        [Serializable] public sealed class Result
        {
            public string format = "hybridclr.startup-windows-player.json";
            public bool passed, workloadPassed, selectionImmutable, profileMatchesBackend, hotfixPresentBeforeLoad;
            public int nativeBackendMask;
            public bool nativePrepareRejected;
            public ulong selectedGeneration, committedGeneration;
            public string effectiveMode, effectiveModeAfterRequest, currentSha256, error, nextMode;
            public int pid, caseCount, nativeLoadCode, interpreterEntries, aotEntries;
            public int[] actual, expected;
            public bool changedSelected, unchangedSelected, diagnosticsEnabled, restartRequired;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Run()
        {
            var result = new Result { pid = System.Diagnostics.Process.GetCurrentProcess().Id };
            string output = Argument("-startupReport");
            try
            {
                string root = Argument("-startupPairRoot");
#if STARTUP_DHE_PROFILE
                const HybridExecutionMode compiledMode = HybridExecutionMode.DHE;
#else
                const HybridExecutionMode compiledMode = HybridExecutionMode.LegacyInterpreter;
#endif
                result.effectiveMode = HybridStartup.EffectiveMode.ToString();
                result.selectedGeneration = HybridStartup.GetSelection().GenerationAtStartup;
                result.nativeBackendMask = hclr_startup_loaded_backend_mask();
                result.nativePrepareRejected = hclr_startup_prepare(root, Argument("-startupStore")) == (int)StartupStatus.AlreadyPrepared;
                result.profileMatchesBackend = compiledMode == HybridStartup.EffectiveMode && result.nativeBackendMask == (compiledMode == HybridExecutionMode.DHE ? 1 : 2);
                if (!result.profileMatchesBackend) throw new Exception("Compiled profile, native selection or loaded backend inventory differs.");
                try
                {
                    byte[] dll = File.ReadAllBytes(Path.Combine(root, "shared", "StartupHotfix.dll"));
                    using (var sha = SHA256.Create()) result.currentSha256 = BitConverter.ToString(sha.ComputeHash(dll)).Replace("-", "").ToLowerInvariant();
                    result.hotfixPresentBeforeLoad = AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "StartupHotfix");
                    Assembly assembly;
#if STARTUP_DHE_PROFILE
                    if (!result.hotfixPresentBeforeLoad) throw new Exception("DHE Base AOT assembly is absent.");
                    byte[] before = File.ReadAllBytes(Path.Combine(root, "shared", "base.mv"));
                    byte[] after = File.ReadAllBytes(Path.Combine(root, "shared", "current.mv"));
                    if (HasArgument("-startupFaultDhe")) before[0] ^= 1;
                    result.nativeLoadCode = (int)HybridCLR.RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(dll, before, after);
                    if (result.nativeLoadCode != 0) throw new Exception("DHE registration rejected: " + result.nativeLoadCode);
                    assembly = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "StartupHotfix");
                    result.diagnosticsEnabled = HybridCLR.RuntimeApi.AreDifferentialDispatchDiagnosticsEnabled();
                    HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
#else
                    if (result.hotfixPresentBeforeLoad) throw new Exception("Legacy Player unexpectedly includes hotfix AOT assembly.");
                    assembly = Assembly.Load(dll);
#endif
                    Type entry = assembly.GetType("StartupHotfix.Entry", true);
                    result.actual = new[] {
                        Invoke(entry, "Changed", 5), Invoke(entry, "Unchanged", 7),
                        Invoke(entry, "InterfaceCall", 6), Invoke(entry, "DelegateCall", 8),
                        Invoke(entry, "ValueCall", 12), Invoke(entry, "StaticCall", 19),
                        Invoke(entry, "ExceptionCall"), Invoke(entry, "Added", 3),
                        (int)entry.GetMethod("Echo").MakeGenericMethod(typeof(int)).Invoke(null, new object[] { 23 }),
                        (int)assembly.GetType("StartupHotfix.AddedType", true).GetField("Value").GetValue(
                            Activator.CreateInstance(assembly.GetType("StartupHotfix.AddedType", true)))
                    };
                    result.expected = new[] { 205, 21, 24, 208, 21, 19, 17, 34, 23, 27 };
                    result.caseCount = result.actual.Length;
                    result.workloadPassed = result.actual.SequenceEqual(result.expected);
#if STARTUP_DHE_PROFILE
                    result.changedSelected = HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Changed"));
                    result.unchangedSelected = HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Unchanged"));
                    result.interpreterEntries = HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
                    result.aotEntries = HybridCLR.RuntimeApi.GetDifferentialAotEntryCount();
                    if (!result.changedSelected || result.unchangedSelected) throw new Exception("DHE changed/unchanged selection mismatch.");
#endif
                }
                catch (Exception exception) { result.error = exception.ToString(); }
                string next = OptionalArgument("-startupNextMode");
                if (!string.IsNullOrEmpty(next))
                {
                    result.nextMode = next;
                    var committed = HybridStartup.RequestNextStartupModeAsync(
                        (HybridExecutionMode)Enum.Parse(typeof(HybridExecutionMode), next)).GetAwaiter().GetResult();
                    result.restartRequired = committed.RestartRequired; result.committedGeneration = committed.CommittedGeneration;
                }
                if (HasArgument("-startupClear")) { var committed = HybridStartup.ClearNextStartupModeAsync().GetAwaiter().GetResult(); result.restartRequired = committed.RestartRequired; result.committedGeneration = committed.CommittedGeneration; }
                result.effectiveModeAfterRequest = HybridStartup.EffectiveMode.ToString();
                result.selectionImmutable = result.effectiveMode == result.effectiveModeAfterRequest;
                result.passed = result.workloadPassed && result.selectionImmutable && result.profileMatchesBackend && result.nativePrepareRejected && string.IsNullOrEmpty(result.error);
            }
            catch (Exception exception) { result.error = exception.ToString(); }
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output, JsonUtility.ToJson(result, true));
            Debug.Log("STARTUP_RESULT " + JsonUtility.ToJson(result));
            Application.Quit(result.passed ? 0 : 2);
        }
        [DllImport("GameAssembly.dll", CallingConvention = CallingConvention.Cdecl)] static extern int hclr_startup_loaded_backend_mask();
        [DllImport("GameAssembly.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)] static extern int hclr_startup_prepare(string root, string store);
        static int Invoke(Type type, string name, params object[] args) { return (int)type.GetMethod(name).Invoke(null, args); }
        static bool HasArgument(string name) { return Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0; }
        static string OptionalArgument(string name)
        { var args = Environment.GetCommandLineArgs(); int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
        static string Argument(string name) { return OptionalArgument(name) ?? throw new Exception("Missing argument " + name); }
    }
}
