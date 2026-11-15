using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectTraiding.Diagnostics.Protocol;

namespace ProjectTraiding.Diagnostics.Endpoints;

public static partial class MoexCandlesWebSocketEndpoints
{
    private static readonly TimeSpan WebSocketConnectTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StompConnectedTimeout = TimeSpan.FromSeconds(15);
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
            .table-wrap { max-width: 100%; overflow-x: auto; }
            table.candles { border-collapse: collapse; width: 100%; font-size: .82rem; }
            table.candles th, table.candles td { padding: 3px 6px; white-space: nowrap; text-align: right; }
            table.candles th:first-child, table.candles td:first-child,
            table.candles th:nth-child(2), table.candles td:nth-child(2),
            table.candles th:nth-child(3), table.candles td:nth-child(3) { text-align: left; }
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
            <div><span class="label">Авторизация ISS+</span><span class="value" id="auth-mode">—</span></div>
            <div><span class="label">С последнего MESSAGE</span><span class="value" id="message-age">Ожидание сообщения</span></div>
          </section>
          <section aria-live="polite">
            <h2>Последняя свеча из сообщения</h2>
            <p id="candle-message-info">Тип: — · Получено: — · seqnum: —</p>
            <div class="table-wrap">
              <table class="candles" id="latest-candle">
                <thead><tr><th>Дата</th><th>FROM</th><th>TILL</th><th>OPEN</th><th>HIGH</th><th>LOW</th><th>CLOSE</th><th>VOLUME</th></tr></thead>
                <tbody><tr><td>—</td><td>—</td><td>—</td><td>—</td><td>—</td><td>—</td><td>—</td><td>—</td></tr></tbody>
              </table>
            </div>
            <p>Завершённость свечи протоколом здесь не определяется.</p>
            <h2 id="candle-table-title">Последние свечи</h2>
            <div class="table-wrap"><table class="candles">
              <thead><tr><th>Дата</th><th>FROM</th><th>TILL</th><th>OPEN</th><th>HIGH</th><th>LOW</th><th>CLOSE</th><th>VOLUME</th></tr></thead>
              <tbody id="candles-body"><tr><td colspan="8">Ожидание MESSAGE</td></tr></tbody>
            </table></div>
          </section>
          <main id="events" aria-live="polite"></main>
          <script>
            const source = new EventSource(`${location.pathname}/events`);
            const connection = document.getElementById('connection');
            const authenticated = document.getElementById('authenticated');
            const subscription = document.getElementById('subscription');
            const lastReceived = document.getElementById('last-received');
            const events = document.getElementById('events');
            const candleMessageInfo = document.getElementById('candle-message-info');
            const latestCandle = document.querySelector('#latest-candle tbody');
            const candleTableTitle = document.getElementById('candle-table-title');
            const candlesBody = document.getElementById('candles-body');
            const authMode = document.getElementById('auth-mode');
            const messageAge = document.getElementById('message-age');
            let lastMessageAt = null;
            let disconnected = false;

            function formatElapsed(seconds) {
              const minutes = Math.floor(seconds / 60);
              const remainder = seconds % 60;
              return String(minutes).padStart(2, '0') + ':' + String(remainder).padStart(2, '0');
            }

            setInterval(() => {
              if (lastMessageAt === null) {
                messageAge.textContent = disconnected ? 'Отключено · ожидание сообщения' : 'Ожидание сообщения';
                return;
              }
              const elapsed = Math.max(0, Math.floor((Date.now() - lastMessageAt) / 1000));
              messageAge.textContent = formatElapsed(elapsed) + (disconnected ? ' · Отключено' : '');
            }, 1000);

            function formatValue(value) {
              if (value === undefined) return '—';
              if (value === null) return 'null';
              if (Array.isArray(value)) return '[' + value.map(formatValue).join(', ') + ']';
              if (typeof value === 'object') return JSON.stringify(value);
              return String(value);
            }

