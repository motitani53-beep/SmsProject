namespace SmsGateway.Shared.Models;

/// <summary>
/// Campaign row <c>status</c> values (stored as strings in PostgreSQL).
/// </summary>
public enum CampaignStatus
{
    /// <summary>Default / placeholder.</summary>
    Pending,

    /// <summary>Awaiting <see cref="Campaign.ScheduledTime"/>; not yet claimed by scheduler.</summary>
    Scheduled,

    /// <summary>Scheduler claimed the row; RabbitMQ publish in progress (or stuck until recovery).</summary>
    Processing,

    /// <summary>Messages published to RabbitMQ; pipeline same as immediate campaigns.</summary>
    InProgress,

    /// <summary>Lifecycle complete (optional future use).</summary>
    Completed,
}

/// <summary>Maps <see cref="CampaignStatus"/> to the exact strings persisted in <c>campaigns.status</c>.</summary>
public static class CampaignStatusDb
{
    public const string Pending = "Pending";
    public const string Scheduled = "Scheduled";
    public const string Processing = "Processing";
    public const string InProgress = "In Progress";
    public const string Completed = "Completed";

    public static string ToDatabaseString(this CampaignStatus status) => status switch
    {
        CampaignStatus.Pending => Pending,
        CampaignStatus.Scheduled => Scheduled,
        CampaignStatus.Processing => Processing,
        CampaignStatus.InProgress => InProgress,
        CampaignStatus.Completed => Completed,
        _ => Pending
    };

    public static bool IsScheduled(string? status) =>
        string.Equals(status, Scheduled, StringComparison.OrdinalIgnoreCase);

    public static bool IsProcessing(string? status) =>
        string.Equals(status, Processing, StringComparison.OrdinalIgnoreCase);

    public static bool IsInProgress(string? status) =>
        string.Equals(status, InProgress, StringComparison.OrdinalIgnoreCase);
}
