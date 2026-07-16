using System;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Лог-события записи последних значений реального времени в оперативное хранилище.
    /// </summary>
    public static partial class MoexRealtimeLatestLogMessages
    {
        [LoggerMessage(EventId = 440, EventName = "MoexRealtimeLatestWritten", Level = LogLevel.Debug,
            Message = "Realtime latest value written: key={Key}.")]
        public static partial void LatestWritten(ILogger logger, string key);

        [LoggerMessage(EventId = 441, EventName = "MoexRealtimeLatestWriteFailed", Level = LogLevel.Warning,
            Message = "Realtime latest value write failed: key={Key}, errorType={ErrorType}.")]
        public static partial void LatestWriteFailed(
            ILogger logger, Exception exception, string key, string errorType);
    }
}