            function displayDate(value) {
              if (typeof value !== 'string') return formatValue(value);
              const separator = value.search(/[T ]/);
              return separator < 0 ? value : value.slice(0, separator);
            }

            function mapRows(message) {
              if (!Array.isArray(message.columns) || !Array.isArray(message.data)) return [];
              const records = [];
              for (const row of message.data) {
                if (!Array.isArray(row)) continue;
                const record = {};
                for (let index = 0; index < message.columns.length; index++) {
                  record[message.columns[index]] = row[index];
                }
                records.push(record);
              }
              return records;
            }

            function compareFromDescending(left, right) {
              const leftValue = formatValue(left.FROM);
              const rightValue = formatValue(right.FROM);
              const leftTime = Date.parse(leftValue);
              const rightTime = Date.parse(rightValue);
              if (Number.isFinite(leftTime) && Number.isFinite(rightTime)) return rightTime - leftTime;
              return rightValue.localeCompare(leftValue);
            }

            function setLatestCandle(record) {
              const values = [
                displayDate(record.FROM), formatValue(record.FROM), formatValue(record.TILL),
                formatValue(record.OPEN), formatValue(record.HIGH), formatValue(record.LOW),
                formatValue(record.CLOSE), formatValue(record.VOLUME)
              ];
              const row = document.createElement('tr');
              for (const value of values) {
                const cell = document.createElement('td');
                cell.textContent = value;
                row.append(cell);
              }
              latestCandle.replaceChildren(row);
            }

            function clearLatestCandle() {
              const row = document.createElement('tr');
              for (let index = 0; index < 8; index++) {
                const cell = document.createElement('td');
                cell.textContent = '—';
                row.append(cell);
              }
              latestCandle.replaceChildren(row);
            }

