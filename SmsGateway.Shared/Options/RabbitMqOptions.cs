namespace SmsGateway.Shared.Options;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public int NumberOfTopics { get; set; } = 5;
    public string TopicNamePrefix { get; set; } = "smsc.topic";
    public int MessagesPerSecond { get; set; } = 5;
    public string ServerStatusQueue { get; set; } = "smsc_server_status";
    public string OutputQueue { get; set; } = "smsc_results";
    public string CampaignPendingQueue { get; set; } = "campaign_pending";
}
