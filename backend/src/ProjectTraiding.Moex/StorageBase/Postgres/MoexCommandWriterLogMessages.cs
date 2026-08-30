namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public static partial class MoexCommandWriterLogMessages
    {
        [LoggerMessage(
            EventId = 260, EventName = "MoexCommandWriteStarted", Level = LogLevel.Debug,
            Message = "Moex DB write started: table={Table}.")]
        public static partial void WriteStarted(ILogger logger, string table);

        [LoggerMessage(
            EventId = 261, EventName = "MoexCommandWriteRolledBack", Level = LogLevel.Error,
            Message = "Moex DB write rolled back: table={Table}, errorType={ErrorType}.")]
        public static partial void WriteRolledBack(ILogger logger, Exception exception, string table, string errorType);

        [LoggerMessage(
            EventId = 262, EventName = "MoexCommandLoadTasksCancelledAll", Level = LogLevel.Warning,
            Message = "Moex load tasks cancelled (all): cancelled={CancelledCount}, cancelRequested={CancelRequestedCount}, time={Elapsed}.")]
        public static partial void LoadTasksCancelledAll(
            ILogger logger,
            int cancelledCount,
            int cancelRequestedCount,
            TimeSpan elapsed);

        [LoggerMessage(
            EventId = 263, EventName = "MoexCommandLoadTasksCancelledInstrument", Level = LogLevel.Warning,
            Message = "Moex load tasks cancelled (instrument): secid={Secid}, cancelled={CancelledCount}, cancelRequested={CancelRequestedCount}, time={Elapsed}.")]
        public static partial void LoadTasksCancelledInstrument(
            ILogger logger,
            string secid,
            int cancelledCount,
            int cancelRequestedCount,
            TimeSpan elapsed);

        [LoggerMessage(
            EventId = 264, EventName = "MoexCommandRealtimeTradesEnabled", Level = LogLevel.Information,
            Message = "Moex realtime trades enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeTradesEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 265, EventName = "MoexCommandRealtimeTradesDisabled", Level = LogLevel.Information,
            Message = "Moex realtime trades disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeTradesDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 266, EventName = "MoexCommandRealtimeOrderbookEnabled", Level = LogLevel.Information,
            Message = "Moex realtime orderbook enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeOrderbookEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 267, EventName = "MoexCommandRealtimeOrderbookDisabled", Level = LogLevel.Information,
            Message = "Moex realtime orderbook disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeOrderbookDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 268, EventName = "MoexCommandRealtimeCandlesEnabled", Level = LogLevel.Information,
            Message = "Moex realtime candles enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeCandlesEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 269, EventName = "MoexCommandRealtimeCandlesDisabled", Level = LogLevel.Information,
            Message = "Moex realtime candles disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeCandlesDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 270, EventName = "MoexCommandRealtimeInstrumentDisabled", Level = LogLevel.Information,
            Message = "Moex realtime instrument disabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeInstrumentDisabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 271, EventName = "MoexCommandRealtimeAllDisabled", Level = LogLevel.Warning,
            Message = "Moex realtime all subscriptions disabled: rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeAllDisabled(ILogger logger, int rowsWritten, TimeSpan elapsed);

        [LoggerMessage(
            EventId = 272, EventName = "MoexCommandRealtimeInstrumentEnabled", Level = LogLevel.Information,
            Message = "Moex realtime instrument enabled: secid={Secid}, rows={RowsWritten}, time={Elapsed}.")]
        public static partial void RealtimeInstrumentEnabled(ILogger logger, string secid, int rowsWritten, TimeSpan elapsed);
    }
}
