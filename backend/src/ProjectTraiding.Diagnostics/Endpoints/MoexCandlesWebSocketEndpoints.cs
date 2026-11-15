using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectTraiding.Diagnostics.Protocol;

namespace ProjectTraiding.Diagnostics.Endpoints;

public static partial class MoexCandlesWebSocketEndpoints
{
    private const string WebSocketUrl = "wss://iss.moex.com/infocx/v3/websocket";
    private const string SubscriptionId = "sub-1";
    private const string SubscriptionDestination = "MXSE.candles";
    private const string SubscriptionSelector = "ticker=\"MXSE.TQBR.SBER\" and interval=\"M1\"";

    private const string CandlesPage = """
        <!doctype html>
        <html lang="ru">
        <head>
          <meta charset="utf-8">
          <meta name="viewport" content="width=device-width, initial-scale=1">
          <title>MOEX ISS+ · SBER M1</title>
          <style>
            :root { color-scheme: dark; font: 15px/1.5 system-ui, sans-serif; background: #10151d; color: #e8edf4; }
            body { max-width: 1100px; margin: 0 auto; padding: 28px 18px 60px; }
            h1 { margin: 0 0 4px; font-size: 1.5rem; }
            p { color: #aab5c4; margin: 4px 0 20px; }
            .status { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 10px; margin: 18px 0 24px; }
            .status div, article { border: 1px solid #303b4a; border-radius: 8px; background: #171f2a; padding: 12px 14px; }
            .label { display: block; color: #93a2b6; font-size: .78rem; text-transform: uppercase; letter-spacing: .06em; }
            .value { overflow-wrap: anywhere; }
            #events { display: grid; gap: 10px; }
            article header { display: flex; gap: 12px; align-items: baseline; justify-content: space-between; flex-wrap: wrap; }
            article time { color: #93a2b6; font-size: .85rem; }
            article h2 { display: inline; margin: 0; font: 600 1rem ui-monospace, monospace; }
            article .details { margin: 8px 0; color: #d1dae5; white-space: pre-wrap; overflow-wrap: anywhere; }
            pre { margin: 8px 0 0; padding: 12px; border-radius: 6px; background: #0d1218; white-space: pre-wrap; overflow-wrap: anywhere; }
            .error { border-color: #9d4141; }
          </style>
        </head>
        <body>
          <h1>MOEX ISS+ · SBER · минутные свечи</h1>
          <p>Диагностический поток MXSE.candles (M1). Получение MESSAGE само по себе не подтверждает завершение свечи.</p>
          <section class="status" aria-live="polite">
            <div><span class="label">WebSocket</span><span class="value" id="connection">Подключение…</span></div>
            <div><span class="label">STOMP CONNECTED</span><span class="value" id="authenticated">Ожидание</span></div>
            <div><span class="label">Подписка / RECEIPT</span><span class="value" id="subscription">Ожидание CONNECTED</span></div>
            <div><span class="label">Последнее сообщение MOEX</span><span class="value" id="last-received">—</span></div>
          </section>
          <main id="events" aria-live="polite"></main>
          <script>
            const source = new EventSource(`${location.pathname}/events`);
            const connection = document.getElementById('connection');
            const authenticated = document.getElementById('authenticated');
            const subscription = document.getElementById('subscription');
            const lastReceived = document.getElementById('last-received');
            const events = document.getElementById('events');

            function addEvent(item) {
              const article = document.createElement('article');
              if (item.kind === 'error' || item.kind === 'moex_error') article.classList.add('error');
              const header = document.createElement('header');
              const title = document.createElement('h2');
              title.textContent = item.command || item.kind;
              const time = document.createElement('time');
              time.textContent = item.receivedAt ? new Date(item.receivedAt).toLocaleString() : '';
              header.append(title, time);
              article.append(header);
              if (item.details) {
                const details = document.createElement('div');
                details.className = 'details';
                details.textContent = item.details;
                article.append(details);
              }
              if (item.headers?.length) {
                const headers = document.createElement('pre');
                headers.textContent = 'Заголовки:\n' + item.headers.join('\n');
                article.append(headers);
              }
              if (item.body) {
                const body = document.createElement('pre');
                body.textContent = item.body;
                article.append(body);
              }
              events.prepend(article);
            }

            source.addEventListener('diagnostic', event => {
              const item = JSON.parse(event.data);
              if (item.receivedAt) lastReceived.textContent = new Date(item.receivedAt).toLocaleString();
              switch (item.kind) {
                case 'connecting': connection.textContent = 'Соединение с MOEX…'; break;
                case 'websocket_connected': connection.textContent = 'WebSocket установлен'; break;
                case 'connect_sent': connection.textContent = 'WebSocket установлен; ожидается STOMP CONNECTED'; break;
                case 'stomp_connected': authenticated.textContent = 'Получен'; subscription.textContent = 'SUBSCRIBE отправляется'; break;
                case 'subscribe_sent': subscription.textContent = 'SUBSCRIBE отправлен; ожидается RECEIPT'; break;
                case 'receipt':
                  if (item.details?.includes('receipt-id:sub-1')) subscription.textContent = 'Получен RECEIPT для sub-1';
                  break;
                case 'message':
                  if (subscription.textContent.startsWith('SUBSCRIBE')) subscription.textContent = 'Получен MESSAGE; RECEIPT ожидается отдельно';
                  break;
                case 'moex_error': subscription.textContent = 'MOEX вернул ERROR'; break;
                case 'connection_closed': connection.textContent = 'Соединение закрыто'; source.close(); break;
                case 'error': connection.textContent = 'Ошибка; поток завершён'; source.close(); break;
                case 'closed': connection.textContent = 'Диагностическое соединение закрыто'; source.close(); break;
              }
              addEvent(item);
            });

            source.onerror = () => {
              if (connection.textContent !== 'Соединение закрыто' && connection.textContent !== 'Диагностическое соединение закрыто') {
                connection.textContent = 'Поток событий завершён';
              }
              source.close();
            };
          </script>
        </body>
        </html>
        """;

