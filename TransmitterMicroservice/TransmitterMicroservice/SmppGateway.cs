using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Inetlab.SMPP;
using Inetlab.SMPP.Builders;
using Inetlab.SMPP.Common;
using Inetlab.SMPP.PDU;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TransmitterMicroservice.Interfaces;
using TransmitterMicroservice.Options;

namespace TransmitterMicroservice;

/// <summary>
/// Handles all SMPP operations: connection, binding, EnquireLink, and sending SMS.
/// </summary>
public class SmppGateway : ISmppGateway
{
    private const int EnquireLinkReportIntervalMinutes = 2;

    /// <summary>Hard timeout when acquiring <see cref="_smppSendLock"/> to prevent silent deadlocks.</summary>
    private static readonly TimeSpan SmppLockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Expected SMSC MessageId format: yyMMddHHmmss (12 digits) + phone number (9-15 digits) = 21-27 chars, all digits.
    /// Rejects garbage numeric values from mismatched PDU responses.
    /// </summary>
    private static readonly Regex ValidSmscMessageIdPattern = new(@"^\d{21,27}$", RegexOptions.Compiled);

    /// <summary>Original length &lt; 30 (after newline normalize): scenario A caps total length at <see cref="PaddingShortMessageMaxTotalLength"/>.</summary>
    private const int PaddingShortMessageMaxOriginalLength = 30;

    /// <summary>Scenario A: total length (text + added spaces, before idempotency marker) must not exceed this.</summary>
    private const int PaddingShortMessageMaxTotalLength = 70;

    /// <summary>Scenario B: max spaces added in total.</summary>
    private const int PaddingLongMessageMaxSpaces = 60;

    private const int PaddingShortLineMaxLength = 20;
    private const int PaddingPerLineMin = 1;
    private const int PaddingPerLineMax = 8;

    /// <summary>Appended only for idempotency; stripped before SubmitSm (never sent to SMSC).</summary>
    private const char PaddingCompleteMarker = '\u200C';

    private readonly ILogger<SmppGateway> _logger;
    private readonly SmppOptions _options;
    private readonly IRabbitMqManager _rabbitMqManager;
    private SmppClient? _client;
    private bool _isBound;
    private readonly object _lock = new();
    private Timer? _enquireLinkTimer;
    private bool _eventsSubscribed;
    private DateTime _lastRabbitReport = DateTime.MinValue;

    /// <summary>
    /// Serializes SMPP PDU operations (SubmitSm, EnquireLink) so that an EnquireLinkResp
    /// cannot be mismatched to a SubmitSm request on the shared TCP connection.
    /// </summary>
    private readonly SemaphoreSlim _smppSendLock = new(1, 1);

    public SmppGateway(ILogger<SmppGateway> logger, IOptions<SmppOptions> options, IRabbitMqManager rabbitMqManager)
    {
        _logger = logger;
        _options = options.Value;
        _rabbitMqManager = rabbitMqManager;
    }

    public bool IsBound()
    {
        lock (_lock)
        {
            return _isBound;
        }
    }

