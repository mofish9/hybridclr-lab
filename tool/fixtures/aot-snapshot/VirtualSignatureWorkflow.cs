using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class VirtualSignatureWorkflow
{
    internal const string Probe = "HybridCLR.Lab.VirtualSignatures.Cases";
    internal static readonly string[] Expected = {
        "reference-interface", "reference-virtual", "value-interface", "value-virtual", "byref-interface", "byref-virtual",
        "generic-reference-interface", "generic-value-interface", "generic-reference-virtual", "generic-value-virtual",
        "closed-generic-interface-value", "delegate-value", "delegate-reference", "reflection-value", "reflection-byref",
        "reflection-generic-value", "interface-map-value", "interface-null", "virtual-null", "interface-exception",
        "virtual-exception", "repeated-interface-value", "concurrent-interface-value", "input-reference-and-value-preserved",
        "receiver-storage-fields"
    };
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    internal static int Reference(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("virtual-signature-reference <Current DLL root> <Base Native DLL> <new output>");
        string current = Path.GetFullPath(args[0]), native = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Reference output must be new.");
        Directory.CreateDirectory(output);
        int business = FrozenResourceWorkflow.Reference(new[] { current, native, Path.Combine(output, "business-reference.json") });
        var model = AssemblyLoadContext.Default.Assemblies.Single(assembly => assembly.GetName().Name == "HybridCLR.ValueLayoutModel");
        var method = model.GetType(Probe, true)!.GetMethod("Run")!;
        string[] checks = Array.Empty<string>(); string error = null;
        using var trace = new StringWriter(); var original = Console.Out;
        try { Console.SetOut(trace); checks = (string[])method.Invoke(null, null)!; }
        catch (TargetInvocationException exception) { error = exception.InnerException?.ToString() ?? exception.ToString(); }
        finally { Console.SetOut(original); }
        string log = trace.ToString(); File.WriteAllText(Path.Combine(output, "reference.log"), log);
        string[] observed = log.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.StartsWith("DHE virtual signature check: "))
            .Select(line => line["DHE virtual signature check: ".Length..]).ToArray();
        bool passed = business == 0 && error == null && checks.SequenceEqual(Expected) && observed.SequenceEqual(Expected);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new {
            passed, error, checks, expected = Expected, source = current, native, nativeSha256 = Hash(native),
            inputs = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash),
            hostSha256 = Hash(typeof(VirtualSignatureWorkflow).Assembly.Location),
            scope = "CLR reference for 46 existing business cases and complete managed virtual-signature sequence"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Virtual signature reference: " + passed + "; checks=" + checks.Length);
        if (error != null) Console.WriteLine(error);
        return passed ? 0 : 1;
    }

    internal static int BaseInputs(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("virtual-signature-base-inputs <Current DLL root> <Base proof> <new output>");
        string source = Path.GetFullPath(args[0]), output = Path.GetFullPath(args[2]);
        if (Directory.Exists(output)) throw new IOException("Base inputs must be new.");
        Directory.CreateDirectory(output);
        string identityPath = Path.Combine(args[1], "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        foreach (string file in Directory.GetFiles(source, "*.dll")) File.Copy(file, Path.Combine(output, Path.GetFileName(file)));
        var native = snapshot.Assemblies.Single(row => row.AssemblyName == "HybridCLR.ValueLayoutNative");
        File.Copy(native.Path, Path.Combine(output, native.AssemblyName + ".dll"));
        string model = Path.Combine(output, "HybridCLR.ValueLayoutModel.dll");
        string[] fields, methods;
        using (var module = ModuleDefMD.Load(File.ReadAllBytes(model)))
        {
            fields = module.Find("HybridCLR.Lab.VirtualSignatures.Packet", false)!.Fields.Select(field => field.FullName).ToArray();
            methods = module.Find("HybridCLR.Lab.VirtualSignatures.Processor", false)!.Methods.Where(method => method.IsVirtual)
                .Select(method => method.FullName).ToArray();
            var revision = module.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            revision.Body = new CilBody(); revision.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 59));
            revision.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); module.Write(model);
        }
        File.WriteAllText(Path.Combine(output, "inputs.json"), JsonSerializer.Serialize(new {
            source, snapshotSha256 = snapshot.Sha256, fields, methods,
            currentInputs = Directory.GetFiles(source, "*.dll").ToDictionary(Path.GetFileName, Hash),
            baseInputs = Directory.GetFiles(output, "*.dll").ToDictionary(Path.GetFileName, Hash),
            scope = "Preserve compiler virtual signatures and layouts, authenticate ordinary Native source, set Base revision 59"
        }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Virtual signature Base input: fields=" + fields.Length + "; virtual methods=" + methods.Length);
        return 0;
    }
}
