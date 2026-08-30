namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public static partial class CustomFeaturesWriterLogMessages
    {
        [LoggerMessage(
            EventId = 300, EventName = "CustomFeaturesWriteStarted", Level = LogLevel.Debug,
            Message = "CustomFeatures DB write started: table={Table}.")]
        public static partial void WriteStarted(ILogger logger, string table);

        [LoggerMessage(
            EventId = 301, EventName = "CustomFeaturesWriteCompleted", Level = LogLevel.Information,
            Message = "CustomFeatures DB write completed: table={Table}, id={Id}, rows={RowsWritten}, time={ElapsedMs}.")]
        public static partial void WriteCompleted(ILogger logger, string table, long id, int rowsWritten, TimeSpan elapsedMs);

        [LoggerMessage(
            EventId = 302, EventName = "CustomFeaturesWriteRolledBack", Level = LogLevel.Error,
            Message = "CustomFeatures DB write rolled back: table={Table}, errorType={ErrorType}.")]
        public static partial void WriteRolledBack(ILogger logger, Exception exception, string table, string errorType);
    }
}
