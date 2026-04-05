using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Inetlab.SMPP.PDU;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TransmitterMicroservice.DTOs;
using TransmitterMicroservice.Interfaces;
using TransmitterMicroservice.Options;

namespace TransmitterMicroservice;

public class TransmitterService : BackgroundService
{
    private readonly ILogger<TransmitterService> _logger;
    private readonly ISmppGateway _smppGateway;
    private readonly IRabbitMqManager _rabbitMqManager;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly SmppOptions _smppOptions;
    private TokenBucketRateLimiter? _rateLimiter;

    public TransmitterService(
        ILogger<TransmitterService> logger,
        ISmppGateway smppGateway,
        IRabbitMqManager rabbitMqManager,
        IOptions<SmppOptions> smppOptions,
        IOptions<RabbitMqOptions> rabbitMqOptions)
    {
        _logger = logger;
        _smppGateway = smppGateway;
        _rabbitMqManager = rabbitMqManager;
        _smppOptions = smppOptions.Value;
        _rabbitMqOptions = rabbitMqOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("=== Transmitter Microservice Starting ===");
        _logger.LogInformation("SMPP Server: {Host}:{Port}", _smppOptions.Host, _smppOptions.Port);
        _logger.LogInformation("RabbitMQ: {Host}:{Port}", _rabbitMqOptions.Host, _rabbitMqOptions.Port);
        _logger.LogInformation("High Priority Queue: {HighPriorityQueue}", _rabbitMqOptions.HighPriorityQueue);
        _logger.LogInformation("Input Queues: {InputQueues}", string.Join(", ", _rabbitMqOptions.InputQueues));
        _logger.LogInformation("Output Queue: {OutputQueue}", _rabbitMqOptions.OutputQueue);
        _logger.LogInformation("Rate Limit: {MessagesPerSecond} messages/second", _rabbitMqOptions.MessagesPerSecond);

        try
        {
            InitializeRateLimiter();
            await RunPollingLoopAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in TransmitterService");
            throw;
        }
    }

    private void InitializeRateLimiter()
    {
        var options = new TokenBucketRateLimiterOptions
        {
            TokenLimit = _rabbitMqOptions.MessagesPerSecond,
            ReplenishmentPeriod = TimeSpan.FromSeconds(1),
            TokensPerPeriod = _rabbitMqOptions.MessagesPerSecond,
            AutoReplenishment = true
        };

        _rateLimiter = new TokenBucketRateLimiter(options);
        _logger.LogInformation("Rate limiter initialized: {MessagesPerSecond} messages/second",
            _rabbitMqOptions.MessagesPerSecond);
    }

    private async Task RunPollingLoopAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Starting polling loop...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool smppReady = await _smppGateway.EnsureConnectedAsync(stoppingToken);
                if (!smppReady)
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                if (!_smppGateway.IsBound())
                {
                    _logger.LogWarning("SMPP not bound (safety guard); pausing consumption.");
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                bool rabbitMqReady = _rabbitMqManager.EnsureChannelHealthy();
                if (!rabbitMqReady)
                {
                    await Task.Delay(2000, stoppingToken);
                    continue;
                }

                var (result, queueName) = _rabbitMqManager.GetNextMessage();

                if (result != null && queueName != null)
                {
                    await ProcessMessageAsync(result, queueName, stoppingToken);
                }
                else
                {
                    await Task.Delay(100, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in polling loop");
                await Task.Delay(2000, stoppingToken);
            }
        }

        _logger.LogInformation("Polling loop stopped");
    }

