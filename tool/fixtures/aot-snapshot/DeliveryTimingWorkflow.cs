using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

internal static class DeliveryTimingWorkflow
{
    private sealed record Sample(int Index, int Pid, DateTime StartUtc, long Milliseconds, long PeakWorkingSetBytes,
        int ExitCode, bool Passed, string Stage);

    internal static int Run(string[] args)
    {
        if (args.Length != 4) throw new ArgumentException("delivery-timing <Player.exe> <delivery> <count> <output>");
        string player = Path.GetFullPath(args[0]), delivery = Path.GetFullPath(args[1]);
        int count = int.Parse(args[2]); string output = Path.GetFullPath(args[3]);
        if (count < 1 || count > 1000 || !File.Exists(player) || !Directory.Exists(delivery) || Directory.Exists(output))
            throw new ArgumentException("Invalid timing inputs.");
        Directory.CreateDirectory(output);
        string hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(delivery, "dhe-delivery.json"))));
        var retained = new List<Process>();
        var completed = new List<Sample>();
        try
        {
            for (int index = 1; index <= count; index++)
            {
                var sample = RunOne(index, player, delivery, hash, output, retained);
                completed.Add(sample);
                File.WriteAllText(Path.Combine(output, $"sample-{index:D3}.json"), JsonSerializer.Serialize(sample));
                if (!sample.Passed || sample.ExitCode != 0 || sample.Stage != "complete")
                    throw new InvalidDataException("Player correctness failed; sampling stopped.");
                Console.WriteLine($"Completed {index}/{count}, PID {sample.Pid}");
            }
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "failure.json"), JsonSerializer.Serialize(new { passed = false, error = error.ToString(), completed }));
            return 1;
        }
        finally { foreach (var process in retained) process.Dispose(); }
        var samples = completed.OrderBy(sample => sample.Milliseconds).ToArray();
        int uniqueProcessIdentities = samples.Select(sample => (sample.Pid, sample.StartUtc)).Distinct().Count();
        if (uniqueProcessIdentities != samples.Length)
            throw new InvalidOperationException("Timing sample contains duplicate PID/start identities.");
        if (samples.Any(sample => !sample.Passed || sample.ExitCode != 0 || sample.Stage != "complete"))
            throw new InvalidDataException("Timing sample contains a failed Player run.");
        long Percentile(double p) => samples[(int)Math.Clamp(Math.Ceiling(samples.Length * p) - 1, 0, samples.Length - 1)].Milliseconds;
        var summary = new { passed = samples.Length == count, count, manifestHash = hash,
            p50Ms = Percentile(.50), p95Ms = Percentile(.95), p99Ms = Percentile(.99),
            minMs = samples[0].Milliseconds, maxMs = samples[^1].Milliseconds,
            uniqueProcessIdentities, uniquePids = samples.Select(sample => sample.Pid).Distinct().Count(),
            meanMs = samples.Average(sample => sample.Milliseconds), samples,
            performanceGatePassed = false,
            uniquePidSampleRequirementMet = count >= 100 && samples.Select(sample => sample.Pid).Distinct().Count() == count,
            scope = "Sequential Player end-to-end smoke; includes startup and asset checks. No comparative performance gate or mobile memory claim." };
        File.WriteAllText(Path.Combine(output, "timing.json"), JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Delivery timing passed: {count}; P50={summary.p50Ms}ms P95={summary.p95Ms}ms P99={summary.p99Ms}ms");
        return 0;
    }

    private static Sample RunOne(int index, string player, string delivery, string hash, string output, List<Process> retained)
    {
        string report = Path.Combine(output, $"run-{index:D3}.json"), log = Path.Combine(output, $"run-{index:D3}.log"),
            consoleLog = Path.Combine(output, $"console-{index:D3}.log");
        var timer = Stopwatch.StartNew();
        var process = Process.Start(new ProcessStartInfo(player)
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true,
            ArgumentList = { "-batchmode", "-nographics", "-snapshotResult", report,
                "-dheDeliveryRoot", delivery, "-dheDeliveryHash", hash, "-logFile", log }
        }) ?? throw new IOException("Could not start Player.");
        retained.Add(process);
        // Retain the kernel process handle through the entire sequential sample.
        // Windows cannot recycle its process object while this handle remains open.
        _ = process.Handle;
        int pid = process.Id; DateTime start = process.StartTime.ToUniversalTime();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        long peakWorkingSet = 0;
        while (!process.WaitForExit(50))
        {
            if (timer.ElapsedMilliseconds >= 120000)
            {
                process.Kill(true); process.WaitForExit(10000);
                throw new TimeoutException("Player timing run timed out.");
            }
            try { process.Refresh(); peakWorkingSet = Math.Max(peakWorkingSet, process.PeakWorkingSet64); }
            catch (InvalidOperationException) when (process.HasExited) { break; }
        }
        timer.Stop();
        if (!Task.WaitAll(new Task[] { stdout, stderr }, 10000)) throw new TimeoutException("Player output streams did not close.");
        File.WriteAllText(consoleLog, stdout.Result + stderr.Result);
        File.WriteAllText(Path.Combine(output, $"pid-{index:D3}.txt"), $"{pid}\n{start:O}\n");
        using var document = JsonDocument.Parse(File.ReadAllBytes(report));
        var root = document.RootElement;
        bool passed = root.GetProperty("passed").GetBoolean() && root.GetProperty("revision").GetInt32() == 73 &&
            root.GetProperty("metadataCommitted").GetBoolean() && root.GetProperty("checks").GetArrayLength() == 42 &&
            root.GetProperty("manifestSha256").GetString() == hash &&
            File.ReadLines(log).Count(line => line.StartsWith("DHE case begin: ", StringComparison.Ordinal)) == 46;
        return new Sample(index, pid, start, timer.ElapsedMilliseconds, peakWorkingSet, process.ExitCode,
            passed, root.GetProperty("stage").GetString() ?? "");
    }
}
