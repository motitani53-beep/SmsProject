using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SmsGateway.Shared.Models;

[Table("failed_results_log")]
public class FailedResultsLog
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Required]
    [Column("source_type")]
    [MaxLength(20)]
    public string SourceType { get; set; } = "DLR";

    [Column("smsc_message_id")]
    [MaxLength(100)]
    public string? SmscMessageId { get; set; }

    [Column("payload_json", TypeName = "jsonb")]
    public string? PayloadJson { get; set; }

    [Column("retry_count")]
    public int RetryCount { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
