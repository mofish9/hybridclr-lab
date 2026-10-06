using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace AotSelectionProbe
{
    static class Native
    {
#if UNITY_EDITOR
        public static void Arm(int poison) { throw new NotSupportedException(); }
        public static int Count() { throw new NotSupportedException(); }
#else
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Arm(int poison);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Count();
#endif
    }
    public static class PlayerRunner
    {
        [Serializable] public sealed class Result
        {
            public string format="hybridclr.aot-isolation.v2", scenario, error, currentSha256, consumerSha256, callback;
            public int pid, nativeModeBefore, mode, modeAfter, selectionResult, repeatSelectionResult, invalidSelectionResult;
            public int visibleBefore, visibleAfterSelect, visibleAfterLoad, caseCount, differential;
            public int dheImplementationCalls, dheLoadCode, aotEntries, interpreterEntries, concurrentSuccesses;
            public bool bootstrapExecuted, nameResolvedBefore, nameIdentityMatches, consumerTypeMatches;
            public bool changedSelected, unchangedSelected, workloadPassed, testPassed, faultObserved;
            public bool prematureLoadRejected, wrongLoaderRejected;
            public int[] actual, expected, concurrentReturns;
        }
        static Result result;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            result=new Result {pid=System.Diagnostics.Process.GetCurrentProcess().Id,callback="BeforeSceneLoad",bootstrapExecuted=true,scenario=Argument("-scenario")};
            try
            {
                string root=Argument("-startupPairRoot");
                byte[] dll=File.ReadAllBytes(Path.Combine(root,"shared/StartupHotfix.dll"));
                byte[] consumer=File.ReadAllBytes(Path.Combine(root,"shared/StartupConsumer.dll"));
                result.currentSha256=Hash(dll); result.consumerSha256=Hash(consumer);
                result.visibleBefore=VisibleCount();
                result.nameResolvedBefore=Type.GetType("StartupHotfix.Worker, StartupHotfix",false)!=null;
#if STARTUP_DHE_PROFILE
                result.nativeModeBefore=(int)HybridCLR.RuntimeApi.GetExecutionMode();
                result.invalidSelectionResult=Select(99);
                try { Assembly.Load(dll); } catch(InvalidOperationException) { result.prematureLoadRejected=true; }
                if(result.scenario=="concurrent") {
                    var tasks=Enumerable.Range(0,12).Select(n=>Task.Run(()=>Select(n%2+1))).ToArray();
                    Task.WaitAll(tasks); result.concurrentReturns=tasks.Select(task=>task.Result).ToArray();
                    result.concurrentSuccesses=result.concurrentReturns.Count(value=>value==0);
                    result.selectionResult=result.concurrentSuccesses==1 ? 0 : -1;
                }
                else result.selectionResult=Select(result.scenario.StartsWith("dhe") ? 1 : 2);
                result.mode=(int)HybridCLR.RuntimeApi.GetExecutionMode();
                result.repeatSelectionResult=Select(result.mode==1 ? 2 : 1);
#else
                result.mode=2; result.selectionResult=0; result.repeatSelectionResult=1; result.invalidSelectionResult=2;
                result.prematureLoadRejected=true; result.wrongLoaderRejected=true;
#endif
                result.visibleAfterSelect=VisibleCount();
                Assembly loaded;
#if STARTUP_DHE_PROFILE
                if(result.mode==1)
                {
                    try { Assembly.Load(dll); } catch(InvalidOperationException) { result.wrongLoaderRejected=true; }
                    result.dheLoadCode=(int)HybridCLR.RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(dll,
                        File.ReadAllBytes(Path.Combine(root,"shared/base.mv")),File.ReadAllBytes(Path.Combine(root,"shared/current.mv")));
                    if(result.dheLoadCode!=0)throw new Exception("DHE load failed: "+result.dheLoadCode);
                    loaded=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="StartupHotfix");
                    HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
                    Native.Arm(result.scenario=="dhe-poison" ? 1 : 0);
                }
                else
                {
                    // Poison the entire selected boundary before loading Current.
                    Native.Arm(result.scenario=="legacy-poison" ? 1 : 0);
                    if(result.scenario!="legacy-bad-mv") {
                        try { HybridCLR.RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(dll,
                            File.ReadAllBytes(Path.Combine(root,"shared/base.mv")),File.ReadAllBytes(Path.Combine(root,"shared/current.mv"))); }
                        catch(InvalidOperationException) { result.wrongLoaderRejected=true; }
                    } else result.wrongLoaderRejected=true;
                    loaded=Assembly.Load(dll);
                }
#else
                loaded=Assembly.Load(dll);
#endif
                result.visibleAfterLoad=VisibleCount();
                result.nameIdentityMatches=Assembly.Load("StartupHotfix")==loaded &&
                    Type.GetType("StartupHotfix.Worker, StartupHotfix",true)==loaded.GetType("StartupHotfix.Worker",true);
                var other=Assembly.Load(consumer);
                result.consumerTypeMatches=(Type)other.GetType("StartupConsumer.Entry",true).GetMethod("WorkerType").Invoke(null,null)==loaded.GetType("StartupHotfix.Worker",true);
                result.actual=Workload.Run(loaded,other); result.expected=Workload.Expected; result.caseCount=result.actual.Length;
                result.differential=result.actual.Where((value,n)=>value!=result.expected[n]).Count(); result.workloadPassed=result.differential==0;
#if STARTUP_DHE_PROFILE
                if(result.mode==1) {
                    Type entry=loaded.GetType("StartupHotfix.Entry",true);
                    result.changedSelected=HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Changed"));
                    result.unchangedSelected=HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Unchanged"));
                    result.aotEntries=HybridCLR.RuntimeApi.GetDifferentialAotEntryCount();
                    result.interpreterEntries=HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
                }
#endif
            }
            catch(Exception error) { result.error=error.ToString(); result.faultObserved=result.error.Contains("LAB_DHE_IMPLEMENTATION_FAULT"); }
            finally {
#if STARTUP_DHE_PROFILE
                result.dheImplementationCalls=Native.Count(); Native.Arm(-1);
                result.modeAfter=(int)HybridCLR.RuntimeApi.GetExecutionMode();
#else
                result.modeAfter=result.mode;
#endif
            }
            bool mechanism=result.visibleBefore==0 && !result.nameResolvedBefore && result.selectionResult==0 &&
                result.repeatSelectionResult==1 && result.invalidSelectionResult==2 && result.prematureLoadRejected &&
                result.wrongLoaderRejected && result.modeAfter==result.mode;
            result.testPassed=mechanism && (result.scenario=="dhe-poison" ? result.faultObserved && result.dheImplementationCalls>0 :
                result.workloadPassed && result.nameIdentityMatches && result.consumerTypeMatches && result.visibleAfterLoad==1);
            if(result.mode==2) result.testPassed &= result.dheImplementationCalls==0;
#if STARTUP_DHE_PROFILE
            if(result.mode==1 && result.scenario!="dhe-poison") result.testPassed &=
                result.changedSelected && !result.unchangedSelected && result.aotEntries>0 && result.interpreterEntries>0 && result.dheImplementationCalls>0;
#endif
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Finish()
        {
            string output=Argument("-startupReport"); Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(result,true)); Debug.Log("AOT_ISOLATION_RESULT "+JsonUtility.ToJson(result));
            Application.Quit(result.testPassed ? 0 : 2);
        }
#if STARTUP_DHE_PROFILE
        static int Select(int mode) { return (int)HybridCLR.RuntimeApi.SelectExecutionMode((HybridCLR.ExecutionMode)mode); }
#endif
        static int VisibleCount() { return AppDomain.CurrentDomain.GetAssemblies().Count(a=>a.GetName().Name=="StartupHotfix"); }
        static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        static string Argument(string name) { var args=Environment.GetCommandLineArgs();int n=Array.IndexOf(args,name);if(n<0||n+1>=args.Length)throw new Exception("Missing "+name);return args[n+1]; }
    }
}
