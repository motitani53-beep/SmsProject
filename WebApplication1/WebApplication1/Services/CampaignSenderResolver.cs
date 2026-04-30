namespace WebApplication1.Services;

/// <summary>
/// Resolves campaign sender type/value into <see cref="SmsGateway.Shared.Models.DeliveryDetails.ActualSender"/>.
/// Random campaigns use <see cref="SenderPhoneNumberService"/> pool; fixed sender uses <see cref="SmsGateway.Shared.Models.Campaign.SenderValue"/> only (no pool).
/// </summary>
public static class CampaignSenderResolver
{
    /// <summary>
    /// Normalizes UI / API synonyms to stored campaign sender_type values: <c>random</c>, <c>manual_number</c>, <c>manual_string</c>.
    /// </summary>
    public static string NormalizeSenderType(string? senderType, string? senderValue)
    {
        var t = (senderType ?? "").Trim();
        if (string.IsNullOrEmpty(t) && !string.IsNullOrWhiteSpace(senderValue))
            return "manual_number";

        if (t.Equals("specific", StringComparison.OrdinalIgnoreCase) || t == "0")
            return "manual_number";

        if (t.Equals("alphanumeric", StringComparison.OrdinalIgnoreCase))
            return "manual_string";

        return t;
    }

    public static bool IsRandomSender(string? normalizedSenderType) =>
        string.Equals(normalizedSenderType?.Trim(), "random", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// For random: round-robin pool. For any other type with a non-empty <paramref name="senderValue"/>: returns trimmed value (no pool).
    /// Otherwise defers to <see cref="SenderPhoneNumberService.GetNextPhoneNumberForCampaign"/> for fallbacks.
    /// </summary>
    public static string ResolveActualSender(
        int campaignId,
        int messageIndex,
        string? senderType,
        string? senderValue,
        SenderPhoneNumberService senderService)
    {
        var normalized = NormalizeSenderType(senderType, senderValue);
        if (IsRandomSender(normalized))
            return senderService.GetNextPhoneNumberForCampaign(campaignId, messageIndex, normalized, senderValue);

        var trimmedValue = senderValue?.Trim();
        if (!string.IsNullOrEmpty(trimmedValue))
            return trimmedValue;

        return senderService.GetNextPhoneNumberForCampaign(campaignId, messageIndex, normalized, senderValue);
    }
}
