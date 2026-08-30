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
    }
}
