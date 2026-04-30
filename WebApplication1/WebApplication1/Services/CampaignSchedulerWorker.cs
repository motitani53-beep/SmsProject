using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.Models;

namespace WebApplication1.Services;

/// <summary>
/// Claims due scheduled campaigns with PostgreSQL <c>FOR UPDATE SKIP LOCKED</c>, moves them to <see cref="CampaignStatusDb.Processing"/>,
/// publishes pending delivery rows to RabbitMQ, then promotes to <see cref="CampaignStatusDb.InProgress"/> with <c>scheduling_type = immediate</c>.
/// Recovers campaigns stuck in Processing (e.g. crash mid-publish) after <see cref="StuckProcessingThreshold"/>.
/// </summary>
public sealed class CampaignSchedulerWorker : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan StuckProcessingThreshold = TimeSpan.FromMinutes(10);

    private const int BackoffSecondsWhenNoQueueAvailable = 30;
    private const string HighPriorityRoutingKey = "sms.priority";
    private const string HighPriorityAssignedQueue = "sms_high_priority";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly ILogger<CampaignSchedulerWorker> _logger;

    public CampaignSchedulerWorker(
        IServiceScopeFactory scopeFactory,
        IRabbitMqService rabbitMqService,
        ILogger<CampaignSchedulerWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _rabbitMqService = rabbitMqService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "CampaignSchedulerWorker started (interval {Seconds}s, stuck recovery after {StuckMinutes}m)",
            Interval.TotalSeconds,
            StuckProcessingThreshold.TotalMinutes);

        // Run once immediately so due campaigns are not delayed by the first timer tick.
        try
        {
            await RunCycleAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("CampaignSchedulerWorker stopped.");
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CampaignSchedulerWorker initial cycle failed");
        }

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
                    _logger.LogError(ex, "CampaignSchedulerWorker cycle failed");
                }
            }
        }
        finally
        {
            timer.Dispose();
        }

        _logger.LogInformation("CampaignSchedulerWorker stopped.");
    }

    private async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        List<int> recoveredIds;
        List<int> claimedIds;

        using (var scope = _scopeFactory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            recoveredIds = await RecoverStuckProcessingCampaignIdsAsync(db, cancellationToken).ConfigureAwait(false);
            claimedIds = await ClaimDueScheduledCampaignIdsAsync(db, cancellationToken).ConfigureAwait(false);
        }

        if (recoveredIds.Count > 0)
        {
            _logger.LogWarning(
                "CampaignSchedulerWorker recovered {Count} stuck Processing campaign(s) (no progress for ≥{Minutes}m): {Ids}",
                recoveredIds.Count,
                StuckProcessingThreshold.TotalMinutes,
                string.Join(", ", recoveredIds));
        }

        if (claimedIds.Count > 0)
        {
            _logger.LogInformation("CampaignSchedulerWorker claimed {Count} scheduled campaign(s): {Ids}",
                claimedIds.Count, string.Join(", ", claimedIds));
        }

        var toProcess = new List<int>(recoveredIds.Count + claimedIds.Count);
        foreach (var id in recoveredIds)
            toProcess.Add(id);
        foreach (var id in claimedIds)
        {
            if (!toProcess.Contains(id))
                toProcess.Add(id);
        }

        if (toProcess.Count == 0)
            return;

        foreach (var campaignId in toProcess)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            try
            {
                await ActivateAndPublishCampaignAsync(campaignId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to publish campaign {CampaignId}; remains in Processing for recovery.", campaignId);
            }
        }
    }

    /// <summary>
    /// Re-claims campaigns stuck in Processing: lease refresh via <c>processing_started_at</c> so only one worker proceeds.
    /// Does not change status (remains Processing). Ignores fresh Processing rows (&lt; 10 minutes since <c>processing_started_at</c>).
    /// </summary>
    private async Task<List<int>> RecoverStuckProcessingCampaignIdsAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var recoverBefore = DateTime.UtcNow.Subtract(StuckProcessingThreshold);
        var now = DateTime.UtcNow;

        const string sql = """
            UPDATE campaigns AS c
            SET processing_started_at = @now
            FROM (
                SELECT id FROM campaigns
                WHERE status = 'Processing'
                  AND (
                    processing_started_at IS NULL
                    OR processing_started_at <= @recoverBefore
                  )
                ORDER BY processing_started_at NULLS FIRST, id
                FOR UPDATE SKIP LOCKED
            ) AS picked
            WHERE c.id = picked.id
            RETURNING c.id
            """;

        return await ExecuteReturningIdsAsync(db, sql, cancellationToken, cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("now", now));
            cmd.Parameters.Add(new NpgsqlParameter("recoverBefore", recoverBefore));
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Atomic claim: Scheduled → Processing with <c>processing_started_at</c>. Does not set <c>scheduling_type</c> until publish completes.
    /// </summary>
    private async Task<List<int>> ClaimDueScheduledCampaignIdsAsync(
        ApplicationDbContext db,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        const string sql = """
            UPDATE campaigns AS c
            SET status = 'Processing',
                processing_started_at = @now
            FROM (
                SELECT id FROM campaigns
                WHERE status = 'Scheduled'
                  AND scheduling_type = 'scheduled'
                  AND scheduled_time IS NOT NULL
                  AND scheduled_time <= @now
                ORDER BY scheduled_time, id
                FOR UPDATE SKIP LOCKED
            ) AS picked
            WHERE c.id = picked.id
            RETURNING c.id
            """;

        return await ExecuteReturningIdsAsync(db, sql, cancellationToken, cmd =>
        {
            cmd.Parameters.Add(new NpgsqlParameter("now", now));
        }).ConfigureAwait(false);
    }

    private static async Task<List<int>> ExecuteReturningIdsAsync(
        ApplicationDbContext db,
        string sql,
        CancellationToken cancellationToken,
        Action<NpgsqlCommand> bindParameters)
    {
        var ids = new List<int>();
        await db.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await using var cmd = (NpgsqlCommand)db.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = sql;
            bindParameters(cmd);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                ids.Add(reader.GetInt32(0));
        }
        finally
        {
            await db.Database.CloseConnectionAsync().ConfigureAwait(false);
        }

        return ids;
    }

    private async Task ActivateAndPublishCampaignAsync(int campaignId, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var messageProcessing = scope.ServiceProvider.GetRequiredService<MessageProcessingService>();

        var campaign = await db.Campaigns
            .FirstOrDefaultAsync(c => c.Id == campaignId, cancellationToken)
            .ConfigureAwait(false);

        if (campaign == null)
        {
            _logger.LogWarning("Campaign {CampaignId} missing; skipping publish.", campaignId);
            return;
        }

        if (!CampaignStatusDb.IsProcessing(campaign.Status))
        {
            _logger.LogWarning(
                "Campaign {CampaignId} expected status Processing but was {Status}; skipping.",
                campaignId,
                campaign.Status);
            return;
        }

        var isHighPriority = string.Equals(campaign.Priority, "high", StringComparison.OrdinalIgnoreCase);
        string? routingKey = null;

        if (isHighPriority)
        {
            routingKey = HighPriorityRoutingKey;
        }
        else
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_rabbitMqService.TryEnsureConnection())
                {
                    _logger.LogWarning("RabbitMQ unavailable for campaign {CampaignId}; retrying in {Seconds}s.",
                        campaignId, BackoffSecondsWhenNoQueueAvailable);
                    await Task.Delay(TimeSpan.FromSeconds(BackoffSecondsWhenNoQueueAvailable), cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                routingKey = _rabbitMqService.TryGetFirstAvailableSmsTopicRoutingKey();
                if (routingKey != null)
                    break;

                _logger.LogInformation(
                    "No empty SMS topic queue for campaign {CampaignId}. Backing off {Seconds}s.",
                    campaignId, BackoffSecondsWhenNoQueueAvailable);
                await Task.Delay(TimeSpan.FromSeconds(BackoffSecondsWhenNoQueueAvailable), cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        if (routingKey == null)
            return;

        campaign.AssignedQueue = isHighPriority ? HighPriorityAssignedQueue : routingKey;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var deliveryDetails = await db.DeliveryDetails
            .Where(d => d.CampaignId == campaignId && d.Status == DeliveryStatus.Pending)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (deliveryDetails.Count == 0)
        {
            _logger.LogWarning("Campaign {CampaignId} has no pending delivery rows; finalizing to In Progress.", campaignId);
            await FinalizeCampaignAfterPublishAsync(db, campaign, cancellationToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation(
            "Publishing campaign {CampaignId}: {Count} message(s), routing key {RoutingKey}.",
            campaignId, deliveryDetails.Count, routingKey);

        messageProcessing.PublishCampaignToRabbitMqWithRoutingKey(campaign, deliveryDetails, routingKey);

        await FinalizeCampaignAfterPublishAsync(db, campaign, cancellationToken).ConfigureAwait(false);
    }

    private static async Task FinalizeCampaignAfterPublishAsync(
        ApplicationDbContext db,
        Campaign campaign,
        CancellationToken cancellationToken)
    {
        campaign.Status = CampaignStatus.InProgress.ToDatabaseString();
        campaign.SchedulingType = "immediate";
        campaign.ProcessingStartedAt = null;
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
