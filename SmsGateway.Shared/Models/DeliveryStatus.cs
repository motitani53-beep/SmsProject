namespace SmsGateway.Shared.Models;

/// <summary>
/// Delivery status enum for SMS messages
/// </summary>
public enum DeliveryStatus
{
    /// <summary>
    /// Message is pending processing
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Message was successfully delivered to the handset
    /// </summary>
    Successful = 1,

    /// <summary>
    /// Message delivery failed (rejected, undeliverable, etc.)
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Provider received the message; in progress toward delivery
    /// </summary>
    Accepted = 3,

    /// <summary>
    /// Message sent to SMSC, awaiting DLR response
    /// </summary>
    Acceptable = 4,

    /// <summary>
    /// No response from SMSC within timeout
    /// </summary>
    TimeoutSMSC = 5,

    /// <summary>
    /// Unknown status
    /// </summary>
    Unknown = 6,

    /// <summary>
    /// No delivery receipt (DLR) received after validity period
    /// </summary>
    Expired = 7
}
