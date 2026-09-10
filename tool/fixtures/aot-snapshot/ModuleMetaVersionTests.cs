using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class ModuleMetaVersionTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("module-metaversion <real compiled initializer DLL> <prior frozen resource output> <new output>");
        string source = Path.GetFullPath(args[0]), prior = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Test output must be new.");
        Directory.CreateDirectory(output);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string originalHash = Hash(source);
        var mv = MetaVersionSnapshot.Create(source);
        using var module = ModuleDefMD.Load(source);
        var moduleType = mv.Types.Single(type => type.Identity == "<Module>");
        var cctor = mv.Methods.Single(method => method.DeclaringType == "<Module>" && method.Name == ".cctor");
        var checks = new Dictionary<string, bool>
        {
            ["real-compiler-module-initializer"] = module.GlobalType.Methods.Single(method => method.IsStaticConstructor).Body.Instructions.Any(instruction => instruction.OpCode == OpCodes.Call),
            ["global-owner-kept"] = moduleType.Token == 0x02000001 && cctor.DeclaringTypeStableId == moduleType.StableId,
            ["complete-method-inventory"] = mv.Methods.Length == module.GetTypes().Sum(type => type.Methods.Count),
        };
        string changed = Path.Combine(output, Path.GetFileName(source));
        module.GlobalType.Methods.Single(method => method.IsStaticConstructor).Body = new CilBody();
        module.GlobalType.Methods.Single(method => method.IsStaticConstructor).Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        module.Write(changed);
        var modified = MetaVersionSnapshot.Create(changed);
        var changedCctor = modified.Methods.Single(method => method.DeclaringType == "<Module>" && method.Name == ".cctor");
        checks["initializer-body-change-tracked"] = cctor.StableId == changedCctor.StableId && cctor.Version != changedCctor.Version;
        foreach (string dll in Directory.GetFiles(Path.Combine(prior, "current"), "*.dll"))
        {
            string name = Path.GetFileNameWithoutExtension(dll);
            checks["archived-mv-unchanged:" + name] = MetaVersionSnapshot.Create(dll).ToBinary()
                .SequenceEqual(File.ReadAllBytes(Path.Combine(prior, "resource/payload", name + ".mv.bytes")));
        }
        checks["compiler-dll-preserved"] = Hash(source) == originalHash;
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed, checks, sourceSha256 = originalHash,
            hostSha256 = Hash(typeof(ModuleMetaVersionTests).Assembly.Location),
            scope = "Real module initializer MV inventory/fingerprint and exact archived MV compatibility; native differential module execution requires its own gate" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Module MetaVersion tests: " + passed); return passed ? 0 : 1;
    }
}
