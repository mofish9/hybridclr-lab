using System;
using System.IO;
using dnlib.DotNet;
using dnlib.DotNet.Writer;

internal static class EvolutionCurrent
{
    public static int Run(string[] args)
    {
        if (args.Length != 2) throw new ArgumentException("synthesize-evolution <source DLL root> <new output root>");
        string sourceRoot = Path.GetFullPath(args[0]), outputRoot = Path.GetFullPath(args[1]);
        if (!Directory.Exists(sourceRoot) || Directory.Exists(outputRoot)) throw new IOException("Invalid evolution roots.");
        Directory.CreateDirectory(outputRoot);
        foreach (string source in Directory.GetFiles(sourceRoot, "*.dll"))
        {
            string target = Path.Combine(outputRoot, Path.GetFileName(source));
            File.Copy(source, target);
            if (!Path.GetFileNameWithoutExtension(source).Equals("HybridCLR.ValueLayoutModel", StringComparison.Ordinal)) continue;
            using var module = ModuleDefMD.Load(File.ReadAllBytes(target));
            TypeDef payload = module.Find("HybridCLR.Lab.ValueLayout.Payload", false) ?? throw new InvalidDataException("Payload type missing.");
            if (payload.Fields.All(field => field.Name != "Extra"))
                payload.Fields.Add(new FieldDefUser("Extra", new FieldSig(module.CorLibTypes.Int64), FieldAttributes.Public));
            if (payload.Fields.All(field => field.Name != "Reference"))
                payload.Fields.Add(new FieldDefUser("Reference", new FieldSig(module.CorLibTypes.Object), FieldAttributes.Public));
            var options = new ModuleWriterOptions(module);
            options.PEHeadersOptions.TimeDateStamp = 123456789;
            module.Write(target, options);
        }
        Console.WriteLine(outputRoot);
        return 0;
    }
}
