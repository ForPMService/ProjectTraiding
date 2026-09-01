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

        [LoggerMessage(
            EventId = 313, EventName = "CustomFeaturesBulkWriteStarted", Level = LogLevel.Debug,
            Message = "DB write started: table={Table}, input={InputCount}.")]
        public static partial void BulkWriteStarted(ILogger logger, string table, int inputCount);

        [LoggerMessage(
            EventId = 314, EventName = "CustomFeaturesBulkWriteCompleted", Level = LogLevel.Information,
            Message = "DB write completed: table={Table}, rows={RowsWritten}, time={ElapsedMs}.")]
        public static partial void BulkWriteCompleted(ILogger logger, string table, int rowsWritten, TimeSpan elapsedMs);

        [LoggerMessage(
            EventId = 315, EventName = "CustomFeaturesBulkWriteRolledBack", Level = LogLevel.Error,
            Message = "DB write rolled back: table={Table}, atKey={Key}, processed={Processed}, errorType={ErrorType}.")]
        public static partial void BulkWriteRolledBack(ILogger logger, Exception exception, string table, string key, int processed, string errorType);
    }
}
