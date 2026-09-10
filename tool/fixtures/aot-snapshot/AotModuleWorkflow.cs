using System.Text.Json;
using HybridCLR.DheTool;

internal static class AotModuleWorkflow
{
    private static AotAnalysisSnapshot Snapshot(string proof)
    {
        string path = Path.Combine(proof, "base/build-identity.json"); var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        return AotAnalysisSnapshot.Read(path, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
    }
    internal static int NewBase(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("aot-module-new-base <lab> <package> <editor> <runtime manifest> <old Base proof> <new output>");
        string inputs = Path.GetFullPath(args[5]) + ".inputs";
        if (Directory.Exists(inputs)) throw new IOException("Base inputs must be new.");
        Directory.CreateDirectory(inputs); var snapshot = Snapshot(args[4]);
        foreach (var source in snapshot.Assemblies.Where(row => row.Dhe || row.AssemblyName == FrozenEntryWorkflow.NativeName))
            File.Copy(source.Path, Path.Combine(inputs, source.AssemblyName + ".dll"));
        FrozenStaticWorkflow.CompileAndMerge(Path.GetFullPath(args[0]), args[2], "AotModuleInitializer",
            Path.Combine(inputs, "HybridCLR.ValueLayoutModel.dll"), snapshot.Assemblies.Select(row => row.Path),
            Path.Combine(inputs, "compiled"), false, null, true);
        return UnityWorkflow.Run(args.Take(4).Concat(new[] { inputs, args[5], "41", ":all-ordinary-guards:" }).ToArray());
    }
    internal static int Current(string[] args)
    {
        if (args.Length != 6 || (args[4] != "changed" && args[4] != "removed"))
            throw new ArgumentException("aot-module-current <lab> <old Base proof> <46-case Current> <Unity editor> <changed|removed> <new output>");
        string output = Path.GetFullPath(args[5]), current = Path.Combine(output, "current");
        if (Directory.Exists(output)) throw new IOException("Current output must be new.");
        Directory.CreateDirectory(current); var snapshot = Snapshot(args[1]);
        foreach (string path in Directory.GetFiles(args[2], "*.dll")) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        FrozenStaticWorkflow.CompileAndMerge(Path.GetFullPath(args[0]), args[3], "AotModuleInitializer",
            Path.Combine(current, "HybridCLR.ValueLayoutModel.dll"), snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path)
                .Concat(Directory.GetFiles(current, "*.dll")), Path.Combine(output, "compiled"), false,
            args[4] == "removed" ? "MODULE_CURRENT,MODULE_REMOVED" : "MODULE_CURRENT", true);
        Console.WriteLine(current); return 0;
    }
}
