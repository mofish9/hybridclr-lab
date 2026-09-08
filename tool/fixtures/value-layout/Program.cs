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
        RedirectStandardOutput = true, RedirectStandardError = true };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new IOException(executable);
    Task<string> stdout = process.StandardOutput.ReadToEndAsync(), stderr = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    string result = stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult();
    if (process.ExitCode != 0) throw new InvalidOperationException(result);
    return result;
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
        scope = "Current storage research probe; no DHE native guards or production workflow qualification",
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
        return new { name, baseSha256 = old.AssemblySha256, currentSha256 = next.AssemblySha256,
            types = probeImpact.Layouts.Where(type => !type.RequiresOrdinaryAotBridge && type.AssemblyName == name)
                .Select(type => type.CurrentTypeToken).Distinct().OrderBy(token => token).ToArray(),
            methods = probeImpact.Methods.Where(method => method.AssemblyName == name && method.Decision != "native-abi-bridge")
                .Select(method => method.CurrentMethodToken).Concat(entryTokens).Distinct().OrderBy(token => token).ToArray(),
            explicitProbeEntryTokens = entryTokens };
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
