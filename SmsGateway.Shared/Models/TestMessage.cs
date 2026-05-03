using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmsGateway.Shared.Models;

/// <summary>
/// One-off test SMS sent via /api/sms/send-test. Independent of campaigns/delivery_details.
/// </summary>
[Table("test_messages")]
public class TestMessage
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("phone_number")]
    [MaxLength(32)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [Column("message_content")]
    public string MessageContent { get; set; } = string.Empty;

    /// <summary>Free-form lifecycle status: Pending, Sent, Delivered, Failed, Expired.</summary>
    [Required]
    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "Pending";

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual ICollection<TestSmscId> SmscIds { get; set; } = new List<TestSmscId>();
}
