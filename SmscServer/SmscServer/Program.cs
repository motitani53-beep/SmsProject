using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;

namespace SmscServer;

class Program
{
    static async Task Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddCommandLine(args)
            .Build();

        // Create host builder
        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                services.AddHostedService<SmscServerService>();
            })
            .ConfigureLogging(logging =>
            {
                logging.ClearProviders();
                logging.AddConsole();
                logging.SetMinimumLevel(LogLevel.Information);
            })
            .Build();

        var logger = host.Services.GetRequiredService<ILogger<Program>>();
        
        logger.LogInformation("=== SMSC Mock Server ===");
        logger.LogInformation("Port: {Port}", configuration.GetValue<int>("SmscServer:Port", 2775));
        logger.LogInformation("Request Timeout: {Timeout}ms", configuration.GetValue<int>("SmscServer:RequestTimeout", 30000));
        logger.LogInformation("Press Ctrl+C to stop the server");
        logger.LogInformation("");

        try
        {
            await host.RunAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Application terminated unexpectedly");
        }
    }
}
