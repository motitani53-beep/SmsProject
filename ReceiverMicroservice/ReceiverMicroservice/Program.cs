using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ReceiverMicroservice;
using ReceiverMicroservice.Options;
using ReceiverMicroservice.Services;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddCommandLine(args)
    .Build();

var host = Host.CreateDefaultBuilder(args)
    .ConfigureAppConfiguration(config =>
    {
        config.AddConfiguration(configuration);
    })
    .ConfigureServices((context, services) =>
    {
        services.Configure<SmppOptions>(configuration.GetSection(SmppOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<IRabbitMqPublisher, RabbitMqPublisher>();
        services.AddSingleton<SmppReceiverGateway>();
        services.AddHostedService<ReceiverHostedService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.SetMinimumLevel(LogLevel.Information);
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();
logger.LogInformation("=== Receiver Microservice ===");
logger.LogInformation("SMPP: {Host}:{Port} (Receiver)", configuration["Smpp:Host"], configuration["Smpp:Port"]);
logger.LogInformation("RabbitMQ: {Host}:{Port} -> {Queue}", configuration["RabbitMQ:Host"], configuration["RabbitMQ:Port"], configuration["RabbitMQ:OutputQueue"]);
logger.LogInformation("Press Ctrl+C to stop.");
logger.LogInformation("");

await host.RunAsync();
