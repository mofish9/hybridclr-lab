using System.Text.Json;

internal static class DheObservedResults
{
    internal static readonly string[] Fields = { "addResult", "stableResult", "addViaStableResult" };

    internal static void Validate(JsonElement expected, JsonElement actual)
    {
        foreach (string name in Fields)
            if (!expected.TryGetProperty(name, out JsonElement reference) ||
                !actual.TryGetProperty(name, out JsonElement observed) ||
                reference.ValueKind != JsonValueKind.Number || observed.ValueKind != JsonValueKind.Number ||
                !reference.TryGetInt32(out int referenceValue) || !observed.TryGetInt32(out int observedValue) ||
                referenceValue != observedValue)
                throw new InvalidDataException(
                    "Player observation disagrees with the current DLL's CLR reference: " + name);
    }
}
