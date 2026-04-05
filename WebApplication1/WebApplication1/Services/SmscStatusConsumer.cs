using System.Globalization;
using System.Text;
using System.Text.Json;
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
/// Consumes EnquireLink messages from smsc_server_status (and legacy smsc_results),
/// parses timestamp with HH:mm:ss dd/MM/yyyy, and inserts SmscStatus records using scoped DbContext.
/// </summary>
public class SmscStatusConsumer : BackgroundService
{
    private const string EnquireLinkType = "EnquireLink";
    private const string TimestampFormat = "HH:mm:ss dd/MM/yyyy";

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmscStatusConsumer> _logger;
    private readonly RabbitMqOptions _options;
    private readonly SmscStatusStore _store;
    private readonly object _channelLock = new();
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    public SmscStatusConsumer(
        IServiceScopeFactory scopeFactory,
        ILogger<SmscStatusConsumer> logger,
        IOptions<RabbitMqOptions> options,
        SmscStatusStore store)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
        _store = store;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SmscStatusConsumer starting. Queues: {ServerStatus}, {Output} (legacy)",
            _options.ServerStatusQueue, _options.OutputQueue);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!EnsureConnection())
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                bool hadMessage = false;

                // Primary queue: smsc_server_status (smsc_results is consumed by SmsResultProcessor)
                hadMessage |= await TryConsumeOneAsync(_options.ServerStatusQueue, stoppingToken);

                if (!hadMessage)
                    await Task.Delay(500, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmscStatusConsumer loop error");
                await Task.Delay(2000, stoppingToken);
            }
        }

        _logger.LogInformation("SmscStatusConsumer stopped.");
    }

    private async Task<bool> TryConsumeOneAsync(string queueName, CancellationToken stoppingToken)
    {
        BasicGetResult? result = null;
        lock (_channelLock)
        {
            if (_channel == null || _channel.IsClosed)
                return false;
            try
            {
                result = _channel.BasicGet(queueName, autoAck: false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BasicGet failed for {Queue}", queueName);
                return false;
            }
        }

        if (result == null)
            return false;

        try
        {
            var body = result.Body.ToArray();
            var json = Encoding.UTF8.GetString(body);
            if (await TryParseAndSaveEnquireLinkAsync(json))
            {
                Ack(result.DeliveryTag);
                return true;
            }
            Nack(result.DeliveryTag, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing message from {Queue}", queueName);
            Nack(result.DeliveryTag, requeue: true);
        }

        return true;
    }

    /// <summary>
    /// Parses JSON (type, status, reason, timestamp). Uses SmscStatusStore to detect status transition.
    /// Writes to DB only when status has changed (e.g. OK -> Down or Down -> OK). Always acks the message.
    /// </summary>
    private async Task<bool> TryParseAndSaveEnquireLinkAsync(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("type", out var typeEl) || typeEl.GetString() != EnquireLinkType)
            return false;

        var newStatus = "OK";
        if (root.TryGetProperty("status", out var statusEl))
            newStatus = statusEl.GetString() ?? "OK";

        var newReason = "";
        if (root.TryGetProperty("reason", out var reasonEl))
            newReason = reasonEl.GetString() ?? "";

        _logger.LogInformation("Read message from smsc_server_status: Status={Status}, Reason={Reason}", newStatus, newReason ?? "");

        var timestampStr = root.TryGetProperty("timestamp", out var tsEl) ? tsEl.GetString() : null;
        var timestamp = DateTime.UtcNow;
        if (!string.IsNullOrEmpty(timestampStr))
        {
            try
            {
                timestamp = DateTime.ParseExact(timestampStr, TimestampFormat, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            }
            catch (FormatException ex)
            {
                _logger.LogDebug(ex, "Timestamp format not parsed: {Value}", timestampStr);
            }
        }

        if (!_store.ShouldUpdateDatabase(newStatus, newReason))
        {
            _logger.LogDebug("SMSC status unchanged ({Status}); skipping DB write.", newStatus);
            return true;
        }

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var existing = await db.SmscStatus.FindAsync(1);
        if (existing != null)
        {
            existing.Status = newStatus;
            existing.Timestamp = timestamp;
        }
        else
        {
            db.SmscStatus.Add(new SmscStatus { Status = newStatus, Timestamp = timestamp });
        }

        await db.SaveChangesAsync();

        _logger.LogInformation("Wrote status update to DB: {Status} (Reason: {Reason})", newStatus, newReason ?? "");
        return true;
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

    private void Nack(ulong deliveryTag, bool requeue)
    {
        lock (_channelLock)
        {
            try
            {
                _channel?.BasicNack(deliveryTag, false, requeue);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BasicNack failed");
            }
        }
    }

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
                _channel.QueueDeclare(_options.ServerStatusQueue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueDeclare(_options.OutputQueue, durable: true, exclusive: false, autoDelete: false);
            }

            _logger.LogInformation("SmscStatusConsumer connected to RabbitMQ {Host}:{Port}", _options.Host, _options.Port);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SmscStatusConsumer could not connect to RabbitMQ");
            return false;
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
