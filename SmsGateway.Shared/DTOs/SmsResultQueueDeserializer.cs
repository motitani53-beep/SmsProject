using System.Text.Json;

namespace SmsGateway.Shared.DTOs;

/// <summary>
/// Deserializes smsc_results queue payloads. DLR messages use <see cref="DlrRabbitMqPayload"/> so
/// <c>smsc_message_id</c> is read with <see cref="SmscMessageIdJsonConverter"/> (avoids IEEE-754 precision loss).
/// SubmitSmResp uses <see cref="SmsResultDto"/> with the same converter on <see cref="SmsResultDto.SmscMessageId"/>.
/// </summary>
public static class SmsResultQueueDeserializer
{
    /// <summary>Shared options for all smsc_results JSON; property-level converters still apply.</summary>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Parses JSON into <see cref="SmsResultDto"/> for batch processing, or <c>null</c> if not a supported type.</summary>
    public static SmsResultDto? Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
            return null;

        var type = typeEl.GetString();
        if (string.Equals(type, "DLR", StringComparison.OrdinalIgnoreCase))
        {
            var dlr = JsonSerializer.Deserialize<DlrRabbitMqPayload>(json, Options);
            return dlr is null ? null : ToSmsResultDto(dlr);
        }

        if (string.Equals(type, "SubmitSmResp", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Deserialize<SmsResultDto>(json, Options);

        return null;
    }

    private static SmsResultDto ToSmsResultDto(DlrRabbitMqPayload dlr)
    {
        return new SmsResultDto
        {
            Type = dlr.Type,
            SmscMessageId = dlr.SmscMessageId,
            Status = dlr.Status,
            Timestamp = dlr.Timestamp,
        };
    }
}
