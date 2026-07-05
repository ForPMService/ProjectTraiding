using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Лог-события кеша витрины в оперативном хранилище (Redis).
    /// EventId 320–329: зарезервировано за vitrine-cache.
    /// </summary>
    public static partial class VitrineCacheLogMessages
    {
        [LoggerMessage(EventId = 320, EventName = "VitrineCacheHit", Level = LogLevel.Information,
            Message = "Vitrine cache hit: key={Key}, items={Items}.")]
        public static partial void CacheHit(ILogger logger, string key, int items);

        [LoggerMessage(EventId = 321, EventName = "VitrineCacheMiss", Level = LogLevel.Information,
            Message = "Vitrine cache miss: key={Key}.")]
        public static partial void CacheMiss(ILogger logger, string key);

        [LoggerMessage(EventId = 322, EventName = "VitrineCacheFilled", Level = LogLevel.Information,
            Message = "Vitrine cache filled: key={Key}, items={Items}, ttl={Ttl}.")]
        public static partial void CacheFilled(ILogger logger, string key, int items, TimeSpan ttl);

        [LoggerMessage(EventId = 323, EventName = "VitrineCacheCorrupt", Level = LogLevel.Warning,
            Message = "Vitrine cache value unreadable, treated as miss: key={Key}.")]
        public static partial void CacheCorrupt(ILogger logger, string key);

        [LoggerMessage(EventId = 324, EventName = "VitrineCacheUnavailable", Level = LogLevel.Warning,
            Message = "Vitrine cache read unavailable, falling back to DB: key={Key}, errorType={ErrorType}.")]
        public static partial void CacheUnavailable(ILogger logger, Exception exception, string key, string errorType);

        [LoggerMessage(EventId = 325, EventName = "VitrineCacheWriteFailed", Level = LogLevel.Warning,
            Message = "Vitrine cache write failed: key={Key}, errorType={ErrorType}.")]
        public static partial void CacheWriteFailed(ILogger logger, Exception exception, string key, string errorType);
    }
}
