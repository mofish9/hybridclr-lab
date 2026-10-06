namespace StartupHotfix
{
    public interface IWorker { int Work(int value); }
    public class Worker : IWorker { public virtual int Work(int value) { return value * 4; } }
    public struct Pair { public int Left; public int Right; }
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
