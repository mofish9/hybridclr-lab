using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class BaselineLinkerIdentity
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("baseline-linker-identity <original Base DLL> <captured DHE DLL> <new report.json>");
        if (File.Exists(args[2])) throw new IOException("Report must be new.");
        byte[] before = File.ReadAllBytes(args[0]), after = File.ReadAllBytes(args[1]);
        string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
        byte[] Edit(Action<ModuleDefMD> change)
        {
            using var module = ModuleDefMD.Load(before); change(module);
            using var output = new MemoryStream(); module.Write(output); return output.ToArray();
        }
        MethodDef Entry(ModuleDef module) => module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
        var checks = new Dictionary<string, bool>
        {
            ["exact-base-bytes"] = DheAotBaselineIdentity.Matches(before, before),
            ["real-linker-reorder-has-distinct-raw-hashes"] = Hash(before) != Hash(after),
            ["real-linker-reorder-accepted"] = DheAotBaselineIdentity.Matches(before, after),
            ["method-body-change-rejected"] = !DheAotBaselineIdentity.Matches(Edit(module => {
                var entry = Entry(module); entry.Body = new CilBody();
                entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 999)); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret));
            }), after),
            ["value-field-change-rejected"] = !DheAotBaselineIdentity.Matches(Edit(module =>
                module.Find("HybridCLR.Lab.ValueLayout.Payload", false)!.Fields.Single(field => field.Name == "Count").FieldType = module.CorLibTypes.Int64), after),
            ["method-identity-change-rejected"] = !DheAotBaselineIdentity.Matches(Edit(module => Entry(module).Name = "ChangedEntry"), after),
            ["compiler-attribute-value-change-rejected"] = !DheAotBaselineIdentity.Matches(Edit(module =>
                module.Assembly.CustomAttributes.Single(attribute => attribute.TypeFullName == "System.Reflection.AssemblyTitleAttribute")
                    .ConstructorArguments[0] = new CAArgument(module.CorLibTypes.String, new UTF8String("Changed title"))), after),
            ["duplicate-compiler-attribute-rejected"] = !DheAotBaselineIdentity.Matches(Edit(module =>
                module.Assembly.CustomAttributes.Add(module.Assembly.CustomAttributes.Single(attribute => attribute.TypeFullName == "System.Reflection.AssemblyTitleAttribute"))), after),
            ["definition-token-change-rejected"] = !DheAotBaselineIdentity.Matches(Edit(module => {
                var methods = Entry(module).DeclaringType.Methods;
                var first = methods[0]; methods.RemoveAt(0); methods.Add(first);
            }), after),
        };
        byte[] WithBusinessAttributes(bool reverse) => Edit(module => {
            var type = new TypeRefUser(module, "System.Reflection", "AssemblyMetadataAttribute", module.CorLibTypes.AssemblyRef);
            var constructor = new MemberRefUser(module, ".ctor", MethodSig.CreateInstance(module.CorLibTypes.Void,
                module.CorLibTypes.String, module.CorLibTypes.String), type);
            foreach (string value in reverse ? new[] { "second", "first" } : new[] { "first", "second" })
                module.Assembly.CustomAttributes.Add(new CustomAttribute(constructor, new[] {
                    new CAArgument(module.CorLibTypes.String, new UTF8String("business")),
                    new CAArgument(module.CorLibTypes.String, new UTF8String(value)) }));
        });
        checks["business-attribute-order-change-rejected"] = !DheAotBaselineIdentity.Matches(WithBusinessAttributes(false), WithBusinessAttributes(true));
        checks["original-files-preserved"] = Hash(File.ReadAllBytes(args[0])) == Hash(before) && Hash(File.ReadAllBytes(args[1])) == Hash(after);
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed, checks, beforeSha256 = Hash(before), afterSha256 = Hash(after),
            hostSha256 = Hash(File.ReadAllBytes(typeof(BaselineLinkerIdentity).Assembly.Location)),
            scope = "DHE Base versus captured DHE source; ordinary frozen source hashes remain exact" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Baseline linker identity: " + passed);
        return passed ? 0 : 1;
    }
}
