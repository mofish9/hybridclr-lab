using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;

namespace AotModeRelease
{
    public static class PlayerRunner
    {
        [Serializable] public sealed class Sample { public string name; public int checksum, iterations; public double[] milliseconds; }
        [Serializable] public sealed class Result
        {
            public string format="hybridclr.aot-mode.release.v1", scenario, error, currentSha256, consumerSha256, unitySha256, supportSha256;
            public int pid, mode, before, after, selected, repeated, invalid, concurrentSuccesses, supplemental, duplicateSupplemental;
            public int caseCount, differential, unityResult, visibleBefore, visibleAfter;
            public bool passed, diagnostics, prematureRejected, wrongLoaderRejected, identityMatches, changed, unchanged;
            public int[] actual, concurrentResults;
            public double loadMilliseconds, firstEntryMilliseconds;
            public long privateBytesBefore, privateBytesAfter;
            public Sample[] samples;
        }
        static Result result;
        static bool IsCandidate
        {
            get {
#if STARTUP_MODE_SELECTION
                return true;
#else
                return false;
#endif
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Bootstrap()
        {
            result=new Result {pid=Process.GetCurrentProcess().Id,scenario=Argument("-scenario","correctness")};
            try
            {
                string root=Argument("-startupPairRoot");
                byte[] dll=File.ReadAllBytes(Path.Combine(root,"shared/StartupHotfix.dll"));
                byte[] other=File.ReadAllBytes(Path.Combine(root,"shared/StartupConsumer.dll"));
                byte[] unity=File.ReadAllBytes(Path.Combine(root,"shared/StartupUnityHotfix.dll"));
                byte[] support=File.ReadAllBytes(Path.Combine(root,"shared/StartupAotSupport.dll"));
                result.currentSha256=Hash(dll);result.consumerSha256=Hash(other);result.unitySha256=Hash(unity);result.supportSha256=Hash(support);
                bool correctness=result.scenario!="benchmark";
                result.visibleBefore=Visible();
#if STARTUP_MODE_SELECTION
                result.before=(int)HybridCLR.RuntimeApi.GetExecutionMode();
                if(correctness) {
                    result.invalid=Select(99);
                    try { Assembly.Load(dll); } catch(InvalidOperationException) { result.prematureRejected=true; }
                }
                int desired=Argument("-mode","dhe")=="legacy" ? 2 : 1;
                if(result.scenario=="concurrent") {
                    var tasks=Enumerable.Range(0,12).Select(i=>Task.Run(()=>Select(i%2+1))).ToArray();Task.WaitAll(tasks);
                    result.concurrentResults=tasks.Select(t=>t.Result).ToArray();result.concurrentSuccesses=result.concurrentResults.Count(n=>n==0);
                    result.selected=result.concurrentSuccesses==1 ? 0 : -1;
                } else result.selected=Select(desired);
                result.mode=(int)HybridCLR.RuntimeApi.GetExecutionMode();
#elif STARTUP_DHE_PROFILE
                result.mode=1;
#else
                result.mode=2;
#endif
                result.privateBytesBefore=Process.GetCurrentProcess().PrivateMemorySize64;
                var timer=Stopwatch.StartNew();
                result.supplemental=(int)HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(support,HybridCLR.HomologousImageMode.SuperSet);
                if(result.supplemental!=0)throw new Exception("Supplemental metadata failed: "+result.supplemental);
                var loaded=Load(root,"StartupHotfix",dll);
                var unityLoaded=Load(root,"StartupUnityHotfix",unity);
                var consumer=Assembly.Load(other);
                result.loadMilliseconds=timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                result.actual=Workload.Run(loaded,consumer);result.caseCount=result.actual.Length;
                result.differential=result.actual.Where((value,i)=>value!=Workload.Expected[i]).Count();
                result.unityResult=(int)unityLoaded.GetType("StartupUnityHotfix.Entry",true).GetMethod("Run").Invoke(null,null);
                result.firstEntryMilliseconds=timer.Elapsed.TotalMilliseconds;
                result.privateBytesAfter=Process.GetCurrentProcess().PrivateMemorySize64;
                result.visibleAfter=Visible();
                result.identityMatches=Assembly.Load("StartupHotfix")==loaded &&
                    (Type)consumer.GetType("StartupConsumer.Entry",true).GetMethod("WorkerType").Invoke(null,null)==loaded.GetType("StartupHotfix.Worker",true);
#if STARTUP_DHE_PROFILE
                result.diagnostics=HybridCLR.RuntimeApi.AreDifferentialDispatchDiagnosticsEnabled();
                if(result.mode==1) {
                    var entry=loaded.GetType("StartupHotfix.Entry",true);
                    result.changed=HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Changed"));
                    result.unchanged=HybridCLR.RuntimeApi.IsDifferentialMethodChanged(entry.GetMethod("Unchanged"));
                }
#endif
                if(correctness) {
                    result.duplicateSupplemental=(int)HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(support,HybridCLR.HomologousImageMode.SuperSet);
#if STARTUP_MODE_SELECTION
                    result.repeated=Select(result.mode==1?2:1);
                    try {
                        if(result.mode==1)Assembly.Load(dll);
                        else LoadDhe(root,"StartupHotfix",dll);
                    } catch(InvalidOperationException) { result.wrongLoaderRejected=true; }
#endif
                }
#if STARTUP_MODE_SELECTION
                result.after=(int)HybridCLR.RuntimeApi.GetExecutionMode();
#else
                result.after=result.mode;
#endif
                if(result.scenario=="benchmark")result.samples=Measure(loaded,int.Parse(Argument("-iterations","200000")));
                result.passed=result.differential==0 && result.caseCount==Workload.Expected.Length && result.unityResult==236 &&
                    result.identityMatches && result.visibleAfter==2 && result.mode==result.after && !result.diagnostics;
                if(result.mode==1)result.passed &= result.changed && !result.unchanged;
                if(IsCandidate)result.passed &= result.before==0 && result.selected==0 && result.visibleBefore==0 &&
                    (!correctness || (result.invalid==2 && result.repeated==1 && result.prematureRejected && result.wrongLoaderRejected));
                if(correctness)result.passed &= result.duplicateSupplemental!=0;
            }
            catch(Exception error) { result.error=error.ToString();result.passed=false; }
        }

        static Assembly Load(string root,string name,byte[] bytes)
        {
#if STARTUP_DHE_PROFILE
            if(result.mode==1) {
                int code=LoadDhe(root,name,bytes);if(code!=0)throw new Exception("DHE load failed: "+name+" "+code);
                return AppDomain.CurrentDomain.GetAssemblies().Single(a=>a.GetName().Name==name);
            }
#endif
            return Assembly.Load(bytes);
        }
#if STARTUP_DHE_PROFILE
        static int LoadDhe(string root,string name,byte[] bytes) {
            return (int)HybridCLR.RuntimeApi.LoadDifferentialHybridAssemblyWithMetaVersion(bytes,
                File.ReadAllBytes(Path.Combine(root,"shared/"+name+".base.mv")),File.ReadAllBytes(Path.Combine(root,"shared/"+name+".current.mv")));
        }
#endif
#if STARTUP_MODE_SELECTION
        static int Select(int mode) { return (int)HybridCLR.RuntimeApi.SelectExecutionMode((HybridCLR.ExecutionMode)mode); }
#endif
        static Sample[] Measure(Assembly loaded,int iterations)
        {
            return new[]{"BenchNative","BenchChanged","BenchVirtual"}.Select(name=> {
                var method=loaded.GetType("StartupHotfix.Entry",true).GetMethod(name);
                var call=(Func<int,int>)Delegate.CreateDelegate(typeof(Func<int,int>),method);
                call(2048); call(2048);
                var sample=new Sample{name=name,iterations=iterations,milliseconds=new double[9]};
                for(int n=0;n<sample.milliseconds.Length;n++) {
                    var watch=Stopwatch.StartNew();int checksum=call(iterations);watch.Stop();
                    if(n>0 && checksum!=sample.checksum)throw new Exception("Unstable checksum "+name);
                    sample.checksum=checksum;sample.milliseconds[n]=watch.Elapsed.TotalMilliseconds;
                }
                return sample;
            }).ToArray();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Finish() {
            var path=Argument("-startupReport");Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(result,true));UnityEngine.Debug.Log("AOT_MODE_RESULT "+JsonUtility.ToJson(result));Application.Quit(result.passed?0:2);
        }
        static int Visible() { return AppDomain.CurrentDomain.GetAssemblies().Count(a=>a.GetName().Name=="StartupHotfix" || a.GetName().Name=="StartupUnityHotfix"); }
        static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        static string Argument(string name,string fallback=null) { var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);if(index>=0 && index+1<args.Length)return args[index+1];if(fallback!=null)return fallback;throw new Exception("Missing "+name); }
    }
}