            function renderCandleMessage(item) {
              let message;
              try {
                message = JSON.parse(item.body);
              } catch {
                candleMessageInfo.textContent = 'Не удалось разобрать JSON сообщения; полный STOMP кадр доступен ниже.';
                return;
              }

              const properties = message.properties && typeof message.properties === 'object' ? message.properties : {};
              const messageType = formatValue(properties.type ?? 'не указан');
              const seqnum = Object.prototype.hasOwnProperty.call(properties, 'seqnum')
                ? formatValue(properties.seqnum)
                : '—';
              const received = item.receivedAt ? new Date(item.receivedAt).toLocaleString() : '—';
              candleMessageInfo.textContent = `Тип: ${messageType} · Получено: ${received} · seqnum: ${seqnum}`;

              const records = mapRows(message).sort(compareFromDescending);
              const visibleRecords = records.slice(0, 10);
              const tableFragment = document.createDocumentFragment();
              for (const record of visibleRecords) {
                const row = document.createElement('tr');
                const values = [
                  displayDate(record.FROM), record.FROM, record.TILL, record.OPEN,
                  record.HIGH, record.LOW, record.CLOSE, record.VOLUME
                ];
                for (const value of values) {
                  const cell = document.createElement('td');
                  cell.textContent = formatValue(value);
                  row.append(cell);
                }
                tableFragment.append(row);
              }
              candlesBody.replaceChildren(tableFragment);
              candleTableTitle.textContent = messageType === 'updates'
                ? 'Изменённые записи в последнем updates (до 10)'
                : messageType === 'removal'
                  ? 'Удалённые записи в последнем removal (до 10)'
                  : messageType === 'snapshot'
                    ? 'Последние свечи snapshot (до 10)'
                    : 'Строки последнего сообщения (до 10)';

              if (visibleRecords.length === 0) {
                const row = document.createElement('tr');
                const cell = document.createElement('td');
                cell.colSpan = 8;
                cell.textContent = 'В сообщении нет строк данных.';
                row.append(cell);
                candlesBody.append(row);
              }

              if (messageType === 'removal' || visibleRecords.length === 0) {
                clearLatestCandle();
              } else if ((messageType === 'snapshot' || messageType === 'updates') && visibleRecords.length > 0) {
                setLatestCandle(visibleRecords[0]);
              }
            }

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
              if (item.headers?.length || item.body) {
                const rawFrame = document.createElement('details');
                const summary = document.createElement('summary');
                summary.textContent = 'Исходные заголовки и тело STOMP';
                rawFrame.append(summary);
                if (item.headers?.length) {
                  const headers = document.createElement('pre');
                  headers.textContent = 'Заголовки:\n' + item.headers.join('\n');
                  rawFrame.append(headers);
                }
                if (item.body) {
                  const body = document.createElement('pre');
                  body.textContent = item.body;
                  rawFrame.append(body);
                }
                article.append(rawFrame);
              }
              events.prepend(article);
            }

            source.addEventListener('diagnostic', event => {
              const item = JSON.parse(event.data);
              if (item.receivedAt) lastReceived.textContent = new Date(item.receivedAt).toLocaleString();
              if (item.kind === 'message') {
                lastMessageAt = Date.now();
                messageAge.textContent = '00:00' + (disconnected ? ' · Отключено' : '');
                renderCandleMessage(item);
              }
              switch (item.kind) {
                case 'connecting': connection.textContent = 'Соединение с MOEX…'; break;
                case 'websocket_connected': connection.textContent = 'WebSocket установлен'; break;
                case 'connect_sent': connection.textContent = 'WebSocket установлен; ожидается STOMP CONNECTED'; break;
                case 'auth_mode': authMode.textContent = item.details || '—'; break;
                case 'stomp_connected':
                  disconnected = false;
                  connection.textContent = 'Подключено';
                  authenticated.textContent = 'Получен';
                  subscription.textContent = 'SUBSCRIBE отправляется';
                  break;
                case 'subscribe_sent': subscription.textContent = 'SUBSCRIBE отправлен; ожидается RECEIPT'; break;
                case 'receipt':
                  if (item.details?.includes('receipt-id:sub-1')) subscription.textContent = 'Получен RECEIPT для sub-1';
                  break;
                case 'message':
                  if (subscription.textContent.startsWith('SUBSCRIBE')) subscription.textContent = 'Получен MESSAGE; RECEIPT ожидается отдельно';
                  break;
                case 'moex_error':
                  disconnected = true;
                  connection.textContent = 'Ошибка MOEX (STOMP ERROR)';
                  subscription.textContent = 'MOEX вернул ERROR';
                  break;
                case 'connection_closed': disconnected = true; connection.textContent = 'Отключено'; source.close(); break;
                case 'error': disconnected = true; connection.textContent = 'Ошибка; отключено'; source.close(); break;
                case 'closed': disconnected = true; connection.textContent = 'Отключено'; source.close(); break;
              }
              addEvent(item);
            });

            source.onerror = () => {
              disconnected = true;
              if (connection.textContent !== 'Соединение закрыто' && connection.textContent !== 'Диагностическое соединение закрыто') {
                connection.textContent = 'Отключено (поток событий завершён)';
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
        IConfiguration configuration,
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

        string? login = configuration["MoexWebSocket:Login"];
        string? passcode = configuration["MoexWebSocket:Passcode"];
        bool usePassport = !string.IsNullOrWhiteSpace(login) && !string.IsNullOrWhiteSpace(passcode);
        string authDomain = usePassport ? "passport" : "DEMO";
        string authLogin = usePassport ? login! : "guest";
        string authPasscode = usePassport ? passcode! : "guest";

        MoexStompSessionState sessionState = new MoexStompSessionState();
        using CancellationTokenSource connectTimeout = new CancellationTokenSource(WebSocketConnectTimeout);
        using CancellationTokenSource connectCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            connectTimeout.Token);
        using CancellationTokenSource stompConnectedTimeout = new CancellationTokenSource();
        using CancellationTokenSource stompConnectedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            stompConnectedTimeout.Token);

        try
        {
            await WriteEventAsync(
                context,
                new MoexCandlesSseEvent("auth_mode", DateTimeOffset.UtcNow, Details: usePassport ? "Passport" : "DEMO"),
                cancellationToken);
            await WriteEventAsync(context, new MoexCandlesSseEvent("connecting", DateTimeOffset.UtcNow, Details: WebSocketUrl), cancellationToken);
            await webSocket.ConnectAsync(new Uri(WebSocketUrl), connectCancellation.Token);
            connectTimeout.CancelAfter(Timeout.InfiniteTimeSpan);
            await WriteEventAsync(context, new MoexCandlesSseEvent("websocket_connected", DateTimeOffset.UtcNow), cancellationToken);

            await SendFrameAsync(
                webSocket,
                "CONNECT",
                [$"domain:{authDomain}", $"login:{authLogin}", $"passcode:{authPasscode}"],
                cancellationToken);
            stompConnectedTimeout.CancelAfter(StompConnectedTimeout);
            await WriteEventAsync(context, new MoexCandlesSseEvent("connect_sent", DateTimeOffset.UtcNow, Command: "CONNECT"), cancellationToken);

            MoexStompFrameReader frameReader = new MoexStompFrameReader();
            byte[] receiveBuffer = new byte[8192];
            while (!cancellationToken.IsCancellationRequested)
            {
                if (frameReader.TryReadFrame(out MoexStompFrame frame))
                {
                    if (frame.Command == "CONNECTED")
                    {
                        stompConnectedTimeout.Cancel();
                    }

                    await HandleFrameAsync(context, webSocket, frame, cancellationToken, logger, sessionState);

                    if (frame.Command == "ERROR")
                    {
                        break;
                    }

                    continue;
                }

                CancellationToken receiveToken = sessionState.StompConnected
                    ? cancellationToken
                    : stompConnectedCancellation.Token;
                ValueWebSocketReceiveResult receiveResult = await webSocket.ReceiveAsync(receiveBuffer.AsMemory(), receiveToken);
                if (receiveResult.MessageType == WebSocketMessageType.Close)
                {
                    string closeDetails = $"MOEX закрыл WebSocket: {webSocket.CloseStatus} {webSocket.CloseStatusDescription}".Trim();
                    await WriteEventAsync(context, new MoexCandlesSseEvent("connection_closed", DateTimeOffset.UtcNow, Details: closeDetails), cancellationToken);
                    break;
                }

                if (receiveResult.MessageType is not WebSocketMessageType.Text and not WebSocketMessageType.Binary)
                {
                    throw new InvalidDataException($"Ожидался текстовый или бинарный WebSocket-кадр, получен {receiveResult.MessageType}.");
                }

                frameReader.Append(receiveBuffer.AsSpan(0, receiveResult.Count));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Браузер закрыл поток или запрос остановлен хостом; соединение будет закрыто в finally.
        }
        catch (OperationCanceledException) when (connectTimeout.IsCancellationRequested)
        {
            await WriteTimeoutEventAsync(
                context,
                logger,
                "Превышено время ожидания установки WebSocket-соединения (15 секунд).",
                cancellationToken);
        }
        catch (OperationCanceledException) when (stompConnectedTimeout.IsCancellationRequested && !sessionState.StompConnected)
        {
            await WriteTimeoutEventAsync(
                context,
                logger,
                "Превышено время ожидания STOMP CONNECTED после CONNECT (15 секунд).",
                cancellationToken);
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

    private static async Task WriteTimeoutEventAsync(
        HttpContext context,
        ILogger logger,
        string details,
        CancellationToken cancellationToken)
    {
        logger.LogWarning("{DiagnosticTimeout}", details);
        await WriteEventAsync(context, new MoexCandlesSseEvent("error", DateTimeOffset.UtcNow, Details: details), cancellationToken);
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
