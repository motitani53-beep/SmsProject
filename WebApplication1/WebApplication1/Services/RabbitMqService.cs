using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using System.Text;
using System.Text.Json;
using SmsGateway.Shared.Options;

namespace WebApplication1.Services;

public class RabbitMqService : IRabbitMqService, IDisposable
{
    private readonly RabbitMqOptions _options;
    private IConnection? _connection;
    private IModel? _channel;
    private readonly object _lock = new();
    private bool _disposed;

    private const string SmsExchange = "sms_exchange";

    public RabbitMqService(IOptions<RabbitMqOptions> options)
    {
        _options = options.Value;
    }

    public void EnsureConnection()
    {
        if (_channel?.IsOpen == true)
            return;

        lock (_lock)
        {
            if (_channel?.IsOpen == true)
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
        }
    }

    public bool TryEnsureConnection()
    {
        try
        {
            EnsureConnection();
            return _channel?.IsOpen == true;
        }
        catch
        {
            return false;
        }
    }

    public void Publish(string exchange, string routingKey, byte[] body)
    {
        EnsureConnection();

        if (_channel == null)
            throw new InvalidOperationException("RabbitMQ channel is not available.");

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";

        _channel.BasicPublish(exchange, routingKey, props, body);
    }

    public void PublishJson<T>(string exchange, string routingKey, T message)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);
        Publish(exchange, routingKey, body);
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
