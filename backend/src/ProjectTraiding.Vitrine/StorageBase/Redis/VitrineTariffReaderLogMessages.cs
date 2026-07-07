using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Лог-события чтения потока изменения тарифов витриной.
    /// EventId 340–349: зарезервировано за vitrine-tariff-reader.
    /// </summary>
    public static partial class VitrineTariffReaderLogMessages
    {
        [LoggerMessage(EventId = 340, EventName = "VitrineTariffGroupEnsured", Level = LogLevel.Information,
            Message = "Tariff consumer group ensured: stream={Stream}, group={Group}.")]
        public static partial void GroupEnsured(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 341, EventName = "VitrineTariffGroupAlreadyExists", Level = LogLevel.Information,
            Message = "Tariff consumer group already exists: stream={Stream}, group={Group}.")]
        public static partial void GroupAlreadyExists(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 342, EventName = "VitrineTariffEventsReceived", Level = LogLevel.Information,
            Message = "Tariff change events received: stream={Stream}, count={Count}.")]
        public static partial void EventsReceived(ILogger logger, string stream, int count);

        [LoggerMessage(EventId = 343, EventName = "VitrineTariffListenerStarted", Level = LogLevel.Information,
            Message = "Tariff listener started: poll={PollInterval}.")]
        public static partial void ListenerStarted(ILogger logger, TimeSpan pollInterval);

        [LoggerMessage(EventId = 344, EventName = "VitrineTariffListenerStopped", Level = LogLevel.Information,
            Message = "Tariff listener stopped.")]
        public static partial void ListenerStopped(ILogger logger);

        [LoggerMessage(EventId = 345, EventName = "VitrineTariffListenerPollFailed", Level = LogLevel.Warning,
            Message = "Tariff listener poll failed, will retry: errorType={ErrorType}.")]
        public static partial void PollFailed(ILogger logger, Exception exception, string errorType);
    }
}
