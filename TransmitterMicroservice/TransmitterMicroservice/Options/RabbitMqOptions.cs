namespace TransmitterMicroservice.Options;

public class RabbitMqOptions
{
    public const string SectionName = "RabbitMQ";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string HighPriorityQueue { get; set; } = "sms_high_priority";
    public List<string> InputQueues { get; set; } = new();
    public string OutputQueue { get; set; } = "smsc_results";
    public string ServerStatusQueue { get; set; } = "smsc_server_status";
    public int MessagesPerSecond { get; set; } = 5;
}

