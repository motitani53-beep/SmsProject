using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using TransmitterMicroservice.Interfaces;
using TransmitterMicroservice.Options;

namespace TransmitterMicroservice;

/// <summary>
/// Manages RabbitMQ connection, channel, queue declarations, and message operations.
/// Channel operations are thread-safe via lock; reconnection is guarded by a semaphore.
/// </summary>
public class RabbitMqManager : IRabbitMqManager
{
    private readonly ILogger<RabbitMqManager> _logger;
    private readonly RabbitMqOptions _options;
    private readonly object _channelLock = new();
    private readonly SemaphoreSlim _reconnectSemaphore = new(1, 1);
    private IConnection? _connection;
    private IModel? _channel;
    private int _lastQueueIndex;

    public RabbitMqManager(ILogger<RabbitMqManager> logger, IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public bool EnsureChannelHealthy()
    {
        lock (_channelLock)
        {
            if (_channel != null && !_channel.IsClosed)
                return true;
        }

        if (!_reconnectSemaphore.Wait(0))
        {
            _logger.LogDebug("Another thread is reconnecting RabbitMQ. Waiting...");
            _reconnectSemaphore.Wait();

            try
            {
                lock (_channelLock)
                {
                    if (_channel != null && !_channel.IsClosed)
                        return true;
                }
                return false;
            }
            finally
            {
                _reconnectSemaphore.Release();
            }
        }

        try
        {
            lock (_channelLock)
            {
                if (_channel != null && !_channel.IsClosed)
                    return true;
            }

            _logger.LogWarning("RabbitMQ channel is null or closed. Attempting to reinitialize...");
            DisposeResources();
            InitializeConnection();

            lock (_channelLock)
            {
                if (_channel != null && !_channel.IsClosed)
                {
                    _logger.LogInformation("RabbitMQ channel reinitialized successfully");
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reinitializing RabbitMQ channel");
            return false;
        }
        finally
        {
            _reconnectSemaphore.Release();
        }
    }

    public (BasicGetResult? Result, string? QueueName) GetNextMessage()
    {
        lock (_channelLock)
        {
            if (_channel == null || _channel.IsClosed)
                return (null, null);

            BasicGetResult? result = null;
            string? queueName = null;

            if (!string.IsNullOrEmpty(_options.HighPriorityQueue))
            {
                result = _channel.BasicGet(_options.HighPriorityQueue, autoAck: false);
                if (result != null)
                {
                    queueName = _options.HighPriorityQueue;
                    _logger.LogDebug("Found message in high-priority queue");
                }
            }

            if (result == null && _options.InputQueues.Count > 0)
            {
                int startIndex = (_lastQueueIndex + 1) % _options.InputQueues.Count;
                for (int i = 0; i < _options.InputQueues.Count; i++)
                {
                    int currentIndex = (startIndex + i) % _options.InputQueues.Count;
                    var inputQueue = _options.InputQueues[currentIndex];
                    result = _channel.BasicGet(inputQueue, autoAck: false);
                    if (result != null)
                    {
                        queueName = inputQueue;
                        _lastQueueIndex = currentIndex;
                        _logger.LogDebug("Found message in input queue: {QueueName} (index: {Index})", inputQueue, currentIndex);
                        break;
                    }
                }
            }

            return (result, queueName);
        }
    }

    public void SafeAck(ulong deliveryTag, int? deliveryId)
    {
        lock (_channelLock)
        {
            try
            {
                if (_channel != null && !_channel.IsClosed)
                {
                    _channel.BasicAck(deliveryTag, multiple: false);
                }
                else
                {
                    _logger.LogWarning("Cannot acknowledge message - RabbitMQ channel is null or closed. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}",
                        deliveryTag, deliveryId);
                }
            }
            catch (AlreadyClosedException ex)
            {
                _logger.LogWarning(ex, "RabbitMQ channel already closed when attempting to acknowledge. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}",
                    deliveryTag, deliveryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error acknowledging RabbitMQ message. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}",
                    deliveryTag, deliveryId);
            }
        }
    }

    public void SafeNack(ulong deliveryTag, bool requeue, int? deliveryId)
    {
        lock (_channelLock)
        {
            try
            {
                if (_channel != null && !_channel.IsClosed)
                {
                    _channel.BasicNack(deliveryTag, requeue: requeue, multiple: false);
                    if (requeue && deliveryId.HasValue)
                    {
                        _logger.LogWarning("Message requeued - DeliveryId: {DeliveryId}, DeliveryTag: {DeliveryTag}",
                            deliveryId.Value, deliveryTag);
                    }
                }
                else
                {
                    _logger.LogWarning("Cannot nack message - RabbitMQ channel is null or closed. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}, Requeue: {Requeue}",
                        deliveryTag, deliveryId, requeue);
                }
            }
            catch (AlreadyClosedException ex)
            {
                _logger.LogWarning(ex, "RabbitMQ channel already closed when attempting to nack. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}, Requeue: {Requeue}",
                    deliveryTag, deliveryId, requeue);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error nacking RabbitMQ message. DeliveryTag: {DeliveryTag}, DeliveryId: {DeliveryId}, Requeue: {Requeue}",
                    deliveryTag, deliveryId, requeue);
            }
        }
    }

    public void SafePublish(int deliveryId, string? smscMessageId, string status, byte[] body)
        => SafePublish(deliveryId, smscMessageId, status, body, headers: null);

    public void SafePublish(int deliveryId, string? smscMessageId, string status, byte[] body, IReadOnlyDictionary<string, object>? headers)
    {
        lock (_channelLock)
        {
            try
            {
                if (_channel == null || _channel.IsClosed)
                {
                    _logger.LogWarning("Cannot publish SubmitSmResp - RabbitMQ channel is null or closed. DeliveryId: {DeliveryId}",
                        deliveryId);
                    return;
                }

                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                if (headers != null && headers.Count > 0)
                {
                    props.Headers = new Dictionary<string, object>(headers);
                }

                _channel.BasicPublish(
                    exchange: "",
                    routingKey: _options.OutputQueue,
                    basicProperties: props,
                    body: body);

                _logger.LogInformation("Published SubmitSmResp to queue - DeliveryId: {DeliveryId}, SmscMessageId: {SmscMessageId}, Status: {Status}, Headers: {HeaderCount}",
                    deliveryId, smscMessageId, status, headers?.Count ?? 0);
            }
            catch (AlreadyClosedException ex)
            {
                _logger.LogWarning(ex, "RabbitMQ channel already closed when attempting to publish SubmitSmResp. DeliveryId: {DeliveryId}",
                    deliveryId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing SubmitSmResp to RabbitMQ. DeliveryId: {DeliveryId}",
                    deliveryId);
            }
        }
    }

    public void Dispose()
    {
        DisposeResources();
        try
        {
            _reconnectSemaphore.Dispose();
            _logger.LogDebug("RabbitMQ reconnect semaphore disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing RabbitMQ reconnect semaphore");
        }
    }

    private void InitializeConnection()
    {
        _logger.LogInformation("Initializing RabbitMQ connection...");

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        _connection = factory.CreateConnection();
        lock (_channelLock)
        {
            _channel = _connection.CreateModel();
        }

        if (!string.IsNullOrEmpty(_options.HighPriorityQueue))
        {
            _channel!.QueueDeclare(
                queue: _options.HighPriorityQueue,
                durable: true,
                exclusive: false,
                autoDelete: false);
            _logger.LogInformation("RabbitMQ high-priority queue declared: {QueueName}", _options.HighPriorityQueue);
        }

        foreach (var inputQueue in _options.InputQueues)
        {
            _channel!.QueueDeclare(
                queue: inputQueue,
                durable: true,
                exclusive: false,
                autoDelete: false);
            _logger.LogInformation("RabbitMQ input queue declared: {QueueName}", inputQueue);
        }

        _channel!.QueueDeclare(
            queue: _options.OutputQueue,
            durable: true,
            exclusive: false,
            autoDelete: false);
        _logger.LogInformation("RabbitMQ output queue declared: {QueueName}", _options.OutputQueue);

        _channel.QueueDeclare(
            queue: _options.ServerStatusQueue,
            durable: true,
            exclusive: false,
            autoDelete: false);
        _logger.LogInformation("RabbitMQ server status queue declared: {QueueName}", _options.ServerStatusQueue);
    }

    public void SafePublishToServerStatus(byte[] body)
    {
        lock (_channelLock)
        {
            try
            {
                if (_channel == null || _channel.IsClosed)
                {
                    _logger.LogWarning("Cannot publish to server status queue - RabbitMQ channel is null or closed.");
                    return;
                }

                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";

                _channel.BasicPublish(
                    exchange: "",
                    routingKey: _options.ServerStatusQueue,
                    basicProperties: props,
                    body: body);

                _logger.LogDebug("Published to server status queue: {QueueName}", _options.ServerStatusQueue);
            }
            catch (AlreadyClosedException ex)
            {
                _logger.LogWarning(ex, "RabbitMQ channel already closed when attempting to publish to server status queue.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing to server status queue.");
            }
        }
    }

    private void DisposeResources()
    {
        try
        {
            lock (_channelLock)
            {
                if (_channel != null)
                {
                    if (!_channel.IsClosed)
                        _channel.Close();
                    _channel.Dispose();
                    _channel = null;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing RabbitMQ channel");
        }

        try
        {
            if (_connection != null)
            {
                if (_connection.IsOpen)
                    _connection.Close();
                _connection.Dispose();
                _connection = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing RabbitMQ connection");
        }
    }
}
