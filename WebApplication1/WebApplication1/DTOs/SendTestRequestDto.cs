using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace WebApplication1.DTOs;

/// <summary>
/// Body for POST /api/sms/send-test. The message template uses the SAME placeholder syntax as a campaign;
/// the custom_fields dictionary is the first row of the user's CSV so {col} placeholders resolve identically.
/// </summary>
public class SendTestRequestDto
{
    /// <summary>Israeli MSISDN — must match <c>^972\d{9}$</c>.</summary>
    [Required]
    [JsonPropertyName("phone_number")]
    [RegularExpression(@"^972\d{9}$", ErrorMessage = "Phone number must start with 972 and be exactly 12 digits")]
    public string PhoneNumber { get; set; } = string.Empty;

    /// <summary>Raw message template with {column} placeholders.</summary>
    [Required]
    [JsonPropertyName("message_content")]
    public string MessageContent { get; set; } = string.Empty;

    /// <summary>Custom fields from the first CSV row (used for placeholder substitution).</summary>
    [JsonPropertyName("custom_fields")]
    public Dictionary<string, string>? CustomFields { get; set; }

    /// <summary>"hebrew" | "english" | "arabic" — same vocabulary as CampaignRequestDto.</summary>
    [JsonPropertyName("message_language")]
    public string? MessageLanguage { get; set; }

    /// <summary>Optional Sender ID override (sender_value). Falls back to a random pool number when null/empty.</summary>
    [JsonPropertyName("sender_value")]
    public string? SenderValue { get; set; }
}
