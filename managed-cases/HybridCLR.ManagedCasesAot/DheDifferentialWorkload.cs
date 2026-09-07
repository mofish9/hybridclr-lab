#if DHE_EVOLUTION_CURRENT
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace HybridCLR.Lab.ManagedCasesAot
{
    internal static class DheDifferentialWorkload
    {
        internal const string ResultVariable = "HYBRIDCLR_DHE_DIFFERENTIAL_RESULT";
        internal const string EntriesVariable = "HYBRIDCLR_DHE_CASE_ENTRIES";
        private static bool _executed;

        public static void RunIfRequested()
        {
            string path = Environment.GetEnvironmentVariable(ResultVariable);
            if (string.IsNullOrEmpty(path) || _executed) return;
            _executed = true;
            Run(path);
        }

        public static void Run(string path)
        {
            Assembly cases = AppDomain.CurrentDomain.GetAssemblies().Single(assembly =>
                assembly.GetName().Name == "HybridCLR.ManagedCases");
            Type registry = cases.GetType("HybridCLR.Lab.ManagedCases.CaseRegistry", true);
            var definitions = ((IEnumerable)registry.GetProperty("All").GetValue(null, null))
                .Cast<object>().OrderBy(value => (string)Property(value, "Layer"), StringComparer.Ordinal)
                .ThenBy(value => (string)Property(value, "Category"), StringComparer.Ordinal)
                .ThenBy(value => (string)Property(value, "Id"), StringComparer.Ordinal).ToArray();
            Type api = AppDomain.CurrentDomain.GetAssemblies().Select(assembly =>
                assembly.GetType("HybridCLR.RuntimeApi", false)).FirstOrDefault(type => type != null);
            MethodInfo changed = api == null ? null : api.GetMethod("IsDifferentialMethodChanged");
            MethodInfo interpreterCount = api == null ? null : api.GetMethod("GetDifferentialInterpreterEntryCount");
            MethodInfo aotCount = api == null ? null : api.GetMethod("GetDifferentialAotEntryCount");
            if (api != null && (changed == null || interpreterCount == null || aotCount == null))
                throw new MissingMethodException("DHE diagnostic API is incomplete.");

            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false)))
            {
                writer.Write(Encoding.ASCII.GetBytes("DHESUITE"));
                writer.Write(1);
                writer.Write(cases.GetName().Name);
                writer.Write(api != null);
                writer.Write(definitions.Length);
                foreach (object definition in definitions)
                {
                    Delegate callback = (Delegate)Property(definition, "Execute");
                    MethodInfo method = callback.Method;
                    writer.Write((string)Property(definition, "Id"));
                    writer.Write((string)Property(definition, "Category"));
                    writer.Write((string)Property(definition, "Layer"));
                    string[] features = ((IEnumerable)Property(definition, "Features")).Cast<string>().ToArray();
                    writer.Write(features.Length);
                    foreach (string feature in features) writer.Write(feature);
                    writer.Write(method.DeclaringType.FullName);
                    writer.Write(method.Name);
                    writer.Write(method.MetadataToken);
                    writer.Write(changed != null && (bool)changed.Invoke(null, new object[] { method }));
                    writer.Flush();

                    int beforeInterpreter = Counter(interpreterCount);
                    int beforeAot = Counter(aotCount);
                    object observation = null;
                    string exceptionType = null;
                    try { observation = callback.DynamicInvoke(); }
                    catch (TargetInvocationException exception)
                    {
                        if (exception.InnerException == null) throw;
                        exceptionType = exception.InnerException.GetType().FullName;
                    }
                    WriteNullable(writer, observation == null ? null : (string)Property(observation, "ReturnValue"));
                    WriteNullable(writer, observation == null ? null : (string)Property(observation, "SideEffect"));
                    WriteNullable(writer, exceptionType);
                    writer.Write(Counter(interpreterCount) - beforeInterpreter);
                    writer.Write(Counter(aotCount) - beforeAot);
                    writer.Flush();
                }
                writer.Write(0x454e4f44);
            }
        }

        private static object Property(object value, string name) =>
            value.GetType().GetProperty(name).GetValue(value, null);

        private static int Counter(MethodInfo method) =>
            method == null ? 0 : (int)method.Invoke(null, null);

        private static void WriteNullable(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null) writer.Write(value);
        }
    }
}
#endif
