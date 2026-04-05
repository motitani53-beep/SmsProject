namespace WebApplication1.Services;

public interface ISmsBatchProcessor
{
    /// <summary>
    /// Handles EnquireLink messages by updating SMSC status in the database.
    /// Returns true when the payload was recognized as an EnquireLink (and therefore can be ACKed).
    /// </summary>
    Task<bool> TryHandleEnquireLinkInlineAsync(string json, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a batch of buffered SubmitSmResp/DLR items, performing all required database writes.
    /// Uses local caching to resolve intra-batch dependencies and prevents duplicate DLR rows.
    /// </summary>
    Task<SmsBatchProcessResult> ProcessBatchAsync(IEnumerable<BufferedItem> batch, CancellationToken cancellationToken = default);
}

public sealed class SmsBatchProcessResult
{
    public List<ulong> ToAck { get; } = new();
    public List<DlrRetryRequest> DlrRetries { get; } = new();

    /// <summary>Raw JSON bodies to publish to <c>sms_results_dlq</c> after successful batch commit (orphan DLRs, max retries exceeded).</summary>
    public List<byte[]> SmsResultsDlqBodies { get; } = new();
}

public sealed class DlrRetryRequest
{
    public byte[] Body { get; init; } = Array.Empty<byte>();
    public int RetryCount { get; init; }
}

