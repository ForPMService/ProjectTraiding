using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Лог-события чтения потока изменения справочника витриной.
    /// EventId 330–339: зарезервировано за vitrine-catalog-reader.
    /// </summary>
    public static partial class VitrineCatalogReaderLogMessages
    {
        [LoggerMessage(EventId = 330, EventName = "VitrineGroupEnsured", Level = LogLevel.Information,
            Message = "Catalog consumer group ensured: stream={Stream}, group={Group}.")]
        public static partial void GroupEnsured(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 331, EventName = "VitrineGroupAlreadyExists", Level = LogLevel.Information,
            Message = "Catalog consumer group already exists: stream={Stream}, group={Group}.")]
        public static partial void GroupAlreadyExists(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 332, EventName = "VitrineCatalogEventsReceived", Level = LogLevel.Information,
            Message = "Catalog change events received: stream={Stream}, count={Count}.")]
        public static partial void EventsReceived(ILogger logger, string stream, int count);

        [LoggerMessage(EventId = 333, EventName = "VitrineListenerStarted", Level = LogLevel.Information,
            Message = "Catalog listener started: poll={PollInterval}.")]
        public static partial void ListenerStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 334, EventName = "VitrineListenerStopped", Level = LogLevel.Information,
            Message = "Catalog listener stopped.")]
        public static partial void ListenerStopped(ILogger logger);

        [LoggerMessage(EventId = 335, EventName = "VitrineListenerPollFailed", Level = LogLevel.Warning,
            Message = "Catalog listener poll failed, will retry: errorType={ErrorType}.")]
        public static partial void PollFailed(ILogger logger, Exception exception, string errorType);
    }
}
