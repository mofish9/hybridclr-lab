using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class DeclarationWorkflow
{
    private const string Model = "HybridCLR.ValueLayoutModel";
    private const string Owner = "HybridCLR.Lab.UnityCases.EvolvingBehaviour";
    private const string Operation = "HybridCLR.Lab.Declarations.IOperation";
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static AotAnalysisSnapshot Snapshot(string proof)
    {
        string path = Path.Combine(proof, "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(path));
        return AotAnalysisSnapshot.Read(path, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
    }

    internal static int BaseInputs(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("declaration-base-inputs <Current root> <Base proof> <new output>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Base inputs must be new.");
        Directory.CreateDirectory(output);
        var snapshot = Snapshot(Path.GetFullPath(args[1]));
        foreach (string file in Directory.GetFiles(source, "*.dll")) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        var native = snapshot.Assemblies.Single(row => row.AssemblyName == "HybridCLR.ValueLayoutNative");
        File.Copy(native.Path, Path.Combine(output, native.AssemblyName + ".dll"));
        string model = Path.Combine(output, Model + ".dll");
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(model)))
        {
            var revision = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            revision.Body = new CilBody(); revision.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 59));
            revision.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); module.Write(model);
        }
        string declaration = ReadDeclaration(model);
        File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new
        {
            source, declaration, snapshotSha256 = snapshot.Sha256,
            currentInputs = Directory.GetFiles(source, "*.dll").ToDictionary(Path.GetFileName, Hash),
            baseInputs = Directory.GetFiles(output, "*.dll").ToDictionary(Path.GetFileName, Hash),
            scope = "Preserve compiler declarations and complete ordinary Native source; only Base entry revision becomes 59"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Declaration Base input: " + declaration); return 0;
    }

    internal static string CacheExpectation(string proof, string resource)
    {
        var snapshot = Snapshot(proof);
        string current = Path.Combine(resource, "current", Model + ".dll");
        var manifest = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(Path.Combine(resource, "resource/dhe-resource-update.json")));
        string expectedHash = manifest.GetProperty("assemblies").EnumerateArray().Single(row => row.GetProperty("assemblyName").GetString() == Model)
            .GetProperty("dllSha256").GetString()!;
        if (!string.Equals(Hash(current), expectedHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Unbound Current declaration input.");
        return ReadDeclaration(snapshot.Assemblies.Single(row => row.AssemblyName == Model).Path) + ":" + ReadDeclaration(current);
    }

    private static string ReadDeclaration(string path)
    {
        using var module = ModuleDefMD.Load(path);
        var owner = module.Find(Owner, false) ?? throw new InvalidDataException("Missing declaration owner.");
        var methods = new[] { "Measure", "OnBeforeSerialize", "OnAfterDeserialize" }.Select(name => owner.Methods.Single(method => method.Name == name)).ToArray();
        const MethodAttributes mask = MethodAttributes.Virtual | MethodAttributes.Final | MethodAttributes.NewSlot;
        MethodAttributes flags = methods[0].Attributes & mask;
        if ((flags != 0 && flags != mask) || methods.Any(method => (method.Attributes & mask) != flags))
            throw new InvalidDataException("Unexpected compiler method declaration set.");
        int Default(MethodDef method)
        {
            var parameter = method.ParamDefs.Single(row => row.Sequence == 1);
            if (!parameter.IsOptional || !parameter.HasConstant || parameter.Constant.Value is not int value)
                throw new InvalidDataException("Expected a compiler-produced Int32 optional argument.");
            return value;
        }
        int value = Default(methods[0]);
        if (Default(module.Find(Operation, false)!.Methods.Single(method => method.Name == "Measure")) != value)
            throw new InvalidDataException("Interface and implementation defaults must agree in this fixture.");
        return ((int)flags).ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
