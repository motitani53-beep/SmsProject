using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json;

namespace SmsGateway.Shared.Models;

[Table("delivery_details")]
public class DeliveryDetails
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("campaign_id")]
    public int CampaignId { get; set; }

    [Required]
    [Column("phone_number")]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Column("actual_sender")]
    [MaxLength(20)]
    public string? ActualSender { get; set; }

    /// <summary>Provider (SMSC) message id for matching DLRs.</summary>
    [Column("smsc_message_id")]
    [MaxLength(100)]
    public string? SmscMessageId { get; set; }

    [Column("message_content")]
    public string MessageContent { get; set; } = string.Empty;

    [Required]
    [Column("status")]
    public DeliveryStatus Status { get; set; } = DeliveryStatus.Pending;

    /// <summary>True only after a <em>terminal</em> SMSC delivery receipt (DLR) is stored on this row — not on SubmitSmResp or intermediate DLR states (e.g. Enroute, Accepted).</summary>
    [Column("processed")]
    public bool Processed { get; set; } = false;

    [Column("processed_at")]
    public DateTime? ProcessedAt { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("sent_at")]
    public DateTime? SentAt { get; set; }

    [Column("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("additional_data", TypeName = "jsonb")]
    public JsonElement? AdditionalData { get; set; }

    [ForeignKey("CampaignId")]
    public virtual Campaign Campaign { get; set; } = null!;

    /// <summary>Per-segment SMSC message ids for multipart SMS (one row per part).</summary>
    public virtual ICollection<DeliverySmscId> SmscIds { get; set; } = new List<DeliverySmscId>();

    /// <summary>
    /// True when the message is still in progress (pending, sent, or accepted by provider).
    /// </summary>
    [NotMapped]
    public bool IsInProgress =>
        Status == DeliveryStatus.Pending ||
        Status == DeliveryStatus.Acceptable ||
        Status == DeliveryStatus.Accepted;

    /// <summary>
    /// User-friendly display string for the status.
    /// </summary>
    [NotMapped]
    public string StatusDisplay => Status switch
    {
        DeliveryStatus.Pending => "Pending",
        DeliveryStatus.Acceptable => "In Progress",
        DeliveryStatus.Accepted => "In Progress (Provider received)",
        DeliveryStatus.Successful => "Delivered",
        DeliveryStatus.Failed => "Failed",
        DeliveryStatus.Expired => "Expired",
        DeliveryStatus.TimeoutSMSC => "Timeout",
        _ => "Unknown"
    };
}
