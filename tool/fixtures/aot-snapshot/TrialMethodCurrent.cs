using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class TrialMethodCurrent
{
    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("trial-method-current <Base Current DLL root> <new output> <old revision> <new revision>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]);
        int before = int.Parse(args[2]), after = int.Parse(args[3]);
        if (before == after || !Directory.Exists(source) || Directory.Exists(output))
            throw new InvalidDataException("Require existing source, new output and different revisions.");
        string name = "HybridCLR.ValueLayoutModel.dll";
        using var module = ModuleDefMD.Load(File.ReadAllBytes(Path.Combine(source, name)));
        var method = module.GetTypes().Single(t => t.FullName == "HybridCLR.Lab.ValueLayout.Factory")
            .Methods.Single(m => m.Name == "GetRevision");
        var instructions = method.Body.Instructions.Where(i => i.OpCode != OpCodes.Nop).ToArray();
        if (instructions.Length != 2 || !instructions[0].IsLdcI4() ||
            instructions[0].GetLdcI4Value() != before || instructions[1].OpCode != OpCodes.Ret)
            throw new InvalidDataException("Input revision does not match the expected Base method body.");
        Directory.CreateDirectory(output);
        foreach (string path in Directory.GetFiles(source, "*.dll"))
            File.Copy(path, Path.Combine(output, Path.GetFileName(path)));
        method.Body = new CilBody();
        method.Body.Instructions.Add(Instruction.CreateLdcI4(after));
        method.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
        module.Write(Path.Combine(output, name));
        Console.WriteLine("Created Current revision " + before + " -> " + after);
        return 0;
    }
}
