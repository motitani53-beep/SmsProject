using System.Text.Json.Serialization;

namespace SmsGateway.Shared.DTOs;

/// <summary>
/// DLR JSON published to the smsc_results queue by the Receiver. Uses <see cref="SmscMessageIdJsonConverter"/>
/// so large numeric-looking IDs are always emitted as JSON strings, not numbers.
/// </summary>
public sealed class DlrRabbitMqPayload
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "DLR";

    [JsonPropertyName("smsc_message_id")]
    [JsonConverter(typeof(SmscMessageIdJsonConverter))]
    public string SmscMessageId { get; init; } = string.Empty;

    [JsonPropertyName("status")]
    public string Status { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public string Timestamp { get; init; } = string.Empty;
}
