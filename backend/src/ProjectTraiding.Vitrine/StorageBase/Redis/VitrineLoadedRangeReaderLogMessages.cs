using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Лог-события чтения потока изменения диапазонов витриной.
    /// EventId 350–359: зарезервировано за vitrine-loaded-range-reader.
    /// Диапазон 340–349 уже занят читателем тарифов.
    /// </summary>
    public static partial class VitrineLoadedRangeReaderLogMessages
    {
        [LoggerMessage(EventId = 350, EventName = "VitrineLoadedRangeGroupEnsured", Level = LogLevel.Information,
            Message = "Loaded-range consumer group ensured: stream={Stream}, group={Group}.")]
        public static partial void GroupEnsured(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 351, EventName = "VitrineLoadedRangeGroupAlreadyExists", Level = LogLevel.Information,
            Message = "Loaded-range consumer group already exists: stream={Stream}, group={Group}.")]
        public static partial void GroupAlreadyExists(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 352, EventName = "VitrineLoadedRangeEventsReceived", Level = LogLevel.Information,
            Message = "Loaded-range change events received: stream={Stream}, count={Count}.")]
        public static partial void EventsReceived(ILogger logger, string stream, int count);

        [LoggerMessage(EventId = 353, EventName = "VitrineLoadedRangeListenerStarted", Level = LogLevel.Information,
            Message = "Loaded-range listener started: poll={PollInterval}.")]
        public static partial void ListenerStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 354, EventName = "VitrineLoadedRangeListenerStopped", Level = LogLevel.Information,
            Message = "Loaded-range listener stopped.")]
        public static partial void ListenerStopped(ILogger logger);

        [LoggerMessage(EventId = 355, EventName = "VitrineLoadedRangeListenerPollFailed", Level = LogLevel.Warning,
            Message = "Loaded-range listener poll failed, will retry: errorType={ErrorType}.")]
        public static partial void PollFailed(ILogger logger, Exception exception, string errorType);
    }
}
