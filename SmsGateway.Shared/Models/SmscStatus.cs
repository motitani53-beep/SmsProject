using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmsGateway.Shared.Models;

[Table("smsc_status")]
public class SmscStatus
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = string.Empty; 

    [Required]
    [Column("timestamp")]
    public DateTime Timestamp { get; set; }
}
