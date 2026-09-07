using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

namespace HybridCLR.Lab;

internal static class DheDifferentialInstrumentation
{
    public static void Generate(string sourceRoot, string outputRoot, bool preventInlining = false)
    {
        sourceRoot = Path.GetFullPath(sourceRoot);
        outputRoot = Path.GetFullPath(outputRoot);
        if (!Directory.Exists(sourceRoot) || Directory.Exists(outputRoot) || File.Exists(outputRoot))
            throw new IOException("Instrumentation requires an existing seed and a new output directory.");
        string source = Path.Combine(sourceRoot, "HybridCLR.ManagedCases.dll");
        MetaVersionSnapshot before = MetaVersionSnapshot.Create(source);
        using var module = ModuleDefMD.Load(source);
        var byToken = before.Methods.ToDictionary(method => method.Token);
        var getenv = new MemberRefUser(module, "GetEnvironmentVariable",
            MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.String),
            module.CorLibTypes.GetTypeRef("System", "Environment"));
        var combine = new MemberRefUser(module, "Combine",
            MethodSig.CreateStatic(module.CorLibTypes.String, module.CorLibTypes.String, module.CorLibTypes.String),
            module.CorLibTypes.GetTypeRef("System.IO", "Path"));
        var create = new MemberRefUser(module, "CreateDirectory",
            MethodSig.CreateStatic(new ClassSig(module.CorLibTypes.GetTypeRef("System.IO", "DirectoryInfo")), module.CorLibTypes.String),
            module.CorLibTypes.GetTypeRef("System.IO", "Directory"));
        var modified = new List<object>();
        var changedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (MethodDef method in module.GetTypes().SelectMany(type => type.Methods).Where(method => method.HasBody))
        {
            var identity = byToken[method.MDToken.Raw];
            var prefix = new List<Instruction>();
            bool trace = method.MethodSig.Params.Count == 0 &&
                method.MethodSig.RetType.FullName == "HybridCLR.Lab.ManagedCases.CaseObservation";
            if (trace)
            {
                Instruction write = Instruction.Create(OpCodes.Ldstr, identity.StableId);
                prefix.AddRange(new[] { Instruction.Create(OpCodes.Ldstr, "HYBRIDCLR_DHE_CASE_ENTRIES"),
                    Instruction.Create(OpCodes.Call, getenv), Instruction.Create(OpCodes.Dup),
                    Instruction.Create(OpCodes.Brtrue, write), Instruction.Create(OpCodes.Pop),
                    Instruction.Create(OpCodes.Br, method.Body.Instructions[0]), write,
                    Instruction.Create(OpCodes.Call, combine), Instruction.Create(OpCodes.Call, create),
                    Instruction.Create(OpCodes.Pop) });
            }
            else
                prefix.AddRange(new[] { Instruction.Create(OpCodes.Ldc_I4_0), Instruction.Create(OpCodes.Pop) });
            // Archived test Bases use the default 32-byte inline limit. Padding
            // isolates inlining without changing declarations or expected values.
            if (preventInlining)
                prefix.AddRange(Enumerable.Range(0, 64).Select(_ => Instruction.Create(OpCodes.Nop)));
            method.Body.SimplifyBranches();
            for (int index = 0; index < prefix.Count; index++) method.Body.Instructions.Insert(index, prefix[index]);
            method.Body.OptimizeBranches();
            changedIds.Add(identity.StableId);
            modified.Add(new { identity.StableId, identity.Identity, trace });
        }
        Directory.CreateDirectory(outputRoot);
        string destination = Path.Combine(outputRoot, "HybridCLR.ManagedCases.dll");
        module.Write(destination);
        MetaVersionSnapshot after = MetaVersionSnapshot.Create(destination);
        var afterMethods = after.Methods.ToDictionary(method => method.StableId);
        using var rewritten = ModuleDefMD.Load(destination);
        var originalFields = module.GetTypes().SelectMany(type => type.Fields).ToDictionary(field => field.FullName);
        var rewrittenFields = rewritten.GetTypes().SelectMany(type => type.Fields).ToDictionary(field => field.FullName);
        // The PE writer relocates RVA blobs. Require identical compiler data
        // bytes and declarations; keep the real output RVA and MV unchanged.
        var relocatedData = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in before.Fields.Where(field => after.Fields.Any(next => next.StableId == field.StableId && next.Version != field.Version)))
        {
            FieldDef left = originalFields.Values.Single(value => value.MDToken.Raw == field.Token);
            if (rewrittenFields.TryGetValue(left.FullName, out FieldDef? right) &&
                left.DeclaringType.Name == "<PrivateImplementationDetails>" && left.IsStatic && right.IsStatic &&
                left.HasFieldRVA && right.HasFieldRVA && left.Attributes == right.Attributes &&
                left.FieldOffset == right.FieldOffset && left.FieldSig.ToString() == right.FieldSig.ToString() &&
                !left.HasConstant && !right.HasConstant && left.MarshalType == null && right.MarshalType == null &&
                left.CustomAttributes.Count == 0 && right.CustomAttributes.Count == 0 &&
                left.InitialValue != null && right.InitialValue != null && left.InitialValue.SequenceEqual(right.InitialValue))
                relocatedData.Add(field.StableId);
        }
        bool fieldsMatch = before.Fields.Length == after.Fields.Length && before.Fields.All(field =>
            after.Fields.Any(next => next.StableId == field.StableId &&
                (next.Version == field.Version || relocatedData.Contains(field.StableId))));
        if (before.Methods.Length != after.Methods.Length || before.Methods.Any(method =>
                !afterMethods.TryGetValue(method.StableId, out var next) ||
                method.NonCustomMetadataVersion != next.NonCustomMetadataVersion ||
                (changedIds.Contains(method.StableId) && method.BodyVersion == next.BodyVersion)) ||
            !fieldsMatch)
        {
            var methodErrors = before.Methods.Where(method => !afterMethods.TryGetValue(method.StableId, out var next) ||
                method.NonCustomMetadataVersion != next.NonCustomMetadataVersion ||
                (changedIds.Contains(method.StableId) && method.BodyVersion == next.BodyVersion)).Select(method => method.Identity);
            var fieldErrors = before.Fields.Where(field => !after.Fields.Any(next => next.StableId == field.StableId &&
                (next.Version == field.Version || relocatedData.Contains(field.StableId)))).Select(field => field.Identity);
            throw new InvalidDataException("Instrumentation changed declarations or failed to change a body: " +
                string.Join("; ", methodErrors.Concat(fieldErrors).Take(12)));
        }
        foreach (string name in new[] { "HybridCLR.ManagedCasesAot", "HybridCLR.CrossAssemblyDerived", "HybridCLR.MetadataStress" })
            File.Copy(Path.Combine(sourceRoot, name + ".dll"), Path.Combine(outputRoot, name + ".dll"));
        File.WriteAllText(Path.Combine(outputRoot, "dhe-differential-instrumentation.json"), JsonSerializer.Serialize(new
        {
            format = "hybridclr.dhe-differential-instrumentation.json", schemaVersion = 1,
            source, sourceSha256 = DheDifferentialEvidence.Hash(source), destination,
            destinationSha256 = DheDifferentialEvidence.Hash(destination), modified,
            inlinePaddingBytes = preventInlining ? 64 : 0,
            relocatedConstantDataFields = relocatedData.OrderBy(value => value, StringComparer.Ordinal),
            scope = "Correctness instrumentation only; not performance evidence",
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Instrumented managed method bodies: " + changedIds.Count);
    }
}
