using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.DTOs;
using SmsGateway.Shared.Models;
using WebApplication1.Hubs;

namespace WebApplication1.Services;

public sealed class SmsBatchProcessor : ISmsBatchProcessor
{
    private const string SubmitSmRespType = "SubmitSmResp";
    private const string DlrType = "DLR";
    private const string EnquireLinkType = "EnquireLink";
    private const string SegmentStatusSent = "Sent";
    private const string SegmentStatusDelivered = "Delivered";
    private const string SegmentStatusFailed = "Failed";
    private const string SegmentStatusAccepted = "Accepted";

    /// <summary>Orphan DLRs are re-queued while <see cref="BufferedItem.RetryCount"/> is 0, 1, 2; at 3, they go to DLQ (3 retry rounds).</summary>
    private const int MaxDlrRetries = 3;
    private const string TimestampFormat = "HH:mm:ss dd/MM/yyyy";

    private readonly ApplicationDbContext _db;
    private readonly IHubContext<CampaignHub> _campaignHub;
    private readonly ILogger<SmsBatchProcessor> _logger;

    public SmsBatchProcessor(
        ApplicationDbContext db,
        IHubContext<CampaignHub> campaignHub,
        ILogger<SmsBatchProcessor> logger)
    {
        _db = db;
        _campaignHub = campaignHub;
        _logger = logger;
    }

