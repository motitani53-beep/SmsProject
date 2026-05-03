using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.DTOs;
using SmsGateway.Shared.Models;
using SmsGateway.Shared.Options;
using WebApplication1.DTOs;
using WebApplication1.Services;

namespace WebApplication1.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SmsController : ControllerBase
{
    private const string TestSourceTypeHeaderName = "SourceType";
    private const string TestSourceTypeHeaderValue = "Test";
    private const string SmsExchange = "sms_exchange";
    private const string HighPriorityRoutingKey = "sms.priority";

    private static readonly Regex IsraeliMsisdn = new(@"^972\d{9}$", RegexOptions.Compiled);

    private readonly ApplicationDbContext _context;
    private readonly MessageProcessingService _messageProcessingService;
    private readonly SenderPhoneNumberService _senderPhoneService;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<SmsController> _logger;

    public SmsController(
        ApplicationDbContext context,
        MessageProcessingService messageProcessingService,
        SenderPhoneNumberService senderPhoneService,
        IRabbitMqService rabbitMqService,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<SmsController> logger)
    {
        _context = context;
        _messageProcessingService = messageProcessingService;
        _senderPhoneService = senderPhoneService;
        _rabbitMqService = rabbitMqService;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Sends a single test SMS. Persists a row to <c>test_messages</c>, then publishes to <c>sms_high_priority</c>
    /// with header <c>SourceType: Test</c>. SmsResultProcessor uses that header to route results back to
    /// <c>test_smsc_ids</c> instead of <c>delivery_smsc_ids</c>.
    /// </summary>
    [HttpPost("send-test")]
    public async Task<IActionResult> SendTest([FromBody] SendTestRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var phone = (request.PhoneNumber ?? string.Empty).Trim();
        if (!IsraeliMsisdn.IsMatch(phone))
        {
            return BadRequest(new { error = "מספר טלפון חייב להתחיל ב-972 ולהיות באורך 12 ספרות." });
        }

        if (string.IsNullOrWhiteSpace(request.MessageContent))
        {
            return BadRequest(new { error = "תוכן ההודעה ריק." });
        }

        if (!await _context.Database.CanConnectAsync())
        {
            _logger.LogWarning("SendTest rejected: Database unreachable");
            return StatusCode(503, new { error = "Error: Could not connect to Database" });
        }

        if (!_rabbitMqService.TryEnsureConnection())
        {
            _logger.LogWarning("SendTest rejected: Message Queue unreachable");
            return StatusCode(503, new { error = "Error: Could not connect to Message Queue" });
        }

        var resolvedMessage = _messageProcessingService.ReplaceMessageFields(
            request.MessageContent,
            request.CustomFields ?? new Dictionary<string, string>());

        var testMessage = new TestMessage
        {
            PhoneNumber = phone,
            MessageContent = resolvedMessage,
            Status = "Pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.TestMessages.Add(testMessage);
        await _context.SaveChangesAsync();

        var senderValue = string.IsNullOrWhiteSpace(request.SenderValue) ? null : request.SenderValue.Trim();
        var actualSender = !string.IsNullOrEmpty(senderValue)
            ? senderValue
            : CampaignSenderResolver.ResolveActualSender(
                campaignId: 0,
                messageIndex: testMessage.Id,
                senderType: "random",
                senderValue: null,
                senderService: _senderPhoneService);

        var payload = new SmsMessageDto
        {
            Type = "SendRequest",
            DeliveryId = 0,
            CampaignId = 0,
            PhoneNumber = phone,
            MessageText = resolvedMessage,
            ActualSender = actualSender,
            TestMessageId = testMessage.Id
        };

        var headers = new Dictionary<string, object>
        {
            { TestSourceTypeHeaderName, TestSourceTypeHeaderValue }
        };

        try
        {
            _rabbitMqService.PublishJson(SmsExchange, HighPriorityRoutingKey, payload, headers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish test message {TestMessageId} to RabbitMQ", testMessage.Id);
            testMessage.Status = "Failed";
            await _context.SaveChangesAsync();
            return StatusCode(502, new { error = "Failed to publish test message to queue", details = ex.Message });
        }

        _logger.LogInformation(
            "Test SMS {TestMessageId} published to {RoutingKey} with SourceType=Test (recipient {Phone})",
            testMessage.Id, HighPriorityRoutingKey, phone);

        return Ok(new
        {
            testMessageId = testMessage.Id,
            phoneNumber = phone,
            messageContent = resolvedMessage,
            status = testMessage.Status,
            createdAt = testMessage.CreatedAt
        });
    }
}
