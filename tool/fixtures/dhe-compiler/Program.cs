using System.Diagnostics;
using System.Text.Json;
using HybridCLR.Editor.Il2CppDef;

if (args.Length == 4 && args[0] == "crash")
{
    var abandoned = Open(args[1], args[2], args[3]);
    abandoned.Validate();
    Environment.Exit(73);
}
if (args.Length != 2) throw new ArgumentException("Use <original compiler DLL> <new output root>.");
string original = Path.GetFullPath(args[0]), root = Path.GetFullPath(args[1]);
if (File.Exists(root) || Directory.Exists(root)) throw new IOException("Test output must be new.");
string project = Path.Combine(root, "project");
string local = Path.Combine(project, "HybridCLRData", "il2cpp", "build", "deploy", "Unity.IL2CPP.dll");
string argumentsFile = Path.Combine(project, "arguments.txt");
Directory.CreateDirectory(Path.GetDirectoryName(local));
File.Copy(original, local);
File.Copy(Path.Combine(Path.GetDirectoryName(original), "Unity.IL2CPP.DataModel.dll"),
    Path.Combine(Path.GetDirectoryName(local), "Unity.IL2CPP.DataModel.dll"));
File.WriteAllText(argumentsFile, "--test-existing-option");
string before = Hash(original);
var results = new Dictionary<string, bool>();
string cache = Path.Combine(project, "Library", "HybridCLR", "DHE", "Compiler");
string journal = Path.Combine(cache, "transaction.json");
string generated = Path.Combine(project, "generated");
Directory.CreateDirectory(generated);
string cpp = Path.Combine(generated, "Fixture.cpp");
File.WriteAllText(cpp, "void fixture() {}\n");
string identity;
string effective;
using (var session = Open(original, project, local))
{
    identity = UnityEngine.JsonUtility.ToJson(session.Identity);
    effective = session.Identity.additionalIl2CppArgs;
    Check("patched-compiler-installed", Hash(local) == session.Identity.compilerSha256 && Hash(local) != before);
    Check("divide-checks-enabled", effective == "--test-existing-option --enable-divide-by-zero-check");
    byte[] dataModel = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(local), "Unity.IL2CPP.DataModel.dll"));
    Reject("repeated-package-patch-rejected", () => DheAotCompilerPatch.Create(File.ReadAllBytes(local), dataModel));
    Reject("wrong-datamodel-rejected", () => DheAotCompilerPatch.Create(File.ReadAllBytes(original), File.ReadAllBytes(original)));
    Reject("parallel-session-rejected", () => { using var other = Open(original, project, local); });
    Reject("missing-generation-rejected", () => session.RequireGeneration(generated));
    session.RecordGeneration(generated);
    session.RequireGeneration(generated);
    Check("generated-source-provenance", true);
    if (OperatingSystem.IsWindows())
    {
        string recordPath = Path.Combine(cache, "generation.json");
        using (var held = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var release = Task.Run(async () => { await Task.Delay(200); held.Dispose(); });
            session.RecordGeneration(generated);
            release.GetAwaiter().GetResult();
        }
        session.RequireGeneration(generated);
        Check("transient-generation-read-lock-retried", true);
        byte[] originalRecord = File.ReadAllBytes(recordPath);
        using (var held = new FileStream(recordPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Reject("persistent-generation-read-lock-rejected", () => session.RecordGeneration(generated));
        Check("failed-atomic-replace-preserves-old-record", File.ReadAllBytes(recordPath).SequenceEqual(originalRecord) &&
            Directory.GetFiles(cache, "generation.json.dhe-*.tmp").Length == 0);
    }
    File.AppendAllText(cpp, "void changed() {}\n");
    Reject("stale-generated-source-rejected", () => session.RequireGeneration(generated));
    File.WriteAllText(cpp, "void fixture() {}\n");
    string link = Path.Combine(generated, "Linked.cpp");
    File.CreateSymbolicLink(link, cpp);
    Reject("linked-generated-source-rejected", () => session.RecordGeneration(generated));
    File.Delete(link);
    string generationPath = Path.Combine(cache, "generation.json");
    string generation = File.ReadAllText(generationPath);
    File.WriteAllText(generationPath, generation.Replace("dhe-aot-codegen-v1", "unknown-codegen"));
    Reject("wrong-compiler-provenance-rejected", () => session.RequireGeneration(generated));
    File.WriteAllText(generationPath, generation);
}
Check("normal-compiler-restored", Hash(local) == before);
Check("normal-options-restored", File.ReadAllText(argumentsFile) == "--test-existing-option");
Check("completed-journal-cleared", !File.Exists(journal));
try
{
    using var session = Open(original, project, local);
    session.RequireGeneration(generated);
    Check("reopened-identity-stable", UnityEngine.JsonUtility.ToJson(session.Identity) == identity);
    throw new ApplicationException("simulated build failure");
}
catch (ApplicationException) { }
Check("exception-compiler-restored", Hash(local) == before);
Check("exception-options-restored", File.ReadAllText(argumentsFile) == "--test-existing-option");

var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardOutput = true, RedirectStandardError = true };
foreach (string argument in new[] { typeof(Program).Assembly.Location, "crash", original, project, local }) start.ArgumentList.Add(argument);
using (Process child = Process.Start(start))
{
    Task<string> stdout = child.StandardOutput.ReadToEndAsync(), stderr = child.StandardError.ReadToEndAsync();
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    try { await child.WaitForExitAsync(timeout.Token); }
    catch (OperationCanceledException)
    {
        if (!child.HasExited) child.Kill(true);
        await child.WaitForExitAsync();
        throw;
    }
    File.WriteAllText(Path.Combine(root, "interrupted-process.log"), await stdout + await stderr);
    Check("process-interrupted-with-live-journal", child.ExitCode == 73 && File.Exists(journal) && Hash(local) != before);
}
using (var recovered = Open(original, project, local))
{
    recovered.Validate();
    Check("interrupted-session-recovered", UnityEngine.JsonUtility.ToJson(recovered.Identity) == identity);
}
Check("recovered-compiler-restored", Hash(local) == before);
Check("recovered-options-restored", File.ReadAllText(argumentsFile) == "--test-existing-option");

