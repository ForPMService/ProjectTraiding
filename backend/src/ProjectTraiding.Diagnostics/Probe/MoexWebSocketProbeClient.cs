using Microsoft.Extensions.Options;
using ProjectTraiding.Diagnostics.Options;
using System.Diagnostics;
using System.Net.WebSockets;
using System.Text;

namespace ProjectTraiding.Diagnostics.Probe;

/// <summary>
/// Пробник потокового соединения биржи. Отладочная точка: подключается, аутентифицируется,
/// подписывается на один поток и возвращает сырые кадры как есть. Ничего не разбирает
/// по смыслу и ничего никуда не пишет.
/// </summary>
public sealed class MoexWebSocketProbeClient
{
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MinimumDuration = TimeSpan.FromSeconds(1);
    private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);
    private const int ReceiveChunkSize = 16 * 1024;
    private const int UnreadablePreviewBytes = 256;

    private readonly WebSocketProbeOptions _options;
    private readonly ILogger<MoexWebSocketProbeClient> _logger;

    public MoexWebSocketProbeClient(
        IOptions<WebSocketProbeOptions> options,
        ILogger<MoexWebSocketProbeClient> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<WebSocketProbeReport> ProbeAsync(
        string destination,
        string selector,
        TimeSpan duration,
        CancellationToken cancellationToken)
    {
        EnsureCredentialsConfigured();
        EnsureSafeHeaderValue(_options.Domain, "Diagnostics:WebSocketProbe:Domain");
        EnsureSafeHeaderValue(_options.Login, "Diagnostics:WebSocketProbe:Login");
        EnsureSafeHeaderValue(_options.Passcode, "Diagnostics:WebSocketProbe:Passcode");

        if (string.IsNullOrWhiteSpace(destination))
        {
            throw new ArgumentException("destination обязателен.", nameof(destination));
        }

        if (string.IsNullOrWhiteSpace(selector))
        {
            throw new ArgumentException("selector обязателен.", nameof(selector));
        }

        EnsureSafeHeaderValue(destination, nameof(destination));
        EnsureSafeHeaderValue(selector, nameof(selector));

        TimeSpan effective = ClampDuration(duration);
        long startedAt = Stopwatch.GetTimestamp();

        using ClientWebSocket socket = new ClientWebSocket();
        socket.Options.AddSubProtocol("STOMP");

        try
        {
            using CancellationTokenSource handshakeCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            handshakeCts.CancelAfter(OperationTimeout);

            MoexWebSocketLogMessages.Connecting(
                _logger, DiagnosticsLogSources.WebSocket, _options.Url, _options.Domain);

            long handshakeBytes = 0;
            ReceiveOutcome handshake;
            try
            {
                await socket.ConnectAsync(new Uri(_options.Url), handshakeCts.Token);
                await SendFrameAsync(socket, BuildConnectFrame(), handshakeCts.Token);

                while (true)
                {
                    handshake = await ReceiveMessageAsync(
                        socket,
                        _options.MaxCapturedBytes - handshakeBytes,
                        handshakeCts.Token);

                    handshakeBytes += handshake.Bytes;

                    if (handshake.Kind != ReceiveKind.Text
                        || !StompFrame.IsHeartbeat(handshake.Text!))
                    {
                        handshake = handshake with { Bytes = handshakeBytes };
                        break;
                    }
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                MoexWebSocketLogMessages.Failed(
                    _logger, DiagnosticsLogSources.WebSocket, _options.Url,
                    "handshake_timeout", "Биржа не ответила на CONNECT в отведённое время.");

                return new WebSocketProbeReport
                {
                    Connected = false,
                    HandshakeBytes = handshakeBytes,
                    NegotiatedSubProtocol = socket.SubProtocol,
                    TerminationReason = "handshake_timeout",
                    Elapsed = Stopwatch.GetElapsedTime(startedAt),
                };
            }

            if (handshake.Kind == ReceiveKind.Closed)
            {
                return FailedHandshake("websocket_closed", startedAt, socket, handshake);
            }

            if (handshake.Kind == ReceiveKind.InvalidUtf8)
            {
                return FailedHandshake("handshake_invalid_utf8", startedAt, socket, handshake);
            }

            if (handshake.Kind == ReceiveKind.ByteLimitExceeded)
            {
                return FailedHandshake("handshake_byte_limit", startedAt, socket, handshake);
            }

            string handshakeRaw = handshake.Text!;
            StompFrame handshakeFrame;

            try
            {
                handshakeFrame = StompFrame.Parse(handshakeRaw);
            }
            catch (FormatException exception)
            {
                MoexWebSocketLogMessages.Failed(
                    _logger, DiagnosticsLogSources.WebSocket, _options.Url,
                    nameof(FormatException), exception.Message);

                return FailedHandshake(
                    "unexpected_handshake_frame", startedAt, socket, handshake) with
                {
                    RawUnexpectedHandshakeFrame = handshakeRaw,
                };
            }

            if (!string.Equals(handshakeFrame.Command, "CONNECTED", StringComparison.Ordinal))
            {
                MoexWebSocketLogMessages.ConnectRejected(
                    _logger, DiagnosticsLogSources.WebSocket, _options.Url, handshakeFrame.Command);

                bool isError = string.Equals(
                    handshakeFrame.Command, "ERROR", StringComparison.Ordinal);

                return FailedHandshake(
                    isError ? "connect_rejected" : "unexpected_handshake_frame",
                    startedAt, socket, handshake) with
                {
                    RawErrorFrame = isError ? handshakeRaw : null,
                    RawUnexpectedHandshakeFrame = isError ? null : handshakeRaw,
                };
            }

            MoexWebSocketLogMessages.Connected(
                _logger, DiagnosticsLogSources.WebSocket, _options.Url,
                Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);

            string subscriptionId = Guid.NewGuid().ToString("N");

            using (CancellationTokenSource subscribeCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                subscribeCts.CancelAfter(OperationTimeout);

                await SendFrameAsync(
                    socket,
                    BuildSubscribeFrame(subscriptionId, destination, selector),
                    subscribeCts.Token);
            }

            MoexWebSocketLogMessages.Subscribed(
                _logger, DiagnosticsLogSources.WebSocket, destination, selector);

            return await CollectAsync(
                socket,
                handshakeRaw,
                handshake.Bytes,
                handshake.MessageType?.ToString(),
                socket.SubProtocol,
                destination,
                effective,
                startedAt,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            MoexWebSocketLogMessages.Failed(
                _logger, DiagnosticsLogSources.WebSocket, _options.Url,
                exception.GetType().Name, exception.Message);
            throw;
        }
        finally
        {
            await TryCloseAsync(socket);
        }
    }

    private async Task<WebSocketProbeReport> CollectAsync(
        ClientWebSocket socket,
        string rawConnectedFrame,
        long handshakeBytes,
        string? handshakeMessageType,
        string? negotiatedSubProtocol,
        string destination,
        TimeSpan duration,
        long startedAt,
        CancellationToken cancellationToken)
    {
        List<string> rawFrames = new List<string>();
        string? rawReceipt = null;
        string? receiptId = null;
        string? rawError = null;
        long capturedBytes = 0;
        int unparsedFrames = 0;
        int binaryMessages = 0;
        int heartbeats = 0;
        string? unreadablePreview = null;
        bool truncated = false;
        string terminationReason = "duration_elapsed";
        string? closeStatus = null;
        string? closeDescription = null;

        using CancellationTokenSource collectCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        collectCts.CancelAfter(duration);

        try
        {
            while (true)
            {
                long remainingBytes = _options.MaxCapturedBytes - capturedBytes;
                ReceiveOutcome outcome =
                    await ReceiveMessageAsync(socket, remainingBytes, collectCts.Token);

                capturedBytes += outcome.Bytes;

                if (outcome.MessageType == WebSocketMessageType.Binary
                    && outcome.Kind != ReceiveKind.ByteLimitExceeded)
                {
                    binaryMessages++;
                }

                if (outcome.Kind == ReceiveKind.Closed)
                {
                    terminationReason = "websocket_closed";
                    closeStatus = socket.CloseStatus?.ToString();
                    closeDescription = socket.CloseStatusDescription;
                    break;
                }

                if (outcome.Kind == ReceiveKind.InvalidUtf8)
                {
                    unreadablePreview = outcome.UnreadablePreview;
                    terminationReason = "invalid_utf8";
                    break;
                }

                if (outcome.Kind == ReceiveKind.ByteLimitExceeded)
                {
                    truncated = true;
                    terminationReason = "byte_limit";
                    break;
                }

                string raw = outcome.Text!;
                if (StompFrame.IsHeartbeat(raw))
                {
                    heartbeats++;
                    continue;
                }

                rawFrames.Add(raw);
                MoexWebSocketLogMessages.FrameReceived(
                    _logger, DiagnosticsLogSources.WebSocket, PeekCommand(raw), outcome.Bytes);

                StompFrame frame;
                try
                {
                    frame = StompFrame.Parse(raw);
                }
                catch (FormatException)
                {
                    unparsedFrames++;

                    if (rawFrames.Count >= _options.MaxFrames)
                    {
                        truncated = true;
                        terminationReason = "frame_limit";
                        break;
                    }

                    continue;
                }

                if (string.Equals(frame.Command, "RECEIPT", StringComparison.Ordinal))
                {
                    rawReceipt = raw;
                    frame.Headers.TryGetValue("receipt-id", out receiptId);
                }
                else if (string.Equals(frame.Command, "ERROR", StringComparison.Ordinal))
                {
                    rawError = raw;
                    terminationReason = "stomp_error";
                    break;
                }

                if (rawFrames.Count >= _options.MaxFrames)
                {
                    truncated = true;
                    terminationReason = "frame_limit";
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            terminationReason = "duration_elapsed";
        }

        TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

        MoexWebSocketLogMessages.ProbeCompleted(
            _logger, DiagnosticsLogSources.WebSocket, destination,
            rawFrames.Count, capturedBytes, truncated, elapsed.TotalMilliseconds);

        return new WebSocketProbeReport
        {
            Connected = true,
            RawConnectedFrame = rawConnectedFrame,
            RawReceiptFrame = rawReceipt,
            ReceiptId = receiptId,
            RawErrorFrame = rawError,
            RawFrames = rawFrames,
            FramesReceived = rawFrames.Count,
            UnparsedFrames = unparsedFrames,
            HandshakeBytes = handshakeBytes,
            CapturedBytes = capturedBytes,
            HandshakeMessageType = handshakeMessageType,
            NegotiatedSubProtocol = negotiatedSubProtocol,
            BinaryMessagesReceived = binaryMessages,
            HeartbeatsReceived = heartbeats,
            UnreadableContentPreview = unreadablePreview,
            Truncated = truncated,
            TerminationReason = terminationReason,
            CloseStatus = closeStatus,
            CloseDescription = closeDescription,
            Elapsed = elapsed,
        };
    }

    private enum ReceiveKind
    {
        /// <summary>Сообщение получено и прочитано как UTF-8. Двоичный кадр — тоже сюда.</summary>
        Text,
        Closed,

        /// <summary>Содержимое не читается как UTF-8.</summary>
        InvalidUtf8,

        ByteLimitExceeded,
    }

    private readonly record struct ReceiveOutcome(
        ReceiveKind Kind,
        string? Text,
        long Bytes,
        WebSocketMessageType? MessageType,
        string? UnreadablePreview);

    private static async Task<ReceiveOutcome> ReceiveMessageAsync(
        ClientWebSocket socket,
        long remainingBytes,
        CancellationToken cancellationToken)
    {
        using MemoryStream buffer = new MemoryStream(ReceiveChunkSize);
        byte[] chunk = new byte[ReceiveChunkSize];
        long total = 0;
        WebSocketMessageType? messageType = null;

        while (true)
        {
            ValueWebSocketReceiveResult result =
                await socket.ReceiveAsync(chunk.AsMemory(), cancellationToken);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                return new ReceiveOutcome(
                    ReceiveKind.Closed, null, total, WebSocketMessageType.Close, null);
            }

            messageType ??= result.MessageType;
            total += result.Count;

            if (total > remainingBytes)
            {
                return new ReceiveOutcome(
                    ReceiveKind.ByteLimitExceeded, null, total, messageType, null);
            }

            buffer.Write(chunk, 0, result.Count);

            if (result.EndOfMessage)
            {
                break;
            }
        }

        try
        {
            string text = StrictUtf8.GetString(
                buffer.GetBuffer(), 0, checked((int)buffer.Length));

            return new ReceiveOutcome(ReceiveKind.Text, text, total, messageType, null);
        }
        catch (DecoderFallbackException)
        {
            string preview = Convert.ToHexString(
                buffer.GetBuffer(),
                0,
                (int)Math.Min(buffer.Length, UnreadablePreviewBytes));

            return new ReceiveOutcome(
                ReceiveKind.InvalidUtf8, null, total, messageType, preview);
        }
    }

    private static WebSocketProbeReport FailedHandshake(
        string reason,
        long startedAt,
        ClientWebSocket socket,
        ReceiveOutcome outcome) =>
        new WebSocketProbeReport
        {
            Connected = false,
            TerminationReason = reason,
            HandshakeBytes = outcome.Bytes,
            HandshakeMessageType = outcome.MessageType?.ToString(),
            UnreadableContentPreview = outcome.UnreadablePreview,
            NegotiatedSubProtocol = socket.SubProtocol,
            Elapsed = Stopwatch.GetElapsedTime(startedAt),
        };

    private static void EnsureSafeHeaderValue(string value, string parameterName)
    {
        if (value.IndexOfAny(['\r', '\n', '\0']) >= 0)
        {
            throw new ArgumentException(
                $"{parameterName} содержит недопустимый управляющий символ.",
                parameterName);
        }
    }

    private StompFrame BuildConnectFrame()
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["domain"] = _options.Domain,
            ["login"] = _options.Login,
            ["passcode"] = _options.Passcode,
        };

        return new StompFrame("CONNECT", headers, string.Empty);
    }

    private static StompFrame BuildSubscribeFrame(string id, string destination, string selector)
    {
        Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["id"] = id,
            ["destination"] = destination,
            ["selector"] = selector,
            ["receipt"] = id,
        };

        return new StompFrame("SUBSCRIBE", headers, string.Empty);
    }

    private static async Task SendFrameAsync(
        ClientWebSocket socket,
        StompFrame frame,
        CancellationToken cancellationToken)
    {
        byte[] bytes = StompFrame.Serialize(frame);
        await socket.SendAsync(
            bytes.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
    }

    private async Task TryCloseAsync(ClientWebSocket socket)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            using CancellationTokenSource closeCts = new CancellationTokenSource(CloseTimeout);

            await SendFrameAsync(
                socket,
                new StompFrame("DISCONNECT", new Dictionary<string, string>(), string.Empty),
                closeCts.Token);

            await socket.CloseAsync(
                WebSocketCloseStatus.NormalClosure, "probe finished", closeCts.Token);
        }
        catch (Exception exception)
        {
            MoexWebSocketLogMessages.Failed(
                _logger, DiagnosticsLogSources.WebSocket, _options.Url,
                exception.GetType().Name, "Ошибка при закрытии соединения, подавлена.");
        }
    }

    private static string PeekCommand(string raw)
    {
        int lineEnd = raw.IndexOf('\n');
        return lineEnd > 0 ? raw[..lineEnd].TrimEnd('\r') : "UNKNOWN";
    }

    private TimeSpan ClampDuration(TimeSpan requested)
    {
        if (requested < MinimumDuration)
        {
            return MinimumDuration;
        }

        return requested > _options.MaxDuration
            ? _options.MaxDuration
            : requested;
    }

    private void EnsureCredentialsConfigured()
    {
        if (string.IsNullOrWhiteSpace(_options.Login)
            || string.IsNullOrWhiteSpace(_options.Passcode))
        {
            throw new InvalidOperationException(
                "Diagnostics:WebSocketProbe:Login и Diagnostics:WebSocketProbe:Passcode не заданы. " +
                "Положите их в пользовательские секреты, как Moex:AlgKey.");
        }
    }
}
