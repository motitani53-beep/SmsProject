namespace ReceiverMicroservice.Services;

/// <summary>
/// Publishes messages to the smsc_results queue (e.g. DLRs).
/// </summary>
public interface IRabbitMqPublisher
{
    void Publish(byte[] body);
}
