using System;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>Лог-события фоновых служб приёма сделок и стакана.</summary>
    public static partial class MoexRealtimeReceiverLogMessages
    {
        [LoggerMessage(EventId = 450, EventName = "MoexTradesReceiverStarted", Level = LogLevel.Information,
            Message = "Trades receiver started: pollInterval={PollInterval}.")]
        public static partial void TradesStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 451, EventName = "MoexTradesReceiverStopped", Level = LogLevel.Information,
            Message = "Trades receiver stopped.")]
        public static partial void TradesStopped(ILogger logger);

        [LoggerMessage(EventId = 452, EventName = "MoexTradesReceiverCatalogEmpty", Level = LogLevel.Information,
            Message = "Trades receiver instrument catalog is empty.")]
        public static partial void TradesCatalogEmpty(ILogger logger);

        [LoggerMessage(EventId = 453, EventName = "MoexTradesReceiverInstrumentPrepared", Level = LogLevel.Information,
            Message = "Trades receiver instrument prepared: secid={Secid}, market={Market}, sessionId={SessionId}.")]
        public static partial void TradesInstrumentPrepared(
            ILogger logger, string secid, string market, long sessionId);

        [LoggerMessage(EventId = 454, EventName = "MoexTradesReceiverInstrumentPreparationFailed", Level = LogLevel.Warning,
            Message = "Trades receiver instrument preparation failed: secid={Secid}.")]
        public static partial void TradesInstrumentPreparationFailed(
            ILogger logger, Exception exception, string secid);

        [LoggerMessage(EventId = 455, EventName = "MoexTradesReceiverInstrumentPollFailed", Level = LogLevel.Warning,
            Message = "Trades receiver instrument poll failed: secid={Secid}, market={Market}.")]
        public static partial void TradesInstrumentPollFailed(
            ILogger logger, Exception exception, string secid, string market);

        [LoggerMessage(EventId = 456, EventName = "MoexTradesReceiverTurnFailed", Level = LogLevel.Error,
            Message = "Trades receiver turn failed unexpectedly.")]
        public static partial void TradesTurnFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 457, EventName = "MoexTradesReceiverSessionCloseFailed", Level = LogLevel.Warning,
            Message = "Trades receiver session close failed: secid={Secid}, sessionId={SessionId}.")]
        public static partial void TradesSessionCloseFailed(
            ILogger logger, Exception exception, string secid, long sessionId);

        [LoggerMessage(EventId = 458, EventName = "MoexTradesReceiverShutdownFailed", Level = LogLevel.Error,
            Message = "Trades receiver shutdown cleanup failed unexpectedly.")]
        public static partial void TradesShutdownFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 459, EventName = "MoexTradesReceiverInstrumentSeeded", Level = LogLevel.Information,
            Message = "Trades receiver instrument seeded to tail: secid={Secid}, market={Market}, tradeNo={TradeNo}.")]
        public static partial void TradesInstrumentSeeded(
            ILogger logger, string secid, string market, long tradeNo);

        [LoggerMessage(EventId = 460, EventName = "MoexOrderbookReceiverStarted", Level = LogLevel.Information,
            Message = "Orderbook receiver started: pollInterval={PollInterval}.")]
        public static partial void OrderbookStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 461, EventName = "MoexOrderbookReceiverStopped", Level = LogLevel.Information,
            Message = "Orderbook receiver stopped.")]
        public static partial void OrderbookStopped(ILogger logger);

        [LoggerMessage(EventId = 462, EventName = "MoexOrderbookReceiverCatalogEmpty", Level = LogLevel.Information,
            Message = "Orderbook receiver instrument catalog is empty.")]
        public static partial void OrderbookCatalogEmpty(ILogger logger);

        [LoggerMessage(EventId = 463, EventName = "MoexOrderbookReceiverInstrumentPrepared", Level = LogLevel.Information,
            Message = "Orderbook receiver instrument prepared: secid={Secid}, market={Market}, sessionId={SessionId}.")]
        public static partial void OrderbookInstrumentPrepared(
            ILogger logger, string secid, string market, long sessionId);

        [LoggerMessage(EventId = 464, EventName = "MoexOrderbookReceiverInstrumentPreparationFailed", Level = LogLevel.Warning,
            Message = "Orderbook receiver instrument preparation failed: secid={Secid}.")]
        public static partial void OrderbookInstrumentPreparationFailed(
            ILogger logger, Exception exception, string secid);

        [LoggerMessage(EventId = 465, EventName = "MoexOrderbookReceiverInstrumentPollFailed", Level = LogLevel.Warning,
            Message = "Orderbook receiver instrument poll failed: secid={Secid}, market={Market}.")]
        public static partial void OrderbookInstrumentPollFailed(
            ILogger logger, Exception exception, string secid, string market);

        [LoggerMessage(EventId = 466, EventName = "MoexOrderbookReceiverTurnFailed", Level = LogLevel.Error,
            Message = "Orderbook receiver turn failed unexpectedly.")]
        public static partial void OrderbookTurnFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 467, EventName = "MoexOrderbookReceiverSessionCloseFailed", Level = LogLevel.Warning,
            Message = "Orderbook receiver session close failed: secid={Secid}, sessionId={SessionId}.")]
        public static partial void OrderbookSessionCloseFailed(
            ILogger logger, Exception exception, string secid, long sessionId);

        [LoggerMessage(EventId = 468, EventName = "MoexOrderbookReceiverShutdownFailed", Level = LogLevel.Error,
            Message = "Orderbook receiver shutdown cleanup failed unexpectedly.")]
        public static partial void OrderbookShutdownFailed(ILogger logger, Exception exception);
    }
}