    public async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken = default)
    {
        // Consider Bound (and Open) as healthy so we don't try to bind again and get "Already bound"
        if (IsClientConnectedAndBound())
        {
            lock (_lock)
            {
                if (!_isBound)
                {
                    _isBound = true;
                    _logger.LogDebug("SMPP client already in Bound/Open state; syncing _isBound.");
                }
            }
            return true;
        }

        _logger.LogWarning("SMPP client not bound. Attempting reconnection...");

        try
        {
            if (_client == null)
            {
                await InitializeClientAsync(cancellationToken);
            }
            else
            {
                if (!IsClientConnectedAndBound())
                {
                    _logger.LogInformation("SMPP client connection not healthy. Status: {Status}. Reconnecting...", _client.Status);
                }
                await ConnectAndBindAsync(cancellationToken);
            }

            if (IsClientConnectedAndBound())
            {
                lock (_lock) { _isBound = true; }
                _logger.LogInformation("SMPP reconnection successful");
                RestartEnquireLinkTimer();
                return true;
            }

            _logger.LogWarning("SMPP reconnection failed. Will retry in next iteration.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SMPP reconnection attempt");
            return false;
        }
    }

    /// <summary>
    /// True when the client exists and is in Bound state (ready for sending).
    /// Open is not considered ready—sending requires Bound.
    /// </summary>
    private bool IsClientConnectedAndBound()
    {
        if (_client == null) return false;
        return _client.Status == ConnectionStatus.Bound;
    }

    public async Task<SubmitSmResp?> SendSmsAsync(
        string sourceAddress,
        string destinationAddress,
        string messageText,
        int deliveryId,
        Action<string, SubmitSmResp, int, int>? onPartSent = null,
        CancellationToken cancellationToken = default)
    {
        if (_client == null)
        {
            _logger.LogError("SMPP client is null. Cannot send.");
            return null;
        }

        lock (_lock)
        {
            if (!_isBound)
            {
                _logger.LogWarning("SMPP client not bound. Cannot send.");
                return null;
            }
        }

        var marked = ApplySpaceBudgetPadding(messageText);
        var textForSmsc = StripPaddingCompleteMarker(marked);
        SubmitSm[] parts = BuildSubmitSmParts(sourceAddress, destinationAddress, textForSmsc);
        if (parts.Length == 0)
        {
            _logger.LogWarning("SMS builder produced no parts. DeliveryId: {DeliveryId}", deliveryId);
            return null;
        }

        _logger.LogInformation("Sending message ({PartCount} part(s)) - DeliveryId: {DeliveryId}, To: {Destination}",
            parts.Length, deliveryId, destinationAddress);

        // Acquire the SMPP send lock so EnquireLink cannot interleave with SubmitSm on the wire.
        if (!await _smppSendLock.WaitAsync(SmppLockTimeout, cancellationToken))
        {
            _logger.LogError("Timed out acquiring SMPP send lock after {Timeout}s - DeliveryId: {DeliveryId}. Possible deadlock.",
                SmppLockTimeout.TotalSeconds, deliveryId);
            return null;
        }

        SubmitSmResp[] responses;
        try
        {
            responses = await _client.SubmitAsync(parts);
        }
        finally
        {
            _smppSendLock.Release();
        }

        if (responses == null || responses.Length != parts.Length)
        {
            _logger.LogError(
                "SubmitAsync returned unexpected response count (expected {Expected}, got {Actual}) - DeliveryId: {DeliveryId}",
                parts.Length,
                responses?.Length ?? 0,
                deliveryId);
            throw new InvalidOperationException("SubmitAsync did not return one SubmitSmResp per part.");
        }

        SubmitSmResp? lastResp = null;
        string? finalId = null;
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            var resp = responses[i];
            _logger.LogDebug("Processing SubmitSmResp part {Part}/{Total} - DeliveryId: {DeliveryId}",
                i + 1, parts.Length, deliveryId);

            if (resp.Header.Sequence != part.Header.Sequence)
            {
                _logger.LogWarning(
                    "SubmitSmResp sequence mismatch for part {Part}/{Total} - DeliveryId: {DeliveryId}. Request seq {ReqSeq}, response seq {RespSeq}. Still using this response's MessageId.",
                    i + 1,
                    parts.Length,
                    deliveryId,
                    part.Header.Sequence,
                    resp.Header.Sequence);
            }

            if (resp.Header.Status != CommandStatus.ESME_ROK)
            {
                var status = resp.Header.Status;
                _logger.LogError("Submit failed for part {Part}/{Total} - DeliveryId: {DeliveryId}, Status: {Status}",
                    i + 1, parts.Length, deliveryId, status);
                throw new InvalidOperationException($"Submit failed: {status}");
            }

            string smscId = resp.MessageId?.TrimEnd('\0') ?? string.Empty;
            if (string.IsNullOrEmpty(smscId))
            {
                _logger.LogError(
                    "SubmitSmResp missing provider MessageId for part {Part}/{Total} - DeliveryId: {DeliveryId}. Response will not be published.",
                    i + 1,
                    parts.Length,
                    deliveryId);
                throw new InvalidOperationException("SubmitSmResp missing provider MessageId");
            }

            if (!ValidSmscMessageIdPattern.IsMatch(smscId))
            {
                _logger.LogError(
                    "SubmitSmResp returned malformed MessageId '{SmscMessageId}' for part {Part}/{Total} - DeliveryId: {DeliveryId}. " +
                    "Expected format: 21-27 digits (yyMMddHHmmss + phone). Possible PDU mismatch.",
                    smscId, i + 1, parts.Length, deliveryId);
                throw new InvalidOperationException(
                    $"SubmitSmResp returned malformed MessageId '{smscId}' — possible PDU response mismatch");
            }

            lastResp = resp;
            finalId = smscId;
            onPartSent?.Invoke(smscId, resp, i + 1, parts.Length);

            _logger.LogDebug("Part {Part}/{Total} sent - DeliveryId: {DeliveryId}, SmscMessageId: {SmscMessageId}",
                i + 1, parts.Length, deliveryId, smscId);
        }

        _logger.LogInformation("Message sent successfully - DeliveryId: {DeliveryId}, SmscMessageId: {SmscMessageId}",
            deliveryId, finalId);
        return lastResp;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _enquireLinkTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _enquireLinkTimer?.Dispose();
        _enquireLinkTimer = null;
        _logger.LogDebug("EnquireLink timer disposed");

        if (_client == null)
        {
            _smppSendLock.Dispose();
            return;
        }

        // Wait for any in-flight SubmitAsync / EnquireLinkAsync to finish before tearing down.
        if (!await _smppSendLock.WaitAsync(SmppLockTimeout, cancellationToken))
        {
            _logger.LogWarning("Timed out waiting for SMPP send lock during shutdown. Proceeding with disconnect.");
        }

        try
        {
            bool shouldUnbind;
            lock (_lock)
            {
                shouldUnbind = _isBound;
            }

            if (shouldUnbind)
            {
                _logger.LogInformation("Unbinding from SMSC...");
                await _client.UnbindAsync();
                lock (_lock) { _isBound = false; }
            }

            _logger.LogInformation("Disconnecting from SMSC...");
            await _client.DisconnectAsync();
            _logger.LogInformation("SMPP client disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disconnecting SMPP client");
        }
        finally
        {
            // Release the lock only if we acquired it (WaitAsync returned true).
            // Safe to call even if we timed out — Release would throw, but we're in finally.
            try { _smppSendLock.Release(); } catch (SemaphoreFullException) { }
            _smppSendLock.Dispose();

            try
            {
                _client.Dispose();
                _client = null;
                _eventsSubscribed = false;
                _logger.LogDebug("SMPP client disposed");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SMPP client");
            }
        }
    }

    private async Task InitializeClientAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Creating new SMPP client...");
        _client = new SmppClient();
        _eventsSubscribed = false;
        SubscribeToEvents();
        await ConnectAndBindAsync(cancellationToken);
    }

    private void SubscribeToEvents()
    {
        if (_client == null || _eventsSubscribed)
            return;

        _logger.LogDebug("SMPP events subscribed");
        _eventsSubscribed = true;
    }

    private async Task ConnectAndBindAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Already bound: sync flag and skip to avoid "Already bound" exception
            if (_client!.Status == ConnectionStatus.Bound)
            {
                lock (_lock) { _isBound = true; }
                _logger.LogDebug("SMPP client already bound; skipping bind.");
                return;
            }

            _logger.LogInformation("Connecting to SMSC at {Host}:{Port}...", _options.Host, _options.Port);

            if (_client.Status != ConnectionStatus.Open && _client.Status != ConnectionStatus.Bound)
            {
                await _client.ConnectAsync(_options.Host, _options.Port);
                _logger.LogInformation("Connected to SMSC");
            }

            var bindResp = await _client.BindAsync(
                _options.SystemId,
                _options.Password,
                ConnectionMode.Transmitter);

            lock (_lock)
            {
                if (bindResp.Header.Status == CommandStatus.ESME_ROK)
                {
                    _isBound = true;
                    _logger.LogInformation("Successfully bound to SMSC as Transmitter");
                }
                else
                {
                    _isBound = false;
                    var status = bindResp.Header.Status;
                    _logger.LogError(
                        "Failed to bind to SMSC. SMPP Status: {Status} (0x{StatusCode:X}). Client status: {ClientStatus}",
                        status, (int)status, _client?.Status ?? ConnectionStatus.Closed);
                }
            }
        }
        catch (Exception ex)
        {
            var clientStatus = _client?.Status ?? ConnectionStatus.Closed;
            _logger.LogError(ex,
                "Error connecting or binding to SMSC. Client status: {ClientStatus}. Exception: {Message}",
                clientStatus, ex.Message);
            lock (_lock) { _isBound = false; }
            PublishDownStatusToServerStatus($"Initial bind failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Whitespace padding before segment split. Only appends spaces (never truncates user text).
    /// Scenario A: budget = spaces to reach <see cref="PaddingShortMessageMaxTotalLength"/> chars total (hard cap).
    /// Scenario B: budget = <see cref="PaddingLongMessageMaxSpaces"/> spaces max.
    /// Ends with <see cref="PaddingCompleteMarker"/>; strip before SMSC with <see cref="StripPaddingCompleteMarker"/>.
    /// </summary>
    private static string ApplySpaceBudgetPadding(string messageText)
    {
        if (string.IsNullOrEmpty(messageText))
            return messageText;

        if (messageText[^1] == PaddingCompleteMarker)
            return messageText;

        var normalized = messageText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\r", "\n", StringComparison.Ordinal);
        var scenarioShort = normalized.Length < PaddingShortMessageMaxOriginalLength;
        var maxSpaceBudget = scenarioShort
            ? Math.Max(0, PaddingShortMessageMaxTotalLength - normalized.Length)
            : PaddingLongMessageMaxSpaces;

        var lines = normalized.Split('\n');
        var linePads = new int[lines.Length];
        var initialShortLine = new bool[lines.Length];
        var anyInitialShortLine = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var isShort = lines[i].TrimEnd().Length < PaddingShortLineMaxLength;
            initialShortLine[i] = isShort;
            if (isShort)
                anyInitialShortLine = true;
        }

        var remaining = maxSpaceBudget;
        var anyLineReceivedPadding = false;

        while (remaining > 0 && anyInitialShortLine)
        {
            for (var i = 0; i < lines.Length; i++)
            {
                if (!initialShortLine[i] || remaining <= 0)
                    continue;
                var cap = Math.Min(PaddingPerLineMax, remaining);
                var add = Random.Shared.Next(PaddingPerLineMin, cap + 1);
                linePads[i] += add;
                remaining -= add;
                anyLineReceivedPadding = true;
            }
        }

        if (!anyLineReceivedPadding && remaining > 0)
        {
            var lastIdx = lines.Length - 1;
            var cap = Math.Min(PaddingPerLineMax, remaining);
            var add = Random.Shared.Next(PaddingPerLineMin, cap + 1);
            linePads[lastIdx] += add;
        }

        var joined = BuildLinesWithPadding(lines, linePads);
        return joined + PaddingCompleteMarker;
    }

    private static string BuildLinesWithPadding(string[] rawLines, int[] linePads)
    {
        for (var i = 0; i < rawLines.Length; i++)
        {
            var core = rawLines[i].TrimEnd();
            var p = linePads[i];
            rawLines[i] = p > 0 ? core + new string(' ', p) : rawLines[i];
        }

        return string.Join("\n", rawLines);
    }

    private static string StripPaddingCompleteMarker(string text)
    {
        if (string.IsNullOrEmpty(text) || text[^1] != PaddingCompleteMarker)
            return text;
        return text[..^1];
    }

    /// <summary>
    /// Builds one or more SubmitSm PDUs using the SMS builder. Long messages are automatically
    /// split into concatenated parts (UDH). Uses UCS2 encoding for Hebrew, English, Arabic.
    /// </summary>
    /// <param name="sourceAddress">Sender address.</param>
    /// <param name="destinationAddress">Destination phone number.</param>
    /// <param name="messageText">Full message text (any length).</param>
    /// <returns>Array of SubmitSm (single element for short messages, multiple for long with UDH concatenation).</returns>
    private SubmitSm[] BuildSubmitSmParts(string sourceAddress, string destinationAddress, string messageText)
    {
        if (_client == null)
            return Array.Empty<SubmitSm>();

        var builder = SMS.ForSubmit()
            .From(sourceAddress)
            .To(destinationAddress)
            .Text(messageText)
            .Coding(DataCodings.UCS2);

        // Use UDH concatenation for long messages (default; compatible with most SMSCs)
        SubmitSm[] parts = builder.Create(_client);
        return parts ?? Array.Empty<SubmitSm>();
    }

    /// <summary>
    /// Starts the EnquireLink timer if not already running.
    /// </summary>
    private void StartEnquireLinkTimerIfNeeded()
    {
        if (_enquireLinkTimer != null)
            return;

        _enquireLinkTimer = new Timer(async _ => await PerformEnquireLinkAsync(), null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_options.EnquireLinkIntervalSeconds));

        _logger.LogInformation("EnquireLink timer started. Interval: {Interval}s", _options.EnquireLinkIntervalSeconds);
    }

    /// <summary>
    /// Restarts the EnquireLink timer (disposes existing, then starts new). Call after successful
    /// connection/reconnection so the timer is active with the configured interval and the first
    /// EnquireLink runs soon.
    /// </summary>
    private void RestartEnquireLinkTimer()
    {
        _enquireLinkTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        _enquireLinkTimer?.Dispose();
        _enquireLinkTimer = null;

        _enquireLinkTimer = new Timer(async _ => await PerformEnquireLinkAsync(), null,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(_options.EnquireLinkIntervalSeconds));

        _logger.LogInformation("EnquireLink timer restarted. Interval: {Interval}s", _options.EnquireLinkIntervalSeconds);
    }

    private async Task PerformEnquireLinkAsync()
    {
        try
        {
            // Bound or Open = healthy; only clear _isBound when actually disconnected
            if (_client == null || !IsClientConnectedAndBound())
            {
                lock (_lock) { _isBound = false; }
                var status = _client?.Status ?? ConnectionStatus.Closed;
                _logger.LogWarning("SMPP client not connected. Status: {Status}. Setting _isBound = false", status);
                PublishDownStatusToServerStatus($"SMPP not connected: {status}");
                return;
            }

            var enquireLink = new EnquireLink();

            if (!await _smppSendLock.WaitAsync(SmppLockTimeout))
            {
                _logger.LogWarning("Timed out acquiring SMPP send lock for EnquireLink after {Timeout}s. Skipping this cycle.",
                    SmppLockTimeout.TotalSeconds);
                return;
            }

            EnquireLinkResp? resp;
            try
            {
                resp = await _client.EnquireLinkAsync(enquireLink);
            }
            finally
            {
                _smppSendLock.Release();
            }

            if (resp?.Header.Status == CommandStatus.ESME_ROK)
            {
                var timestamp = DateTime.UtcNow;
                var timestampStr = timestamp.ToString("HH:mm:ss dd/MM/yyyy");
                _logger.LogInformation("EnquireLink successful {Timestamp}", timestampStr);

                if ((timestamp - _lastRabbitReport).TotalMinutes >= EnquireLinkReportIntervalMinutes)
                {
                    _lastRabbitReport = timestamp;
                    if (_rabbitMqManager.EnsureChannelHealthy())
                    {
                        var payload = new { type = "EnquireLink", status = "OK", timestamp = timestampStr };
                        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
                        _rabbitMqManager.SafePublishToServerStatus(body);
                        _logger.LogInformation("Sent status update to RabbitMQ: OK (Reason: Periodic check)");
                    }
                }
            }
            else
            {
                lock (_lock) { _isBound = false; }
                var failStatus = resp?.Header.Status ?? CommandStatus.ESME_RINVMSGLEN;
                _logger.LogWarning("EnquireLink failed. SMPP Status: {Status} (0x{StatusCode:X}). Setting _isBound = false",
                    failStatus, (int)failStatus);
                PublishDownStatusToServerStatus($"EnquireLink failed: {failStatus}");
            }
        }
        catch (Exception ex)
        {
            lock (_lock) { _isBound = false; }
            _logger.LogError(ex, "Error performing EnquireLink. Setting _isBound = false");
            PublishDownStatusToServerStatus($"EnquireLink error: {ex.Message}");
        }
    }

    private void PublishDownStatusToServerStatus(string? reason = null)
    {
        if (!_rabbitMqManager.EnsureChannelHealthy())
            return;
        var timestampStr = DateTime.UtcNow.ToString("HH:mm:ss dd/MM/yyyy");
        var payload = new { type = "EnquireLink", status = "Down", timestamp = timestampStr, reason };
        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload));
        _rabbitMqManager.SafePublishToServerStatus(body);
        _logger.LogInformation("Sent status update to RabbitMQ: Down (Reason: {Reason})", reason ?? "—");
    }
}
