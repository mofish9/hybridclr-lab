using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using HybridCLR.DheTool;

internal static class PeHeaderIdentity
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("pe-header-identity <Base Added DLL> <Current Added DLL> <new output directory>");
        if (Directory.Exists(args[2])) throw new IOException("Output must be new.");
        Directory.CreateDirectory(args[2]);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string beforeHash = Hash(args[0]), currentHash = Hash(args[1]);
        var baseline = MetaVersionSnapshot.Create(args[0]);
        var current = MetaVersionSnapshot.Create(args[1]);
        var checks = new Dictionary<string, bool>();
        ResourceUpdateCompatibility original = ResourceUpdateCompatibility.Analyze(baseline, current);
        checks["real-AnyCPU-compiler-linker-header-difference-accepted"] = beforeHash != currentHash && original.Compatible;
        checks["real-nonreference-assembly-shapes-match"] = baseline.AssemblyNonReferenceMetadataVersion == current.AssemblyNonReferenceMetadataVersion;
        void Change(string name, Action<ModuleDefMD> change, bool accept)
        {
            string path = Path.Combine(args[2], name + ".dll");
            using (var module = ModuleDefMD.Load(args[1])) { change(module); module.Write(path); }
            var next = MetaVersionSnapshot.Create(path);
            var result = ResourceUpdateCompatibility.Analyze(baseline, next);
            checks[name] = accept ? result.Compatible : !result.Compatible && result.UnsupportedChanges.Any(value =>
                value.StartsWith("assembly-or-module-metadata-change:", StringComparison.Ordinal));
        }
        Change("native-PE-hints-only-accepted", module => module.Characteristics ^=
            dnlib.PE.Characteristics.Bit32Machine | dnlib.PE.Characteristics.LargeAddressAware, true);
        Change("CLR-32bit-required-rejected", module => module.Is32BitRequired = true, false);
        Change("CLR-32bit-preferred-rejected", module => module.Is32BitPreferred = true, false);
        Change("different-target-machine-rejected", module => module.Machine = dnlib.PE.Machine.AMD64, false);
        Change("different-assembly-version-rejected", module => module.Assembly.Version = new Version(2, 0, 0, 0), false);
        Change("different-module-name-rejected", module => module.Name = "Changed.dll", false);
        Change("other-PE-characteristics-rejected", module => module.Characteristics ^= dnlib.PE.Characteristics.System, false);
        Change("assembly-attribute-value-rejected", module => module.Assembly.CustomAttributes.Single(attribute =>
            attribute.TypeFullName == "System.Runtime.CompilerServices.CompilationRelaxationsAttribute")
            .ConstructorArguments[0] = new CAArgument(module.CorLibTypes.Int32, 0), false);
        checks["immutable-inputs-preserved"] = Hash(args[0]) == beforeHash && Hash(args[1]) == currentHash;
        bool passed = checks.Values.All(value => value);
        File.WriteAllText(Path.Combine(args[2], "result.json"), JsonSerializer.Serialize(new { passed, checks,
            beforeSha256 = beforeHash, currentSha256 = currentHash, original.UnsupportedChanges,
            hostSha256 = Hash(typeof(PeHeaderIdentity).Assembly.Location), scope = "AnyCPU IL compatibility, without native PE hint equivalence for CLR architecture changes" },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PE header identity: " + passed);
        return passed ? 0 : 1;
    }
}
