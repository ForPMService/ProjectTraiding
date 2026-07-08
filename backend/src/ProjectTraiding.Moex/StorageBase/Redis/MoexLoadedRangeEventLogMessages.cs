using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Redis
{
    /// <summary>
    /// Лог-события публикации изменения диапазонов в поток оперативного хранилища.
    /// EventId 420–429: зарезервировано за moex-loaded-range-event.
    /// </summary>
    public static partial class MoexLoadedRangeEventLogMessages
    {
        [LoggerMessage(EventId = 420, EventName = "MoexLoadedRangeEventPublished", Level = LogLevel.Information,
            Message = "Loaded-range change event published: stream={Stream}, secid={Secid}, id={Id}.")]
        public static partial void EventPublished(ILogger logger, string stream, string secid, string id);

        [LoggerMessage(EventId = 421, EventName = "MoexLoadedRangeEventPublishFailed", Level = LogLevel.Warning,
            Message = "Loaded-range change event publish failed: stream={Stream}, errorType={ErrorType}.")]
        public static partial void EventPublishFailed(ILogger logger, Exception exception, string stream, string errorType);
    }
}
