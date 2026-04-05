using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace WebApplication1.DTOs;

public class CampaignRequestDto
{
    [Required]
    [JsonPropertyName("campaign_name")]
    public string CampaignName { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("message_content")]
    public string MessageContent { get; set; } = string.Empty;

    [Required]
    [JsonPropertyName("message_language")]
    public string MessageLanguage { get; set; } = string.Empty;

    [Required]
    [MinLength(1)]
    [JsonPropertyName("recipients")]
    public List<RecipientDto> Recipients { get; set; } = new();

    [Required]
    [JsonPropertyName("sender_config")]
    public SenderConfigDto SenderConfig { get; set; } = new();

    [Required]
    [JsonPropertyName("scheduling")]
    public SchedulingDto Scheduling { get; set; } = new();

    [JsonPropertyName("priority")]
    public string Priority { get; set; } = "low";

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [Required]
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = string.Empty;
}

public class RecipientDto
{
    [Required]
    [RegularExpression(@"^\d+$", ErrorMessage = "Phone number must contain only digits")]
    [JsonPropertyName("phone_number")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Arbitrary string fields; placeholders in message use <c>{key}</c> matching each dictionary key.</summary>
    [JsonPropertyName("custom_fields")]
    public Dictionary<string, string> CustomFields { get; set; } = new();
}

public class SenderConfigDto
{
    [Required]
    [JsonPropertyName("sender_type")]
    public string SenderType { get; set; } = string.Empty;

    [JsonPropertyName("sender_value")]
    public string? SenderValue { get; set; }
}

public class SchedulingDto
{
    [Required]
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("scheduled_time")]
    public DateTime? ScheduledTime { get; set; }
}

