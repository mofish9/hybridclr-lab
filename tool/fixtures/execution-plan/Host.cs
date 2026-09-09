using System.Reflection;
using System.Text.Json;

// Host adapters exercise the actual package validator/loader. They do not
// substitute for the separate IL2CPP Player tests of native execution.
namespace UnityEngine
{
    public static class Debug { public static void Log(object value) { } }
    public static class JsonUtility
    {
        public static T FromJson<T>(string json)
        {
            using var document = JsonDocument.Parse(json);
            return (T)Read(document.RootElement, typeof(T));
        }
        private static object Read(JsonElement json, Type type)
        {
            if (json.ValueKind == JsonValueKind.Null) return null;
            if (type == typeof(string)) return json.GetString();
            if (type.IsPrimitive) return JsonSerializer.Deserialize(json.GetRawText(), type);
            if (type.IsArray)
            {
                Type elementType = type.GetElementType();
                Array array = Array.CreateInstance(elementType, json.GetArrayLength());
                int index = 0;
                foreach (JsonElement value in json.EnumerateArray()) array.SetValue(Read(value, elementType), index++);
                return array;
            }
            object result = Activator.CreateInstance(type, nonPublic: true);
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                if (!field.IsNotSerialized && json.TryGetProperty(field.Name, out JsonElement value))
                    field.SetValue(result, Read(value, field.FieldType));
            return result;
        }
    }
}

namespace HybridCLR
{
    public static class RuntimeApi
    {
        public static int Calls;
        public static uint[][] LastTypes, LastMethods;
        public static LoadImageErrorCode LoadMetadataForAOTAssembly(byte[] bytes, HomologousImageMode mode)
        { Calls++; return LoadImageErrorCode.OK; }
        public static LoadImageErrorCode LoadDifferentialHybridAssembliesWithMetaVersion(byte[][] dlls, byte[][] before, byte[][] after)
        { Calls++; LastTypes = LastMethods = null; return LoadImageErrorCode.OK; }
        public static LoadImageErrorCode LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlan(
            byte[][] dlls, byte[][] before, byte[][] after, uint[][] types, uint[][] methods)
        { Calls++; LastTypes = types; LastMethods = methods; return LoadImageErrorCode.OK; }
        public static LoadImageErrorCode LoadDifferentialHybridAssembliesWithMetaVersionAndExecutionPlanAndSources(
            byte[][] dlls, byte[][] before, byte[][] after, uint[][] types, uint[][] methods,
            int[] sourceKinds, uint[][] excluded)
        { Calls++; LastTypes = types; LastMethods = methods; return LoadImageErrorCode.OK; }
    }
}
