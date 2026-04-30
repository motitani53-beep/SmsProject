namespace SmsGateway.Shared.Options;

public class ResultProcessorOptions
{
    public const string SectionName = "ResultProcessor";

    public int BatchSize { get; set; } = 100;

    /// <summary>How long to buffer SubmitSmResp/DLR before flushing to the DB. Lower = faster SignalR/UI updates (more frequent smaller batches).</summary>
    public int FlushIntervalSeconds { get; set; } = 5;
}
