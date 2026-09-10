using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;

internal static class HotfixGenericBodyCurrent
{
    internal static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("hotfix-generic-body-current <Current DLL root> <new output>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[1]), current = Path.Combine(output, "current");
        if (Directory.Exists(output)) throw new IOException("Generic body Current output must be new.");
        Directory.CreateDirectory(current);
        foreach (string path in Directory.GetFiles(source, "*.dll")) File.Copy(path, Path.Combine(current, Path.GetFileName(path)));
        string modelPath = Path.Combine(current, "HybridCLR.ValueLayoutModel.dll");
        using var module = ModuleDefMD.Load(File.ReadAllBytes(modelPath));
        const string helperName = "HybridCLR.Lab.GenericDispatch.ChangedBodyEvidence";
        if (module.Find(helperName, false) != null) throw new InvalidDataException("Generic body input already contains this fixture.");
        var helper = new TypeDefUser("HybridCLR.Lab.GenericDispatch", "ChangedBodyEvidence", module.CorLibTypes.Object.TypeDefOrRef)
            { Attributes = TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed };
        module.Types.Add(helper);
        var count = new FieldDefUser("Calls", new FieldSig(module.CorLibTypes.Int32), FieldAttributes.Public | FieldAttributes.Static);
        helper.Fields.Add(count);
        MethodDef Method(string name)
        {
            var method = new MethodDefUser(name, MethodSig.CreateStatic(module.CorLibTypes.Void),
                MethodImplAttributes.IL, MethodAttributes.Public | MethodAttributes.Static) { Body = new CilBody() };
            helper.Methods.Add(method); return method;
        }
        var record = Method("Record");
        foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldsfld, count), Instruction.Create(OpCodes.Ldc_I4_1),
            Instruction.Create(OpCodes.Add), Instruction.Create(OpCodes.Stsfld, count), Instruction.Create(OpCodes.Ret) })
            record.Body.Instructions.Add(instruction);
        var require = Method("Require");
        var success = Instruction.Create(OpCodes.Ldstr, "DHE changed generic body pass: 3");
        var exceptionType = new TypeRefUser(module, "System", "InvalidOperationException", module.CorLibTypes.AssemblyRef);
        var exceptionConstructor = new MemberRefUser(module, ".ctor",
            MethodSig.CreateInstance(module.CorLibTypes.Void, module.CorLibTypes.String), exceptionType);
        var write = new MemberRefUser(module, "WriteLine",
            MethodSig.CreateStatic(module.CorLibTypes.Void, module.CorLibTypes.String),
            new TypeRefUser(module, "System", "Console", module.CorLibTypes.AssemblyRef));
        foreach (var instruction in new[] { Instruction.Create(OpCodes.Ldsfld, count), Instruction.Create(OpCodes.Ldc_I4_3),
            Instruction.Create(OpCodes.Beq, success),
            Instruction.Create(OpCodes.Ldstr, "Changed generic body must execute exactly three times."),
            Instruction.Create(OpCodes.Newobj, exceptionConstructor), Instruction.Create(OpCodes.Throw),
            success, Instruction.Create(OpCodes.Call, write), Instruction.Create(OpCodes.Ret) })
            require.Body.Instructions.Add(instruction);
        var identity = module.Find("HybridCLR.Lab.ResourceCases.FrozenResourceCases", false)!.Methods.Single(method => method.Name == "Identity");
        identity.Body.Instructions.Insert(0, Instruction.Create(OpCodes.Call, record));
        var entry = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
        int insertion = entry.Body.Instructions.Count - 2;
        if (insertion < 0 || entry.Body.Instructions.Last().OpCode != OpCodes.Ret || !entry.Body.Instructions[insertion].IsLdcI4())
            throw new InvalidDataException("Unexpected Current entry shape.");
        entry.Body.Instructions.Insert(insertion, Instruction.Create(OpCodes.Call, require));
        module.Write(modelPath);
        File.WriteAllText(Path.Combine(output, "fixture.json"), JsonSerializer.Serialize(new
        {
            source, current, sourceModelSha256 = Hash(Path.Combine(source, "HybridCLR.ValueLayoutModel.dll")),
            currentModelSha256 = Hash(modelPath), expectedCalls = 3,
            hostSha256 = Hash(typeof(HotfixGenericBodyCurrent).Assembly.Location),
            scope = "Change the existing generic method body; preserve its three value-copy cases and require observable Current execution after all 46 business cases"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(current); return 0;
    }
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
}
