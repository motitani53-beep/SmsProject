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
    /// Serializes the message as JSON and publishes with the supplied AMQP headers (e.g. <c>SourceType: Test</c>).
    /// </summary>
    void PublishJson<T>(string exchange, string routingKey, T message, IReadOnlyDictionary<string, object> headers);

    /// <summary>
    /// Ensures the connection is available. Called internally.
    /// </summary>
    void EnsureConnection();

    /// <summary>
    /// Attempts to establish or verify the connection. Returns true if connected, false if unreachable.
    /// </summary>
    bool TryEnsureConnection();

    /// <summary>
    /// Returns the first SMS topic routing key whose bound queue (<c>sms_queue_{i}</c>) has <c>MessageCount == 0</c>, or null if none.
    /// </summary>
    string? TryGetFirstAvailableSmsTopicRoutingKey();
}