    private async Task ProcessMessageAsync(BasicGetResult result, string queueName, CancellationToken cancellationToken)
    {
        ulong deliveryTag = result.DeliveryTag;
        string? messageBody = null;
        int? deliveryId = null;

        try
        {
            using var lease = await _rateLimiter!.AcquireAsync(permitCount: 1);
            if (!lease.IsAcquired)
            {
                _logger.LogWarning("Rate limiter denied permit. Requeuing message.");
                _rabbitMqManager.SafeNack(deliveryTag, requeue: true, deliveryId: null);
                return;
            }

            messageBody = Encoding.UTF8.GetString(result.Body.ToArray());
            _logger.LogDebug("Processing message from queue {QueueName}: {MessageBody}", queueName, messageBody);

            var sendRequest = JsonSerializer.Deserialize<SendRequestDto>(messageBody);

            if (sendRequest == null)
            {
                _logger.LogWarning("Failed to deserialize message. Rejecting without requeue.");
                _rabbitMqManager.SafeNack(deliveryTag, requeue: false, deliveryId: null);
                return;
            }

            deliveryId = sendRequest.DeliveryId;

            if (sendRequest.Type != "SendRequest")
            {
                _logger.LogDebug("Skipping message with Type: {Type} for DeliveryId: {DeliveryId}",
                    sendRequest.Type, sendRequest.DeliveryId);
                _rabbitMqManager.SafeAck(deliveryTag, deliveryId);
                return;
            }

            if (!deliveryId.HasValue)
            {
                _logger.LogWarning("Missing DeliveryId in SendRequest. Rejecting without requeue.");
                _rabbitMqManager.SafeNack(deliveryTag, requeue: false, deliveryId: null);
                return;
            }

            var sourceAddress = sendRequest.ActualSender ?? "0000000000";
            var destinationAddress = sendRequest.PhoneNumber;
            var messageText = sendRequest.MessageText;
            var deliveryIdValue = deliveryId.Value;

            void OnPartSent(SubmitSmResp submitResp, int partNumber, int totalParts)
            {
                var smscMessageId = submitResp.MessageId?.Trim() ?? string.Empty;
                if (string.IsNullOrEmpty(smscMessageId))
                {
                    // Fail fast: do not publish placeholder/internal IDs; require provider MessageId.
                    throw new InvalidOperationException("SubmitSmResp missing provider MessageId");
                }

                _logger.LogInformation("Part Sent - DeliveryId: {DeliveryId}, SmscMessageId: {SmscMessageId}, Status: {Status}",
                    deliveryIdValue, smscMessageId, submitResp.Header.Status.ToString());

                var resultDto = new SubmitSmRespDto
                {
                    Type = "SubmitSmResp",
                    DeliveryId = deliveryIdValue,
                    CampaignId = sendRequest.CampaignId,
                    PhoneNumber = sendRequest.PhoneNumber,
                    SmscMessageId = smscMessageId,
                    Status = submitResp.Header.Status.ToString(),
                    CommandStatus = (int)submitResp.Header.Status,
                    PartNumber = partNumber,
                    TotalParts = totalParts
                };
                var json = JsonSerializer.Serialize(resultDto);
                var body = Encoding.UTF8.GetBytes(json);
                _rabbitMqManager.SafePublish(deliveryIdValue, smscMessageId, submitResp.Header.Status.ToString(), body);
            }

            var sendResult = await _smppGateway.SendSmsAsync(
                sourceAddress,
                destinationAddress,
                messageText,
                sendRequest.DeliveryId,
                onPartSent: OnPartSent,
                cancellationToken);

            if (sendResult == null)
            {
                _logger.LogWarning("SendSmsAsync returned null (e.g. not bound). Nacking message for requeue. DeliveryId: {DeliveryId}, DeliveryTag: {DeliveryTag}",
                    deliveryId, deliveryTag);
                _rabbitMqManager.SafeNack(deliveryTag, requeue: true, deliveryId);
                return;
            }

            _rabbitMqManager.SafeAck(deliveryTag, deliveryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}, Queue: {QueueName}, MessageBody: {MessageBody}",
                deliveryTag, deliveryId, queueName, messageBody);

            _rabbitMqManager.SafeNack(deliveryTag, requeue: true, deliveryId);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping TransmitterService...");

        try
        {
            _rateLimiter?.Dispose();
            _rateLimiter = null;
            _logger.LogDebug("Rate limiter disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing rate limiter");
        }

        await _smppGateway.StopAsync(cancellationToken);
        _rabbitMqManager.Dispose();
        _logger.LogDebug("RabbitMQ resources disposed");

        await base.StopAsync(cancellationToken);
        _logger.LogInformation("TransmitterService stopped");
    }
}
