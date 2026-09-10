using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HybridCLR.DheTool;

internal static class ModuleResourceCompiler
{
    internal static int Run(string[] args)
    {
        if (args.Length != 6) throw new ArgumentException("module-resource-current <lab> <Base proof> <46-case Current root> <Current settings> <Unity editor> <new output>");
        string lab = Path.GetFullPath(args[0]), proof = Path.GetFullPath(args[1]), original = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[5]);
        if (Directory.Exists(output)) throw new IOException("Module resource output must be new.");
        string current = Path.Combine(output, "current"); Directory.CreateDirectory(current);
        string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
        string identityPath = Path.Combine(proof, "base/build-identity.json");
        var identity = JsonSerializer.Deserialize<JsonElement>(File.ReadAllBytes(identityPath));
        var snapshot = AotAnalysisSnapshot.Read(identityPath, identity,
            identity.GetProperty("aotAssemblyNames").EnumerateArray().Select(row => row.GetString()!),
            identity.GetProperty("assemblies").EnumerateArray().Select(row => row.GetProperty("assemblyName").GetString()!))!;
        var originals = Directory.GetFiles(original, "*.dll").ToDictionary(Path.GetFileName, Hash);
        foreach (string file in Directory.GetFiles(original, "*.dll")) File.Copy(file, Path.Combine(current, Path.GetFileName(file)));
        string source = Path.Combine(lab, "tool/fixtures/aot-snapshot/Workloads/PublicModuleInitializer.cs");
        string data = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[4]))!, "Data");
        string host = Path.Combine(data, "NetCoreRuntime/dotnet.exe"), compiler = Path.Combine(data, "DotNetSdkRoslyn/csc.dll");
        foreach (string name in new[] { "HybridCLR.TransactionInitializer", "HybridCLR.TransactionPeer" })
        {
            var start = new ProcessStartInfo(host) { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in new[] { compiler, "-nologo", "-noconfig", "-nostdlib+", "-target:library", "-optimize+", "-debug-",
                "-utf8output", "-deterministic+", "-langversion:9.0", "-out:" + Path.Combine(current, name + ".dll") }
                .Concat(snapshot.Assemblies.Where(row => !row.Dhe).Select(row => "-r:" + row.Path)).Append(source)) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException(name); }
            string log = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult(); File.WriteAllText(Path.Combine(output, name + ".compile.log"), log);
            if (process.ExitCode != 0) throw new InvalidOperationException(log);
        }
        string settings = File.ReadAllText(args[3]).Replace("\r\n", "\n");
        const string last = "  - HybridCLR.ValueLayoutAdded\n";
        if (settings.Split(last).Length != 3) throw new InvalidDataException("Expected the four-assembly Current configuration.");
        settings = settings.Replace(last, last + "  - HybridCLR.TransactionInitializer\n  - HybridCLR.TransactionPeer\n");
        File.WriteAllText(Path.Combine(output, "HybridCLRSettings.asset"), settings);
        if (originals.Any(row => Hash(Path.Combine(original, row.Key!)) != row.Value || Hash(Path.Combine(current, row.Key!)) != row.Value))
            throw new InvalidDataException("Existing Current bytes changed.");
        File.WriteAllText(Path.Combine(output, "compiler-evidence.json"), JsonSerializer.Serialize(new { passed = true,
            sourceSha256 = Hash(source), compilerSha256 = Hash(compiler), compilerHostSha256 = Hash(host), snapshotSha256 = snapshot.Sha256,
            original, originalHashes = originals, currentHashes = Directory.GetFiles(current, "*.dll").ToDictionary(Path.GetFileName, Hash),
            scope = "Real standalone module initializers for the unchanged public resource Player, with original four Current DLLs preserved" }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Public module Current compiled."); return 0;
    }
}
