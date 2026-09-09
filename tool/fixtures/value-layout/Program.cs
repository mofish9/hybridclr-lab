using System.Diagnostics;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using HybridCLR.DheTool;

string[] assemblies = { "HybridCLR.ValueLayoutModel", "HybridCLR.ValueLayoutOther", "HybridCLR.ValueLayoutConsumer" };
const string nativeAssembly = "HybridCLR.ValueLayoutNative";
var json = new JsonSerializerOptions { WriteIndented = true };
string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
void NewOutput(string path)
{
    if (File.Exists(path) || Directory.Exists(path)) throw new IOException("Output must be new: " + path);
}
string Run(string executable, string root, params string[] arguments)
{
    var start = new ProcessStartInfo(executable) { WorkingDirectory = root, UseShellExecute = false,
        RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        WindowStyle = ProcessWindowStyle.Hidden };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException(executable);
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    Console.WriteLine("Process " + process.Id + ": " + Path.GetFileName(executable));
    if (!process.WaitForExit(20 * 60 * 1000))
    {
        process.Kill(entireProcessTree: true);
        throw new TimeoutException(executable);
    }
    string result = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    if (process.ExitCode != 0) throw new InvalidOperationException(result);
    return result;
}
if (args.Length == 2 && (args[0] == "probe-audit" || args[0] == "api-audit"))
{
    bool publicApi = args[0] == "api-audit";
    string root = Path.GetFullPath(args[1]);
    string reportPath = Path.Combine(root, "audit.json"); NewOutput(reportPath);
    JsonElement Read(string path) => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, path)));
    var auditChecks = new Dictionary<string, bool>();
    string runtimePath = (publicApi ? "runtime-reflection" : "runtime-fixed") + "/DHE-Unity2022/runtime-manifest.json";
    string nativePath = (publicApi ? "native-reflection" : "native-fixed") + "/DHE-Unity2022/native-gate.json";
    string oldBase = publicApi ? "base-old-reflection" : "base-old-fixed";
    var runtime = Read(runtimePath);
    string runtimeHash = Hash(Path.Combine(root, runtimePath));
    var native = Read(nativePath);
    auditChecks["real-headers-native"] = native.GetProperty("passed").GetBoolean() && native.GetProperty("mergeReady").GetBoolean() &&
        !native.GetProperty("surrogateExternalHeadersUsed").GetBoolean() && native.GetProperty("nativeExitCode").GetInt32() == 0 &&
        native.GetProperty("runtimeManifestSha256").GetString()!.Equals(runtimeHash, StringComparison.OrdinalIgnoreCase);
    var runCases = new[] {
        (Base: oldBase, Payload: "payload-method-old", Report: "method-old-fixed-result.json", Revision: 73),
        (Base: oldBase, Payload: "payload-noop-old", Report: "noop-old-result.json", Revision: 41),
        (Base: oldBase, Payload: "payload-latest-old", Report: publicApi ? "latest-old-reflection-result.json" : "latest-old-result.json", Revision: 73),
        (Base: "base-new", Payload: "payload-latest-new", Report: "latest-new-result.json", Revision: 73),
        (Base: "base-new", Payload: "payload-noop-new", Report: "noop-new-result.json", Revision: 41),
    };
    foreach (string built in runCases.Select(item => item.Base).Distinct())
    {
        var binding = Read(built + "/build-binding.json");
        var guards = Read(built + "/native-manifest.json");
        auditChecks[built + "-native-binding"] = binding.GetProperty("runtimeManifestSha256").GetString() == runtimeHash &&
            Hash(Path.Combine(root, built, "runtime-manifest.json")) == runtimeHash &&
            Hash(Path.Combine(root, built, "player-executable", "GameAssembly.dll")) == binding.GetProperty("gameAssemblySha256").GetString() &&
            Hash(Path.Combine(root, built, "player-executable", "CurrentStorage.exe")) == binding.GetProperty("playerSha256").GetString() &&
            Hash(Path.Combine(root, built, "native-manifest.json")) == binding.GetProperty("nativeManifestSha256").GetString() &&
            guards.GetProperty("guardMode").GetString() == "universal" &&
            guards.GetProperty("unsupportedGuardedMethodCount").GetInt32() == 0 &&
            guards.GetProperty("supportedGuardedMethodCount").GetInt32() > 0;
    }
    foreach (var item in runCases)
    {
        var result = Read(item.Report);
        if (publicApi)
            auditChecks[item.Report + "-public-api"] = result.GetProperty("rejectedInvalidPlans").GetInt32() == 5 &&
                result.GetProperty("reflectionSignaturePassed").GetBoolean() && result.GetProperty("reflectionValueCallsPassed").GetBoolean() &&
                result.GetProperty("scope").GetString()!.StartsWith("Public RuntimeApi", StringComparison.Ordinal);
        string[] records = result.GetProperty("records").EnumerateArray().Select(record => record.GetString()!).ToArray();
        var reference = Read(item.Payload == "payload-method-old" ? "reference-method.json" :
            item.Payload == "payload-noop-old" ? "reference-base.json" : "reference-layout.json");
        string[] expectedRecords = reference.GetProperty("records").EnumerateArray().Select(record => record.GetString()!).ToArray();
        auditChecks[item.Report + "-correctness"] = result.GetProperty("passed").GetBoolean() && result.GetProperty("loadCode").GetInt32() == 0 &&
            result.GetProperty("consumerPassed").GetBoolean() && result.GetProperty("reflectionPassed").GetBoolean() &&
            reference.GetProperty("passed").GetBoolean() && records.Length == 14 && records.SequenceEqual(expectedRecords);
        auditChecks[item.Report + "-dispatch"] = result.GetProperty("directRevision").GetInt32() == item.Revision &&
            result.GetProperty("reflectedRevision").GetInt32() == item.Revision && result.GetProperty("revisionPassed").GetBoolean() &&
            result.GetProperty("revisionInterpreterEntries").GetInt32() == (item.Revision == 73 ? 1 : 0) &&
            result.GetProperty("unchangedAotEntries").GetInt32() == 1;
        var plan = Read(item.Payload + "/plan.json");
        auditChecks[item.Report + "-base-mv"] = plan.GetProperty("assemblies").EnumerateArray().All(selection =>
        {
            string name = selection.GetProperty("name").GetString()!;
            var snapshot = MetaVersionSnapshot.Create(Path.Combine(root, item.Base, "baseline", name + ".dll"));
            return snapshot.AssemblySha256.Equals(selection.GetProperty("baseSha256").GetString(), StringComparison.OrdinalIgnoreCase) &&
                snapshot.ToBinary().SequenceEqual(File.ReadAllBytes(Path.Combine(root, item.Payload, "base", name + ".mv")));
        });
    }
    auditChecks["different-base-layouts"] = MetaVersionSnapshot.Create(Path.Combine(root, oldBase, "baseline/HybridCLR.ValueLayoutModel.dll"))
        .Types.Single(type => type.Identity == "HybridCLR.Lab.ValueLayout.Payload").Version !=
        MetaVersionSnapshot.Create(Path.Combine(root, "base-new/baseline/HybridCLR.ValueLayoutModel.dll"))
        .Types.Single(type => type.Identity == "HybridCLR.Lab.ValueLayout.Payload").Version;
    auditChecks["same-current-dll-and-mv"] = assemblies.All(name => new[] { ".dll", ".mv" }.All(extension =>
        Hash(Path.Combine(root, "payload-latest-old/current", name + extension)) == Hash(Path.Combine(root, "payload-latest-new/current", name + extension))));
    string rejected = Path.Combine(root, "wrong-base-result.json"); NewOutput(rejected);
    bool wrongBaseRejected = false;
    try { Run("dotnet", root, typeof(MetaVersionSnapshot).Assembly.Location, "probe-run",
        Path.Combine(root, "base-new/player-executable"), Path.Combine(root, "payload-latest-old"), rejected, "73"); }
    catch (InvalidOperationException error) { wrongBaseRejected = error.Message.Contains("Payload Base does not match the built Player"); }
    auditChecks["wrong-base-rejected-before-player"] = wrongBaseRejected && !File.Exists(rejected);
    bool wrongExpectedRejected = false;
    string wrongExpected = Path.Combine(root, "wrong-expected-result.json"); NewOutput(wrongExpected);
    try { Run("dotnet", root, typeof(MetaVersionSnapshot).Assembly.Location, "probe-run", Path.Combine(root, "base-new/player-executable"),
        Path.Combine(root, "payload-latest-new"), wrongExpected, "41"); }
    catch (InvalidOperationException) { wrongExpectedRejected = true; }
    auditChecks["updated-behaviour-required"] = wrongExpectedRejected && File.Exists(wrongExpected) &&
        !Read("wrong-expected-result.json").GetProperty("passed").GetBoolean() &&
        Read("wrong-expected-result.json").GetProperty("directRevision").GetInt32() == 73;
    File.WriteAllText(reportPath, JsonSerializer.Serialize(new {
        passed = auditChecks.Values.All(value => value),
        scope = publicApi ? "Public RuntimeApi and ordinary reflection; resource build/staging/loader not qualified" :
            "Guarded Windows storage probe, not public resource workflow qualification",
        auditChecks, runtimeSource = runtime.GetProperty("source"), runtimeManifestSha256 = runtimeHash,
        nativeGateSha256 = Hash(Path.Combine(root, nativePath)),
        toolSha256 = Hash(typeof(MetaVersionSnapshot).Assembly.Location),
        results = runCases.Select(item => new { path = item.Report, sha256 = Hash(Path.Combine(root, item.Report)) }).ToArray(),
    }, json));
    foreach (var check in auditChecks) Console.WriteLine(check.Key + ": " + check.Value);
    return auditChecks.Values.All(value => value) ? 0 : 1;
}
if (args.Length == 6 && args[0] == "probe-build")
{
    string lab = Path.GetFullPath(args[1]), editor = Path.GetFullPath(args[2]),
        project = Path.GetFullPath(args[3]), runtime = Path.GetFullPath(args[4]), output = Path.GetFullPath(args[5]);
    NewOutput(output);
    Directory.CreateDirectory(output);
    string manifest = Path.Combine(Path.GetDirectoryName(runtime)!, "runtime-manifest.json");
    File.Copy(manifest, Path.Combine(output, "runtime-manifest.json"));
    string runtimeHash = Hash(manifest);
    Console.WriteLine(Run(editor, lab, "-batchmode", "-quit", "-nographics", "-projectPath", project,
        "-executeMethod", "HybridCLR.Lab.Editor.CurrentStorageProbeBuild.Prepare", "-probeRuntime", runtime,
        "-probeOutput", output, "-logFile", Path.Combine(output, "prepare.log")));
    var camel = new JsonSerializerOptions(json) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    var entries = assemblies.Select(name =>
    {
        string dll = Path.Combine(output, "baseline", name + ".dll");
        var snapshot = MetaVersionSnapshot.Create(dll);
        string mvJson = Path.Combine(output, name + ".mv.json");
        File.WriteAllText(mvJson, JsonSerializer.Serialize(snapshot.ToJson(dll), camel));
        return new { assemblyName = name, baseMetaVersionJson = mvJson, baselineSha256 = Hash(dll) };
    }).ToArray();
    string guards = Path.Combine(output, "guard-plan.json");
    File.WriteAllText(guards, JsonSerializer.Serialize(new { schemaVersion = 1, complete = true, assemblies = entries }, json));
    Console.WriteLine(Run(editor, lab, "-batchmode", "-quit", "-nographics", "-projectPath", project,
        "-executeMethod", "HybridCLR.Lab.Editor.CurrentStorageProbeBuild.Build", "-probeOutput", output,
        "-probePlayerRoot", Path.Combine(output, "player-executable"), "-probeGuardsPlan", guards,
        "-logFile", Path.Combine(output, "build.log")));
    foreach (var entry in entries)
        if (Hash(Path.Combine(output, "baseline", entry.assemblyName + ".dll")) != entry.baselineSha256)
            throw new InvalidDataException("Final Base differs from guarded metadata: " + entry.assemblyName);
    if (Hash(manifest) != runtimeHash) throw new InvalidDataException("Runtime manifest changed during build.");
    File.WriteAllText(Path.Combine(output, "build-binding.json"), JsonSerializer.Serialize(new
    {
        labHead = Run("git", lab, "rev-parse", "HEAD").Trim(), runtimeManifestSha256 = runtimeHash,
        playerSha256 = Hash(Path.Combine(output, "player-executable", "CurrentStorage.exe")),
        gameAssemblySha256 = Hash(Path.Combine(output, "player-executable", "GameAssembly.dll")),
        nativeManifestSha256 = Hash(Path.Combine(output, "native-manifest.json")), assemblies = entries,
    }, json));
    Console.WriteLine(output); return 0;
}
if (args.Length == 5 && args[0] == "probe-run")
{
    string playerRoot = Path.GetFullPath(args[1]), payload = Path.GetFullPath(args[2]), result = Path.GetFullPath(args[3]);
    NewOutput(result);
    string output = Path.GetDirectoryName(playerRoot)!;
    using var binding = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "build-binding.json")));
    foreach (var item in new[] { (File: "CurrentStorage.exe", Key: "playerSha256"), (File: "GameAssembly.dll", Key: "gameAssemblySha256") })
        if (Hash(Path.Combine(playerRoot, item.File)) != binding.RootElement.GetProperty(item.Key).GetString())
            throw new InvalidDataException("Player identity mismatch: " + item.File);
    using var plan = JsonDocument.Parse(File.ReadAllText(Path.Combine(payload, "plan.json")));
    foreach (var selection in plan.RootElement.GetProperty("assemblies").EnumerateArray())
    {
        string name = selection.GetProperty("name").GetString()!;
        var embedded = binding.RootElement.GetProperty("assemblies").EnumerateArray().Single(item =>
            item.GetProperty("assemblyName").GetString() == name);
        if (!string.Equals(embedded.GetProperty("baselineSha256").GetString(),
                selection.GetProperty("baseSha256").GetString(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Payload Base does not match the built Player: " + name);
        byte[] expected = MetaVersionSnapshot.Create(Path.Combine(output, "baseline", name + ".dll")).ToBinary();
        if (!expected.SequenceEqual(File.ReadAllBytes(Path.Combine(payload, "base", name + ".mv"))))
            throw new InvalidDataException("Base MV mismatch: " + name);
    }
    Console.WriteLine(Run(Path.Combine(playerRoot, "CurrentStorage.exe"), playerRoot,
        "-batchmode", "-nographics", "-logFile", result + ".log", "-currentStoragePayload", payload,
        "-currentStorageResult", result, "-expectedRevision", args[4], "-requireGuards", "true"));
    using var report = JsonDocument.Parse(File.ReadAllText(result));
    if (!report.RootElement.GetProperty("passed").GetBoolean()) throw new InvalidDataException("Player failed: " + result);
    Console.WriteLine(File.ReadAllText(result)); return 0;
}
if (args.Length == 3 && args[0] == "probe-refresh")
{
    string lab = Path.GetFullPath(args[1]), project = Path.GetFullPath(args[2]);
    if (!File.Exists(Path.Combine(project, "probe-source.json"))) throw new IOException("Not a probe project.");
    string sourceHead = Run("git", lab, "rev-parse", "HEAD").Trim();
    string receipt = Path.Combine(project, "probe-refresh-" + sourceHead + ".json"); NewOutput(receipt);
    var refreshed = new[] { (Name: "CurrentStorageRuntime.cs", Folder: "Assets"),
        (Name: "CurrentStorageProbeBuild.cs", Folder: "Assets/Editor") }.Select(item =>
    {
        string source = Path.Combine(lab, "tool/fixtures/value-layout/Unity", item.Name),
            target = Path.Combine(project, item.Folder, item.Name);
        string beforeSha256 = Hash(target); File.Copy(source, target, true);
        return new { path = target, beforeSha256, afterSha256 = Hash(target) };
    }).ToArray();
    File.WriteAllText(receipt, JsonSerializer.Serialize(new { sourceHead,
        sourceChanges = Run("git", lab, "status", "--porcelain").Trim(), refreshed }, json));
    Console.WriteLine(receipt); return 0;
}
if (args.Length == 6 && args[0] == "probe-project")
{
    string lab = Path.GetFullPath(args[1]), package = Path.GetFullPath(args[2]),
        fixture = Path.GetFullPath(args[4]), destination = Path.GetFullPath(args[5]);
    NewOutput(destination);
    string engineVersion = args[3] == "Unity2022Fgs" ? "2022.3.62f3" :
        args[3] == "Tuanjie2022Fgs" ? "2022.3.62t12" : throw new ArgumentException("Unknown engine.");
    void CopyTree(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            if (Path.GetFileName(file) != ".git") File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            if (Path.GetFileName(directory) != ".git") CopyTree(directory, Path.Combine(target, Path.GetFileName(directory)));
    }
    CopyTree(Path.Combine(lab, "unity2021-dhe-demo/ProjectSettings"), Path.Combine(destination, "ProjectSettings"));
    CopyTree(package, Path.Combine(destination, "Packages/com.code-philosophy.hybridclr"));
    var packageManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(lab, "unity2021-dhe-demo/Packages/manifest.json")))!;
    packageManifest["dependencies"]!.AsObject().Remove("com.code-philosophy.hybridclr");
    if (args[3] == "Unity2022Fgs") packageManifest["dependencies"]!.AsObject().Remove("com.unity.modules.infinity");
    File.WriteAllText(Path.Combine(destination, "Packages/manifest.json"), packageManifest.ToJsonString(json));
    File.WriteAllText(Path.Combine(destination, "ProjectSettings/ProjectVersion.txt"), "m_EditorVersion: " + engineVersion + "\n");
    Directory.CreateDirectory(Path.Combine(destination, "Assets/Editor"));
    Directory.CreateDirectory(Path.Combine(destination, "Assets/Plugins/ValueLayout"));
    foreach (string name in assemblies.Append(nativeAssembly))
        File.Copy(Path.Combine(fixture, name + ".dll"), Path.Combine(destination, "Assets/Plugins/ValueLayout", name + ".dll"));
    string templateRoot = Path.Combine(lab, "tool/fixtures/value-layout/Unity");
    File.Copy(Path.Combine(templateRoot, "CurrentStorageRuntime.cs"), Path.Combine(destination, "Assets/CurrentStorageRuntime.cs"));
    File.Copy(Path.Combine(templateRoot, "CurrentStorageProbeBuild.cs"), Path.Combine(destination, "Assets/Editor/CurrentStorageProbeBuild.cs"));
    File.WriteAllText(Path.Combine(destination, "Assets/link.xml"), "<linker>" +
        string.Join("", assemblies.Append(nativeAssembly).Append("Assembly-CSharp").Select(name => "<assembly fullname=\"" + name + "\" preserve=\"all\"/>")) + "</linker>");
    File.WriteAllText(Path.Combine(destination, "probe-source.json"), JsonSerializer.Serialize(new
    {
        scope = "Current storage API fixture; build output binds native guards separately; resource workflow not qualified",
        labHead = Run("git", lab, "rev-parse", "HEAD").Trim(), labChanges = Run("git", lab, "status", "--porcelain").Trim(),
        packageHead = Run("git", package, "rev-parse", "HEAD").Trim(), engineVersion,
        inputAssemblies = assemblies.Append(nativeAssembly).Select(name => new { name, sha256 = Hash(Path.Combine(fixture, name + ".dll")) }).ToArray(),
    }, json));
    Console.WriteLine(destination);
    return 0;
}
if (args.Length == 4 && args[0] == "probe-payload")
{
    string probeBase = Path.GetFullPath(args[1]), probeCurrent = Path.GetFullPath(args[2]), probeOutput = Path.GetFullPath(args[3]);
    NewOutput(probeOutput);
    var probeImpact = DheValueLayoutImpact.Analyze(assemblies.Select(name => Path.Combine(probeBase, name + ".dll")),
        assemblies.Select(name => Path.Combine(probeCurrent, name + ".dll")), new[] { Path.Combine(probeBase, nativeAssembly + ".dll") });
    var selections = assemblies.Select(name =>
    {
        string beforeFile = Path.Combine(probeBase, name + ".dll"), afterFile = Path.Combine(probeCurrent, name + ".dll");
        var old = MetaVersionSnapshot.Create(beforeFile); var next = MetaVersionSnapshot.Create(afterFile);
        old.WriteBinary(Path.Combine(probeOutput, "base", name + ".mv"));
        next.WriteBinary(Path.Combine(probeOutput, "current", name + ".mv"));
        File.Copy(afterFile, Path.Combine(probeOutput, "current", name + ".dll"));
        // The probe explicitly enters its static, argument-free roots with
        // Current metadata. Full native entry/caller coverage remains a gate.
        uint[] entryTokens = probeImpact.ChangedValueTypes.Length == 0 ? Array.Empty<uint>() :
            next.Methods.Where(method => method.Name == "Run" &&
                (method.DeclaringType == "HybridCLR.Lab.ValueLayout.ValueLayoutProbe" ||
                 method.DeclaringType == "HybridCLR.Lab.ValueLayoutConsumer.Calls"))
                .Select(method => method.Token).ToArray();
        var oldMethods = old.Methods.ToDictionary(method => method.StableId, StringComparer.Ordinal);
        uint[] changedMethodTokens = next.Methods
            .Where(method => !oldMethods.TryGetValue(method.StableId, out var previous) ||
                !string.Equals(previous.Version, method.Version, StringComparison.Ordinal))
            .Select(method => method.Token).ToArray();
        return new { name, baseSha256 = old.AssemblySha256, currentSha256 = next.AssemblySha256,
            types = probeImpact.Layouts.Where(type => !type.RequiresOrdinaryAotBridge && type.AssemblyName == name)
                .Select(type => type.CurrentTypeToken).Distinct().OrderBy(token => token).ToArray(),
            methods = probeImpact.Methods.Where(method => method.AssemblyName == name && method.Decision != "native-abi-bridge")
                .Select(method => method.CurrentMethodToken).Concat(changedMethodTokens).Concat(entryTokens)
                .Distinct().OrderBy(token => token).ToArray(),
            changedMethodTokens, explicitProbeEntryTokens = entryTokens };
    }).ToArray();
    File.WriteAllText(Path.Combine(probeOutput, "plan.json"), JsonSerializer.Serialize(new
    {
        format = "hybridclr.dhe-current-storage-probe", releaseReady = false, assemblies = selections,
        ordinaryAotSha256 = Hash(Path.Combine(probeBase, nativeAssembly + ".dll")), impact = probeImpact,
    }, json));
    Console.WriteLine(Path.Combine(probeOutput, "plan.json"));
    return 0;
}
if (args.Length == 3 && args[0] == "build")
{
    string lab = Path.GetFullPath(args[1]), output = Path.GetFullPath(args[2]); NewOutput(output);
    foreach (string version in new[] { "base", "current" })
    {
        string root = Path.Combine(output, version);
        Console.WriteLine(Run("dotnet", lab, "build", "managed-cases/HybridCLR.ValueLayoutModel/HybridCLR.ValueLayoutModel.csproj",
            "-c", "Release", "--no-incremental", "--nologo", "-o", root,
            "-p:DefineConstants=" + (version == "base" ? "DHE_VALUE_LAYOUT_BASE" : "DHE_VALUE_LAYOUT_CURRENT")));
    }
    Console.WriteLine(Run("dotnet", lab, "build", "managed-cases/HybridCLR.ValueLayoutConsumer/HybridCLR.ValueLayoutConsumer.csproj",
        "-c", "Release", "--no-incremental", "--nologo", "-o", Path.Combine(output, "consumer"),
        "-p:DheValueLayoutBaseRoot=" + Path.Combine(output, "base")));
    foreach (string name in assemblies.Skip(1).Append(nativeAssembly))
        foreach (string version in new[] { "base", "current" })
            File.Copy(Path.Combine(output, "consumer", name + ".dll"), Path.Combine(output, version, name + ".dll"));
    File.WriteAllText(Path.Combine(output, "build.json"), JsonSerializer.Serialize(new
    {
        lab, sourceHead = Run("git", lab, "rev-parse", "HEAD").Trim(),
        sourceChanges = Run("git", lab, "status", "--porcelain").Trim(),
        unchangedConsumer = Hash(Path.Combine(output, "base", assemblies[2] + ".dll")) ==
            Hash(Path.Combine(output, "current", assemblies[2] + ".dll")),
        inputs = new[] { "base", "current" }.Select(version => new
        {
            version, assemblies = assemblies.Append(nativeAssembly).Select(name => new { name,
                sha256 = Hash(Path.Combine(output, version, name + ".dll")) }).ToArray()
        }).ToArray(),
    }, json));
    return 0;
}
if (args.Length == 3 && args[0] == "reference")
{
    string root = Path.GetFullPath(args[1]), report = Path.GetFullPath(args[2]); NewOutput(report);
    AssemblyLoadContext.Default.Resolving += (_, name) =>
        File.Exists(Path.Combine(root, name.Name + ".dll"))
            ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, name.Name + ".dll")) : null;
    var model = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, assemblies[0] + ".dll"));
    var records = (string[])model.GetType("HybridCLR.Lab.ValueLayout.ValueLayoutProbe", true)!.GetMethod("Run")!.Invoke(null, null)!;
    var consumer = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, assemblies[2] + ".dll"));
    bool unchangedConsumer = (bool)consumer.GetType("HybridCLR.Lab.ValueLayoutConsumer.Calls", true)!.GetMethod("Run")!.Invoke(null, null)!;
    bool passed = records.Length == 14 && records.All(record => record.EndsWith("\tpassed", StringComparison.Ordinal)) && unchangedConsumer;
    Directory.CreateDirectory(Path.GetDirectoryName(report)!);
    File.WriteAllText(report, JsonSerializer.Serialize(new
    {
        passed, scope = "CLR value semantics; not a DHE Player result", root, records, unchangedConsumer,
        assemblies = assemblies.Append(nativeAssembly).Select(name => new { name, sha256 = Hash(Path.Combine(root, name + ".dll")) }).ToArray(),
    }, json));
    foreach (string record in records) Console.WriteLine(record);
    Console.WriteLine("unchanged-consumer\t" + unchangedConsumer);
    return passed ? 0 : 1;
}
if (args.Length != 4 || args[0] != "analyze")
    throw new ArgumentException("build <lab> <new output>; reference <DLL root> <new report>; analyze <Base DLL root> <Current DLL root> <new report>");
