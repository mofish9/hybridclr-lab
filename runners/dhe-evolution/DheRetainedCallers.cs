using System.Globalization;

internal static class DheRetainedCallers
{
    internal sealed record Evidence(string Name, bool PresentInBase, bool ExpectedChanged, bool NativeMethodChanged,
        int AotEntries, int InterpreterEntries);

    internal static Evidence[] Validate(string path, IReadOnlyDictionary<string, bool> expected)
    {
        if (!File.Exists(path)) throw new InvalidDataException("Missing native caller routing evidence.");
        var records = new Dictionary<string, Evidence>(StringComparer.Ordinal);
        foreach (string line in File.ReadAllLines(path))
        {
            string[] fields = line.Split('\t');
            if (fields.Length != 4 || !expected.TryGetValue(fields[0], out bool changed) ||
                fields[1] is not ("0" or "1") ||
                !int.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out int aot) ||
                !int.TryParse(fields[3], NumberStyles.None, CultureInfo.InvariantCulture, out int interpreted))
                throw new InvalidDataException("Invalid native caller routing record.");
            bool actualChanged = fields[1] == "1";
            // The caller set is restricted by the host to unchanged Base
            // methods or newly added methods/types. A new method has no Base
            // guard: IsChangedMethod is false and DHE boundary counts need not
            // increase during interpreter-to-interpreter calls.
            if (actualChanged || (!changed && aot == 0) ||
                !records.TryAdd(fields[0], new Evidence(fields[0], !changed, changed, actualChanged, aot, interpreted)))
                throw new InvalidDataException("Native caller routing disagrees with Base/Current MV or lacks execution: " + fields[0]);
        }
        if (records.Count != expected.Count) throw new InvalidDataException("Incomplete native caller routing evidence.");
        return records.Values.OrderBy(record => record.Name, StringComparer.Ordinal).ToArray();
    }
}
