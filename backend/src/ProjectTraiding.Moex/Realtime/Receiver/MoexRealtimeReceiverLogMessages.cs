using System;

namespace ProjectTraiding.Moex.Realtime.Receiver
{
    /// <summary>Лог-события фоновых служб приёма сделок, стакана и свечей.</summary>
    public static partial class MoexRealtimeReceiverLogMessages
    {
        [LoggerMessage(EventId = 450, EventName = "MoexTradesReceiverStarted", Level = LogLevel.Information,
            Message = "Trades receiver started: pollInterval={PollInterval}.")]
        public static partial void TradesStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 451, EventName = "MoexTradesReceiverStopped", Level = LogLevel.Information,
            Message = "Trades receiver stopped.")]
        public static partial void TradesStopped(ILogger logger);

        [LoggerMessage(EventId = 452, EventName = "MoexTradesReceiverSubscriptionsEmpty", Level = LogLevel.Information,
            Message = "Trades receiver subscription list is empty.")]
        public static partial void TradesSubscriptionsEmpty(ILogger logger);

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

        [LoggerMessage(EventId = 470, EventName = "MoexTradesReceiverInstrumentFetchTimedOut", Level = LogLevel.Warning,
            Message = "Trades receiver instrument fetch timed out: secid={Secid}, market={Market}, budget={Budget}.")]
        public static partial void TradesInstrumentFetchTimedOut(
            ILogger logger, string secid, string market, TimeSpan budget);

        [LoggerMessage(EventId = 460, EventName = "MoexOrderbookReceiverStarted", Level = LogLevel.Information,
            Message = "Orderbook receiver started: pollInterval={PollInterval}.")]
        public static partial void OrderbookStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 461, EventName = "MoexOrderbookReceiverStopped", Level = LogLevel.Information,
            Message = "Orderbook receiver stopped.")]
        public static partial void OrderbookStopped(ILogger logger);

        [LoggerMessage(EventId = 462, EventName = "MoexOrderbookReceiverSubscriptionsEmpty", Level = LogLevel.Information,
            Message = "Orderbook receiver subscription list is empty.")]
        public static partial void OrderbookSubscriptionsEmpty(ILogger logger);

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

        [LoggerMessage(EventId = 469, EventName = "MoexOrderbookReceiverInstrumentFetchTimedOut", Level = LogLevel.Warning,
            Message = "Orderbook receiver instrument fetch timed out: secid={Secid}, market={Market}, budget={Budget}.")]
        public static partial void OrderbookInstrumentFetchTimedOut(
            ILogger logger, string secid, string market, TimeSpan budget);

        [LoggerMessage(EventId = 471, EventName = "MoexCandlesReceiverStarted", Level = LogLevel.Information,
            Message = "Candles receiver started: pollInterval={PollInterval}.")]
        public static partial void CandlesStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 472, EventName = "MoexCandlesReceiverStopped", Level = LogLevel.Information,
            Message = "Candles receiver stopped.")]
        public static partial void CandlesStopped(ILogger logger);

        [LoggerMessage(EventId = 473, EventName = "MoexCandlesReceiverSubscriptionsEmpty", Level = LogLevel.Information,
            Message = "Candles receiver subscription list is empty.")]
        public static partial void CandlesSubscriptionsEmpty(ILogger logger);

        [LoggerMessage(EventId = 474, EventName = "MoexCandlesReceiverInstrumentPrepared", Level = LogLevel.Information,
            Message = "Candles receiver instrument prepared: secid={Secid}, market={Market}, sessionId={SessionId}.")]
        public static partial void CandlesInstrumentPrepared(
            ILogger logger, string secid, string market, long sessionId);

        [LoggerMessage(EventId = 475, EventName = "MoexCandlesReceiverInstrumentPreparationFailed", Level = LogLevel.Warning,
            Message = "Candles receiver instrument preparation failed: secid={Secid}.")]
        public static partial void CandlesInstrumentPreparationFailed(
            ILogger logger, Exception exception, string secid);

        [LoggerMessage(EventId = 476, EventName = "MoexCandlesReceiverInstrumentPollFailed", Level = LogLevel.Warning,
            Message = "Candles receiver instrument poll failed: secid={Secid}, market={Market}.")]
        public static partial void CandlesInstrumentPollFailed(
            ILogger logger, Exception exception, string secid, string market);

        [LoggerMessage(EventId = 477, EventName = "MoexCandlesReceiverTurnFailed", Level = LogLevel.Error,
            Message = "Candles receiver turn failed unexpectedly.")]
        public static partial void CandlesTurnFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 478, EventName = "MoexCandlesReceiverSessionCloseFailed", Level = LogLevel.Warning,
            Message = "Candles receiver session close failed: secid={Secid}, sessionId={SessionId}.")]
        public static partial void CandlesSessionCloseFailed(
            ILogger logger, Exception exception, string secid, long sessionId);

        [LoggerMessage(EventId = 479, EventName = "MoexCandlesReceiverShutdownFailed", Level = LogLevel.Error,
            Message = "Candles receiver shutdown cleanup failed unexpectedly.")]
        public static partial void CandlesShutdownFailed(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 480, EventName = "MoexCandlesReceiverInstrumentFetchTimedOut", Level = LogLevel.Warning,
            Message = "Candles receiver instrument fetch timed out: secid={Secid}, market={Market}, budget={Budget}.")]
        public static partial void CandlesInstrumentFetchTimedOut(
            ILogger logger, string secid, string market, TimeSpan budget);
    }
}
