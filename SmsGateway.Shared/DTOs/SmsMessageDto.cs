using System.Text.Json.Serialization;

namespace SmsGateway.Shared.DTOs;

public class SmsMessageDto
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "SendRequest";

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

    /// <summary>
    /// Set ONLY for test SMS sent via /api/sms/send-test. Coexists with the AMQP header <c>SourceType: Test</c>.
    /// Null for normal campaign traffic.
    /// </summary>
    [JsonPropertyName("test_message_id")]
    public int? TestMessageId { get; set; }
}
