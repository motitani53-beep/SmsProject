using SmsGateway.Shared.DTOs;

namespace WebApplication1.Services;

public sealed class BufferedItem
{
    public ulong DeliveryTag { get; set; }
    public SmsResultDto Dto { get; set; } = null!;
    public byte[]? RawBody { get; set; }
    public int RetryCount { get; set; }

    /// <summary>
    /// Optional AMQP header value. <c>"Test"</c> means this result belongs to test_messages / test_smsc_ids;
    /// null/empty means normal campaign traffic (delivery_details / delivery_smsc_ids).
    /// </summary>
    public string? SourceType { get; set; }
}

