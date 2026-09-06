using dnlib.DotNet;
using dnlib.DotNet.Emit;

namespace HybridCLR.DheTool;

internal static class ManagedCaseVariants
{
    internal static void WriteBase2CurrentAssembly(string source, string destination)
    {
        source = Path.GetFullPath(source);
        destination = Path.GetFullPath(destination);
        if (!File.Exists(source)) throw new FileNotFoundException(source);
        if (source.Equals(destination, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Managed case variant must not overwrite its seed assembly.");

        using var module = ModuleDefMD.Load(source);
        TypeDef probeType = module.GetTypes().Single(type =>
            type.FullName == "HybridCLR.Lab.ManagedCasesAot.DheMultiBaseProbe");
        MethodDef probe = probeType.Methods.Single(method => method.Name == "CurrentValue" &&
            method.IsStatic && method.Parameters.Count == 0);
        probe.Body = new CilBody
        {
            Instructions =
            {
                Instruction.Create(OpCodes.Ldc_I4_2),
                Instruction.Create(OpCodes.Ret),
            },
        };

        TypeDef calculatorType = module.GetTypes().Single(type =>
            type.FullName == "HybridCLR.Lab.ManagedCasesAot.DheDemoCalculator");
        MethodDef constructor = calculatorType.Methods.Single(method => method.IsInstanceConstructor &&
            method.Parameters.Count == 1);
        FieldDef touchValue = calculatorType.Fields.Single(field =>
            field.Name == "TouchValue" && field.IsStatic);
        Instruction existingReturn = constructor.Body.Instructions.Last(instruction =>
            instruction.OpCode == OpCodes.Ret);
        int returnIndex = constructor.Body.Instructions.IndexOf(existingReturn);
        constructor.Body.Instructions.RemoveAt(returnIndex);
        Instruction finalReturn = Instruction.Create(OpCodes.Ret);
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldsfld, touchValue));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, int.MinValue));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Bne_Un_S, finalReturn));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4_0));
        constructor.Body.Instructions.Add(Instruction.Create(OpCodes.Stsfld, touchValue));
        constructor.Body.Instructions.Add(finalReturn);
        constructor.Body.OptimizeBranches();

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        module.Write(destination);
    }
}
