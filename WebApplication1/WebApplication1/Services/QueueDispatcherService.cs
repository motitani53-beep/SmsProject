using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.Models;
using SmsGateway.Shared.Options;

namespace WebApplication1.Services;

/// <summary>
/// Consumes campaign IDs from campaign_pending, finds an available topic queue (MessageCount == 0),
/// updates the campaign (assigned_queue, status = "In Progress"), and publishes all delivery messages to that queue.
/// </summary>
public class QueueDispatcherService : BackgroundService
{
    private const string SmsExchange = "sms_exchange";
    private const int BackoffSecondsWhenNoQueueAvailable = 30;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueueDispatcherService> _logger;
    private readonly RabbitMqOptions _options;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly object _channelLock = new();
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    public QueueDispatcherService(
        IServiceScopeFactory scopeFactory,
        ILogger<QueueDispatcherService> logger,
        IOptions<RabbitMqOptions> options,
        IRabbitMqService rabbitMqService)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _rabbitMqService = rabbitMqService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QueueDispatcherService starting. Listening on queue: {Queue}", _options.CampaignPendingQueue);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!EnsureConnection())
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                BasicGetResult? result = null;
                lock (_channelLock)
                {
                    if (_channel == null || _channel.IsClosed) continue;
                    try
                    {
                        result = _channel.BasicGet(_options.CampaignPendingQueue, autoAck: false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "BasicGet failed for {Queue}", _options.CampaignPendingQueue);
                    }
                }

                if (result == null)
                {
                    await Task.Delay(500, stoppingToken);
                    continue;
                }

                var body = result.Body.ToArray();
                var campaignIdStr = Encoding.UTF8.GetString(body);
                if (!int.TryParse(campaignIdStr.Trim(), out var campaignId))
                {
                    _logger.LogWarning("Invalid CampaignId in message: {Body}", campaignIdStr);
                    Ack(result.DeliveryTag);
                    continue;
                }

                await ProcessCampaignAsync(campaignId, result.DeliveryTag, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "QueueDispatcherService loop error");
                await Task.Delay(2000, stoppingToken);
            }
        }

        _logger.LogInformation("QueueDispatcherService stopped.");
    }

    private async Task ProcessCampaignAsync(int campaignId, ulong deliveryTag, CancellationToken stoppingToken)
    {
        string? selectedRoutingKey = null;

        while (!stoppingToken.IsCancellationRequested)
        {
            selectedRoutingKey = TryFindAvailableQueue();
            if (selectedRoutingKey != null)
                break;

            _logger.LogInformation("No queue available for campaign {CampaignId}. Backing off for {Seconds}s before retry.", campaignId, BackoffSecondsWhenNoQueueAvailable);
            await Task.Delay(TimeSpan.FromSeconds(BackoffSecondsWhenNoQueueAvailable), stoppingToken);
        }

        if (selectedRoutingKey == null)
            return;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var messageProcessing = scope.ServiceProvider.GetRequiredService<MessageProcessingService>();

        var campaign = await db.Campaigns.FindAsync(campaignId);
        if (campaign == null)
        {
            _logger.LogWarning("Campaign {CampaignId} not found. Acking message.", campaignId);
            Ack(deliveryTag);
            return;
        }

        // 1) Update DB first (assigned_queue, status); only then publish messages
        campaign.AssignedQueue = selectedRoutingKey;
        campaign.Status = "In Progress";
        await db.SaveChangesAsync();

        // 2) Load delivery details and publish to the selected topic queue via MessageProcessingService
        var deliveryDetails = await db.DeliveryDetails
            .Where(d => d.CampaignId == campaignId)
            .OrderBy(d => d.Id)
            .ToListAsync();

        _logger.LogInformation("Campaign {Id} assigned to queue {QueueName}. Loading {Count} messages...",
            campaignId, selectedRoutingKey, deliveryDetails.Count);

        messageProcessing.PublishCampaignToRabbitMqWithRoutingKey(campaign, deliveryDetails, selectedRoutingKey);

        Ack(deliveryTag);
    }

    /// <summary>
    /// Iterates smsc.topic.0 .. smsc.topic.(N-1) (queue names sms_queue_0 .. sms_queue_(N-1)), returns first with MessageCount == 0.
    /// </summary>
    private string? TryFindAvailableQueue() => _rabbitMqService.TryGetFirstAvailableSmsTopicRoutingKey();

    private bool EnsureConnection()
    {
        lock (_channelLock)
        {
            if (_channel != null && !_channel.IsClosed)
                return true;
        }

        try
        {
            _connection?.Close();
            _connection?.Dispose();
            lock (_channelLock)
            {
                _channel?.Close();
                _channel?.Dispose();
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection();
            lock (_channelLock)
            {
                _channel = _connection.CreateModel();
                _channel.QueueDeclare(
                    queue: _options.CampaignPendingQueue,
                    durable: true,
                    exclusive: false,
                    autoDelete: false);
            }

            _logger.LogInformation("QueueDispatcherService connected to RabbitMQ {Host}:{Port}", _options.Host, _options.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "QueueDispatcherService could not connect to RabbitMQ");
            return false;
        }
    }

    private void Ack(ulong deliveryTag)
    {
        lock (_channelLock)
        {
            try
            {
                _channel?.BasicAck(deliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BasicAck failed");
            }
        }
    }

    public override void Dispose()
    {
        if (_disposed) return;
        lock (_channelLock)
        {
            _channel?.Close();
            _channel?.Dispose();
            _channel = null;
        }
        _connection?.Close();
        _connection?.Dispose();
        _connection = null;
        _disposed = true;
        base.Dispose();
    }
}
