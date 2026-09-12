namespace UnityEngine
{
    public enum RuntimePlatform { WindowsEditor, OSXEditor }
    public static class Application { public static RuntimePlatform platform = RuntimePlatform.WindowsEditor; }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json) => System.Text.Json.JsonSerializer.Deserialize<T>(json,
            new System.Text.Json.JsonSerializerOptions { IncludeFields = true });
    }
    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine(message);
        public static void LogWarning(object message) => Console.WriteLine(message);
    }
}
namespace UnityEditor
{
    public static class EditorApplication { public static string applicationContentsPath; }
    public sealed class MenuItem : Attribute { public MenuItem(string path) { } }
}
namespace UnityEditor.PackageManager
{
    public sealed class PackageInfo
    {
        public static string TestRoot;
        public string resolvedPath;
        public static PackageInfo FindForAssembly(System.Reflection.Assembly _) => new PackageInfo { resolvedPath = TestRoot };
    }
}
namespace UnityEditor.Build
{
    public sealed class BuildFailedException : Exception { public BuildFailedException(string message) : base(message) { } }
}
namespace HybridCLR.Editor
{
    public static class SettingsUtil
    {
        public static string ProjectDir;
        public static string PackagePathInProject => "Packages/com.code-philosophy.hybridclr@8.13.0";
    }
}
