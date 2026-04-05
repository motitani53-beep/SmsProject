using SmsGateway.Shared.DTOs;

namespace WebApplication1.Services;

public sealed class BufferedItem
{
    public ulong DeliveryTag { get; set; }
    public SmsResultDto Dto { get; set; } = null!;
    public byte[]? RawBody { get; set; }
    public int RetryCount { get; set; }
}

