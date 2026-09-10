using System.Security.Cryptography;
using System.Text.Json;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using HybridCLR.DheTool;

internal static class FrozenResourceCasesCompiler
{
    public const string ProbeName = "HybridCLR.Lab.ValueLayout.FrozenResourceCases";
    public const int CaseCount = 23;

    public static void Compile(string lab, string current, AotAnalysisSnapshot snapshot, string editor,
        string output, Func<string, string[], string> execute)
    {
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(editor))!, "Data");
        string compiler = Path.Combine(data, "DotNetSdkRoslyn/csc.dll"), host = Path.Combine(data, "NetCoreRuntime/dotnet.exe");
        if (!File.Exists(compiler) || !File.Exists(host)) throw new FileNotFoundException("Unity 2022 Windows compiler is required.");
        string source = Path.Combine(lab, "tool/fixtures/aot-snapshot/Workloads/FrozenResourceCases.cs");
        string directory = Path.Combine(output, "compiled-checks"); Directory.CreateDirectory(directory);
        string library = Path.Combine(directory, "FrozenResourceCases.dll");
        string[] references = snapshot.Assemblies.Where(image => !image.Dhe).Select(image => image.Path)
            .Concat(Directory.GetFiles(current, "*.dll")).ToArray();
        string[] arguments = new[] { compiler, "-nologo", "-noconfig", "-nostdlib+", "-target:library", "-optimize+", "-debug-", "-utf8output",
            "-deterministic+", "-langversion:9.0", "-out:" + library }
            .Concat(references.Select(path => "-r:" + (Path.GetFileNameWithoutExtension(path) == "HybridCLR.ValueLayoutModel" ? "model=" : "") + path)).Append(source).ToArray();
        execute(host, arguments);
        string model = Path.Combine(current, "HybridCLR.ValueLayoutModel.dll");
        using (var target = ModuleDefMD.Load(File.ReadAllBytes(model)))
        using (var compiled = ModuleDefMD.Load(File.ReadAllBytes(library)))
        {
            if (target.Find(ProbeName, false) != null) throw new InvalidDataException("Resource suite already exists.");
            // Keep compiler metadata alive until the final writer has materialized
            // signatures, custom attributes, bodies and nested generic definitions.
            foreach (TypeRef reference in compiled.GetTypeRefs())
                if (reference.ResolutionScope == compiled || reference.DefinitionAssembly?.Name == compiled.Assembly.Name)
                    reference.ResolutionScope = target;
            foreach (var type in compiled.Types.Where(type => type.Name != "<Module>").ToArray())
            {
                if (target.GetTypes().Any(original => original.FullName == type.FullName))
                    throw new InvalidDataException("Compiler-generated type conflicts with Current: " + type.FullName);
                compiled.Types.Remove(type); target.Types.Add(type);
            }
            var run = target.Find(ProbeName, false)!.Methods.Single(method => method.Name == "Run");
            var entry = target.Find("HybridCLR.Lab.ValueLayout.Factory", false)!.Methods.Single(method => method.Name == "GetRevision");
            entry.Body = new CilBody(); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Call, run));
            entry.Body.Instructions.Add(Instruction.Create(OpCodes.Pop)); entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ldc_I4, 73));
            entry.Body.Instructions.Add(Instruction.Create(OpCodes.Ret)); target.Write(model);
        }
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        File.WriteAllText(Path.Combine(directory, "compiler-evidence.json"), JsonSerializer.Serialize(new {
            source, sourceSha256 = Hash(source), compiler, compilerSha256 = Hash(compiler), hostSha256 = Hash(host),
            librarySha256 = Hash(library), currentModelSha256 = Hash(model), snapshotSha256 = snapshot.Sha256,
            caseCount = CaseCount, scope = "Unity compiler against immutable Base references and actual Current; suite merged into hotfix Model"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
