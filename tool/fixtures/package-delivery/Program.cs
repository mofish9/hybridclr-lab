using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using HybridCLR.DheTool;
using Tool = HybridCLR.DheTool.Program;

internal static class PackageDeliveryTests
{
    public static int Main(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("<package> <approved runtime> <Editor contents> <new output>");
        string packageSource = Path.GetFullPath(args[0]), nativeSource = Path.GetFullPath(args[1]);
        string editor = Path.GetFullPath(args[2]), output = Path.GetFullPath(args[3]);
        if (Directory.Exists(output)) throw new IOException("Output must be new.");
        Directory.CreateDirectory(output);
        string project = Path.Combine(output, "Project with spaces");
        string package = Path.Combine(project, "Packages/com.code-philosophy.hybridclr@8.13.0");
        void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (string file in Directory.GetFiles(source))
                if (Path.GetFileName(file) != ".git") File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
            foreach (string directory in Directory.GetDirectories(source))
                if (Path.GetFileName(directory) is not (".git" or "bin" or "obj")) Copy(directory, Path.Combine(target, Path.GetFileName(directory)));
        }
        Copy(packageSource, package);
        string platform = OperatingSystem.IsWindows() ? "WindowsEditor" : OperatingSystem.IsMacOS() ? "OSXEditor" : "LinuxEditor";
        string installed = RuntimeSourceBinding.InstalledRoot(project, platform);
        Copy(nativeSource, installed);
        string compiler = Path.Combine(Path.GetDirectoryName(installed)!, "build/deploy");
        Directory.CreateDirectory(compiler);
        foreach (string file in new[] { "Unity.IL2CPP.dll", "Unity.IL2CPP.DataModel.dll" })
            File.Copy(Path.Combine(editor, "il2cpp/build/deploy", file), Path.Combine(compiler, file));
        Directory.CreateDirectory(Path.Combine(project, "ProjectSettings"));
        string projectVersion = Path.Combine(project, "ProjectSettings/ProjectVersion.txt");
        File.WriteAllText(projectVersion, "m_EditorVersion: 2022.3.62f3\n");
        File.WriteAllText(Path.Combine(project, "ProjectSettings/HybridCLRSettings.asset"),
            "  hybridclrRepoURL: https://github.com/mofish9/hybridclr.git\n  il2cppPlusRepoURL: https://github.com/mofish9/il2cpp_plus.git\n");
        var checks = new Dictionary<string, bool>();
        void Require(string name, bool value) { checks[name] = value; if (!value) throw new InvalidOperationException(name); }
        int Run(params string[] command) => Tool.Main(command);
        int Verify() => Run("verify-installation", "-ProjectPath", project);
        Require("capture-without-lab-or-repo-checkouts", Run("capture-installation", "-ProjectPath", project,
            "-PackageRoot", package, "-EditorContents", editor, "-EditorVersion", "2022.3.62f3") == 0);
        Require("installed-project-verifies", Verify() == 0);
        void Mutation(string name, string path, Action change)
        {
            byte[]? before = File.Exists(path) ? File.ReadAllBytes(path) : null;
            try { change(); Require(name, Verify() != 0); }
            finally { if (before == null) File.Delete(path); else File.WriteAllBytes(path, before); }
        }
        string native = Path.Combine(installed, "vm/Type.cpp");
        Mutation("native-source-drift-rejected", native, () => File.AppendAllText(native, "\n// stale runtime\n"));
        string extraNative = Path.Combine(installed, "Unexpected.cpp");
        Mutation("extra-native-source-rejected", extraNative, () => File.WriteAllText(extraNative, "// extra"));
        string marker = Path.Combine(package, "Unexpected.txt");
        Mutation("mixed-package-rejected", marker, () => File.WriteAllText(marker, "wrong package"));
        Mutation("editor-version-change-rejected", projectVersion, () => File.WriteAllText(projectVersion, "m_EditorVersion: 2022.3.61f1\n"));
        string receipt = Path.Combine(project, "HybridCLRData/DHE/runtime-manifest.json");
        void ReceiptMutation(string name, Action<JsonObject> change) => Mutation(name, receipt, () =>
        {
            var doc = JsonNode.Parse(File.ReadAllText(receipt))!.AsObject(); change(doc);
            File.WriteAllText(receipt, doc.ToJsonString());
        });
        ReceiptMutation("foreign-runtime-commit-rejected", doc => doc["source"]!["il2cpp_plus"]!["commit"] = new string('1', 40));
        ReceiptMutation("foreign-tool-id-rejected", doc => doc["installation"]!["toolPackageId"] = new string('2', 64));
        ReceiptMutation("surrogate-header-claim-rejected", doc => doc["externalHeaders"]!["surrogate"] = true);
        string generated = Path.Combine(installed, "hybridclr/generated/MethodBridge.cpp");
        byte[] originalGenerated = File.ReadAllBytes(generated);
        File.AppendAllText(generated, "\n// normal package generator output\n");
        Require("owned-generator-output-may-change", Verify() == 0);
        File.WriteAllBytes(generated, originalGenerated);
        Require("restored-installation-verifies", Verify() == 0);
        string config = Path.Combine(output, "new-config.json"), bundle = Path.Combine(package, "Tools~/DHE");
        Require("generate-package-config", Run("new-config", "-Output", config, "-ProjectPath", project, "-ToolchainRoot", bundle) == 0);
        using var configDoc = JsonDocument.Parse(File.ReadAllBytes(config));
        using var toolDoc = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(bundle, "dhe-toolchain-manifest.json")));
        Require("config-pins-real-bundle", configDoc.RootElement.GetProperty("expectedToolchainPackageId").GetString() == toolDoc.RootElement.GetProperty("packageId").GetString());
        Require("config-resolves-installed-tool", Path.GetFullPath(configDoc.RootElement.GetProperty("toolchainRoot").GetString()!) == Path.GetFullPath(bundle));
        Require("config-resolves-installation-receipt", Path.GetFullPath(configDoc.RootElement.GetProperty("runtimeManifestPath").GetString()!) == Path.GetFullPath(receipt));
        Require("exploratory-bundle-cannot-claim-release", Run("verify-package", "-PackageRoot", bundle, "-RequireRelease") != 0);

        JsonElement Identity(object value) => JsonSerializer.SerializeToElement(value);
        var emptyBase = Identity(new { aotMetadataSetId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Array.Empty<byte>())) });
        Require("legacy-empty-base-ignores-current-metadata-settings", Tool.ReadBaseAotMetadataNames(emptyBase, new[] { "mscorlib" }).Length == 0);
        var namedBase = Identity(new { aotMetadataAssemblyNames = new[] { "System" }, aotAssemblyNames = new[] { "System", "mscorlib" } });
        Require("base-metadata-selection-does-not-follow-project-changes", Tool.ReadBaseAotMetadataNames(namedBase, new[] { "mscorlib" }).SequenceEqual(new[] { "System" }));
        bool invalidSelectionRejected = false;
        try { Tool.ReadBaseAotMetadataNames(Identity(new { aotMetadataAssemblyNames = new[] { "System", "system" } }), Array.Empty<string>()); }
        catch { invalidSelectionRejected = true; }
        Require("duplicate-base-metadata-names-rejected", invalidSelectionRejected);

        int Exec(string executable, params string[] arguments)
        {
            var start = new ProcessStartInfo(executable) { WorkingDirectory = output, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60000)) { process.Kill(true); throw new TimeoutException(executable); }
            Task.WaitAll(stdout, stderr);
            return process.ExitCode;
        }
        string repository = Path.Combine(output, "svn-repository"), checkout = Path.Combine(output, "svn-checkout");
        Require("svn-fixture-created", Exec("svnadmin", "create", repository) == 0 &&
            Exec("svn", "checkout", new Uri(repository).AbsoluteUri, checkout) == 0);
        string svnPackage = Path.Combine(checkout, "com.code-philosophy.hybridclr@8.13.0");
        Directory.CreateDirectory(svnPackage);
        string svnFile = Path.Combine(svnPackage, "Example@file.txt"); File.WriteAllText(svnFile, "fixture");
        Require("svn-fixture-committed", Exec("svn", "add", Tool.SvnLiteralPath(svnPackage)) == 0 &&
            Exec("svn", "commit", "-m", "local fixture", Tool.SvnLiteralPath(checkout)) == 0);
        var tracked = typeof(Tool).GetMethod("IsTrackedPath", BindingFlags.Static | BindingFlags.NonPublic)!;
        Require("svn-package-suffix-tracked", (bool)tracked.Invoke(null, new object[] { checkout, svnPackage })!);
        Require("svn-file-at-character-tracked", (bool)tracked.Invoke(null, new object[] { checkout, svnFile })!);
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(new { passed = checks.Values.All(value => value), checks },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Package delivery checks passed: " + checks.Count);
        return 0;
    }
}
