using System.Text.Json.Serialization;

namespace TransmitterMicroservice.DTOs;

/// <summary>
/// DTO for messages received from RabbitMQ queue "sms_messages"
/// </summary>
public class SendRequestDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("delivery_id")]
    public int DeliveryId { get; set; }

    [JsonPropertyName("campaign_id")]
    public int CampaignId { get; set; }

    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [JsonPropertyName("message_text")]
    public string MessageText { get; set; } = string.Empty;

    [JsonPropertyName("actual_sender")]
    public string? ActualSender { get; set; }
}

