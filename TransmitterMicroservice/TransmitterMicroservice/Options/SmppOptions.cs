namespace TransmitterMicroservice.Options;

public class SmppOptions
{
    public const string SectionName = "Smpp";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 2775;
    public string SystemId { get; set; } = "SMSC_MOCK";
    public string Password { get; set; } = "password";
    public int EnquireLinkIntervalSeconds { get; set; } = 120; // 2 minutes
}

