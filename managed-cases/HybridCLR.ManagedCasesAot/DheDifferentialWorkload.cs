#if DHE_EVOLUTION_CURRENT
#nullable disable
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
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
            WriteLayoutDiagnostics(cases, path + ".layout.log");

            using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
            using (var writer = new BinaryWriter(stream, new UTF8Encoding(false)))
            using (var diagnostics = new StreamWriter(new FileStream(path + ".exceptions.log",
                FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
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
                    Exception observedException = null;
                    try { observation = callback.DynamicInvoke(); }
                    catch (Exception exception)
                    {
                        Exception original = exception is TargetInvocationException invocation && invocation.InnerException != null
                            ? invocation.InnerException : exception;
                        exceptionType = original.GetType().FullName;
                        observedException = exception;
                    }
                    WriteNullable(writer, observation == null ? null : (string)Property(observation, "ReturnValue"));
                    WriteNullable(writer, observation == null ? null : (string)Property(observation, "SideEffect"));
                    WriteNullable(writer, exceptionType);
                    writer.Write(Counter(interpreterCount) - beforeInterpreter);
                    writer.Write(Counter(aotCount) - beforeAot);
                    writer.Flush();
                    if (observedException != null)
                    {
                        diagnostics.WriteLine((string)Property(definition, "Id"));
                        diagnostics.WriteLine(observedException.ToString());
                        diagnostics.Flush();
                    }
                }
                writer.Write(0x454e4f44);
            }
        }

        private static void WriteLayoutDiagnostics(Assembly cases, string path)
        {
            using (var writer = new StreamWriter(new FileStream(path, FileMode.CreateNew,
                FileAccess.Write, FileShare.Read), new UTF8Encoding(false)))
            {
                try
                {
                    Type definition = cases.GetType("HybridCLR.Lab.ManagedCases.CaseRegistry+Pair`1", true);
                    Type pair = definition.MakeGenericType(typeof(int));
                    Type nested = definition.MakeGenericType(pair);
                    foreach (Type type in new[] { pair, nested })
                    {
                        writer.WriteLine(type.FullName);
                        foreach (FieldInfo field in type.GetFields())
                        {
                            MethodInfo offset = field.GetType().GetMethod("GetFieldOffset",
                                BindingFlags.Instance | BindingFlags.NonPublic);
                            writer.WriteLine(field.Name + " type=" + field.FieldType.FullName +
                                (offset == null ? " marshaledOffset=" + Marshal.OffsetOf(type, field.Name).ToInt64()
                                    : " managedOffset=" + offset.Invoke(field, null)));
                        }
                        try { writer.WriteLine("marshaledSize=" + Marshal.SizeOf(Activator.CreateInstance(type))); }
                        catch (ArgumentException exception) { writer.WriteLine("marshaledSizeUnavailable=" + exception.Message); }
                    }
                    object first = Activator.CreateInstance(pair, new object[] { 2, 3 });
                    object second = Activator.CreateInstance(pair, new object[] { 5, 7 });
                    object outer = Activator.CreateInstance(nested, new[] { first, second });
                    foreach (FieldInfo outerField in nested.GetFields())
                        foreach (FieldInfo innerField in pair.GetFields())
                            writer.WriteLine(outerField.Name + "." + innerField.Name + "=" +
                                innerField.GetValue(outerField.GetValue(outer)));
                }
                catch (Exception exception)
                {
                    writer.WriteLine(exception.ToString());
                }
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