string baseline = Path.GetFullPath(args[1]), current = Path.GetFullPath(args[2]), outputReport = Path.GetFullPath(args[3]);
NewOutput(outputReport);
string[] Paths(string root) => assemblies.Select(name => Path.Combine(root, name + ".dll")).ToArray();
var before = Paths(baseline).Select(MetaVersionSnapshot.Create).ToDictionary(value => value.AssemblyName);
var after = Paths(current).Select(MetaVersionSnapshot.Create).ToDictionary(value => value.AssemblyName);
string[] nativePaths = { Path.Combine(baseline, nativeAssembly + ".dll") };
var impact = DheValueLayoutImpact.Analyze(Paths(baseline), Paths(current), nativePaths);
var comparisons = assemblies.ToDictionary(name => name, name => ResourceUpdateCompatibility.Analyze(before[name], after[name],
    currentAssemblySet: after.Values));
var checks = new Dictionary<string, bool>();
const string owner = "HybridCLR.ValueLayoutConsumer", prefix = "HybridCLR.Lab.ValueLayoutConsumer.Calls";
var methods = after[owner].Methods.Where(method => method.DeclaringType == prefix).ToDictionary(method => method.Name);
DheValueLayoutMethodImpact? Decision(string name) => impact.Methods.SingleOrDefault(method =>
    method.AssemblyName == owner && method.MethodIdentity == methods[name].Identity);
