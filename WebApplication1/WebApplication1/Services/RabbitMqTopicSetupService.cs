using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Retry;
using RabbitMQ.Client;
using SmsGateway.Shared.Options;

namespace WebApplication1.Services;

public class RabbitMqTopicSetupService
{
    /// <summary>Matches <see cref="SmsResultProcessor"/> dead-letter / orphan publishing.</summary>
    private const string SmsResultsDlq = "sms_results_dlq";

    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTopicSetupService> _logger;

    public RabbitMqTopicSetupService(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTopicSetupService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public void EnsureTopicsExist()
    {
        var numberOfTopics = _options.NumberOfTopics;
        var topicPrefix = _options.TopicNamePrefix;

        if (numberOfTopics < 1)
        {
            _logger.LogWarning(
                "RabbitMQ Topics:NumberOfTopics is {Count}, no topics will be created",
                numberOfTopics);
            return;
        }

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password
        };

        // Polly v8: AttemptNumber is zero-based; delays 2^1, 2^2, … => 2s, 4s, 8s, 16s, 32s for 5 retries.
        var pipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                MaxRetryAttempts = 5,
                DelayGenerator = static args =>
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, args.AttemptNumber + 1));
                    return new ValueTask<TimeSpan?>(delay);
                },
                OnRetry = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "RabbitMQ EnsureTopicsExist failed (attempt {AttemptNumber}/5). Retrying after {DelaySeconds}s...",
                        args.AttemptNumber + 1,
                        args.RetryDelay.TotalSeconds);
                    return default;
                }
            })
            .Build();

        try
        {
            pipeline.Execute(() => DeclareTopics(factory, numberOfTopics, topicPrefix));
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create RabbitMQ topics after retries. Ensure RabbitMQ is running at {Host}:{Port}",
                _options.Host,
                _options.Port);
            throw;
        }
    }

    private void DeclareTopics(ConnectionFactory factory, int numberOfTopics, string topicPrefix)
    {
        using var connection = factory.CreateConnection();
        using var channel = connection.CreateModel();

        // Main exchange for SMS publishing
        const string smsExchange = "sms_exchange";
        channel.ExchangeDeclare(
            exchange: smsExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false);
        _logger.LogInformation("RabbitMQ exchange created: {ExchangeName}", smsExchange);

        // Queues and bindings to sms_exchange (0-based indexing)
        for (var i = 0; i < numberOfTopics; i++)
        {
            var routingKey = $"{topicPrefix}.{i}";
            var queueName = $"sms_queue_{i}";
            channel.QueueDeclare(queue: queueName, durable: true, exclusive: false, autoDelete: false);
            channel.QueueBind(queue: queueName, exchange: smsExchange, routingKey: routingKey);

            _logger.LogInformation(
                "RabbitMQ queue created and bound: {QueueName} -> {RoutingKey}",
                queueName,
                routingKey);
        }

        // High-priority queue for urgent SMS messages
        const string highPriorityQueue = "sms_high_priority";
        const string highPriorityRoutingKey = "sms.priority";
        channel.QueueDeclare(queue: highPriorityQueue, durable: true, exclusive: false, autoDelete: false);
        channel.QueueBind(queue: highPriorityQueue, exchange: smsExchange, routingKey: highPriorityRoutingKey);
        _logger.LogInformation(
            "RabbitMQ high-priority queue created and bound: {QueueName} -> {RoutingKey}",
            highPriorityQueue,
            highPriorityRoutingKey);

        // Results queue for Transmitter microservice responses (must exist before dlr_retry DLX routing key)
        var resultsQueue = _options.OutputQueue;
        channel.QueueDeclare(queue: resultsQueue, durable: true, exclusive: false, autoDelete: false);
        _logger.LogInformation("RabbitMQ results queue created: {QueueName}", resultsQueue);

        // Dead-letter queue for orphan DLR payloads (SmsResultProcessor publishes here)
        channel.QueueDeclare(queue: SmsResultsDlq, durable: true, exclusive: false, autoDelete: false);
        _logger.LogInformation("RabbitMQ DLQ created: {QueueName}", SmsResultsDlq);

        // DLR retry: TTL then dead-letter to default exchange -> results queue (matches SmsResultProcessor)
        const string dlrRetryQueue = "dlr_retry";
        const int dlrRetryTtlMs = 60000;
        var dlrRetryArgs = new Dictionary<string, object>
        {
            { "x-message-ttl", dlrRetryTtlMs },
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", resultsQueue }
        };
        channel.QueueDeclare(queue: dlrRetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: dlrRetryArgs);
        _logger.LogInformation(
            "RabbitMQ DLR retry queue created: {QueueName} (TTL {TtlMs}ms, DLX -> {OutputQueue})",
            dlrRetryQueue,
            dlrRetryTtlMs,
            resultsQueue);

        // Campaign pending queue for QueueDispatcherService
        var campaignPendingQueue = _options.CampaignPendingQueue;
        channel.QueueDeclare(queue: campaignPendingQueue, durable: true, exclusive: false, autoDelete: false);
        _logger.LogInformation("RabbitMQ campaign pending queue created: {QueueName}", campaignPendingQueue);

        _logger.LogInformation(
            "RabbitMQ: Created sms_exchange, {Count} topic queue(s), high-priority queue, results queue, DLQ, dlr_retry, and campaign_pending",
            numberOfTopics);
    }
}
