using StackExchange.Redis;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Внутренняя техническая механика Redis-кеша списка: чтение с разбором,
    /// запись со сроком жизни и удаление ключа. Предметных ключей и запросов не знает.
    /// </summary>
    internal sealed class VitrineListCache<TItem>
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger _logger;
        private readonly JsonTypeInfo<List<TItem>> _typeInfo;
        private readonly TimeSpan _ttl;

        internal VitrineListCache(
            IConnectionMultiplexer redis,
            ILogger logger,
            JsonTypeInfo<List<TItem>> typeInfo,
            TimeSpan ttl)
        {
            _redis = redis;
            _logger = logger;
            _typeInfo = typeInfo;
            _ttl = ttl;
        }

        internal async Task<List<TItem>?> TryReadAsync(string key)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                RedisValue value = await db.StringGetAsync(key);
                if (value.IsNullOrEmpty)
                {
                    VitrineCacheLogMessages.CacheMiss(_logger, key);
                    return null;
                }

                List<TItem>? list = JsonSerializer.Deserialize((string)value!, _typeInfo);
                if (list is null)
                {
                    // Успешно разобранный JSON null — негодное значение. Исключение разбора
                    // по-прежнему попадёт в общий catch и будет недоступностью кеша.
                    VitrineCacheLogMessages.CacheCorrupt(_logger, key);
                    return null;
                }

                VitrineCacheLogMessages.CacheHit(_logger, key, list.Count);
                return list;
            }
            catch (Exception ex)
            {
                // Недоступность Redis и ошибки разбора не роняют выдачу: предметный кеш
                // получит null и обратится к базе истины.
                VitrineCacheLogMessages.CacheUnavailable(
                    _logger,
                    ex,
                    key,
                    ex.GetType().Name);
                return null;
            }
        }

        internal async Task WriteAsync(string key, List<TItem> list)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(list, _typeInfo);
                await db.StringSetAsync(key, json, _ttl);
                VitrineCacheLogMessages.CacheFilled(_logger, key, list.Count, _ttl);
            }
            catch (Exception ex)
            {
                // Неудача наполнения некритична: предметные данные уже получены из истины.
                VitrineCacheLogMessages.CacheWriteFailed(
                    _logger,
                    ex,
                    key,
                    ex.GetType().Name);
            }
        }

        internal async Task InvalidateAsync(string key)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                await db.KeyDeleteAsync(key);
                VitrineCacheLogMessages.CacheInvalidated(_logger, key);
            }
            catch (Exception ex)
            {
                VitrineCacheLogMessages.CacheInvalidateFailed(
                    _logger,
                    ex,
                    key,
                    ex.GetType().Name);
            }
        }
    }
}
