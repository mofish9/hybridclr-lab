internal static class DheEvolutionEvidence
{
    internal static string[] Validate(string path, IEnumerable<string> required)
    {
        string[] executed = File.Exists(path)
            ? File.ReadAllLines(path).Where(line => !string.IsNullOrWhiteSpace(line))
                .Distinct(StringComparer.Ordinal).OrderBy(line => line, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();
        string[] missing = required.Except(executed, StringComparer.Ordinal).ToArray();
        if (missing.Length != 0)
            throw new InvalidDataException("Player did not execute required evolution checks: " + string.Join(",", missing));
        return executed;
    }
}
