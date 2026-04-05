using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.Models;

namespace WebApplication1.Services;

/// <summary>
/// Updates campaign progress from delivery rows, applies DLR timeouts, and completes campaigns when all messages are processed.
/// Completion requires every <c>delivery_details</c> row for the campaign to have <c>processed = true</c> and
/// <c>TotalMessages</c> to match the row count (submit failures are already <c>processed</c> and count toward completion).
/// </summary>
public sealed class CampaignMonitoringWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);
    private const string InProgressStatus = "In Progress";
    private const string CompletedStatus = "Completed";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CampaignMonitoringWorker> _logger;

    public CampaignMonitoringWorker(
        IServiceScopeFactory scopeFactory,
        IConfiguration configuration,
        ILogger<CampaignMonitoringWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CampaignMonitoringWorker started (interval {Seconds}s)",
            Interval.TotalSeconds);

        using var timer = new PeriodicTimer(Interval);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    if (!await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                        break;

                    await RunCycleAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "CampaignMonitoringWorker cycle failed");
                }
            }
        }
        finally
        {
            timer.Dispose();
        }

        _logger.LogInformation("CampaignMonitoringWorker stopped.");
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        var dlrTimeoutHours = _configuration.GetValue("CampaignSettings:DlrTimeoutHours", 72);
        if (dlrTimeoutHours < 1)
            dlrTimeoutHours = 72;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var activeCampaigns = await db.Campaigns
            .Where(c => c.Status == InProgressStatus)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (activeCampaigns.Count == 0)
            return;

        var timeoutThreshold = TimeSpan.FromHours(dlrTimeoutHours);
        var timeoutMessage = $"DLR Timeout after {dlrTimeoutHours}h";
        var anyChange = false;

        foreach (var campaign in activeCampaigns)
        {
            if (DateTime.UtcNow - campaign.CreatedAt > timeoutThreshold)
            {
                // Uses idx_delivery_details_campaign_processed (campaign_id, processed).
                var stuck = await db.DeliveryDetails
                    .Where(d => d.CampaignId == campaign.Id && !d.Processed)
                    .ToListAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (stuck.Count > 0)
                {
                    var now = DateTime.UtcNow;
                    foreach (var row in stuck)
                    {
                        row.Status = DeliveryStatus.Expired;
                        row.Processed = true;
                        row.ProcessedAt = now;
                        row.ErrorMessage = timeoutMessage;
                    }

                    await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                    _logger.LogWarning(
                        "Campaign {CampaignId}: forced expiry of {Count} stuck message(s) after DLR timeout ({Hours}h).",
                        campaign.Id,
                        stuck.Count,
                        dlrTimeoutHours);
                }
            }

            // Fast path: any row with processed = false (index seek on campaign_id + processed).
            var hasUnprocessed = await db.DeliveryDetails
                .AsNoTracking()
                .AnyAsync(d => d.CampaignId == campaign.Id && !d.Processed, cancellationToken)
                .ConfigureAwait(false);

            var stats = await GetDeliveryStatsAsync(db, campaign.Id, cancellationToken).ConfigureAwait(false);
            var processedCount = stats.Processed;
            var successCount = stats.Success;
            var failedCount = stats.Failed;

            if (!hasUnprocessed && stats.Total != campaign.TotalMessages)
            {
                _logger.LogWarning(
                    "Campaign {CampaignId}: all delivery rows are processed but TotalMessages ({TotalMessages}) does not match delivery row count ({RowCount}). Not marking campaign completed.",
                    campaign.Id,
                    campaign.TotalMessages,
                    stats.Total);
            }

            if (campaign.TotalSentMessages != processedCount)
            {
                campaign.TotalSentMessages = processedCount;
                anyChange = true;
            }

            if (campaign.SuccessCount != successCount)
            {
                campaign.SuccessCount = successCount;
                anyChange = true;
            }

            if (campaign.FailedCount != failedCount)
            {
                campaign.FailedCount = failedCount;
                anyChange = true;
            }

            // Require every message processed and header total aligned with delivery row count (processed == total rows when !hasUnprocessed).
            if (!hasUnprocessed
                && campaign.TotalMessages > 0
                && stats.Total == campaign.TotalMessages
                && processedCount == campaign.TotalMessages)
            {
                campaign.Status = CompletedStatus;
                campaign.CompletedAt = DateTime.UtcNow;
                anyChange = true;
            }
        }

        if (anyChange)
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// One aggregation round-trip per campaign for counters (replaces three separate COUNT queries).
    /// </summary>
    private static async Task<DeliveryStats> GetDeliveryStatsAsync(
        ApplicationDbContext db,
        int campaignId,
        CancellationToken cancellationToken)
    {
        var row = await db.DeliveryDetails
            .AsNoTracking()
            .Where(d => d.CampaignId == campaignId)
            .GroupBy(_ => 0)
            .Select(g => new DeliveryStats(
                g.Count(),
                g.Count(x => x.Processed),
                g.Count(x => x.Status == DeliveryStatus.Successful),
                g.Count(x =>
                    x.Status == DeliveryStatus.Failed ||
                    x.Status == DeliveryStatus.TimeoutSMSC ||
                    x.Status == DeliveryStatus.Expired)))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row;
    }

    private readonly record struct DeliveryStats(int Total, int Processed, int Success, int Failed);
}
