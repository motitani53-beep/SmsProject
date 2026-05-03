using RabbitMQ.Client;

namespace TransmitterMicroservice.Interfaces;

/// <summary>
/// Manages RabbitMQ connection, channel, queue declarations, and message operations.
/// </summary>
public interface IRabbitMqManager
{
    /// <summary>
    /// Ensures the channel is healthy. Reinitializes if null or closed (thread-safe).
    /// </summary>
    /// <returns>True if channel is ready, false otherwise.</returns>
    bool EnsureChannelHealthy();

    /// <summary>
    /// Gets the next message using priority queue first, then round-robin on input queues.
    /// </summary>
    /// <returns>Message result and queue name, or (null, null) if no message.</returns>
    (BasicGetResult? Result, string? QueueName) GetNextMessage();

    /// <summary>
    /// Acknowledges a message (thread-safe).
    /// </summary>
    void SafeAck(ulong deliveryTag, int? deliveryId);

    /// <summary>
    /// Nacks a message (thread-safe).
    /// </summary>
    void SafeNack(ulong deliveryTag, bool requeue, int? deliveryId);

    /// <summary>
    /// Publishes a message to the results queue (thread-safe).
    /// </summary>
    void SafePublish(int deliveryId, string? smscMessageId, string status, byte[] body);

    /// <summary>
    /// Publishes a message to the results queue with custom AMQP headers (thread-safe).
    /// Used to forward <c>SourceType: Test</c> from the inbound SendRequest onto the SubmitSmResp.
    /// </summary>
    void SafePublish(int deliveryId, string? smscMessageId, string status, byte[] body, IReadOnlyDictionary<string, object>? headers);

    /// <summary>
    /// Publishes a message to the server status queue (e.g. EnquireLink) (thread-safe).
    /// </summary>
    void SafePublishToServerStatus(byte[] body);

    /// <summary>
    /// Disposes connection and channel.
    /// </summary>
    void Dispose();
}
