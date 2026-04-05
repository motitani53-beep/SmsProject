using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ReceiverMicroservice.Options;

namespace ReceiverMicroservice.Services;

public class RabbitMqPublisher : IRabbitMqPublisher, IDisposable
{
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly RabbitMqOptions _options;
    private readonly object _lock = new();
    private IConnection? _connection;
    private IModel? _channel;
    private bool _disposed;

    public RabbitMqPublisher(ILogger<RabbitMqPublisher> logger, IOptions<RabbitMqOptions> options)
    {
        _logger = logger;
        _options = options.Value;
    }

    public void Publish(byte[] body)
    {
        lock (_lock)
        {
            try
            {
                EnsureChannel();
                if (_channel == null || _channel.IsClosed)
                {
                    _logger.LogWarning("Cannot publish - RabbitMQ channel is null or closed.");
                    return;
                }

                var props = _channel.CreateBasicProperties();
                props.Persistent = true;
                props.ContentType = "application/json";
                _channel.BasicPublish(exchange: "", routingKey: _options.OutputQueue, basicProperties: props, body: body);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing to RabbitMQ");
            }
        }
    }

    private void EnsureChannel()
    {
        if (_channel != null && !_channel.IsClosed)
            return;

        _connection?.Close();
        _connection?.Dispose();
        _channel?.Close();
        _channel?.Dispose();

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };
        _connection = factory.CreateConnection();
        _channel = _connection.CreateModel();
        _channel.QueueDeclare(queue: _options.OutputQueue, durable: true, exclusive: false, autoDelete: false);
        _logger.LogInformation("RabbitMQ publisher connected to {Host}:{Port}, queue: {Queue}", _options.Host, _options.Port, _options.OutputQueue);
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (_lock)
        {
            _channel?.Close();
            _channel?.Dispose();
            _connection?.Close();
            _connection?.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
