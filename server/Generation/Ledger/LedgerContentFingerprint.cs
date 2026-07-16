using System.Buffers;
using System.Security.Cryptography;
using System.Globalization;
using System.Text.Json;

namespace Lingmai.RedMist.Generation.Ledger;

internal static class LedgerContentFingerprint
{
    public static string Compute(LedgerEventData value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("schemaVersion", value.SchemaVersion);
            writer.WriteString("entryId", value.EntryId);
            writer.WriteString("playerId", value.PlayerId);
            writer.WriteString("type", value.Type);
            writer.WritePropertyName("actors");
            writer.WriteStartArray();
            foreach (string actor in value.Actors) writer.WriteStringValue(actor);
            writer.WriteEndArray();
            writer.WriteNumber("severity", value.Severity);
            writer.WriteString("chapter", value.Chapter);
            writer.WriteNumber("worldClock", value.WorldClock);
            writer.WriteString("sourceNodeId", value.SourceNodeId);
            writer.WritePropertyName("factRefs");
            writer.WriteStartArray();
            foreach (string factRef in value.FactRefs) writer.WriteStringValue(factRef);
            writer.WriteEndArray();
            writer.WritePropertyName("payload");
            WriteCanonical(writer, value.Payload);
            writer.WriteEndObject();
        }

        return $"sha256:{Convert.ToHexString(SHA256.HashData(buffer.WrittenSpan)).ToLowerInvariant()}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (JsonProperty property in value.EnumerateObject().OrderBy(
                             item => item.Name,
                             StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (JsonElement item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                if (!value.TryGetDecimal(out decimal number) || number != decimal.Truncate(number))
                    throw new LedgerValidationException();
                writer.WriteRawValue(
                    number.ToString("0", CultureInfo.InvariantCulture),
                    skipInputValidation: false);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;
            default:
                throw new LedgerValidationException();
        }
    }
}
