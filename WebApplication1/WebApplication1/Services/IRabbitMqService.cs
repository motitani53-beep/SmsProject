namespace WebApplication1.Services;

public interface IRabbitMqService
{
    /// <summary>
    /// Publishes a message to the specified exchange with the given routing key.
    /// </summary>
    void Publish(string exchange, string routingKey, byte[] body);

    /// <summary>
    /// Serializes the message as JSON and publishes to the specified exchange.
    /// </summary>
    void PublishJson<T>(string exchange, string routingKey, T message);

    /// <summary>
    /// Ensures the connection is available. Called internally.
    /// </summary>
    void EnsureConnection();

    /// <summary>
    /// Attempts to establish or verify the connection. Returns true if connected, false if unreachable.
    /// </summary>
    bool TryEnsureConnection();
}
