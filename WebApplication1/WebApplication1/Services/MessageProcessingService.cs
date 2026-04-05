using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using SmsGateway.Shared.Data;
using SmsGateway.Shared.DTOs;
using SmsGateway.Shared.Models;
using SmsGateway.Shared.Options;

namespace WebApplication1.Services;

public class MessageProcessingService
{
    private const string SmsExchange = "sms_exchange";

    private readonly ApplicationDbContext _context;
    private readonly SenderPhoneNumberService _senderPhoneService;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly RabbitMqOptions _rabbitMqOptions;
    private readonly ILogger<MessageProcessingService> _logger;

    public MessageProcessingService(
        ApplicationDbContext context,
        SenderPhoneNumberService senderPhoneService,
        IRabbitMqService rabbitMqService,
        IOptions<RabbitMqOptions> rabbitMqOptions,
        ILogger<MessageProcessingService> logger)
    {
        _context = context;
        _senderPhoneService = senderPhoneService;
        _rabbitMqService = rabbitMqService;
        _rabbitMqOptions = rabbitMqOptions.Value;
        _logger = logger;
    }

    /// <summary>
    /// Replaces <c>{key}</c> placeholders in the message with values from <paramref name="customFields"/>.
    /// </summary>
    public string ReplaceMessageFields(string messageContent, IReadOnlyDictionary<string, string>? customFields)
    {
        if (string.IsNullOrEmpty(messageContent) || customFields == null || customFields.Count == 0)
            return messageContent;

        var result = messageContent;
        foreach (var kv in customFields)
        {
            if (string.IsNullOrEmpty(kv.Key))
                continue;
            var placeholder = "{" + kv.Key + "}";
            result = result.Replace(placeholder, kv.Value ?? "");
        }

        return result;
    }

    /// <summary>
    /// Builds a flat string dictionary from a JSON object stored in <see cref="DeliveryDetails.AdditionalData"/> (jsonb).
    /// </summary>
    public static IReadOnlyDictionary<string, string> AdditionalDataToCustomFields(JsonElement? additionalData)
    {
        if (!additionalData.HasValue || additionalData.Value.ValueKind != JsonValueKind.Object)
            return new Dictionary<string, string>();

        var dict = new Dictionary<string, string>();
        foreach (var prop in additionalData.Value.EnumerateObject())
        {
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString() ?? "",
                JsonValueKind.Number => prop.Value.GetRawText(),
                JsonValueKind.True => "true",
                JsonValueKind.False => "false",
                JsonValueKind.Null => "",
                _ => prop.Value.GetRawText()
            };
        }

        return dict;
    }

    /// <summary>
    /// Publishes campaign delivery messages to RabbitMQ after they are saved to the DB.
    /// All messages for a campaign use the same routing key (campaign.Id % numberOfTopics) to maintain order.
    /// </summary>
    public void PublishCampaignToRabbitMq(Campaign campaign, List<DeliveryDetails> deliveryDetails)
    {
        var numberOfTopics = _rabbitMqOptions.NumberOfTopics;

        if (numberOfTopics < 1)
        {
            _logger.LogWarning("RabbitMQ NumberOfTopics is {Count}, skipping publish", numberOfTopics);
            return;
        }

        var topicIndex = campaign.Id % numberOfTopics;
        var routingKey = $"{_rabbitMqOptions.TopicNamePrefix}.{topicIndex}";

        _logger.LogInformation("Publishing {Count} messages for campaign {CampaignId} to RabbitMQ with routing key {RoutingKey}",
            deliveryDetails.Count, campaign.Id, routingKey);

        for (var i = 0; i < deliveryDetails.Count; i++)
        {
            var delivery = deliveryDetails[i];

            var customFields = AdditionalDataToCustomFields(delivery.AdditionalData);
            var messageText = ReplaceMessageFields(campaign.MessageContent, customFields);
            var actualSender = _senderPhoneService.GetNextPhoneNumberForCampaign(
                campaign.Id, i, campaign.SenderType ?? string.Empty, campaign.SenderValue);

            var message = new SmsMessageDto
            {
                DeliveryId = delivery.Id,
                CampaignId = campaign.Id,
                PhoneNumber = delivery.PhoneNumber,
                MessageText = messageText,
                ActualSender = actualSender
            };

            _logger.LogInformation("Publishing to RabbitMQ - DeliveryId: {DeliveryId}, DestinationNumber: {DestinationNumber}",
                delivery.Id, delivery.PhoneNumber);

            _rabbitMqService.PublishJson(SmsExchange, routingKey, message);
        }

        _logger.LogInformation("Published {Count} messages for campaign {CampaignId} to RabbitMQ", deliveryDetails.Count, campaign.Id);
    }

    /// <summary>
    /// Publishes campaign delivery messages to RabbitMQ using the specified routing key (e.g. from QueueDispatcher).
    /// </summary>
    public void PublishCampaignToRabbitMqWithRoutingKey(Campaign campaign, List<DeliveryDetails> deliveryDetails, string routingKey)
    {
        _logger.LogInformation("Publishing {Count} messages for campaign {CampaignId} to RabbitMQ with routing key {RoutingKey}",
            deliveryDetails.Count, campaign.Id, routingKey);

        for (var i = 0; i < deliveryDetails.Count; i++)
        {
            var delivery = deliveryDetails[i];

            var customFields = AdditionalDataToCustomFields(delivery.AdditionalData);
            var messageText = ReplaceMessageFields(campaign.MessageContent, customFields);
            var actualSender = _senderPhoneService.GetNextPhoneNumberForCampaign(
                campaign.Id, i, campaign.SenderType ?? string.Empty, campaign.SenderValue);

            var message = new SmsMessageDto
            {
                DeliveryId = delivery.Id,
                CampaignId = campaign.Id,
                PhoneNumber = delivery.PhoneNumber,
                MessageText = messageText,
                ActualSender = actualSender
            };

            _rabbitMqService.PublishJson(SmsExchange, routingKey, message);
        }

        _logger.LogInformation("Published {Count} messages for campaign {CampaignId} to RabbitMQ", deliveryDetails.Count, campaign.Id);
    }
}
