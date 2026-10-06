using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HybridCLR.Lab.ManagedCases;

public static class AotModeRegression
{
    public static string[] Run(string root, string progress)
    {
        CaseRegistry.RuntimeTarget = "StandaloneWindows64";
        if (Array.IndexOf(Environment.GetCommandLineArgs(), "-verifyFixedMode") >= 0)
        {
            var api = Type.GetType("HybridCLR.RuntimeApi, HybridCLR.Runtime", true);
            var select = api.GetMethod("SelectExecutionMode");
            var mode = Enum.ToObject(select.GetParameters()[0].ParameterType, 2);
            if (Convert.ToInt32(select.Invoke(null, new[] { mode })) != 3 ||
                Convert.ToInt32(api.GetMethod("GetExecutionMode").Invoke(null, null)) != 1)
                throw new Exception("Disabled selection must report NotSupported and remain DHE");
        }
        var records = new List<string>();
        File.WriteAllText(progress, "");
        var assembly = Assembly.Load(File.ReadAllBytes(Path.Combine(root, "HybridCLR.CrossAssemblyDerived.dll")));
        typeof(CaseRegistry).Assembly.GetTypes();
        var cross = assembly.GetType("HybridCLR.Lab.CrossAssemblyDerived.CrossAssemblyLazyVTableProbe", true);
        string crossResult = (string)cross.GetMethod("Run").Invoke(null, null);
        if (crossResult != "derived:26:34") throw new Exception("Cross-assembly VTable: " + crossResult);
        foreach (var definition in CaseRegistry.All)
        {
            File.AppendAllText(progress, definition.Id + "\n");
            // Match the established Player suite's worker execution, avoiding
            // Unity's main-thread synchronization context for async cases.
            var task = Task.Run(() => Execute(definition, progress));
            if (!task.Wait(10000)) throw new TimeoutException("Case timeout: " + definition.Id);
            records.Add(task.GetAwaiter().GetResult());
        }
        return records.ToArray();
    }
    static string Encode(string? value) => value == null ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    static string Execute(CaseDefinition definition, string progress)
    {
        string? value = null, side = null, error = null;
        try { var observation = definition.Execute(); value = observation.ReturnValue; side = observation.SideEffect; }
        catch (Exception exception) {
            error = exception.GetType().FullName;
            File.AppendAllText(progress, "EXCEPTION " + definition.Id + " " + exception.Message + "\n");
        }
        return definition.Id + "\t" + Encode(value) + "\t" + Encode(side) + "\t" + Encode(error);
    }
}
