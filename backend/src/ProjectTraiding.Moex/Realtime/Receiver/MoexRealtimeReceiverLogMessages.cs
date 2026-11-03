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
            Message = "Trades receiver instrument prepared: secid={Secid}, market={Market}.")]
        public static partial void TradesInstrumentPrepared(
            ILogger logger, string secid, string market);

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
            Message = "Orderbook receiver instrument prepared: secid={Secid}, market={Market}.")]
        public static partial void OrderbookInstrumentPrepared(
            ILogger logger, string secid, string market);

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
            Message = "Candles receiver instrument prepared: secid={Secid}, market={Market}.")]
        public static partial void CandlesInstrumentPrepared(
            ILogger logger, string secid, string market);

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

        [LoggerMessage(EventId = 480, EventName = "MoexCandlesReceiverInstrumentFetchTimedOut", Level = LogLevel.Warning,
            Message = "Candles receiver instrument fetch timed out: secid={Secid}, market={Market}, interval={CandleInterval}, budget={Budget}.")]
        public static partial void CandlesInstrumentFetchTimedOut(
            ILogger logger, string secid, string market, int candleInterval, TimeSpan budget);

        [LoggerMessage(EventId = 487, EventName = "MoexCandlesReceiverIntervalPollFailed", Level = LogLevel.Warning,
            Message = "Candles receiver interval poll failed: secid={Secid}, market={Market}, interval={CandleInterval}.")]
        public static partial void CandlesIntervalPollFailed(
            ILogger logger, Exception exception, string secid, string market, int candleInterval);

        [LoggerMessage(EventId = 488, EventName = "MoexCandlesReceiverRowSkipped", Level = LogLevel.Warning,
            Message = "MOEX candle row skipped: secid={Secid}, dataKind={DataKind}, interval={CandleInterval}, position={Position}, reason={Reason}.")]
        public static partial void CandleRowSkipped(
            ILogger logger, string secid, string dataKind, int candleInterval, int position, string reason);

        [LoggerMessage(EventId = 482, EventName = "MoexTradesReceiverInstrumentStopped", Level = LogLevel.Information,
            Message = "Trades receiver instrument stopped: secid={Secid}, rows={RowsTotal}.")]
        public static partial void TradesInstrumentStopped(
            ILogger logger, string secid, long rowsTotal);

        [LoggerMessage(EventId = 484, EventName = "MoexOrderbookReceiverInstrumentStopped", Level = LogLevel.Information,
            Message = "Orderbook receiver instrument stopped: secid={Secid}, rows={RowsTotal}.")]
        public static partial void OrderbookInstrumentStopped(
            ILogger logger, string secid, long rowsTotal);

        [LoggerMessage(EventId = 486, EventName = "MoexCandlesReceiverInstrumentStopped", Level = LogLevel.Information,
            Message = "Candles receiver instrument stopped: secid={Secid}, rows={RowsTotal}.")]
        public static partial void CandlesInstrumentStopped(
            ILogger logger, string secid, long rowsTotal);
    }
}
