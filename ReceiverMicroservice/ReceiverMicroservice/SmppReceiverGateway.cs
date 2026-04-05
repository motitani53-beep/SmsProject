using System.Text;
using System.Text.Json;
using Inetlab.SMPP;
using Inetlab.SMPP.Common;
using Inetlab.SMPP.PDU;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReceiverMicroservice.Options;
using ReceiverMicroservice.Services;

namespace ReceiverMicroservice;

/// <summary>
/// SMPP client bound as Receiver: receives DLRs (DeliverSm) and publishes them to RabbitMQ.
/// EnquireLink runs every 30 seconds; success is logged to console only (no RabbitMQ).
/// </summary>
public class SmppReceiverGateway
{
    private readonly ILogger<SmppReceiverGateway> _logger;
    private readonly SmppOptions _options;
    private readonly IRabbitMqPublisher _rabbitMqPublisher;
    private SmppClient? _client;
    private Timer? _enquireLinkTimer;
    private readonly object _lock = new();

    public SmppReceiverGateway(
        ILogger<SmppReceiverGateway> logger,
        IOptions<SmppOptions> options,
        IRabbitMqPublisher rabbitMqPublisher)
    {
        _logger = logger;
        _options = options.Value;
        _rabbitMqPublisher = rabbitMqPublisher;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting SMPP Receiver Gateway...");
        _client = new SmppClient();

        _client.evDeliverSm += OnDeliverSm;

        _logger.LogInformation("Connecting to SMSC at {Host}:{Port}...", _options.Host, _options.Port);
        await _client.ConnectAsync(_options.Host, _options.Port);

        var bindResp = await _client.BindAsync(
            _options.SystemId,
            _options.Password,
            ConnectionMode.Receiver);

        if (bindResp.Header.Status != CommandStatus.ESME_ROK)
        {
            _logger.LogError("Failed to bind as Receiver. Status: {Status}", bindResp.Header.Status);
            return;
        }

        _logger.LogInformation("Successfully bound to SMSC as Receiver.");
        StartEnquireLinkTimer();
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _enquireLinkTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _enquireLinkTimer?.Dispose();
        _enquireLinkTimer = null;

        if (_client == null) return;

        try
        {
            _client.evDeliverSm -= OnDeliverSm;
            if (_client.Status == ConnectionStatus.Bound || _client.Status == ConnectionStatus.Open)
                await _client.UnbindAsync();
            await _client.DisconnectAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during disconnect");
        }
        finally
        {
            _client.Dispose();
            _client = null;
        }
    }

    private void OnDeliverSm(object? sender, DeliverSm deliverSm)
    {
        try
        {
            if (deliverSm.MessageType != MessageTypes.SMSCDeliveryReceipt)
                return;

            var receipt = deliverSm.Receipt;
            if (receipt == null)
            {
                _logger.LogWarning("DeliverSm has no Receipt data.");
                return;
            }

            var smscMessageId = receipt.MessageId ?? string.Empty;

            var status = receipt.State.ToString();
            var timestamp = DateTime.UtcNow.ToString("HH:mm:ss dd/MM/yyyy");

            var payload = new
            {
                type = "DLR",
                smsc_message_id = smscMessageId,
                status,
                timestamp
            };
            var json = JsonSerializer.Serialize(payload);
            Console.WriteLine(json);
            var body = Encoding.UTF8.GetBytes(json);
            _rabbitMqPublisher.Publish(body);

            _logger.LogInformation("DLR published - MessageId: {MessageId}, Status: {Status}", smscMessageId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing DeliverSm");
        }
    }

    private void StartEnquireLinkTimer()
    {
        _enquireLinkTimer = new Timer(
            _ => _ = PerformEnquireLinkAsync(),
            null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_options.EnquireLinkIntervalSeconds));
        _logger.LogInformation("EnquireLink timer started. Interval: {Interval}s", _options.EnquireLinkIntervalSeconds);
    }

    private async Task PerformEnquireLinkAsync()
    {
        try
        {
            if (_client == null || (_client.Status != ConnectionStatus.Bound && _client.Status != ConnectionStatus.Open))
                return;

            var resp = await _client.EnquireLinkAsync(new EnquireLink());
            if (resp?.Header.Status == CommandStatus.ESME_ROK)
            {
                var timestampStr = DateTime.UtcNow.ToString("HH:mm:ss dd/MM/yyyy");
                Console.WriteLine($"EnquireLink successful {timestampStr}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EnquireLink failed");
        }
    }
}
