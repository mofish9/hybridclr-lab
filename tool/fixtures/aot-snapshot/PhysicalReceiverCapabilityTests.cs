using System.Text.Json;
using HybridCLR.DheTool;

internal static class PhysicalReceiverCapabilityTests
{
    internal static int Run(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("physical-receiver-capability <Base Model DLL> <Current Model DLL> <new report>");
        if (File.Exists(args[2])) throw new IOException("Report must be new.");
        var baseline = MetaVersionSnapshot.Create(args[0]);
        var current = MetaVersionSnapshot.Create(args[1]);
        var selected = current.Types.Single(type => type.Identity == "HybridCLR.Lab.ValueLayout.Payload");
        var evolved = ResourceUpdateCompatibility.Analyze(baseline, current, currentStorageTypes: new[] { selected.StableId });
        const string capability = "physical-current-receiver-dispatch-v1";
        var available = ResourceUpdateCompatibility.KnownRuntimeCapabilities;
        var checks = new Dictionary<string, bool>
        {
            ["layout-update-requires-physical-receiver"] = evolved.RequiredRuntimeCapabilities.Contains(capability),
            ["patched-runtime-admitted"] = ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v34", available, evolved.RequiredRuntimeCapabilities),
            ["old-runtime-rejected-before-execution"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v33", available.Where(value => value != capability), evolved.RequiredRuntimeCapabilities),
            ["renaming-old-contract-does-not-grant-capability"] = !ResourceUpdateCompatibility.CanExecuteUpdate(
                ResourceUpdateCompatibility.RuntimeProtocol, "dhe-runtime-v34", available.Where(value => value != capability), evolved.RequiredRuntimeCapabilities),
            ["noop-does-not-invent-layout-requirement"] = !ResourceUpdateCompatibility.Analyze(baseline, baseline)
                .RequiredRuntimeCapabilities.Contains(capability)
        };
        File.WriteAllText(args[2], JsonSerializer.Serialize(new { passed = checks.Values.All(value => value), checks,
            baselineSha256 = baseline.AssemblySha256, currentSha256 = current.AssemblySha256,
            scope = "Real metadata layout requirement and capability negotiation; no Player execution or general admission claim" },
            new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Physical receiver capability: " + checks.Values.All(value => value));
        return checks.Values.All(value => value) ? 0 : 1;
    }
}
