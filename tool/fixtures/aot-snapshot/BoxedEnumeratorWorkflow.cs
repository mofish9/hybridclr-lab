using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class BoxedEnumeratorWorkflow
{
    internal const string ProbeName = "HybridCLR.Lab.ResourceCases.BoxedEnumeratorCases";

    internal static int Compile(string[] args)
    {
        if (args.Length != 5) throw new ArgumentException("boxed-enumerator-current <lab> <Base proof> <Current root> <Unity editor> <new output>");
        string output = Path.GetFullPath(args[4]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        string identityPath = Path.Combine(args[1], "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        string current = Path.Combine(output, "current");
        Directory.CreateDirectory(current);
        foreach (string file in Directory.GetFiles(args[2], "*.dll")) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
        string model = Path.Combine(current, "HybridCLR.ValueLayoutModel.dll");
        FrozenStaticWorkflow.CompileAndMerge(args[0], args[3], "BoxedEnumeratorCases", model,
            snapshot.Assemblies.Where(row => !row.Dhe).Select(row => row.Path).Concat(Directory.GetFiles(current, "*.dll")),
            Path.Combine(output, "compiler"), false);
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(model)))
        {
            var run = module.Find(ProbeName, false)!.Methods.Single(method => method.Name == "Run");
            var entry = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            entry.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, run));
            module.Write(model);
        }
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed = true, current, snapshotSha256 = snapshot.Sha256,
            currentModelSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(model))),
            scope = "Additional boxed value interface case; all original Current suite calls are preserved."
        }, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }
}
