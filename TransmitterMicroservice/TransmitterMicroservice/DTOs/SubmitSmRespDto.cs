using System.Text.Json.Serialization;

namespace TransmitterMicroservice.DTOs;

/// <summary>
/// DTO for SubmitSmResp messages published to RabbitMQ queue "smsc_results"
/// </summary>
public class SubmitSmRespDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "SubmitSmResp";

    [JsonPropertyName("delivery_id")]
    public int DeliveryId { get; set; }

    [JsonPropertyName("campaign_id")]
    public int CampaignId { get; set; }

    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("smsc_message_id")]
    public string? SmscMessageId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("command_status")]
    public int CommandStatus { get; set; }

    [JsonPropertyName("part_number")]
    public int PartNumber { get; set; }

    [JsonPropertyName("total_parts")]
    public int TotalParts { get; set; }
}

