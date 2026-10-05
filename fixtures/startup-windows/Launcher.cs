using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using HybridCLR.StartupPrototype;

sealed class Profile
{
    public string player { get; set; }
    public string gameAssembly { get; set; }
    public string metadata { get; set; }
    public string playerSha256 { get; set; }
    public string gameAssemblySha256 { get; set; }
    public string metadataSha256 { get; set; }
}
sealed class Pair
{
    public string currentSha256 { get; set; }
    public Profile DHE { get; set; }
    public Profile LegacyInterpreter { get; set; }
}
static class Launcher
{
    static int Main(string[] args)
    {
        try
        {
            string root = Path.GetFullPath(Required(args, "--root"));
            if (Array.IndexOf(args, "--reference") >= 0)
            {
                byte[] currentBytes = File.ReadAllBytes(Path.Combine(root, "shared/StartupHotfix.dll"));
                var assembly = System.Reflection.Assembly.Load(currentBytes);
                var entry = assembly.GetType("StartupHotfix.Entry", true);
                int Invoke(string name, params object[] values) { return (int)entry.GetMethod(name).Invoke(null, values); }
                var actual = new[] {
                    Invoke("Changed", 5), Invoke("Unchanged", 7), Invoke("InterfaceCall", 6), Invoke("DelegateCall", 8),
                    Invoke("ValueCall", 12), Invoke("StaticCall", 19), Invoke("ExceptionCall"), Invoke("Added", 3),
                    (int)entry.GetMethod("Echo").MakeGenericMethod(typeof(int)).Invoke(null, new object[] { 23 }),
                    (int)assembly.GetType("StartupHotfix.AddedType", true).GetField("Value").GetValue(
                        Activator.CreateInstance(assembly.GetType("StartupHotfix.AddedType", true)))
                };
                File.WriteAllText(Required(args, "--report"), JsonSerializer.Serialize(new {
                    format = "hybridclr.startup-windows-reference.json", caseCount = actual.Length, actual,
                    currentSha256 = StartupStore.Hex(StartupStore.Digest(currentBytes))
                }));
                return 0;
            }
            string storePath = Path.GetFullPath(Required(args, "--store"));
            string reportPath = Path.GetFullPath(Required(args, "--report"));
            var store = new StartupStore(storePath, "windows-startup-fixture-v1");
            long generation;
            HybridExecutionMode mode = store.Read(out generation);
            var pair = JsonSerializer.Deserialize<Pair>(File.ReadAllText(Path.Combine(root, "pair.json")));
            var profile = mode == HybridExecutionMode.DHE ? pair.DHE : pair.LegacyInterpreter;
            Verify(root, profile.player, profile.playerSha256);
            Verify(root, profile.gameAssembly, profile.gameAssemblySha256);
            Verify(root, profile.metadata, profile.metadataSha256);
            Verify(root, "shared/StartupHotfix.dll", pair.currentSha256);
            var start = new ProcessStartInfo(Path.Combine(root, profile.player)) {
                UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(Path.Combine(root, profile.player))
            };
            foreach (string arg in new[] { "-batchmode", "-nographics", "-startupPairRoot", root,
                "-startupStore", storePath, "-startupReport", reportPath, "-logFile", reportPath + ".log" })
                start.ArgumentList.Add(arg);
            string next = Optional(args, "--next");
            if (next != null) { start.ArgumentList.Add("-startupNextMode"); start.ArgumentList.Add(next); }
            if (Array.IndexOf(args, "--fault-dhe") >= 0) start.ArgumentList.Add("-startupFaultDhe");
            if (Array.IndexOf(args, "--clear") >= 0) start.ArgumentList.Add("-startupClear");
            using (var child = Process.Start(start))
            {
                Console.WriteLine(JsonSerializer.Serialize(new { stage = "selected-before-player-start", mode = mode.ToString(), generation, childPid = child.Id }));
                if (!child.WaitForExit(120000)) { child.Kill(true); throw new TimeoutException("Player did not exit in 120 seconds."); }
                if (!File.Exists(reportPath)) throw new Exception("Player did not produce a report.");
                using (var report = JsonDocument.Parse(File.ReadAllText(reportPath)))
                {
                    if (report.RootElement.GetProperty("effectiveMode").GetString() != mode.ToString())
                        throw new Exception("Selected profile and actual IL2CPP Player disagree.");
                    if (report.RootElement.GetProperty("pid").GetInt32() != child.Id)
                        throw new Exception("Report PID is not the launched process.");
                }
                return child.ExitCode;
            }
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.ToString()); return 10; }
    }
    static void Verify(string root, string path, string hash)
    {
        string actual = StartupStore.Hex(StartupStore.Digest(File.ReadAllBytes(Path.Combine(root, path))));
        if (actual != hash) throw new InvalidDataException("Pair artifact hash mismatch: " + path);
    }
    static string Optional(string[] args, string name)
    { int index = Array.IndexOf(args, name); return index >= 0 && index + 1 < args.Length ? args[index + 1] : null; }
    static string Required(string[] args, string name) { return Optional(args, name) ?? throw new Exception("Missing " + name); }
}