    public async Task<bool> TryHandleEnquireLinkInlineAsync(string json, CancellationToken cancellationToken = default)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != EnquireLinkType)
                return false;

            if (!root.TryGetProperty("timestamp", out var tsEl))
                return true;

            var timestampStr = tsEl.GetString();
            if (string.IsNullOrEmpty(timestampStr))
                return true;

            var timestamp = DateTime.ParseExact(
                timestampStr,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

            var status = "OK";
            if (root.TryGetProperty("status", out var se))
                status = se.GetString() ?? "OK";

            var existing = await _db.SmscStatus.FindAsync(new object?[] { 1 }, cancellationToken);
            if (existing != null)
            {
                existing.Status = status;
                existing.Timestamp = timestamp;
            }
            else
            {
                _db.SmscStatus.Add(new SmscStatus { Status = status, Timestamp = timestamp });
            }

            await _db.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<SmsBatchProcessResult> ProcessBatchAsync(IEnumerable<BufferedItem> batch, CancellationToken cancellationToken = default)
    {
        var result = new SmsBatchProcessResult();
        var list = batch.ToList();
        if (list.Count == 0)
            return result;

        var submitIds = new HashSet<int>();
        var dlrSmscIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in list)
        {
            var dto = item.Dto;
            if (dto.Type == SubmitSmRespType && dto.DeliveryId > 0)
                submitIds.Add(dto.DeliveryId);
            if (!string.IsNullOrWhiteSpace(dto.SmscMessageId))
                dlrSmscIds.Add(dto.SmscMessageId.Trim());
        }

        var dlrMatchedSegments = await _db.DeliverySmscIds
            .Where(s => dlrSmscIds.Contains(s.SmscMessageId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var touchedDetailIds = new HashSet<int>(submitIds);
        foreach (var seg in dlrMatchedSegments)
            touchedDetailIds.Add(seg.DeliveryDetailId);

        var allSegmentsForBatch = await _db.DeliverySmscIds
            .Where(s => touchedDetailIds.Contains(s.DeliveryDetailId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var segmentBySmscAndPart = new Dictionary<(string Smsc, int Part), DeliverySmscId>();
        var segmentsBySmsc = new Dictionary<string, List<DeliverySmscId>>(StringComparer.Ordinal);
        foreach (var seg in allSegmentsForBatch)
        {
            segmentBySmscAndPart[(seg.SmscMessageId, seg.PartNumber)] = seg;
            if (!segmentsBySmsc.TryGetValue(seg.SmscMessageId, out var rowList))
            {
                rowList = new List<DeliverySmscId>();
                segmentsBySmsc[seg.SmscMessageId] = rowList;
            }

            rowList.Add(seg);
        }

        var segmentsByDeliveryId = allSegmentsForBatch
            .GroupBy(s => s.DeliveryDetailId)
            .ToDictionary(g => g.Key, g => g.ToList());

        var rowsById = await _db.DeliveryDetails
            .Where(d => touchedDetailIds.Contains(d.Id))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
        var rowById = rowsById.ToDictionary(r => r.Id);

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTime.UtcNow;

            var groupedBySmsc = new Dictionary<string, List<(int Index, BufferedItem Item)>>(StringComparer.Ordinal);
            var standaloneItems = new List<(int Index, BufferedItem Item)>();
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i];
                var dto = item.Dto;
                var smsc = dto.SmscMessageId?.Trim();
                if (!string.IsNullOrEmpty(smsc) && (dto.Type == SubmitSmRespType || dto.Type == DlrType))
                {
                    if (!groupedBySmsc.TryGetValue(smsc, out var group))
                    {
                        group = new List<(int Index, BufferedItem Item)>();
                        groupedBySmsc[smsc] = group;
                    }
                    group.Add((i, item));
                    continue;
                }

                standaloneItems.Add((i, item));
            }

            foreach (var (smsc, group) in groupedBySmsc)
            {
                group.Sort((a, b) => a.Index.CompareTo(b.Index));
                foreach (var (_, groupedItem) in group)
                {
                    if (groupedItem.Dto.Type == SubmitSmRespType)
                        ProcessSubmitSmResp(groupedItem, groupedItem.Dto, rowById, segmentBySmscAndPart, segmentsBySmsc, segmentsByDeliveryId, now, result);
                    else if (groupedItem.Dto.Type == DlrType)
                        ProcessDlr(groupedItem, groupedItem.Dto, rowById, segmentsBySmsc, segmentsByDeliveryId, now, result);
                }
            }

            foreach (var (_, item) in standaloneItems)
            {
                var dto = item.Dto;
                if (dto.Type == SubmitSmRespType)
                    ProcessSubmitSmResp(item, dto, rowById, segmentBySmscAndPart, segmentsBySmsc, segmentsByDeliveryId, now, result);
                else if (dto.Type == DlrType)
                    ProcessDlr(item, dto, rowById, segmentsBySmsc, segmentsByDeliveryId, now, result);
                else
                    result.ToAck.Add(item.DeliveryTag);
            }

            // Snapshot modified delivery rows (current in-memory Status) before SaveChanges; broadcast after commit so clients see persisted state.
            var deliveryStatusBroadcasts = CollectDeliveryStatusUpdates(_db);
            await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

            // Hub sends immediately after successful commit (same campaign group as JoinCampaign). Parallelize for large batches.
            await Task.WhenAll(
                deliveryStatusBroadcasts.Select(async t =>
                {
                    try
                    {
                        await _campaignHub
                            .SendDeliveryStatusAsync(t.CampaignId, t.DeliveryId, t.Status, cancellationToken)
                            .ConfigureAwait(false);
                        _logger.LogInformation("SignalR update sent for delivery {Id}", t.DeliveryId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "SignalR broadcast skipped for campaign {CampaignId} delivery {DeliveryId}",
                            t.CampaignId,
                            t.DeliveryId);
                    }
                })
            ).ConfigureAwait(false);

            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private void ProcessSubmitSmResp(
        BufferedItem item,
        SmsResultDto dto,
        Dictionary<int, DeliveryDetails> rowById,
        Dictionary<(string Smsc, int Part), DeliverySmscId> segmentBySmscAndPart,
        Dictionary<string, List<DeliverySmscId>> segmentsBySmsc,
        Dictionary<int, List<DeliverySmscId>> segmentsByDeliveryId,
        DateTime now,
        SmsBatchProcessResult result)
    {
        if (dto.DeliveryId <= 0 || !rowById.TryGetValue(dto.DeliveryId, out var parent))
        {
            _logger.LogWarning("SubmitSmResp skipped: unknown delivery_id {DeliveryId}", dto.DeliveryId);
            result.ToAck.Add(item.DeliveryTag);
            return;
        }

        var smscRaw = dto.SmscMessageId?.Trim();
        if (string.IsNullOrEmpty(smscRaw))
        {
            _logger.LogWarning("SubmitSmResp skipped: missing smsc_message_id for delivery_id {DeliveryId}", dto.DeliveryId);
            result.ToAck.Add(item.DeliveryTag);
            return;
        }

        var commandStatus = ResolveSubmitSmRespCommandStatus(dto);
        var partNumber = dto.PartNumber > 0 ? dto.PartNumber : 1;
        var totalParts = dto.TotalParts > 0 ? dto.TotalParts : 1;

        var compositeKey = (smscRaw, partNumber);

        // Same SMSC MessageId across multipart parts: match on (smsc_message_id, part_number). Never update a row for a different part.
        if (segmentBySmscAndPart.TryGetValue(compositeKey, out var tracked) &&
            tracked.DeliveryDetailId == parent.Id)
        {
            tracked.TotalParts = totalParts;
            tracked.Status = commandStatus == 0 ? SegmentStatusSent : SegmentStatusFailed;
            if (commandStatus != 0)
                tracked.DeliveredAt = null;
        }
        else
        {
            if (segmentBySmscAndPart.TryGetValue(compositeKey, out var otherDelivery) &&
                otherDelivery.DeliveryDetailId != parent.Id)
            {
                _logger.LogWarning(
                    "SubmitSmResp: ({Smsc}, part {Part}) already belongs to delivery_detail_id {Existing}; also received for delivery_detail_id {DeliveryId}. Insert may violate unique index.",
                    smscRaw,
                    partNumber,
                    otherDelivery.DeliveryDetailId,
                    parent.Id);
            }

            var seg = new DeliverySmscId
            {
                DeliveryDetailId = parent.Id,
                SmscMessageId = smscRaw,
                PartNumber = partNumber,
                TotalParts = totalParts,
                Status = commandStatus == 0 ? SegmentStatusSent : SegmentStatusFailed,
                DeliveredAt = null
            };
            _db.DeliverySmscIds.Add(seg);
            segmentBySmscAndPart[compositeKey] = seg;

            if (!segmentsBySmsc.TryGetValue(smscRaw, out var smscList))
            {
                smscList = new List<DeliverySmscId>();
                segmentsBySmsc[smscRaw] = smscList;
            }

            smscList.Add(seg);

            if (!segmentsByDeliveryId.TryGetValue(parent.Id, out var segList))
            {
                segList = new List<DeliverySmscId>();
                segmentsByDeliveryId[parent.Id] = segList;
            }

            segList.Add(seg);
        }

        if (commandStatus == 0)
        {
            if (!parent.SentAt.HasValue)
                parent.SentAt = now;
            // Acceptable (4): first "sent to SMSC" state — tracked row is picked up by CollectDeliveryStatusUpdates → SignalR after SaveChanges.
            SetParentStatusMonotonic(parent, DeliveryStatus.Acceptable);
            parent.ErrorMessage = null;
            parent.Processed = false;
            parent.ProcessedAt = null;
            if (string.IsNullOrEmpty(parent.SmscMessageId))
                parent.SmscMessageId = smscRaw;
        }
        else
        {
            parent.ErrorMessage = BuildSubmitSmRespErrorMessage(commandStatus, dto);
            SetParentStatusMonotonic(parent, DeliveryStatus.Failed);
            parent.Processed = true;
            parent.ProcessedAt = now;
        }

        result.ToAck.Add(item.DeliveryTag);
    }

    private void ProcessDlr(
        BufferedItem item,
        SmsResultDto dto,
        Dictionary<int, DeliveryDetails> rowById,
        Dictionary<string, List<DeliverySmscId>> segmentsBySmsc,
        Dictionary<int, List<DeliverySmscId>> segmentsByDeliveryId,
        DateTime now,
        SmsBatchProcessResult result)
    {
        var smsc = dto.SmscMessageId?.Trim() ?? string.Empty;
        if (string.IsNullOrEmpty(smsc))
        {
            result.ToAck.Add(item.DeliveryTag);
            return;
        }

        if (!segmentsBySmsc.TryGetValue(smsc, out var candidates) || candidates.Count == 0)
        {
            HandleOrphanDlr(item, smsc, result);
            return;
        }

        var partFromDto = dto.PartNumber > 0 ? dto.PartNumber : 0;
        List<DeliverySmscId> segmentsToUpdate;
        if (partFromDto > 0)
        {
            var match = candidates.FirstOrDefault(s => s.PartNumber == partFromDto);
            if (match == null)
            {
                _logger.LogWarning(
                    "DLR: smsc_message_id {Smsc} has no segment with part_number {Part}",
                    smsc,
                    partFromDto);
                HandleOrphanDlr(item, smsc, result);
                return;
            }

            segmentsToUpdate = [match];
        }
        else if (candidates.Count == 1)
        {
            segmentsToUpdate = [candidates[0]];
        }
        else
        {
            var parentIds = candidates.Select(s => s.DeliveryDetailId).Distinct().ToList();
            if (parentIds.Count != 1)
            {
                _logger.LogWarning(
                    "DLR: smsc_message_id {Smsc} matches {SegmentCount} segments across {DeliveryCount} deliveries without part_number; cannot route.",
                    smsc,
                    candidates.Count,
                    parentIds.Count);
                HandleOrphanDlr(item, smsc, result);
                return;
            }

            // Same MessageId on every part: one DLR may apply to all segments for this delivery.
            segmentsToUpdate = candidates;
        }

        DeliveryDetails? parentForCompletion = null;
        foreach (var segment in segmentsToUpdate)
        {
            if (!rowById.TryGetValue(segment.DeliveryDetailId, out var parent))
            {
                HandleOrphanDlr(item, smsc, result);
                return;
            }

            parentForCompletion ??= parent;
            ApplyDlrToSegment(segment, parent, dto, now);
        }

        if (parentForCompletion != null)
            TryCompleteMultipartParent(parentForCompletion, segmentsByDeliveryId, now);

        result.ToAck.Add(item.DeliveryTag);
    }

    private void ApplyDlrToSegment(DeliverySmscId segment, DeliveryDetails parent, SmsResultDto dto, DateTime now)
    {
        var mapped = MapDlrStatusToDeliveryStatus(dto.Status);
        var isTerminal = IsTerminalDlrMessageState(dto.Status);
        var delAt = ParseDlrTimestamp(dto.Timestamp);

        if (mapped == DeliveryStatus.Successful && isTerminal)
        {
            segment.Status = SegmentStatusDelivered;
            segment.DeliveredAt = delAt ?? now;
        }
        else if (mapped == DeliveryStatus.Failed || mapped == DeliveryStatus.Expired)
        {
            segment.Status = SegmentStatusFailed;
            segment.DeliveredAt = delAt;
            parent.ErrorMessage = dto.Status ?? "DLR Failure";
            SetParentStatusMonotonic(parent, DeliveryStatus.Failed);
            parent.Processed = true;
            parent.ProcessedAt = now;
        }
        else
        {
            segment.Status = SegmentStatusAccepted;
            segment.DeliveredAt = delAt;
            // Intermediate provider ACK (e.g. ACCEPTED): promote parent so DB/UI/SignalR can show Accepted (3) without waiting for final DLR.
            if (mapped == DeliveryStatus.Accepted
                && parent.Status is not DeliveryStatus.Successful
                and not DeliveryStatus.Failed
                and not DeliveryStatus.Expired
                and not DeliveryStatus.TimeoutSMSC)
            {
                SetParentStatusMonotonic(parent, DeliveryStatus.Accepted);
            }
        }
    }

    /// <summary>
    /// When distinct delivered part numbers match total_parts, mark parent Successful and processed.
    /// </summary>
    private void TryCompleteMultipartParent(
        DeliveryDetails parent,
        Dictionary<int, List<DeliverySmscId>> segmentsByDeliveryId,
        DateTime now)
    {
        if (parent.Status == DeliveryStatus.Failed)
            return;

        if (!segmentsByDeliveryId.TryGetValue(parent.Id, out var segments) || segments.Count == 0)
            return;

        var totalParts = segments.Max(s => s.TotalParts);
        if (totalParts <= 0)
            totalParts = segments.Select(s => s.PartNumber).Where(n => n > 0).Distinct().Count();

        var deliveredDistinctParts = segments
            .Where(s => string.Equals(s.Status, SegmentStatusDelivered, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.PartNumber)
            .Where(n => n > 0)
            .Distinct()
            .Count();

        _logger.LogInformation(
            "Message {parentId}: {deliveredCount}/{totalParts} segments delivered.",
            parent.Id,
            deliveredDistinctParts,
            totalParts);

        if (totalParts <= 0 || deliveredDistinctParts < totalParts)
            return;

        parent.Status = DeliveryStatus.Successful;
        parent.DeliveredAt = segments
            .Where(s => s.DeliveredAt.HasValue)
            .Select(s => s.DeliveredAt!.Value)
            .DefaultIfEmpty(now)
            .Max();
        parent.Processed = true;
        parent.ProcessedAt = now;
        parent.ErrorMessage = null;
    }

    private static void SetParentStatusMonotonic(DeliveryDetails parent, DeliveryStatus nextStatus)
    {
        var currentTerminal = parent.Status is DeliveryStatus.Successful
            or DeliveryStatus.Failed
            or DeliveryStatus.Expired
            or DeliveryStatus.TimeoutSMSC;

        if (currentTerminal && nextStatus == DeliveryStatus.Acceptable)
            return;

        parent.Status = nextStatus;
    }

    private void HandleOrphanDlr(BufferedItem item, string smscId, SmsBatchProcessResult result)
    {
        if (item.RetryCount < MaxDlrRetries)
        {
            if (item.RawBody != null)
            {
                result.DlrRetries.Add(new DlrRetryRequest
                {
                    Body = item.RawBody,
                    RetryCount = item.RetryCount + 1
                });
            }
            else
            {
                _logger.LogWarning(
                    "DLR orphan retry skipped: missing RawBody for SmscMessageId {SmscMessageId}, RetryCount {RetryCount}. Acking to avoid stuck message.",
                    smscId, item.RetryCount);
            }

            result.ToAck.Add(item.DeliveryTag);
        }
        else
        {
            _logger.LogError(
                "DLR Orphan: SmscMessageId {SmscMessageId} not found after 3 retries",
                smscId);

            if (item.RawBody != null)
                result.SmsResultsDlqBodies.Add(item.RawBody);

            var payloadJson = item.RawBody != null ? Encoding.UTF8.GetString(item.RawBody) : null;
            _db.FailedResultsLog.Add(new FailedResultsLog
            {
                SourceType = DlrType,
                SmscMessageId = smscId,
                PayloadJson = payloadJson,
                RetryCount = item.RetryCount
            });

            result.ToAck.Add(item.DeliveryTag);
        }
    }

    /// <summary>
    /// Resolves SMPP command status: prefers <see cref="SmsResultDto.CommandStatus"/>, else legacy <see cref="SmsResultDto.Status"/> string (ESME_ROK = 0).
    /// </summary>
    private static int ResolveSubmitSmRespCommandStatus(SmsResultDto dto)
    {
        if (dto.CommandStatus.HasValue)
            return dto.CommandStatus.Value;
        if (string.IsNullOrEmpty(dto.Status) ||
            string.Equals(dto.Status, "ESME_ROK", StringComparison.OrdinalIgnoreCase))
            return 0;
        return -1;
    }

    private static string BuildSubmitSmRespErrorMessage(int commandStatus, SmsResultDto dto)
    {
        if (commandStatus >= 0)
            return $"SMSC Error: {commandStatus}";
        return $"SMSC Error: {dto.Status ?? "unknown"}";
    }

    private static bool IsNonTerminalDlrMessageState(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return false;

        var u = status.Trim().ToUpperInvariant();
        if (u is "ENROUTE" or "ACCEPTED")
            return true;
        if (u is "ENROUTED" or "ACCEPTD")
            return true;
        if (u.Contains("SCHEDULED", StringComparison.Ordinal))
            return true;
        if (u.StartsWith("BUFFER", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }

    private static bool IsTerminalDlrMessageState(string? status) =>
        !string.IsNullOrWhiteSpace(status) && !IsNonTerminalDlrMessageState(status);

    private static DeliveryStatus MapDlrStatusToDeliveryStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return DeliveryStatus.Unknown;
        var s = status.ToUpperInvariant();
        if (s.Contains("DELIV") || s == "DELIVERED")
            return DeliveryStatus.Successful;
        if (s.Contains("ACCEPT") || s.Contains("ACCEPTD"))
            return DeliveryStatus.Accepted;
        if (s.Contains("REJECT") || s.Contains("UNDELIV") || s.Contains("FAIL"))
            return DeliveryStatus.Failed;
        if (s.Contains("EXPIR"))
            return DeliveryStatus.Expired;
        return DeliveryStatus.Unknown;
    }

    /// <summary>
    /// Collects <see cref="DeliveryDetails"/> rows whose <see cref="DeliveryDetails.Status"/> changed in this unit of work,
    /// immediately before <see cref="DbContext.SaveChangesAsync"/>. Includes, in the same batch:
    /// <list type="bullet">
    /// <item><description>First SubmitSmResp success: <see cref="DeliveryStatus.Pending"/> → <see cref="DeliveryStatus.Acceptable"/> (4) for "נשלח".</description></item>
    /// <item><description>SubmitSmResp failure: → <see cref="DeliveryStatus.Failed"/> (2).</description></item>
    /// <item><description>DLR terminal failure/expiry: → <see cref="DeliveryStatus.Failed"/> (2).</description></item>
    /// <item><description>Intermediate DLR mapped to <see cref="DeliveryStatus.Accepted"/> (3): parent row updated in <see cref="ApplyDlrToSegment"/>.</description></item>
    /// <item><description>Multipart completion: → <see cref="DeliveryStatus.Successful"/> (1) in <see cref="TryCompleteMultipartParent"/>.</description></item>
    /// </list>
    /// Each modified row is broadcast once per batch with its final <see cref="DeliveryDetails.Status"/> for that batch.
    /// </summary>
    private static List<(int CampaignId, int DeliveryId, DeliveryStatus Status)> CollectDeliveryStatusUpdates(
        ApplicationDbContext db)
    {
        var list = new List<(int, int, DeliveryStatus)>();
        foreach (var entry in db.ChangeTracker.Entries<DeliveryDetails>())
        {
            if (entry.State != EntityState.Modified)
                continue;
            if (!entry.Property(e => e.Status).IsModified)
                continue;
            var e = entry.Entity;
            list.Add((e.CampaignId, e.Id, e.Status));
        }

        return list;
    }

    private static DateTime? ParseDlrTimestamp(string? timestamp)
    {
        if (string.IsNullOrWhiteSpace(timestamp))
            return null;
        try
        {
            return DateTime.ParseExact(
                timestamp,
                TimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        }
        catch
        {
            return DateTime.UtcNow;
        }
    }
}
