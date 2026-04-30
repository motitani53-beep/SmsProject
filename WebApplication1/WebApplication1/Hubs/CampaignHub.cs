using Microsoft.AspNetCore.SignalR;

namespace WebApplication1.Hubs;

/// <summary>Real-time delivery status updates for clients subscribed to a campaign group.</summary>
public sealed class CampaignHub : Hub
{
    /// <summary>Adds the connection to <c>Campaign_{campaignId}</c> for <see cref="ReceiveStatusUpdate"/> events.</summary>
    public async Task JoinCampaign(int campaignId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, $"Campaign_{campaignId}");
    }

    public const string ReceiveStatusUpdate = "ReceiveStatusUpdate";
}
