using Microsoft.AspNetCore.SignalR;
using SmsGateway.Shared.Models;
using WebApplication1.Hubs;

namespace WebApplication1.Services;

/// <summary>
/// Sends <see cref="CampaignHub.ReceiveStatusUpdate"/> to the campaign group.
/// <paramref name="status"/> is the stored <see cref="DeliveryStatus"/> underlying value (same as DB):
/// 0=Pending, 1=Successful, 2=Failed, 3=Accepted, 4=Acceptable, 5=TimeoutSMSC, 6=Unknown, 7=Expired.
/// </summary>
public static class CampaignHubBroadcast
{
    public static Task SendDeliveryStatusAsync(
        this IHubContext<CampaignHub> hub,
        int campaignId,
        int deliveryId,
        DeliveryStatus status,
        CancellationToken cancellationToken = default)
    {
        // Payload: DeliveryId + Status (PascalCase in C#; JSON may be camelCase depending on hub protocol — client accepts both).
        return hub.Clients
            .Group($"Campaign_{campaignId}")
            .SendAsync(
                CampaignHub.ReceiveStatusUpdate,
                new { DeliveryId = deliveryId, Status = (int)status },
                cancellationToken);
    }
}
