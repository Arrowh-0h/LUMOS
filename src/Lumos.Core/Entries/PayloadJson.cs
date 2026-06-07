using System.Text.Json;

namespace Lumos.Core.Entries;

internal static class PayloadJson
{
    /// <summary>
    /// JSON options used for serializing entry payloads to the payload_json
    /// column. Compact output (no indentation), polymorphic by [JsonPolymorphic].
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false,
        // Don't change PropertyNamingPolicy — we want the EXACT property
        // names so they're stable for export/import.
    };

    public static string Serialize(EntryPayload payload)
        => JsonSerializer.Serialize<EntryPayload>(payload, Options);

    public static EntryPayload Deserialize(string json)
        => JsonSerializer.Deserialize<EntryPayload>(json, Options)
           ?? throw new InvalidOperationException("Payload JSON was null.");
}
