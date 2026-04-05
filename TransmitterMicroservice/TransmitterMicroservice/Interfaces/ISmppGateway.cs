using Inetlab.SMPP.PDU;

namespace TransmitterMicroservice.Interfaces;

/// <summary>
/// Gateway for SMPP operations: connection, binding, EnquireLink, and sending SMS.
/// </summary>
public interface ISmppGateway
{
    /// <summary>
    /// Returns true if the SMPP client is currently bound and ready to send. Does not attempt reconnection.
    /// </summary>
    bool IsBound();

    /// <summary>
    /// Ensures the SMPP client is connected and bound. Attempts reconnection if not.
    /// </summary>
    /// <returns>True if ready to send, false otherwise.</returns>
    Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an SMS (possibly split into multiple parts). Uses UCS2 encoding.
    /// </summary>
    /// <param name="sourceAddress">Sender address.</param>
    /// <param name="destinationAddress">Destination phone number.</param>
    /// <param name="messageText">Message text (supports Hebrew, English, Arabic).</param>
    /// <param name="deliveryId">Delivery ID to map to SMPP Sequence for tracking.</param>
    /// <param name="onPartSent">Optional callback invoked after each part is sent successfully (resp, partNumber, totalParts).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The last SubmitSmResp, or null if send failed.</returns>
    Task<SubmitSmResp?> SendSmsAsync(
        string sourceAddress,
        string destinationAddress,
        string messageText,
        int deliveryId,
        Action<SubmitSmResp, int, int>? onPartSent = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unbinds, disconnects, and disposes SMPP resources.
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);
}
