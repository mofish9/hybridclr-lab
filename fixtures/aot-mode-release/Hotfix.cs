namespace StartupHotfix
{
    public interface IWorker { int Work(int value); }
    public class OtherWorker : IWorker
    {
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public int Work(int value) { return value*5; }
    }
    public class Service : StartupAotSupport.IService { public int Read(int value) { return value+41; } }
    public class Worker : IWorker { public virtual int Work(int value) { return value * 4; } }
    public struct Pair { public int Left; public int Right; }
    public class Marker : System.Attribute { public int Value { get; set; } }
    [Marker(Value=37)] public class Holder<T>
    {
        public int Field;
        public int Property { get; set; }
        public T Value;
        public event System.Action<int> Changed;
        public void Fire(int value) { if (Changed != null) Changed(value); }
    }
    public static class Init { public static readonly int Value; static Init() { Value=31; } }
    public static class Entry
    {
        static int state;
        public static int Changed(int value)
        {
#if CURRENT_PAYLOAD
            return value + 200;
#else
            return value + 100;
#endif
        }
        public static int Unchanged(int value) { return value * 3; }
        public static T Echo<T>(T value) { return value; }
        public static int InterfaceCall(int value) { IWorker worker = new Worker(); return worker.Work(value); }
        public static int DelegateCall(int value) { System.Func<int, int> call = Changed; return call(value); }
        public static int ValueCall(int value) { var pair = new Pair { Left = value, Right = 9 }; return pair.Left + pair.Right; }
        public static int StaticCall(int value) { state = value; return state; }
        public static int FieldReflection() { var x=new Holder<int>(); var f=x.GetType().GetField("Field"); f.SetValue(x,51); return (int)f.GetValue(x); }
        public static int PropertyReflection() { var x=new Holder<int>(); var p=x.GetType().GetProperty("Property"); p.SetValue(x,43,null); return (int)p.GetValue(x,null); }
        public static int Attributes() { return ((Marker)typeof(Holder<int>).GetCustomAttributes(typeof(Marker),false)[0]).Value; }
        public static int EventCall() { var x=new Holder<int>(); int sum=0; System.Action<int> h=v=>sum+=v; x.Changed+=h; x.Fire(8); x.Changed-=h; x.Fire(3); return sum; }
        public static int ArrayCopy() { var a=new Pair[1]; a[0].Left=9; var b=new Pair[1]; System.Array.Copy(a,b,1); object x=b[0]; return ((Pair)x).Left; }
        public static int GenericValue() { var h=new Holder<Pair>{Value=new Pair{Left=13}}; return h.Value.Left; }
        public static int Cctor() { return Init.Value; }
        public static int Finally() { int n=0; try { n=20; throw new System.Exception(); } catch { n+=2; } finally { n+=4; } return n; }
        public static int SupplementalGeneric() { return StartupAotSupport.Generic.Echo(new Pair{Left=47}).Left; }
        public static int AotInterface() { return StartupAotSupport.Generic.Call(new Service(),11); }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static int Leaf(int n) { return n*3+1; }
        public static int BenchNative(int count)
        { int sum=0; for(int i=0;i<count;i++)sum=unchecked(sum+Leaf(i&255)); return sum; }
        public static int BenchChanged(int count)
        {
            int sum=0; for(int i=0;i<count;i++)sum=unchecked(sum+Changed(i&255));
#if CURRENT_PAYLOAD
            return sum+1;
#else
            return sum;
#endif
        }
        public static int BenchVirtual(int count)
        { IWorker[] workers={new Worker(),new OtherWorker()};int sum=0;for(int i=0;i<count;i++)sum=unchecked(sum+workers[i&1].Work(i&255));return sum; }
        public static int ExceptionCall()
        { try { throw new System.InvalidOperationException("expected"); } catch (System.InvalidOperationException) { return 17; } }
#if CURRENT_PAYLOAD
        public static int Added(int value) { return value + 31; }
#endif
    }
#if CURRENT_PAYLOAD
    public class AddedType { public int Value = 27; }
#endif
}
