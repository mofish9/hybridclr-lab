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
        public static int Select(int mode) { throw new NotSupportedException(); }
        public static int GetMode() { throw new NotSupportedException(); }
        public static void Arm(int poison) { throw new NotSupportedException(); }
        public static int Count(int kind) { throw new NotSupportedException(); }
#else
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Select(int mode);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int GetMode();
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern void Arm(int poison);
        [MethodImpl(MethodImplOptions.InternalCall)] public static extern int Count(int kind);
#endif
    }
    public static class PlayerRunner
    {
        [Serializable] public sealed class Result
        {
            public string format="hybridclr.aot-selection-probe.v1", scenario, error, currentSha256, consumerSha256, callback;
            public int pid, nativeModeBefore, mode, modeAfter, selectionResult, repeatSelectionResult, invalidSelectionResult;
            public int visibleBefore, visibleAfterSelect, visibleAfterLoad, caseCount, differential;
            public int reflectionHookCalls, allocationHookCalls, fieldHookCalls, dheLoadCode, aotEntries, interpreterEntries;
            public bool bootstrapExecuted, nameResolvedBefore, nameIdentityMatches, consumerTypeMatches;
            public bool changedSelected, unchangedSelected, workloadPassed, testPassed, preaccessEscaped;
            public bool baseTypeEqualsCurrent, faultObserved;
            public int directBaseResultAfter, currentResultAfter, concurrentSuccesses;
            public int[] actual, expected, concurrentReturns;
        }
        static Result result;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            result = new Result { pid=System.Diagnostics.Process.GetCurrentProcess().Id, callback="BeforeSceneLoad", bootstrapExecuted=true, scenario=Argument("-scenario") };
            try
            {
                string root=Argument("-startupPairRoot");
                result.visibleBefore=VisibleCount();
                result.nameResolvedBefore=Type.GetType("StartupHotfix.Worker, StartupHotfix", false) != null;
                Type premature=null;
#if STARTUP_DHE_PROFILE
                result.nativeModeBefore=Native.GetMode();
                result.invalidSelectionResult=Native.Select(99);
                if(result.scenario=="preaccess-legacy") { premature=ReadGeneratedBaseType(); result.preaccessEscaped=premature != null; }
                int desired=result.scenario=="dhe" ? 1 : 2;
                if(result.scenario=="concurrent")
                {
                    var tasks=Enumerable.Range(0, 12).Select(n=>Task.Run(()=>Native.Select(n%2+1))).ToArray();
                    Task.WaitAll(tasks); result.concurrentReturns=tasks.Select(task=>task.Result).ToArray();
                    result.concurrentSuccesses=result.concurrentReturns.Count(value=>value==0);
                    result.selectionResult=result.concurrentSuccesses==1 ? 0 : -1;
                }
                else result.selectionResult=Native.Select(desired);
                result.mode=Native.GetMode();
                result.repeatSelectionResult=Native.Select(result.mode==1 ? 2 : 1);
#else
                result.mode=2; result.selectionResult=0; result.repeatSelectionResult=1; result.invalidSelectionResult=2;
#endif
                result.visibleAfterSelect=VisibleCount();
                byte[] dll=File.ReadAllBytes(Path.Combine(root,"shared/StartupHotfix.dll"));
                byte[] consumer=File.ReadAllBytes(Path.Combine(root,"shared/StartupConsumer.dll"));
                result.currentSha256=Hash(dll); result.consumerSha256=Hash(consumer);
                Assembly loaded;
#if STARTUP_DHE_PROFILE
                if(result.mode==1)
                {
                    result.dheLoadCode=(int)HybridCLR.RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(dll,
                        File.ReadAllBytes(Path.Combine(root,"shared/base.mv")), File.ReadAllBytes(Path.Combine(root,"shared/current.mv")));
                    if(result.dheLoadCode!=0) throw new Exception("DHE load failed: "+result.dheLoadCode);
                    loaded=AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name=="StartupHotfix");
                    HybridCLR.RuntimeApi.ResetDifferentialDispatchCounters();
                }
                else loaded=Assembly.Load(dll);
#else
                loaded=Assembly.Load(dll);
#endif
                result.visibleAfterLoad=VisibleCount();
                result.nameIdentityMatches=Assembly.Load("StartupHotfix")==loaded && Type.GetType("StartupHotfix.Worker, StartupHotfix",true)==loaded.GetType("StartupHotfix.Worker",true);
                var other=Assembly.Load(consumer).GetType("StartupConsumer.Entry",true);
                result.consumerTypeMatches=(Type)other.GetMethod("WorkerType").Invoke(null,null)==loaded.GetType("StartupHotfix.Worker",true);
#if STARTUP_DHE_PROFILE
                Native.Arm(result.scenario=="legacy-poison" ? 1 : 0);
#endif
                try
                {
                    Type entry=loaded.GetType("StartupHotfix.Entry",true);
                    result.actual=new[] {
                        Invoke(entry,"Changed",5), Invoke(entry,"Unchanged",7), Invoke(entry,"InterfaceCall",6), Invoke(entry,"DelegateCall",8),
                        Invoke(entry,"ValueCall",12), Invoke(entry,"StaticCall",19), Invoke(entry,"ExceptionCall"), Invoke(entry,"Added",3),
                        (int)entry.GetMethod("Echo").MakeGenericMethod(typeof(int)).Invoke(null,new object[]{23}),
                        (int)loaded.GetType("StartupHotfix.AddedType",true).GetField("Value").GetValue(Activator.CreateInstance(loaded.GetType("StartupHotfix.AddedType",true))),
                        (int)other.GetMethod("Run").Invoke(null,null)
                    };
                    result.expected=new[]{205,21,24,208,21,19,17,34,23,27,229};
                    result.caseCount=result.actual.Length; result.differential=result.actual.Where((value,n)=>value!=result.expected[n]).Count();
                    result.workloadPassed=result.differential==0;
#if STARTUP_DHE_PROFILE
                    if(result.mode==1) {
                        result.changedSelected=HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Changed"));
                        result.unchangedSelected=HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Unchanged"));
                        result.aotEntries=HybridCLR.RuntimeApi.GetDifferentialAotEntryCount();
                        result.interpreterEntries=HybridCLR.RuntimeApi.GetDifferentialInterpreterEntryCount();
                    }
                    if(premature!=null) {
                        result.baseTypeEqualsCurrent=premature==loaded.GetType("StartupHotfix.Worker",true);
                        result.directBaseResultAfter=ReadGeneratedBaseMethod(); result.currentResultAfter=Invoke(entry,"Changed",5);
                    }
#endif
                }
                catch(Exception error) { result.error=error.ToString(); result.faultObserved=result.error.Contains("LAB_DHE_HOOK_FAULT"); }
                finally {
#if STARTUP_DHE_PROFILE
                    result.reflectionHookCalls=Native.Count(0); result.allocationHookCalls=Native.Count(1); result.fieldHookCalls=Native.Count(2);
                    Native.Arm(-1); result.modeAfter=Native.GetMode();
#else
                    result.modeAfter=result.mode;
#endif
                }
                bool mechanism=result.visibleBefore==0 && !result.nameResolvedBefore && result.selectionResult==0 && result.repeatSelectionResult==1 && result.invalidSelectionResult==2 &&
                    result.mode==result.modeAfter && result.nameIdentityMatches && result.consumerTypeMatches && result.visibleAfterLoad==1;
                result.testPassed=mechanism && (result.scenario=="legacy-poison" ? result.faultObserved : result.workloadPassed);
                if(result.scenario=="preaccess-legacy") result.testPassed &= result.preaccessEscaped && !result.baseTypeEqualsCurrent && result.directBaseResultAfter==105 && result.currentResultAfter==205;
                if(result.mode==1) result.testPassed &= result.changedSelected && !result.unchangedSelected && result.aotEntries>0 && result.interpreterEntries>0;
            }
            catch(Exception error) { result.error=error.ToString(); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Finish()
        {
            string output=Argument("-startupReport"); Directory.CreateDirectory(Path.GetDirectoryName(output));
            File.WriteAllText(output,JsonUtility.ToJson(result,true)); Debug.Log("AOT_SELECTION_RESULT "+JsonUtility.ToJson(result));
            Application.Quit(result.testPassed ? 0 : 2);
        }
#if STARTUP_DHE_PROFILE
        [MethodImpl(MethodImplOptions.NoInlining)] static Type ReadGeneratedBaseType() { return typeof(StartupHotfix.Worker); }
        [MethodImpl(MethodImplOptions.NoInlining)] static int ReadGeneratedBaseMethod() { return StartupHotfix.Entry.Changed(5); }
#endif
        static int VisibleCount() { return AppDomain.CurrentDomain.GetAssemblies().Count(a=>a.GetName().Name=="StartupHotfix"); }
        static int Invoke(Type type,string name,params object[] args) { return (int)type.GetMethod(name).Invoke(null,args); }
        static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        static string Argument(string name) { var args=Environment.GetCommandLineArgs();int n=Array.IndexOf(args,name);if(n<0 || n+1>=args.Length)throw new Exception("Missing "+name);return args[n+1]; }
    }
}
