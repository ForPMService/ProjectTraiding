using System;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Лог-события записи живого прогресса задачи в оперативное хранилище.
    /// EventId 410–419: зарезервировано за moex-load-progress.
    /// </summary>
    public static partial class MoexLoadProgressLogMessages
    {
        [LoggerMessage(EventId = 410, EventName = "MoexLoadProgressWritten", Level = LogLevel.Debug,
            Message = "Load progress written: key={Key}, rowsRead={RowsRead}.")]
        public static partial void ProgressWritten(ILogger logger, string key, long rowsRead);

        [LoggerMessage(EventId = 411, EventName = "MoexLoadProgressWriteFailed", Level = LogLevel.Warning,
            Message = "Load progress write failed: key={Key}, errorType={ErrorType}.")]
        public static partial void ProgressWriteFailed(ILogger logger, Exception exception, string key, string errorType);
    }
}
