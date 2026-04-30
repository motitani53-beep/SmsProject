using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using SmsGateway.Shared.DTOs;
using SmsGateway.Shared.Options;

namespace WebApplication1.Services;

/// <summary>
/// Consumes from smsc_results queue, buffers SubmitSmResp and DLR messages,
/// and processes them in batches via <see cref="ISmsBatchProcessor"/> (SubmitSmResp rows in <c>delivery_smsc_ids</c> are upserted by composite <c>smsc_message_id</c> + <c>part_number</c> in <see cref="SmsBatchProcessor"/>), then ack only after SaveChanges.
/// JSON uses <see cref="SmsResultQueueDeserializer"/> (<see cref="DlrRabbitMqPayload"/> for DLR, <see cref="SmsResultDto"/> for SubmitSmResp) and <see cref="SmsResultQueueDeserializer.Options"/> with <see cref="SmscMessageIdJsonConverter"/> so large SMSC IDs are never rounded.
/// DLR retries are published to <c>dlr_retry</c> with TTL; when TTL expires, RabbitMQ dead-letters
/// to the default exchange with routing key = output queue (no manual drain loop).
/// Real-time delivery status SignalR broadcasts are sent from <see cref="SmsBatchProcessor"/> (and timeout expiry in <see cref="CampaignMonitoringWorker"/>) after DB commits, not from this class.
/// </summary>
public class SmsResultProcessor : BackgroundService
{
    private const string SubmitSmRespType = "SubmitSmResp";
    private const string DlrType = "DLR";
    private const string DlrRetryQueue = "dlr_retry";
    private const string SmsResultsDlq = "sms_results_dlq";
    private const string RetryCountHeader = "x-retry-count";
    private const int DlrRetryTtlMs = 60000;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsResultProcessor> _logger;
    private readonly RabbitMqOptions _rabbitOptions;
    private readonly ResultProcessorOptions _processorOptions;
    private readonly object _channelLock = new();
    private readonly List<BufferedItem> _buffer = new();
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    public SmsResultProcessor(
        IServiceScopeFactory scopeFactory,
        ILogger<SmsResultProcessor> logger,
        IOptions<RabbitMqOptions> rabbitOptions,
        IOptions<ResultProcessorOptions> processorOptions)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _rabbitOptions = rabbitOptions.Value;
        _processorOptions = processorOptions.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("SmsResultProcessor starting. Queue: {Queue}, BatchSize: {BatchSize}, FlushInterval: {Flush}s",
            _rabbitOptions.OutputQueue, _processorOptions.BatchSize, _processorOptions.FlushIntervalSeconds);

