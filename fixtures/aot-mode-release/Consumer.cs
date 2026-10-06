namespace StartupConsumer
{
    public static class Entry
    {
        public static int Run() { return StartupHotfix.Entry.Changed(5) + new StartupHotfix.Worker().Work(6); }
        public static System.Type WorkerType() { return typeof(StartupHotfix.Worker); }
    }
}
