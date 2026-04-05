using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ReceiverMicroservice;

/// <summary>
/// Background service that runs the SMPP Receiver Gateway and keeps the application running.
/// </summary>
public class ReceiverHostedService : BackgroundService
{
    private readonly SmppReceiverGateway _gateway;
    private readonly ILogger<ReceiverHostedService> _logger;

    public ReceiverHostedService(SmppReceiverGateway gateway, ILogger<ReceiverHostedService> logger)
    {
        _gateway = gateway;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Receiver microservice starting.");
        await _gateway.StartAsync(stoppingToken);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Receiver microservice stopping.");
        await _gateway.StopAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
