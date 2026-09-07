using System.Text.Json;

namespace UnityEngine
{
    public static class JsonUtility
    {
        public static string ToJson(object value, bool prettyPrint = false) =>
            JsonSerializer.Serialize(value, new JsonSerializerOptions { IncludeFields = true, WriteIndented = prettyPrint });
        public static T FromJson<T>(string value) =>
            JsonSerializer.Deserialize<T>(value, new JsonSerializerOptions { IncludeFields = true });
    }
}
