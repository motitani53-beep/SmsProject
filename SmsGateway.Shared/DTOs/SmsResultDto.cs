using System.Text.Json.Serialization;

namespace SmsGateway.Shared.DTOs;

/// <summary>
/// Parsed result message from smsc_results queue: SubmitSmResp or DLR.
/// </summary>
public class SmsResultDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("delivery_id")]
    public int DeliveryId { get; set; }

    [JsonPropertyName("campaign_id")]
    public int? CampaignId { get; set; }

    [JsonPropertyName("phone_number")]
    public string? PhoneNumber { get; set; }

    [JsonPropertyName("smsc_message_id")]
    public string? SmscMessageId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    /// <summary>SMPP command status (0 = ESME_ROK). When omitted, legacy messages use <see cref="Status"/> string.</summary>
    [JsonPropertyName("command_status")]
    public int? CommandStatus { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("part_number")]
    public int PartNumber { get; set; }

    [JsonPropertyName("total_parts")]
    public int TotalParts { get; set; }
}
