using System.Globalization;
using System.Net;
using Inetlab.SMPP;
using Inetlab.SMPP.Common;
using Inetlab.SMPP.PDU;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SmscServer;

public class SmscServerService : BackgroundService
{
    private readonly ILogger<SmscServerService> _logger;
    private readonly IConfiguration _configuration;
    private SmppServer? _server;

    /// <summary>Serializes MessageId generation across concurrent SubmitSm handlers.</summary>
    private readonly object _submitSmMessageIdLock = new();

    /// <summary>
    /// Last UTC timestamp (whole seconds) used in a generated MessageId. Advanced by at least one second
    /// whenever a new SubmitSm would reuse the same yyMMddHHmmss prefix (multi-part batch or same-millisecond burst).
    /// </summary>
    private DateTime _lastSubmitSmMessageIdUtcSecond = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);

    public SmscServerService(ILogger<SmscServerService> logger, IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var port = _configuration.GetValue<int>("SmscServer:Port", 2775);
        _server = new SmppServer(new IPEndPoint(IPAddress.Any, port));

        // אישור חיבורים
        _server.evClientBind += (sender, client, bindPdu) =>
        {
            // ב-Inetlab, ה-CommandId מייצג את סוג החיבור (Transmitter, Receiver וכו')
            _logger.LogInformation("Client connected: {SystemId} with Command: {Command}",
                bindPdu.SystemId,
                bindPdu.Header.Command);

            bindPdu.Response.Header.Status = CommandStatus.ESME_ROK;
        };
        // טיפול בקבלת הודעה (SubmitSm)
        _server.evClientSubmitSm += async (sender, client, submitSm) =>
        {
            var sourceAddr = submitSm.SourceAddress?.Address ?? "";
            var destAddr = submitSm.DestinationAddress?.Address ?? "";

            // Decode user data (short_message and/or payload) using DataCoding — same as Inetlab samples.
            var content = client.EncodingMapper.GetMessageText(submitSm) ?? string.Empty;
            var len = content.Length;
            var coding = submitSm.DataCoding.ToString();
            _logger.LogInformation(@"
┌──────────────── SMS RECEIVED ────────────────┐
│ From:    {Source}
│ To:      {Dest}
│ Coding:  {Coding}
│ Length:  {Len}
│ Content: [{Content}]
└──────────────────────────────────────────────┘",
                sourceAddr, destAddr, coding, len, content);

            // Format: yyMMddHHmmss + RecipientPhoneNumber; strictly increasing time suffix so each PDU (incl. concat parts) is unique.
            var messageId = BuildUniqueSubmitSmMessageId(destAddr);

            // 1. מענה מיידי לאותו Client ששלח (SubmitSmResp) – MessageId as plain string
            var response = new SubmitSmResp(submitSm) { MessageId = messageId };
            response.Header.Status = CommandStatus.ESME_ROK;
            await client.SendResponseAsync(response);
            _logger.LogInformation("Sent SubmitSmResp to Transmitter for MessageId: {MessageId}", messageId);

            // 2. שליחת ה-DLR לפוד ה-Receiver (בנפרד)
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1000); // סימולציה של השהייה

                    var dlrPdu = CreateDlrPdu(messageId, sourceAddr, destAddr);

                    // חיפוש לקוח שמחובר כ-Receiver או Transceiver כדי לשלוח לו את הדו"ח
                    var receiverClient = _server.ConnectedClients
                        .FirstOrDefault(c => c.BindingMode == ConnectionMode.Receiver ||
                                             c.BindingMode == ConnectionMode.Transceiver);

                    if (receiverClient != null)
                    {
                        // שליחת DeliverSm וקבלת DeliverSmResp באופן אוטומטי על ידי הספרייה
                        await receiverClient.DeliverAsync(dlrPdu);
                        _logger.LogInformation("Sent DeliverSm (DLR) to Receiver client for MessageId: {MessageId}", messageId);
                    }
                    else
                    {
                        _logger.LogWarning("No Receiver client connected! Could not send DLR for {MessageId}", messageId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error routing DLR for MessageId {MessageId}", messageId);
                }
            });
        };

        _server.Start();
        _logger.LogInformation("SMSC Mock Server running on port {Port}", port);

        while (!stoppingToken.IsCancellationRequested)
            await Task.Delay(1000, stoppingToken);
    }

    /// <summary>
    /// Thread-safe MessageId: <c>{yyMMddHHmmss}{destination}</c>. Uses <see cref="DateTime.UtcNow"/> truncated to seconds,
    /// but if that second is not strictly after the last issued id (same burst / same clock second), advances by one second.
    /// </summary>
    private string BuildUniqueSubmitSmMessageId(string destinationAddress)
    {
        lock (_submitSmMessageIdLock)
        {
            var now = DateTime.UtcNow;
            var candidate = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, DateTimeKind.Utc);

            if (_lastSubmitSmMessageIdUtcSecond >= candidate)
                candidate = _lastSubmitSmMessageIdUtcSecond.AddSeconds(1);

            _lastSubmitSmMessageIdUtcSecond = candidate;
            return candidate.ToString("yyMMddHHmmss", CultureInfo.InvariantCulture) + destinationAddress;
        }
    }

    /// <summary>
    /// Creates a Delivery Receipt (DLR) PDU using the same messageId format as SubmitSmResp (yyMMddHHmmss + RecipientPhoneNumber)
    /// so the Web API can match it against the database.
    /// </summary>
    private DeliverSm CreateDlrPdu(string messageId, string source, string dest)
    {
        return new DeliverSm
        {
            SourceAddress = new SmeAddress(dest),
            DestinationAddress = new SmeAddress(source),
            MessageType = MessageTypes.SMSCDeliveryReceipt,
            DataCoding = DataCodings.ASCII,
            Receipt = new Receipt
            {
                MessageId = messageId, // Same format as SubmitSmResp: yyMMddHHmmss + recipient phone
                State = MessageState.Delivered,
                SubmitDate = DateTime.UtcNow,
                DoneDate = DateTime.UtcNow,
                ErrorCode = "000",
                Text = $"id:{messageId} stat:DELIVRD"
            }
        };
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Stop();
        return base.StopAsync(cancellationToken);
    }
}