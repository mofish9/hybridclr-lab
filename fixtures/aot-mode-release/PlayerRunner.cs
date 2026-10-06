using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace AotModeRelease
{
    public static class PlayerRunner
    {
        [Serializable] public sealed class Sample { public string name; public int checksum, iterations; public double[] milliseconds; }
        [Serializable] public sealed class Result
        {
            public string format="hybridclr.aot-mode.release.v1", scenario, error, currentSha256, consumerSha256, unitySha256, supportSha256;
            public int pid, mode, before, after, selected, repeated, invalid, concurrentSuccesses, supplemental, duplicateSupplemental;
            public int caseCount, differential, unityResult, visibleBefore, visibleAfter, rejectedLoadAttempts, bundleResult;
            public uint nativeTypeLookup;
            public bool passed, diagnostics, prematureRejected, wrongLoaderRejected, identityMatches, changed, unchanged;
            public int[] actual, concurrentResults;
            public double loadMilliseconds, firstEntryMilliseconds, selectionMilliseconds, selectionToEntryMilliseconds, processToEntryMilliseconds;
            public long privateBytesBefore, privateBytesAfter;
            public Sample[] samples;
            public string[] regressionRecords;
        }
        static Result result;
        static AssetBundle sceneBundle;
        [DllImport("AotImageProbe")]
        static extern uint CheckCurrentTypes(uint phase);
        // Windows Player fixture only; the library has no platform persistence or launcher code.
        [StructLayout(LayoutKind.Sequential)]
        struct MemoryCounters
        {
            public uint size, pageFaults;
            public UIntPtr peakWorkingSet, workingSet, peakPaged, paged, peakNonPaged, nonPaged, pagefile, peakPagefile, privateBytes;
        }
        [DllImport("psapi.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetProcessMemoryInfo(IntPtr process, ref MemoryCounters counters, uint size);
        [DllImport("kernel32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);
        static double ProcessElapsedMilliseconds()
        {
            if(!GetProcessTimes(new IntPtr(-1),out long creation,out _,out _,out _))
                throw new Exception("Windows process creation time query failed");
            return (DateTime.UtcNow.ToFileTimeUtc()-creation)/10000.0;
        }
        static long PrivateBytes()
        {
            var counters=new MemoryCounters {size=(uint)Marshal.SizeOf(typeof(MemoryCounters))};
            if(!GetProcessMemoryInfo(new IntPtr(-1),ref counters,counters.size))throw new Exception("Windows process memory query failed");
            return checked((long)counters.privateBytes.ToUInt64());
        }
        // Keep the same AOT harness roots on both DHE Players. Only the launch
        // argument decides whether the candidate-only native API is called.
        static bool IsCandidate => Argument("-selectMode","false")=="true";

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
                if(correctness)CheckCurrentTypes(0); // Cache Unity's public handles before selection.
                result.visibleBefore=Visible();
                var startupTimer=Stopwatch.StartNew();
                if(IsCandidate) {
                result.before=QueryMode();
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
                result.mode=QueryMode();
                } else {
#if STARTUP_DHE_PROFILE
                result.mode=1;
#else
                result.mode=2;
#endif
                }
                result.selectionMilliseconds=startupTimer.Elapsed.TotalMilliseconds;
                // Exceeds the finite metadata-image index pool. Rejected loads
                // must leave it available for the legitimate loads below.
                if (IsCandidate && correctness && result.mode == 1)
                    for (int i = 0; i < 512; ++i)
                    {
                        try { Assembly.Load(dll); }
                        catch (InvalidOperationException) { ++result.rejectedLoadAttempts; }
                    }
                result.privateBytesBefore=PrivateBytes();
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
                result.selectionToEntryMilliseconds=startupTimer.Elapsed.TotalMilliseconds;
                // Windows fixture: include native initialization before the AOT callback.
                result.processToEntryMilliseconds=ProcessElapsedMilliseconds();
                result.privateBytesAfter=PrivateBytes();
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
                    result.nativeTypeLookup=CheckCurrentTypes(1);
                    if(result.nativeTypeLookup!=63)throw new Exception("Native Current type lookup/image identity failed: "+result.nativeTypeLookup);
                    result.duplicateSupplemental=(int)HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(support,HybridCLR.HomologousImageMode.SuperSet);
#if STARTUP_DHE_PROFILE
                    if(IsCandidate) {
                    result.repeated=Select(result.mode==1?2:1);
                    try {
                        if(result.mode==1)Assembly.Load(dll);
                        else LoadDhe(root,"StartupHotfix",dll);
                    } catch(InvalidOperationException) { result.wrongLoaderRejected=true; }
                    }
#endif
                }
                result.after=IsCandidate?QueryMode():result.mode;
                if(result.scenario=="benchmark")result.samples=Measure(loaded,int.Parse(Argument("-iterations","200000")));
                result.passed=result.differential==0 && result.caseCount==Workload.Expected.Length && result.unityResult==236 &&
                    result.identityMatches && result.visibleAfter==2 && result.mode==result.after && !result.diagnostics;
                if(result.mode==1)result.passed &= result.changed && !result.unchanged;
                if(IsCandidate)result.passed &= result.before==0 && result.selected==0 && result.visibleBefore==0 &&
                    (!correctness || (result.invalid==2 && result.repeated==1 && result.prematureRejected && result.wrongLoaderRejected));
                if(IsCandidate && correctness && result.mode==1)result.passed &= result.rejectedLoadAttempts==512;
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
        static int Select(int mode) {
#if STARTUP_DHE_PROFILE
            return (int)HybridCLR.RuntimeApi.SelectExecutionMode((HybridCLR.ExecutionMode)mode);
#else
            throw new NotSupportedException();
#endif
        }
        static int QueryMode() {
#if STARTUP_DHE_PROFILE
            return (int)HybridCLR.RuntimeApi.GetExecutionMode();
#else
            return 2;
#endif
        }
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
            if(result.passed && result.scenario!="benchmark") {
                try { RunRegressionIfRequested(); BeginBundleCheck(); return; }
                catch(Exception error) { result.error=error.ToString(); result.passed=false; }
            }
            WriteResult();
        }
        static void RunRegressionIfRequested() {
            string root=Argument("-regressionRoot","");
            if(root.Length==0)return;
            if(Argument("-regressionMetadata","none")=="superset")
                foreach(string name in new[]{"mscorlib","System","System.Core","HybridCLR.BoundaryContracts"}) {
                    var bytes=File.ReadAllBytes(Path.Combine(root,"aot",name+".dll"));
                    int code=(int)HybridCLR.RuntimeApi.LoadMetadataForAOTAssembly(bytes,HybridCLR.HomologousImageMode.SuperSet);
                    if(code!=0)throw new Exception("Regression metadata failed: "+name+" "+code);
                }
            Assembly.Load(File.ReadAllBytes(Path.Combine(root,"HybridCLR.ManagedCases.dll")));
            var driver=Assembly.Load(File.ReadAllBytes(Path.Combine(root,"AotModeRegression.dll")));
            result.regressionRecords=(string[])driver.GetType("AotModeRegression",true).GetMethod("Run").Invoke(null,new object[]{root,Argument("-startupReport")+".progress"});
        }
        static void WriteResult() {
            var path=Argument("-startupReport");Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,JsonUtility.ToJson(result,true));UnityEngine.Debug.Log("AOT_MODE_RESULT "+JsonUtility.ToJson(result));Application.Quit(result.passed?0:2);
        }
        static void BeginBundleCheck() {
            string root=Argument("-bundleRoot");
            var assembly=Assembly.Load("StartupUnityHotfix");
            var workerType=assembly.GetType("StartupUnityHotfix.Worker",true);
            var assets=AssetBundle.LoadFromFile(Path.Combine(root,"hotfix-assets"));
            if(assets==null)throw new Exception("Unable to load hotfix assets bundle");
            var prefab=assets.LoadAsset<GameObject>("Assets/HotfixPrefab.prefab");
            var instance=UnityEngine.Object.Instantiate(prefab);
            var worker=instance.GetComponent(workerType);
            var data=assets.LoadAsset("Assets/HotfixData.asset");
            if(worker==null || worker.GetType()!=workerType || data.GetType()!=assembly.GetType("StartupUnityHotfix.Data",true))
                throw new Exception("Bundle script identity mismatch");
            result.bundleResult=(int)workerType.GetField("Value").GetValue(worker)+(int)data.GetType().GetField("Value").GetValue(data);
            var addedType=assembly.GetType("StartupUnityHotfix.AddedWorker",true);
            var added=instance.GetComponent(addedType);
            var addedData=assets.LoadAsset("Assets/AddedData.asset");
            if(added==null || added.GetType()!=addedType || addedData==null || addedData.GetType()!=assembly.GetType("StartupUnityHotfix.AddedData",true))
                throw new Exception("Current-only bundle script identity mismatch");
            result.bundleResult+=(int)addedType.GetField("Value").GetValue(added)+(int)addedData.GetType().GetField("Value").GetValue(addedData);
            sceneBundle=AssetBundle.LoadFromFile(Path.Combine(root,"hotfix-scene"));
            if(sceneBundle==null)throw new Exception("Unable to load hotfix scene bundle");
            UnityEngine.Object.Destroy(instance); assets.Unload(false);
            SceneManager.sceneLoaded+=OnBundleSceneLoaded;
            SceneManager.LoadScene("Assets/HotfixBundle.unity",LoadSceneMode.Additive);
        }
        static void OnBundleSceneLoaded(Scene scene,LoadSceneMode mode) {
            if(scene.path!="Assets/HotfixBundle.unity")return;
            SceneManager.sceneLoaded-=OnBundleSceneLoaded;
            try {
            var workerType=Assembly.Load("StartupUnityHotfix").GetType("StartupUnityHotfix.Worker",true);
            var sceneWorker=scene.GetRootGameObjects().Single().GetComponent(workerType);
            if(sceneWorker==null || sceneWorker.GetType()!=workerType)throw new Exception("Bundle scene script identity mismatch");
            result.bundleResult+=(int)workerType.GetField("Value").GetValue(sceneWorker);
            var addedType=Assembly.Load("StartupUnityHotfix").GetType("StartupUnityHotfix.AddedWorker",true);
            var added=scene.GetRootGameObjects().Single().GetComponent(addedType);
            if(added==null || added.GetType()!=addedType)throw new Exception("Current-only scene script identity mismatch");
            result.bundleResult+=(int)addedType.GetField("Value").GetValue(added);
            result.passed &= result.bundleResult==1298;
            sceneBundle.Unload(false);
            } catch(Exception error) { result.error=error.ToString(); result.passed=false; }
            WriteResult();
        }
        static int Visible() { return AppDomain.CurrentDomain.GetAssemblies().Count(a=>a.GetName().Name=="StartupHotfix" || a.GetName().Name=="StartupUnityHotfix"); }
        static string Hash(byte[] bytes) { using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant(); }
        static string Argument(string name,string fallback=null) { var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);if(index>=0 && index+1<args.Length)return args[index+1];if(fallback!=null)return fallback;throw new Exception("Missing "+name); }
    }
}
