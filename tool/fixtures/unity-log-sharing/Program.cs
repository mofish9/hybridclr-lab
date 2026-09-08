using System.Text;
using System.Text.Json;
using HybridCLR.DheTool;

if (args.Length != 1) throw new ArgumentException("Pass a new output directory.");
string output = Path.GetFullPath(args[0]);
if (Directory.Exists(output) || File.Exists(output)) throw new IOException("Output must be new.");
Directory.CreateDirectory(output);
var checks = new Dictionary<string, bool>();
var errors = new Dictionary<string, string>();
void Check(string name, Func<bool> test)
{
    try { checks[name] = test(); }
    catch (Exception exception) { checks[name] = false; errors[name] = exception.ToString(); }
}
string log = Path.Combine(output, "unity-stage.log");
string success = "团结 Editor\nExiting batchmode successfully now!\n";
File.WriteAllText(log, success, new UTF8Encoding(true));
Check("closed-log-with-utf8-bom", () => UnityBatchLog.Read(log) == success);
using (var writer = new FileStream(log, FileMode.Open, FileAccess.Write,
           FileShare.ReadWrite | FileShare.Delete))
{
    Check("open-writer-success-log", () => UnityBatchLog.Read(log) == success);
    byte[] failure = Encoding.UTF8.GetBytes("executeMethod method StageRuntimePlan threw exception\n" +
        "Application will terminate with return code 1\n");
    writer.Position = writer.Length;
    writer.Write(failure);
    writer.Flush();
    Check("open-writer-failure-markers-preserved", () =>
        UnityBatchLog.Read(log) == success + Encoding.UTF8.GetString(failure));
    long before = writer.Length;
    Check("read-does-not-modify-log", () =>
        UnityBatchLog.Read(log) == success + Encoding.UTF8.GetString(failure) && writer.Length == before);
}
using (var exclusive = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
{
    Check("exclusive-owner-remains-an-error", () =>
    {
        try { UnityBatchLog.Read(log); return false; }
        catch (IOException) { return true; }
    });
}
Check("missing-file-remains-an-error", () =>
{
    try { UnityBatchLog.Read(Path.Combine(output, "missing.log")); return false; }
    catch (FileNotFoundException) { return true; }
});
bool passed = checks.Values.All(value => value);
File.WriteAllText(Path.Combine(output, "report.json"), JsonSerializer.Serialize(
    new { passed, platform = Environment.OSVersion.ToString(), checks, errors },
    new JsonSerializerOptions { WriteIndented = true }));
foreach (var check in checks) Console.WriteLine(check.Key + ": " + check.Value);
return passed ? 0 : 1;
