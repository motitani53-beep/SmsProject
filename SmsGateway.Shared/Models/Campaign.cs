using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmsGateway.Shared.Models;

[Table("campaigns")]
public class Campaign
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("campaign_name")]
    [MaxLength(255)]
    public string CampaignName { get; set; } = string.Empty;

    [Required]
    [Column("message_content")]
    public string MessageContent { get; set; } = string.Empty;

    [Required]
    [Column("message_language")]
    [MaxLength(50)]
    public string MessageLanguage { get; set; } = string.Empty;

    [Column("sender_type")]
    [MaxLength(50)]
    public string SenderType { get; set; } = string.Empty;

    [Column("sender_value")]
    [MaxLength(50)]
    public string? SenderValue { get; set; }

    [Column("scheduling_type")]
    [MaxLength(50)]
    public string SchedulingType { get; set; } = string.Empty;

    [Column("scheduled_time")]
    public DateTime? ScheduledTime { get; set; }

    [Column("priority")]
    [MaxLength(50)]
    public string Priority { get; set; } = string.Empty;

    [Column("code")]
    [MaxLength(100)]
    public string? Code { get; set; }

    [Column("provider")]
    [MaxLength(50)]
    public string Provider { get; set; } = string.Empty;

    [Column("campaign_cost", TypeName = "decimal(18,2)")]
    public decimal CampaignCost { get; set; } = 0;

    [Column("total_messages")]
    public int TotalMessages { get; set; }

    [Column("total_sent_messages")]
    public int TotalSentMessages { get; set; } = 0;

    [Column("success_count")]
    public int SuccessCount { get; set; } = 0;

    [Column("failed_count")]
    public int FailedCount { get; set; } = 0;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("completed_at")]
    public DateTime? CompletedAt { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    [Column("assigned_queue")]
    [MaxLength(100)]
    public string? AssignedQueue { get; set; }

    // 1-to-many relationship with DeliveryDetails
    public virtual ICollection<DeliveryDetails> DeliveryDetails { get; set; } = new List<DeliveryDetails>();
}
