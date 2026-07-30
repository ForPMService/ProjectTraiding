namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Общие события чтения Redis-потоков витриной.
    /// EventId 330–339: активный диапазон vitrine-stream-reader.
    /// EventId 340–359 выведены из активного использования, но остаются зарезервированными:
    /// в исторических журналах они обозначают прежние события тарифов и диапазонов.
    /// </summary>
    public static partial class VitrineStreamLogMessages
    {
        [LoggerMessage(EventId = 330, EventName = "VitrineStreamGroupEnsured", Level = LogLevel.Information,
            Message = "Vitrine stream consumer group ensured: stream={Stream}, group={Group}.")]
        public static partial void GroupEnsured(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 331, EventName = "VitrineStreamGroupAlreadyExists", Level = LogLevel.Information,
            Message = "Vitrine stream consumer group already exists: stream={Stream}, group={Group}.")]
        public static partial void GroupAlreadyExists(ILogger logger, string stream, string group);

        [LoggerMessage(EventId = 332, EventName = "VitrineStreamEventsReceived", Level = LogLevel.Information,
            Message = "Vitrine stream events received: stream={Stream}, count={Count}.")]
        public static partial void EventsReceived(ILogger logger, string stream, int count);

        [LoggerMessage(EventId = 333, EventName = "VitrineStreamListenerStarted", Level = LogLevel.Information,
            Message = "Vitrine stream listener started: stream={Stream}, poll={PollInterval}.")]
        public static partial void ListenerStarted(ILogger logger, string stream, TimeSpan pollInterval);

        [LoggerMessage(EventId = 334, EventName = "VitrineStreamListenerStopped", Level = LogLevel.Information,
            Message = "Vitrine stream listener stopped: stream={Stream}.")]
        public static partial void ListenerStopped(ILogger logger, string stream);

        [LoggerMessage(EventId = 335, EventName = "VitrineStreamListenerPollFailed", Level = LogLevel.Warning,
            Message = "Vitrine stream listener poll failed, will retry: stream={Stream}, errorType={ErrorType}.")]
        public static partial void PollFailed(
            ILogger logger,
            Exception exception,
            string stream,
            string errorType);
    }
}