var foreign = Open(original, project, local);
File.WriteAllText(local, "foreign user compiler change");
Reject("foreign-compiler-recovery-rejected", foreign.Dispose);
Check("foreign-compiler-preserved", File.ReadAllText(local) == "foreign user compiler change" && File.Exists(journal));
File.Copy(original, local, true);
using (var recovered = Open(original, project, local)) { recovered.Validate(); }

var foreignOptions = Open(original, project, local);
File.WriteAllText(argumentsFile, "--foreign-user-option");
Reject("foreign-options-recovery-rejected", foreignOptions.Dispose);
Check("foreign-options-preserved", File.ReadAllText(argumentsFile) == "--foreign-user-option" && File.Exists(journal));
File.WriteAllText(argumentsFile, effective);
using (var recovered = Open(original, project, local)) { recovered.Validate(); }

var badJournal = Open(original, project, local);
string validJournal = File.ReadAllText(journal);
File.WriteAllText(journal, validJournal.Replace("hybridclr.dhe-compiler-transaction.json", "unknown-transaction"));
Reject("invalid-journal-rejected", badJournal.Dispose);
Check("invalid-journal-preserved", File.ReadAllText(journal).Contains("unknown-transaction"));
File.WriteAllText(journal, validJournal);
using (var recovered = Open(original, project, local)) { recovered.Validate(); }

var badBackup = Open(original, project, local);
string backupPath = Path.Combine(cache, before + ".original.dll");
File.WriteAllText(backupPath, "broken backup");
Reject("invalid-backup-rejected", badBackup.Dispose);
Check("invalid-backup-preserved", File.ReadAllText(backupPath) == "broken backup");
File.Copy(original, backupPath, true);
using (var recovered = Open(original, project, local)) { recovered.Validate(); }

Reject("project-path-escape-rejected", () => { using var escaped = Open(original, project, original); });
Reject("explicitly-disabled-divide-checks-rejected", () => DheAotCompilerSession.WithDivideChecks("--enable-divide-by-zero-check=false"));
Check("existing-divide-check-not-duplicated", DheAotCompilerSession.WithDivideChecks(effective) == effective);
Check("original-editor-compiler-untouched", Hash(original) == before);
Check("final-project-state-restored", Hash(local) == before && !File.Exists(journal) && File.ReadAllText(argumentsFile) == "--test-existing-option");
bool passed = results.Values.All(value => value);
File.WriteAllText(Path.Combine(root, "report.json"), JsonSerializer.Serialize(new
{
    format = "hybridclr.dhe-compiler-integration-tests.json", schemaVersion = 1, passed,
    original, originalSha256 = before, runnerSha256 = Hash(typeof(Program).Assembly.Location),
    compilerIdentity = JsonSerializer.Deserialize<JsonElement>(identity), results,
}, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine("DHE compiler integration: " + results.Count(pair => pair.Value) + "/" + results.Count);
return passed ? 0 : 1;

void Check(string name, bool value) => results.Add(name, value);
void Reject(string name, Action action)
{
    try { action(); Check(name, false); }
    catch (Exception exception) when (exception is IOException || exception is InvalidDataException || exception is ArgumentException)
    { Check(name, true); }
}
static string Hash(string path) => DheAotCompilerPatch.Hash(File.ReadAllBytes(path));
static DheAotCompilerSession Open(string original, string project, string local) =>
    new DheAotCompilerSession(project, original, local, "test-engine",
        () => File.ReadAllText(Path.Combine(project, "arguments.txt")),
        value => File.WriteAllText(Path.Combine(project, "arguments.txt"), value));
