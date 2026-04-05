using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmsGateway.Shared.Models;

[Table("delivery_smsc_ids")]
public class DeliverySmscId
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("delivery_detail_id")]
    public int DeliveryDetailId { get; set; }

    [Required]
    [Column("smsc_message_id")]
    [MaxLength(100)]
    public string SmscMessageId { get; set; } = string.Empty;

    [Column("part_number")]
    public int PartNumber { get; set; }

    [Column("total_parts")]
    public int TotalParts { get; set; }

    [Required]
    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "Sent";

    [Column("delivered_at")]
    public DateTime? DeliveredAt { get; set; }

    [ForeignKey("DeliveryDetailId")]
    public virtual DeliveryDetails DeliveryDetail { get; set; } = null!;
}