        var flushInterval = TimeSpan.FromSeconds(_processorOptions.FlushIntervalSeconds);
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var lastFlushAt = DateTime.UtcNow;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!EnsureConnection())
                {
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                // Fill buffer up to BatchSize
                var remaining = _processorOptions.BatchSize - _buffer.Count;
                for (var i = 0; i < remaining; i++)
                {
                    var got = TryGetOne(out var body, out var deliveryTag, out var headers);
                    if (!got || body == null)
                        break;

                    var json = Encoding.UTF8.GetString(body);
                    bool enquireLinkHandled;
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var batchProcessor = scope.ServiceProvider.GetRequiredService<ISmsBatchProcessor>();
                        enquireLinkHandled = await batchProcessor.TryHandleEnquireLinkInlineAsync(json, stoppingToken);
                    }
                    if (enquireLinkHandled)
                    {
                        Ack(deliveryTag);
                        continue;
                    }

                    SmsResultDto? dto = null;
                    try
                    {
                        dto = SmsResultQueueDeserializer.Deserialize(json);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Invalid JSON in smsc_results, nacking without requeue. DeliveryTag: {Tag}", deliveryTag);
                        Nack(deliveryTag, requeue: false);
                        continue;
                    }

                    if (dto == null || (dto.Type != SubmitSmRespType && dto.Type != DlrType))
                    {
                        Ack(deliveryTag);
                        continue;
                    }

                    var retryCount = GetRetryCount(headers);
                    _buffer.Add(new BufferedItem { DeliveryTag = deliveryTag, Dto = dto, RawBody = body, RetryCount = retryCount });
                }

                var shouldFlushBySize = _buffer.Count >= _processorOptions.BatchSize;
                var shouldFlushByTime = (DateTime.UtcNow - lastFlushAt) >= flushInterval;

                if (shouldFlushBySize || (shouldFlushByTime && _buffer.Count > 0))
                {
                    await FlushBatchAsync();
                    lastFlushAt = DateTime.UtcNow;
                }
                else
                {
                    // Wait before next poll (PeriodicTimer allows only one concurrent waiter, so we use Task.Delay)
                    await Task.Delay(pollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("SmsResultProcessor is stopping (cancel requested).");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SmsResultProcessor loop error; will retry in 2s.");
                try
                {
                    await Task.Delay(2000, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        // Final flush on shutdown
        if (_buffer.Count > 0)
        {
            try
            {
                await FlushBatchAsync();
                _logger.LogInformation("SmsResultProcessor flushed {Count} buffered messages on shutdown.", _buffer.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing final batch on shutdown.");
            }
        }

        _logger.LogInformation("SmsResultProcessor stopped.");
    }

    private bool TryGetOne(out byte[]? body, out ulong deliveryTag, out IDictionary<string, object>? headers)
    {
        body = null;
        deliveryTag = 0;
        headers = null;
        lock (_channelLock)
        {
            if (_channel == null || _channel.IsClosed)
                return false;
            try
            {
                var result = _channel.BasicGet(_rabbitOptions.OutputQueue, autoAck: false);
                if (result == null)
                    return false;
                body = result.Body.ToArray();
                deliveryTag = result.DeliveryTag;
                headers = result.BasicProperties.Headers as IDictionary<string, object>;
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "BasicGet failed for {Queue}", _rabbitOptions.OutputQueue);
                return false;
            }
        }
    }

    private async Task FlushBatchAsync()
    {
        if (_buffer.Count == 0)
            return;

        var batch = new List<BufferedItem>(_buffer);
        _buffer.Clear();

        // Sort: SubmitSmResp first, then DLR
        batch.Sort((a, b) =>
        {
            var aIsResp = a.Dto.Type == SubmitSmRespType ? 0 : 1;
            var bIsResp = b.Dto.Type == SubmitSmRespType ? 0 : 1;
            return aIsResp.CompareTo(bIsResp);
        });

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var batchProcessor = scope.ServiceProvider.GetRequiredService<ISmsBatchProcessor>();
            var result = await batchProcessor.ProcessBatchAsync(batch);
            _logger.LogInformation(
                "SmsResultProcessor committed batch of {Count} SMSC result(s); SignalR updates (if any) were sent from SmsBatchProcessor for touched delivery rows.",
                batch.Count);

            foreach (var retry in result.DlrRetries)
                PublishToDlrRetry(retry.Body, retry.RetryCount);

            foreach (var dlqBody in result.SmsResultsDlqBodies)
                PublishToSmsResultsDlq(dlqBody);

            foreach (var tag in result.ToAck)
                Ack(tag);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch processing failed. Nacking {Count} messages.", batch.Count);
            foreach (var item in batch)
                Nack(item.DeliveryTag, requeue: true);
        }
    }

    /// <summary>
    /// Reads <c>x-retry-count</c> from RabbitMQ message headers. Must accept <see cref="int"/> — the client often
    /// returns integer headers as <see cref="int"/>, not <see cref="long"/>; treating those as 0 caused every
    /// orphan DLR republish to reset to 1 so the count never reached <see cref="SmsBatchProcessor"/> DLQ threshold.
    /// </summary>
    private static int GetRetryCount(IDictionary<string, object>? headers)
    {
        if (headers == null || headers.Count == 0)
            return 0;

        object? val = null;
        foreach (var kv in headers)
        {
            if (string.Equals(kv.Key, RetryCountHeader, StringComparison.OrdinalIgnoreCase))
            {
                val = kv.Value;
                break;
            }
        }

        if (val == null)
            return 0;

        if (val is byte[] bytes)
            return int.TryParse(Encoding.UTF8.GetString(bytes), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                ? Math.Max(0, n)
                : 0;

        if (val is int i)
            return Math.Max(0, i);
        if (val is uint u)
            return u > int.MaxValue ? int.MaxValue : (int)u;
        if (val is long l)
            return l < 0 ? 0 : l > int.MaxValue ? int.MaxValue : (int)l;
        if (val is ulong ul)
            return ul > int.MaxValue ? int.MaxValue : (int)ul;
        if (val is short s)
            return Math.Max(0, (int)s);
        if (val is ushort us)
            return us;

        if (val is string str &&
            int.TryParse(str, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            return Math.Max(0, parsed);

        if (val is IConvertible conv)
        {
            try
            {
                return Math.Max(0, conv.ToInt32(CultureInfo.InvariantCulture));
            }
            catch
            {
                return 0;
            }
        }

        return 0;
    }

    private void PublishToSmsResultsDlq(byte[]? body)
    {
        if (body == null || body.Length == 0)
            return;
        lock (_channelLock)
        {
            try
            {
                if (_channel == null || _channel.IsClosed)
                    return;
                _channel.QueueDeclare(SmsResultsDlq, durable: true, exclusive: false, autoDelete: false);
                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                _channel.BasicPublish("", SmsResultsDlq, props, body);
                _logger.LogWarning("Published orphan DLR payload to {Queue} ({Length} bytes)", SmsResultsDlq, body.Length);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish to {Queue}", SmsResultsDlq);
            }
        }
    }

    /// <summary>
    /// <c>dlr_retry</c>: per-message TTL via <c>x-message-ttl</c>; on expiry, dead-letter to default exchange
    /// with routing key = main results queue so messages re-enter <see cref="RabbitMqOptions.OutputQueue"/> automatically.
    /// </summary>
    /// <remarks>
    /// If an existing <c>dlr_retry</c> queue was declared without these arguments, delete it in RabbitMQ
    /// before redeploying (PRECONDITION_FAILED on mismatch).
    /// </remarks>
    private Dictionary<string, object> CreateDlrRetryQueueArguments()
    {
        return new Dictionary<string, object>
        {
            { "x-message-ttl", DlrRetryTtlMs },
            // Default exchange: route dead letters by queue name back to the main results queue.
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", _rabbitOptions.OutputQueue }
        };
    }

    private void PublishToDlrRetry(byte[]? body, int retryCount)
    {
        if (body == null)
            return;
        lock (_channelLock)
        {
            try
            {
                if (_channel == null || _channel.IsClosed)
                    return;
                _channel.QueueDeclare(DlrRetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: CreateDlrRetryQueueArguments());
                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                props.Headers ??= new Dictionary<string, object>();
                props.Headers[RetryCountHeader] = retryCount;
                _channel.BasicPublish("", DlrRetryQueue, props, body);
                _logger.LogDebug("Published DLR to {Queue} (TTL {TtlMs}ms, DLX -> {OutputQueue}); retry count {Count}",
                    DlrRetryQueue, DlrRetryTtlMs, _rabbitOptions.OutputQueue, retryCount);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to publish to dlr_retry");
            }
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
                HostName = _rabbitOptions.Host,
                Port = _rabbitOptions.Port,
                UserName = _rabbitOptions.UserName,
                Password = _rabbitOptions.Password,
                AutomaticRecoveryEnabled = true,
                NetworkRecoveryInterval = TimeSpan.FromSeconds(10)
            };

            _connection = factory.CreateConnection();
            lock (_channelLock)
            {
                _channel = _connection.CreateModel();
                // Main queue must exist before dlr_retry (dead letters route here by name).
                _channel.QueueDeclare(_rabbitOptions.OutputQueue, durable: true, exclusive: false, autoDelete: false);
                _channel.QueueDeclare(DlrRetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: CreateDlrRetryQueueArguments());
                _channel.QueueDeclare(SmsResultsDlq, durable: true, exclusive: false, autoDelete: false);
            }

            _logger.LogInformation("SmsResultProcessor connected to RabbitMQ");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SmsResultProcessor could not connect to RabbitMQ");
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
