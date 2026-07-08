using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using StackExchange.Redis;
using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Кеширующий читатель диапазонов загрузки по инструменту, схема «чтение через кеш».
    /// Единственный владелец записи ключей диапазонов — витрина. Ключ на инструмент:
    /// сброс адресный — завершение загрузки по одному инструменту трогает только его ключ.
    /// Устройство повторяет BrokerTariffCache; отличие — код инструмента в ключе и в сбросе.
    /// </summary>
    public sealed class LoadedRangeCache
    {
        // Отметка версии в имени ключа разводит несовместимые формы значения.
        private const string KeyPrefix = "vitrine:loaded-ranges:";
        private const string KeySuffix = ":v1";

        private readonly IConnectionMultiplexer _redis;
        private readonly LoadedRangeReadQuery _readQuery;
        private readonly ILogger<LoadedRangeCache> _logger;
        private readonly TimeSpan _ttl;

        public LoadedRangeCache(
            IConnectionMultiplexer redis,
            LoadedRangeReadQuery readQuery,
            ILogger<LoadedRangeCache> logger,
            TimeSpan ttl)
        {
            _redis = redis;
            _readQuery = readQuery;
            _logger = logger;
            _ttl = ttl;
        }

        private static string KeyFor(string secid) => KeyPrefix + secid + KeySuffix;

        public async Task<List<VitrineLoadedRangeDto>> GetBySecidAsync(string secid, CancellationToken ct)
        {
            string key = KeyFor(secid);

            // 1. Попытка чтения из кеша. Любой сбой хранилища не роняет выдачу.
            List<VitrineLoadedRangeDto>? cached = await TryReadFromCacheAsync(key);
            if (cached is not null)
            {
                VitrineCacheLogMessages.CacheHit(_logger, key, cached.Count);
                return cached;
            }

            // 2. Промах, недоступность или негодное значение → истина из базы.
            List<VitrineLoadedRangeDto> fromDb = await _readQuery.GetBySecidAsync(secid, ct);

            // 3. Наполнение кеша. Неудача записи некритична — данные уже получены.
            await TryWriteToCacheAsync(key, fromDb);

            return fromDb;
        }

        private async Task<List<VitrineLoadedRangeDto>?> TryReadFromCacheAsync(string key)
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

                List<VitrineLoadedRangeDto>? list = JsonSerializer.Deserialize(
                    (string)value!, VitrineJsonContext.Default.ListVitrineLoadedRangeDto);

                if (list is null)
                {
                    VitrineCacheLogMessages.CacheCorrupt(_logger, key);
                    return null;
                }

                return list;
            }
            catch (Exception ex)
            {
                VitrineCacheLogMessages.CacheUnavailable(_logger, ex, key, ex.GetType().Name);
                return null;
            }
        }

        public async Task InvalidateAsync(string secid)
        {
            string key = KeyFor(secid);
            try
            {
                IDatabase db = _redis.GetDatabase();
                await db.KeyDeleteAsync(key);
                VitrineCacheLogMessages.CacheInvalidated(_logger, key);
            }
            catch (Exception ex)
            {
                VitrineCacheLogMessages.CacheInvalidateFailed(_logger, ex, key, ex.GetType().Name);
            }
        }

        private async Task TryWriteToCacheAsync(string key, List<VitrineLoadedRangeDto> list)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(
                    list, VitrineJsonContext.Default.ListVitrineLoadedRangeDto);
                await db.StringSetAsync(key, json, _ttl);
                VitrineCacheLogMessages.CacheFilled(_logger, key, list.Count, _ttl);
            }
            catch (Exception ex)
            {
                VitrineCacheLogMessages.CacheWriteFailed(_logger, ex, key, ex.GetType().Name);
            }
        }
    }
}
