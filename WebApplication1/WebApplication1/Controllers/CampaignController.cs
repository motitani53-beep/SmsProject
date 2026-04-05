using System.Collections.Generic;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.Models;
using SmsGateway.Shared.Options;
using WebApplication1.DTOs;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CampaignController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly MessageProcessingService _messageProcessingService;
    private readonly SenderPhoneNumberService _senderPhoneService;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<CampaignController> _logger;

    public CampaignController(
        ApplicationDbContext context,
        MessageProcessingService messageProcessingService,
        SenderPhoneNumberService senderPhoneService,
        IRabbitMqService rabbitMqService,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<CampaignController> logger)
    {
        _context = context;
        _messageProcessingService = messageProcessingService;
        _senderPhoneService = senderPhoneService;
        _rabbitMqService = rabbitMqService;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateCampaign([FromBody] CampaignRequestDto request)
    {
        _logger.LogInformation("Received campaign creation request: {CampaignName}", request.CampaignName);

        if (!ModelState.IsValid)
        {
            _logger.LogWarning("Invalid model state for campaign request");
            return BadRequest(ModelState);
        }

        try
        {
            // Validate scheduling
            if (request.Scheduling.Type == "scheduled" && !request.Scheduling.ScheduledTime.HasValue)
            {
                _logger.LogWarning("Scheduled type requires ScheduledTime");
                return BadRequest(new { error = "Scheduled type requires ScheduledTime" });
            }

            // Verify database connectivity
            if (!await _context.Database.CanConnectAsync())
            {
                _logger.LogWarning("CreateCampaign rejected: Database unreachable");
                return StatusCode(503, new { error = "Error: Could not connect to Database" });
            }

            // Verify RabbitMQ connectivity
            if (!_rabbitMqService.TryEnsureConnection())
            {
                _logger.LogWarning("CreateCampaign rejected: Message Queue unreachable");
                return StatusCode(503, new { error = "Error: Could not connect to Message Queue" });
            }

            var isHighPriority = string.Equals(request.Priority, "high", StringComparison.OrdinalIgnoreCase);
            // Normalize + de-duplicate recipients by phone after request validation.
            var uniqueRecipients = request.Recipients
                .Where(r => !string.IsNullOrWhiteSpace(r.PhoneNumber))
                .GroupBy(r => r.PhoneNumber.Trim())
                .Select(g =>
                {
                    var first = g.First();
                    first.PhoneNumber = g.Key;
                    return first;
                })
                .ToList();

            if (uniqueRecipients.Count == 0)
            {
                _logger.LogWarning("CreateCampaign rejected: no valid recipients after filtering.");
                return BadRequest(new { error = "No valid recipients after filtering" });
            }

            var filteredOutCount = request.Recipients.Count - uniqueRecipients.Count;
            if (filteredOutCount > 0)
            {
                _logger.LogInformation(
                    "Campaign {CampaignName}: filtered out {FilteredOut} duplicate/invalid recipient(s).",
                    request.CampaignName,
                    filteredOutCount);
            }

            // Start with filtered unique count; after inserts we overwrite with exact inserted row count.
            var recipientCount = uniqueRecipients.Count;
            // High-priority: queue assigned immediately; others until QueueDispatcherService picks them up (status tracks lifecycle: In Progress, etc.)
            var campaign = new Campaign
            {
                CampaignName = request.CampaignName,
                MessageContent = request.MessageContent,
                MessageLanguage = request.MessageLanguage,
                SenderType = request.SenderConfig.SenderType,
                SenderValue = request.SenderConfig.SenderValue,
                SchedulingType = request.Scheduling.Type,
                ScheduledTime = request.Scheduling.ScheduledTime,
                Priority = request.Priority,
                Code = request.Code,
                Provider = request.Provider,
                TotalMessages = recipientCount,
                TotalSentMessages = 0,
                Status = "In Progress",
                AssignedQueue = isHighPriority ? "sms_high_priority" : null
            };

            _context.Campaigns.Add(campaign);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Campaign created with ID: {CampaignId}", campaign.Id);

            // Add delivery details (merged recipients and message logs)
            var insertedDeliveryRows = 0;
            for (var i = 0; i < uniqueRecipients.Count; i++)
            {
                var recipientDto = uniqueRecipients[i];

                var customFields = recipientDto.CustomFields ?? new Dictionary<string, string>();

                JsonElement? additionalData = null;
                if (customFields.Count > 0)
                    additionalData = JsonSerializer.SerializeToElement(customFields);

                // Resolved message (same logic as MessageProcessingService, so DB and Rabbit message match)
                var resolvedMessageContent = _messageProcessingService.ReplaceMessageFields(
                    campaign.MessageContent, customFields);

                // Assign actual sender (same logic as MessageProcessingService, so DB and Rabbit message match)
                var actualSender = _senderPhoneService.GetNextPhoneNumberForCampaign(
                    campaign.Id, i, campaign.SenderType ?? string.Empty, campaign.SenderValue);

                _logger.LogInformation("[Campaign {CampaignId}] Mapping recipient {RecipientNumber} to sender {SenderNumber}.",
                    campaign.Id, recipientDto.PhoneNumber, actualSender);

                var deliveryDetail = new DeliveryDetails
                {
                    CampaignId = campaign.Id,
                    PhoneNumber = recipientDto.PhoneNumber,
                    Status = DeliveryStatus.Pending,
                    Processed = false,
                    MessageContent = resolvedMessageContent,
                    AdditionalData = additionalData,
                    ActualSender = actualSender
                };

                _context.DeliveryDetails.Add(deliveryDetail);
                insertedDeliveryRows++;
            }

            await _context.SaveChangesAsync();

            // Keep campaign aggregate aligned with exact number of rows inserted.
            if (campaign.TotalMessages != insertedDeliveryRows)
            {
                campaign.TotalMessages = insertedDeliveryRows;
                await _context.SaveChangesAsync();
            }

            _logger.LogInformation("Added {Count} delivery details to campaign {CampaignId}",
                insertedDeliveryRows, campaign.Id);

            const string highPriorityQueue = "sms_high_priority";
            const string highPriorityRoutingKey = "sms.priority";

            if (string.Equals(campaign.Priority, "high", StringComparison.OrdinalIgnoreCase))
            {
                // High-priority: campaign already saved with Status=In Progress, AssignedQueue=sms_high_priority; publish directly, do not use campaign_pending
                var deliveryDetails = await _context.DeliveryDetails
                    .AsNoTracking()
                    .Where(d => d.CampaignId == campaign.Id)
                    .OrderBy(d => d.Id)
                    .ToListAsync();

                _messageProcessingService.PublishCampaignToRabbitMqWithRoutingKey(campaign, deliveryDetails, highPriorityRoutingKey);
                _logger.LogInformation("High-priority campaign {CampaignId}: published {Count} messages to {Queue}.",
                    campaign.Id, deliveryDetails.Count, highPriorityQueue);

                return Ok(new
                {
                    campaignId = campaign.Id,
                    status = campaign.Status,
                    message = "Campaign created and published to high-priority queue.",
                    recipientsCount = insertedDeliveryRows
                });
            }

            // Normal: publish only campaign ID to campaign_pending; QueueDispatcherService will assign queue and publish SMS messages
            var campaignIdBytes = Encoding.UTF8.GetBytes(campaign.Id.ToString());
            _rabbitMqService.Publish("", _rabbitMqOptions.CampaignPendingQueue, campaignIdBytes);
            _logger.LogInformation("Published campaign {CampaignId} to {Queue} for dispatching.", campaign.Id, _rabbitMqOptions.CampaignPendingQueue);

            return Ok(new
            {
                campaignId = campaign.Id,
                status = campaign.Status,
                message = "Campaign created successfully; dispatching is queued.",
                recipientsCount = insertedDeliveryRows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating campaign");
            return StatusCode(500, new { error = "An error occurred while creating the campaign", details = ex.Message });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetCampaign(int id)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.DeliveryDetails)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
        {
            return NotFound(new { error = "Campaign not found" });
        }

        return Ok(campaign);
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaigns()
    {
        var campaigns = await _context.Campaigns
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(campaigns);
    }

    /// <summary>
    /// Returns delivery summary for a campaign: counts by status and last updated.
    /// </summary>
    [HttpGet("{id}/delivery-summary")]
    public async Task<IActionResult> GetDeliverySummary(int id)
    {
        var exists = await _context.Campaigns.AsNoTracking().AnyAsync(c => c.Id == id);
        if (!exists)
            return NotFound(new { error = "Campaign not found" });

        var statusCounts = await _context.DeliveryDetails
            .AsNoTracking()
            .Where(d => d.CampaignId == id)
            .GroupBy(d => d.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        var lastUpdated = await _context.DeliveryDetails
            .AsNoTracking()
            .Where(d => d.CampaignId == id)
            .Select(d => d.DeliveredAt ?? d.SentAt ?? (DateTime?)d.CreatedAt)
            .MaxAsync();

        int Pending() => statusCounts.Where(x => x.Status == DeliveryStatus.Pending).Sum(x => x.Count);
        int Sent() => statusCounts.Where(x => x.Status == DeliveryStatus.Acceptable || x.Status == DeliveryStatus.Accepted).Sum(x => x.Count);
        int Delivered() => statusCounts.Where(x => x.Status == DeliveryStatus.Successful).Sum(x => x.Count);
        int Failed() => statusCounts.Where(x => x.Status == DeliveryStatus.Failed || x.Status == DeliveryStatus.Expired || x.Status == DeliveryStatus.TimeoutSMSC || x.Status == DeliveryStatus.Unknown).Sum(x => x.Count);

        return Ok(new
        {
            Pending = Pending(),
            Sent = Sent(),
            Delivered = Delivered(),
            Failed = Failed(),
            LastUpdated = lastUpdated
        });
    }
}

