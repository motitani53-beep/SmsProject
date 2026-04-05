using Microsoft.EntityFrameworkCore;
using Serilog;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.Options;
using WebApplication1.Services;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog for file logging
var logPath = builder.Configuration["Logging:LogFilePath"] ?? "logs/api-{Date}.log";
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .WriteTo.Console()
    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache(); // Required for SenderPhoneNumberService


// Configure PostgreSQL database
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Register services
builder.Services.AddScoped<SenderPhoneNumberService>();
builder.Services.AddScoped<MessageProcessingService>();
builder.Services.AddScoped<ISmsBatchProcessor, SmsBatchProcessor>();
builder.Services.Configure<RabbitMqOptions>(builder.Configuration.GetSection(RabbitMqOptions.SectionName));
builder.Services.Configure<SmsGateway.Shared.Options.ResultProcessorOptions>(builder.Configuration.GetSection(SmsGateway.Shared.Options.ResultProcessorOptions.SectionName));
builder.Services.AddSingleton<IRabbitMqService, RabbitMqService>();
builder.Services.AddSingleton<RabbitMqTopicSetupService>();
builder.Services.AddSingleton<SmscStatusStore>();
builder.Services.AddHostedService<SmscStatusConsumer>();
builder.Services.AddHostedService<QueueDispatcherService>();
builder.Services.AddHostedService<CampaignMonitoringWorker>();
builder.Services.AddHostedService<SmsResultProcessor>();

// Add CORS if needed
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure database is created and initialize SMSC status store from DB
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    try
    {
        if (dbContext.Database.GetPendingMigrations().Any())
        {
            dbContext.Database.Migrate();
            Log.Information("Database Migrated successfully with a new changes");
        }
        else
        {
            Log.Information("Database is up to date");
        }
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error connecting to database");
    }

    // Load latest SMSC status from DB into store (latest record by timestamp)
    var store = scope.ServiceProvider.GetRequiredService<SmscStatusStore>();
    try
    {
        var latest = dbContext.SmscStatus
            .AsNoTracking()
            .OrderByDescending(s => s.Timestamp)
            .FirstOrDefaultAsync()
            .GetAwaiter()
            .GetResult();
        if (latest != null)
        {
            store.Initialize(latest.Status, "");
            Log.Information("SmscStatusStore initialized from DB: Status={Status}", latest.Status);
        }
        else
        {
            store.Initialize("", "");
            Log.Information("SmscStatusStore initialized with no existing record");
        }
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Could not load SMSC status from DB; store initialized with empty state");
        store.Initialize("", "");
    }

    // Ensure RabbitMQ topics exist
    var rabbitMqSetup = scope.ServiceProvider.GetRequiredService<RabbitMqTopicSetupService>();
    try
    {
        rabbitMqSetup.EnsureTopicsExist();
    }
    catch (Exception ex)
    {
        Log.Error(ex, "Error setting up RabbitMQ topics");
        throw;
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

Log.Information("Web API started");

app.Run();
