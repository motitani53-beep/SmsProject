using Inetlab.SMPP.PDU;

namespace TransmitterMicroservice;

/// <summary>
/// Single place to read SMSC message id from <see cref="SubmitSmResp"/>. Same path for 1-part and N-part sends.
/// Uses only <see cref="SubmitSmResp.MessageId"/> (opaque string). Never <see cref="Inetlab.SMPP.Common.SmppHeader.Sequence"/>.
/// Strips C-style NUL termination and surrounding whitespace so the full COctet value is preserved.
/// </summary>
internal static class SmppSubmitSmRespMessageId
{
    internal static string GetOpaqueTrimmed(SubmitSmResp resp)
    {
        if (resp.MessageId is null)
            return string.Empty;

        var span = resp.MessageId.AsSpan().Trim();
        var nul = span.IndexOf('\0');
        if (nul >= 0)
            span = span[..nul].TrimEnd();

        return span.Length == 0 ? string.Empty : span.ToString();
    }
}
