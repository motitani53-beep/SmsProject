using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using TransmitterMicroservice;
using TransmitterMicroservice.Interfaces;
using TransmitterMicroservice.Options;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
    .AddCommandLine(args)
    .Build();

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console()
    .WriteTo.File("logs/transmitter-{Date}.log", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

var host = Host.CreateDefaultBuilder(args)
    .UseSerilog()
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton<IConfiguration>(configuration);
        services.Configure<SmppOptions>(configuration.GetSection(SmppOptions.SectionName));
        services.Configure<RabbitMqOptions>(configuration.GetSection(RabbitMqOptions.SectionName));
        services.AddSingleton<ISmppGateway, SmppGateway>();
        services.AddSingleton<IRabbitMqManager, RabbitMqManager>();
        services.AddHostedService<TransmitterService>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddSerilog();
    })
    .Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();

logger.LogInformation("=== Transmitter Microservice ===");
logger.LogInformation("SMPP Server: {Host}:{Port}",
    configuration.GetValue<string>($"{SmppOptions.SectionName}:Host", "localhost"),
    configuration.GetValue<int>($"{SmppOptions.SectionName}:Port", 2775));
logger.LogInformation("RabbitMQ: {Host}:{Port}",
    configuration.GetValue<string>($"{RabbitMqOptions.SectionName}:Host", "localhost"),
    configuration.GetValue<int>($"{RabbitMqOptions.SectionName}:Port", 5672));
logger.LogInformation("Press Ctrl+C to stop the service");
logger.LogInformation("");

try
{
    await host.RunAsync();
}
catch (Exception ex)
{
    logger.LogError(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

