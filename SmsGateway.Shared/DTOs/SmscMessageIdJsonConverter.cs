using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SmsGateway.Shared.DTOs;

/// <summary>
/// SMSC message IDs are opaque digit strings that may exceed <see cref="long"/> and must not pass through
/// IEEE-754 (e.g. default number-to-string coercion loses precision). Reads JSON numbers from the raw UTF-8
/// literal; always writes a JSON string.
/// </summary>
public sealed class SmscMessageIdJsonConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => ReadNumberAsString(ref reader),
            JsonTokenType.Null => null,
            _ => throw new JsonException($"Unexpected token for smsc_message_id: {reader.TokenType}")
        };
    }

    private static string ReadNumberAsString(ref Utf8JsonReader reader)
    {
        if (reader.HasValueSequence)
        {
            var seq = reader.ValueSequence;
            var buf = new byte[seq.Length];
            BuffersExtensions.CopyTo(in seq, buf.AsSpan());
            return Encoding.UTF8.GetString(buf);
        }

        return Encoding.UTF8.GetString(reader.ValueSpan);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}
