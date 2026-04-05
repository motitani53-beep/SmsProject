namespace SmsGateway.Shared.Options;

public class ResultProcessorOptions
{
    public const string SectionName = "ResultProcessor";

    public int BatchSize { get; set; } = 100;
    public int FlushIntervalSeconds { get; set; } = 30;
}