string[] affected = { "DirectCopy", "NestedCopy", "LocalNestedCopy", "GenericCopy", "NullableCopy", "ForwardBox",
    "GenericForwardBox", "ArrayElement", "RefRoundTrip", "ContainerNeighbor", "GenericContainerNeighbor", "NativeRoundTrip", "FullCopyMatches" };
foreach (string name in affected) checks["impact-" + name] = Decision(name)?.Decision == "interpret";
foreach (string name in new[] { "Unrelated", "UnchangedCopy", "OtherAssemblyCopy", "UnchangedGenericCopy", "ExternalGenericInt", "ReferenceOnly" })
    checks["retained-" + name] = Decision(name) == null;
checks["open-generic-context-explicit"] = Decision("OpenGenericCopy")?.Decision == "inspect-generic-context";
checks["all-five-direct-value-layouts-found"] = impact.ChangedValueTypes.Length == 5;
checks["cross-assembly-nested-layout-found"] = impact.Layouts.Any(type => type.TypeIdentity == owner + "|HybridCLR.Lab.ValueLayoutConsumer.LocalWrapper");
checks["reference-container-layout-found"] = impact.Layouts.Any(type => type.TypeIdentity.EndsWith("|HybridCLR.Lab.ValueLayout.InlineOwner"));
checks["derived-reference-layout-found"] = impact.Layouts.Any(type => type.TypeIdentity.EndsWith("|HybridCLR.Lab.ValueLayout.InlineChild"));
checks["reference-indirection-does-not-grow-owner"] = !impact.Layouts.Any(type => type.TypeIdentity.EndsWith("|HybridCLR.Lab.ValueLayout.ReferenceOwner"));
checks["same-full-name-other-assembly-is-unaffected"] = !impact.ChangedValueTypes.Any(type => type.StartsWith("HybridCLR.ValueLayoutOther|"));
checks["ordinary-inline-layout-needs-bridge"] = impact.Layouts.Any(type =>
    type.TypeIdentity == nativeAssembly + "|HybridCLR.Lab.ValueLayoutNative.NativeInlineOwner" && type.RequiresOrdinaryAotBridge);
