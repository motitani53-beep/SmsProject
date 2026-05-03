using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmsGateway.Shared.Models;

/// <summary>
/// One row per SMSC message id returned for a TestMessage. A single test message may have multiple parts (concatenated SMS).
/// Mirrors <see cref="DeliverySmscId"/> for production traffic.
/// </summary>
[Table("test_smsc_ids")]
public class TestSmscId
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("test_message_id")]
    public int TestMessageId { get; set; }

    [Required]
    [Column("smsc_message_id")]
    [MaxLength(100)]
    public string SmscMessageId { get; set; } = string.Empty;

    [Column("part_number")]
    public int PartNumber { get; set; }

    [Required]
    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "Sent";

    [ForeignKey("TestMessageId")]
    public virtual TestMessage TestMessage { get; set; } = null!;
}