    public static IEndpointRouteBuilder MapMoexCandlesWebSocketEndpoints(this IEndpointRouteBuilder routes)
    {
        RouteGroupBuilder diagnosticsGroup = routes.MapGroup("/moex/ws/candles/SBER");

        diagnosticsGroup.MapGet("", () => Results.Content(CandlesPage, "text/html; charset=utf-8"));
        diagnosticsGroup.MapGet("/events", StreamCandlesEventsAsync);

        return routes;
    }

    private static async Task StreamCandlesEventsAsync(
        HttpContext context,
        ILogger<MoexCandlesDiagnosticLoggerCategory> logger,
        CancellationToken cancellationToken)
    {
        HttpResponse response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream; charset=utf-8";
        response.Headers["Cache-Control"] = "no-cache, no-transform";
        response.Headers.Append("X-Accel-Buffering", "no");

        await response.StartAsync(cancellationToken);

        using ClientWebSocket webSocket = new ClientWebSocket();
        webSocket.Options.AddSubProtocol("STOMP");

        MoexStompSessionState sessionState = new MoexStompSessionState();

        try
        {
            await WriteEventAsync(context, new MoexCandlesSseEvent("connecting", DateTimeOffset.UtcNow, Details: WebSocketUrl), cancellationToken);
            await webSocket.ConnectAsync(new Uri(WebSocketUrl), cancellationToken);
            await WriteEventAsync(context, new MoexCandlesSseEvent("websocket_connected", DateTimeOffset.UtcNow), cancellationToken);

            await SendFrameAsync(
                webSocket,
                "CONNECT",
                ["domain:DEMO", "login:guest", "passcode:guest"],
                cancellationToken);
            await WriteEventAsync(context, new MoexCandlesSseEvent("connect_sent", DateTimeOffset.UtcNow, Command: "CONNECT"), cancellationToken);

            MoexStompFrameReader frameReader = new MoexStompFrameReader();
            byte[] receiveBuffer = new byte[8192];
            while (!cancellationToken.IsCancellationRequested)
            {
                if (frameReader.TryReadFrame(out MoexStompFrame frame))
                {
                    await HandleFrameAsync(context, webSocket, frame, cancellationToken, logger, sessionState);

                    if (frame.Command == "ERROR" && !sessionState.StompConnected)
                    {
                        break;
                    }

                    continue;
                }

                ValueWebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(receiveBuffer.AsMemory(), cancellationToken);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    string closeDetails = $"MOEX закрыл WebSocket: {webSocket.CloseStatus} {webSocket.CloseStatusDescription}".Trim();
                    await WriteEventAsync(context, new MoexCandlesSseEvent("connection_closed", DateTimeOffset.UtcNow, Details: closeDetails), cancellationToken);
                    break;
                }

                if (receiveResult.MessageType != WebSocketMessageType.Text)
                {
                    throw new InvalidDataException($"Ожидался текстовый WebSocket-кадр, получен {receiveResult.MessageType}.");
                }

                frameReader.Append(receiveBuffer.AsSpan(0, receiveResult.Count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Браузер закрыл поток или запрос остановлен хостом; соединение будет закрыто в finally.
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MOEX SBER M1 WebSocket diagnostic failed.");
            try
            {
                await WriteEventAsync(context, new MoexCandlesSseEvent("error", DateTimeOffset.UtcNow, Details: exception.Message), cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Клиент SSE уже отключён.
            }
            catch (IOException)
            {
                // Клиент SSE уже отключён.
            }
        }
        finally
        {
            await CloseMoexConnectionAsync(webSocket, sessionState, logger);
            if (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await WriteEventAsync(context, new MoexCandlesSseEvent("closed", DateTimeOffset.UtcNow), cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    // Клиент SSE отключился во время завершения.
                }
                catch (IOException)
                {
                    // Клиент SSE отключился во время завершения.
                }
            }
        }
    }

    private static async Task HandleFrameAsync(
        HttpContext context,
        ClientWebSocket webSocket,
        MoexStompFrame frame,
        CancellationToken cancellationToken,
        ILogger logger,
        MoexStompSessionState sessionState)
    {
        DateTimeOffset receivedAt = DateTimeOffset.UtcNow;
        string eventKind = frame.Command switch
        {
            "CONNECTED" => "stomp_connected",
            "RECEIPT" => "receipt",
            "MESSAGE" => "message",
            "ERROR" => "moex_error",
            _ => "frame"
        };

        await WriteEventAsync(
            context,
            new MoexCandlesSseEvent(
                eventKind,
                receivedAt,
                frame.Command,
                frame.Headers.ToArray(),
                frame.Body,
                frame.Command == "RECEIPT" ? $"receipt-id:{frame.GetHeader("receipt-id")}" : null),
            cancellationToken);

        if (frame.Command == "ERROR")
        {
            logger.LogWarning("MOEX returned STOMP ERROR: {Headers}; {Body}", string.Join(" | ", frame.Headers), frame.Body);
            return;
        }

        if (frame.Command != "CONNECTED")
        {
            return;
        }

        sessionState.StompConnected = true;
        string[] subscribeHeaders =
        [
            $"id:{SubscriptionId}",
            $"receipt:{SubscriptionId}",
            $"destination:{SubscriptionDestination}",
            $"selector:{SubscriptionSelector}"
        ];

        await SendFrameAsync(webSocket, "SUBSCRIBE", subscribeHeaders, cancellationToken);
        sessionState.SubscriptionSent = true;
        await WriteEventAsync(
            context,
            new MoexCandlesSseEvent(
                "subscribe_sent",
                DateTimeOffset.UtcNow,
                "SUBSCRIBE",
                subscribeHeaders),
            cancellationToken);
    }

    private static async Task SendFrameAsync(
        ClientWebSocket webSocket,
        string command,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken)
    {
        StringBuilder frame = new StringBuilder(command.Length + headers.Count * 40 + 8);
        frame.Append(command).Append('\n');
        for (int index = 0; index < headers.Count; index++)
        {
            frame.Append(headers[index]).Append('\n');
        }

        frame.Append('\n').Append('\0');
        byte[] payload = Encoding.UTF8.GetBytes(frame.ToString());
        await webSocket.SendAsync(payload.AsMemory(), WebSocketMessageType.Text, true, cancellationToken);
    }

    private static async Task CloseMoexConnectionAsync(
        ClientWebSocket webSocket,
        MoexStompSessionState sessionState,
        ILogger logger)
    {
        if (webSocket.State is WebSocketState.None or WebSocketState.Closed or WebSocketState.Aborted)
        {
            return;
        }

        using CancellationTokenSource cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        CancellationToken cleanupToken = cleanupTimeout.Token;

        if (webSocket.State == WebSocketState.Open)
        {
            if (sessionState.StompConnected && sessionState.SubscriptionSent)
            {
                await TrySendCleanupFrameAsync(webSocket, "UNSUBSCRIBE", [$"id:{SubscriptionId}"], cleanupToken, logger);
            }

            if (sessionState.StompConnected && webSocket.State == WebSocketState.Open)
            {
                await TrySendCleanupFrameAsync(webSocket, "DISCONNECT", Array.Empty<string>(), cleanupToken, logger);
            }

            if (webSocket.State == WebSocketState.Open)
            {
                try
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Diagnostic stream ended", cleanupToken);
                }
                catch (Exception exception)
                {
                    logger.LogDebug(exception, "MOEX WebSocket close handshake did not complete.");
                    webSocket.Abort();
                }
            }
        }
        else if (webSocket.State == WebSocketState.CloseReceived)
        {
            try
            {
                await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Diagnostic stream ended", cleanupToken);
            }
            catch (Exception exception)
            {
                logger.LogDebug(exception, "MOEX WebSocket close response could not be sent.");
                webSocket.Abort();
            }
        }

        if (IsConnected(webSocket.State))
        {
            webSocket.Abort();
        }

        static bool IsConnected(WebSocketState state) =>
            state is WebSocketState.Connecting or WebSocketState.Open or WebSocketState.CloseSent or WebSocketState.CloseReceived;
    }

    private static async Task TrySendCleanupFrameAsync(
        ClientWebSocket webSocket,
        string command,
        IReadOnlyList<string> headers,
        CancellationToken cancellationToken,
        ILogger logger)
    {
        try
        {
            await SendFrameAsync(webSocket, command, headers, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Could not send STOMP {Command} during cleanup.", command);
        }
    }

    private static async Task WriteEventAsync(
        HttpContext context,
        MoexCandlesSseEvent diagnosticEvent,
        CancellationToken cancellationToken)
    {
        string json = JsonSerializer.Serialize(diagnosticEvent, MoexCandlesSseJsonContext.Default.MoexCandlesSseEvent);
        await context.Response.WriteAsync("event: diagnostic\ndata: ", cancellationToken);
        await context.Response.WriteAsync(json, cancellationToken);
        await context.Response.WriteAsync("\n\n", cancellationToken);
        await context.Response.Body.FlushAsync(cancellationToken);
    }

    internal sealed record MoexCandlesSseEvent(
        string Kind,
        DateTimeOffset? ReceivedAt = null,
        string? Command = null,
        string[]? Headers = null,
        string? Body = null,
        string? Details = null);

    private sealed class MoexStompSessionState
    {
        public bool StompConnected { get; set; }
        public bool SubscriptionSent { get; set; }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
    [JsonSerializable(typeof(MoexCandlesSseEvent))]
    internal sealed partial class MoexCandlesSseJsonContext : JsonSerializerContext
    {
    }
}

internal sealed class MoexCandlesDiagnosticLoggerCategory
{
}