checks["ordinary-methods-never-treated-as-hotfix"] = impact.Methods.Any(method => method.AssemblyName == nativeAssembly) &&
    impact.Methods.Where(method => method.AssemblyName == nativeAssembly).All(method => method.Decision == "native-abi-bridge");
checks["ordinary-dll-reused"] = Hash(nativePaths[0]) == Hash(Path.Combine(current, nativeAssembly + ".dll"));
checks["consumer-dll-reused"] = Hash(Path.Combine(baseline, owner + ".dll")) == Hash(Path.Combine(current, owner + ".dll"));
checks["current-workflow-still-rejects-unsupported-storage"] = !comparisons[assemblies[0]].Compatible &&
    comparisons[assemblies[0]].UnsupportedChanges.Any(reason => reason.StartsWith("added-instance-field-on-existing-value-type:"));
var riskVersions = affected.Select(name =>
{
    var old = before[owner].Methods.Single(method => method.StableId == methods[name].StableId);
    return new { name, bodyUnchanged = old.BodyVersion == methods[name].BodyVersion, versionUnchanged = old.Version == methods[name].Version };
}).ToArray();
checks["existing-method-fingerprints-miss-cross-assembly-layout-risk"] = riskVersions.All(value => value.bodyUnchanged && value.versionUnchanged);
var noOp = DheValueLayoutImpact.Analyze(Paths(baseline), Paths(baseline), nativePaths);
checks["no-op-has-no-layout-obligations"] = noOp.ChangedValueTypes.Length == 0 && noOp.Methods.Length == 0 && noOp.Layouts.Length == 0;
checks["input-order-independent"] = JsonSerializer.Serialize(impact) == JsonSerializer.Serialize(
    DheValueLayoutImpact.Analyze(Paths(baseline).Reverse(), Paths(current).Reverse(), nativePaths));
bool incompleteRejected = false;
try { DheValueLayoutImpact.Analyze(Paths(baseline), Paths(current).Skip(1), nativePaths); }
catch (InvalidDataException) { incompleteRejected = true; }
checks["incomplete-hotfix-set-rejected"] = incompleteRejected;
bool analysisPassed = checks.Values.All(value => value);
Directory.CreateDirectory(Path.GetDirectoryName(outputReport)!);
File.WriteAllText(outputReport, JsonSerializer.Serialize(new
{
    passed = analysisPassed, scope = "Layout impact planning and rejection reproduction; runtime value-layout support is not implemented",
    currentRuntimeSupportsValueLayout = false, baseline, current, checks, riskVersions, impact,
    rejectedChanges = comparisons[assemblies[0]].UnsupportedChanges,
}, json));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return analysisPassed ? 0 : 1;
