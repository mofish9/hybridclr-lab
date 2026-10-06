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
            var task = Task.Run(() => Execute(definition));
            if (!task.Wait(10000)) throw new TimeoutException("Case timeout: " + definition.Id);
            records.Add(task.GetAwaiter().GetResult());
        }
        return records.ToArray();
    }
    static string Encode(string value) => value == null ? "-" : Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
    static string Execute(CaseDefinition definition)
    {
        string value = null, side = null, error = null;
        try { var observation = definition.Execute(); value = observation.ReturnValue; side = observation.SideEffect; }
        catch (Exception exception) { error = exception.GetType().FullName; }
        return definition.Id + "\t" + Encode(value) + "\t" + Encode(side) + "\t" + Encode(error);
    }
}
