using Microsoft.Extensions.Logging;

namespace ProjectTraiding.Diagnostics.Probe;

/// <summary>
/// Лог-события потокового соединения биржи.
/// EventId 430–439 перенесены вместе с потоковой диагностикой: диапазон больше не
/// принадлежит контуру Moex, но остаётся закреплённым за этими событиями в Diagnostics.
/// Повторное использование номеров 430–439 в любом проекте решения запрещено.
///
/// Логин и пароль в журнал НЕ пишутся. Пароль — очевидно. Логин — потому что это
/// идентификатор учётной записи, а для диагностики соединения он не нужен.
/// </summary>
public static partial class MoexWebSocketLogMessages
{
    [LoggerMessage(EventId = 430, EventName = "MoexWsConnecting", Level = LogLevel.Information,
        Message = "MOEX websocket connecting: source={Source}, endpoint={Endpoint}, domain={Domain}.")]
    public static partial void Connecting(ILogger logger, string source, string endpoint, string domain);

    [LoggerMessage(EventId = 431, EventName = "MoexWsConnected", Level = LogLevel.Information,
        Message = "MOEX websocket connected: source={Source}, endpoint={Endpoint}, elapsedMs={ElapsedMs}.")]
    public static partial void Connected(ILogger logger, string source, string endpoint, double elapsedMs);

    [LoggerMessage(EventId = 432, EventName = "MoexWsConnectRejected", Level = LogLevel.Error,
        Message = "MOEX websocket rejected CONNECT: source={Source}, endpoint={Endpoint}, command={Command}.")]
    public static partial void ConnectRejected(ILogger logger, string source, string endpoint, string command);

    [LoggerMessage(EventId = 433, EventName = "MoexWsSubscribed", Level = LogLevel.Information,
        Message = "MOEX websocket subscribed: source={Source}, destination={Destination}, selector={Selector}.")]
    public static partial void Subscribed(ILogger logger, string source, string destination, string selector);

    [LoggerMessage(EventId = 434, EventName = "MoexWsFrameReceived", Level = LogLevel.Debug,
        Message = "MOEX websocket frame: source={Source}, command={Command}, bytes={Bytes}.")]
    public static partial void FrameReceived(ILogger logger, string source, string command, long bytes);

    [LoggerMessage(EventId = 435, EventName = "MoexWsProbeCompleted", Level = LogLevel.Information,
        Message = "MOEX websocket probe completed: source={Source}, destination={Destination}, frames={Frames}, bytes={CapturedBytes}, truncated={Truncated}, elapsedMs={ElapsedMs}.")]
    public static partial void ProbeCompleted(ILogger logger, string source, string destination, int frames, long capturedBytes, bool truncated, double elapsedMs);

    [LoggerMessage(EventId = 436, EventName = "MoexWsFailed", Level = LogLevel.Error,
        Message = "MOEX websocket failed: source={Source}, endpoint={Endpoint}, errorType={ErrorType}, message={ErrorMessage}.")]
    public static partial void Failed(ILogger logger, string source, string endpoint, string errorType, string errorMessage);
}
