namespace StartupAotSupport
{
    public interface IService { int Read(int value); }
    public static class Generic
    {
        public static T Echo<T>(T value) { return value; }
        public static int Call(IService service, int value) { return service.Read(value); }
    }
}
