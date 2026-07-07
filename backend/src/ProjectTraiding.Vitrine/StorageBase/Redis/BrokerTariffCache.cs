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
    /// Кеширующий читатель тарифов брокеров по схеме «чтение через кеш».
    /// Единственный владелец записи ключа vitrine:tariffs:cache:v1 — витрина.
    /// Устройство дословно повторяет InstrumentCatalogCache: смотрит в оперативное
    /// хранилище; при промахе, недоступности хранилища или негодном значении читает
    /// базу истины и наполняет кеш со сроком жизни. Путь к базе не отсекается.
    /// </summary>
    public sealed class BrokerTariffCache
    {
        // Отметка версии в имени ключа разводит несовместимые формы значения по разным
        // ключам: при смене формы данных переходим на :v2, старые значения истекают сами.
        private const string CacheKey = "vitrine:tariffs:cache:v1";

        private readonly IConnectionMultiplexer _redis;
        private readonly BrokerTariffReadQuery _readQuery;
        private readonly ILogger<BrokerTariffCache> _logger;
        private readonly TimeSpan _ttl;

        public BrokerTariffCache(
            IConnectionMultiplexer redis,
            BrokerTariffReadQuery readQuery,
            ILogger<BrokerTariffCache> logger,
            TimeSpan ttl)
        {
            _redis = redis;
            _readQuery = readQuery;
            _logger = logger;
            _ttl = ttl;
        }

        public async Task<List<VitrineBrokerTariffDto>> GetAllAsync(CancellationToken ct)
        {
            // 1. Попытка чтения из кеша. Любой сбой хранилища не роняет выдачу.
            List<VitrineBrokerTariffDto>? cached = await TryReadFromCacheAsync();
            if (cached is not null)
            {
                VitrineCacheLogMessages.CacheHit(_logger, CacheKey, cached.Count);
                return cached;
            }

            // 2. Промах, недоступность или негодное значение → истина из базы.
            List<VitrineBrokerTariffDto> fromDb = await _readQuery.GetAllAsync(ct);

            // 3. Наполнение кеша. Неудача записи некритична — данные уже получены.
            await TryWriteToCacheAsync(fromDb);

            return fromDb;
        }

        private async Task<List<VitrineBrokerTariffDto>?> TryReadFromCacheAsync()
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                RedisValue value = await db.StringGetAsync(CacheKey);
                if (value.IsNullOrEmpty)
                {
                    VitrineCacheLogMessages.CacheMiss(_logger, CacheKey);
                    return null;
                }

                List<VitrineBrokerTariffDto>? list = JsonSerializer.Deserialize(
                    (string)value!, VitrineJsonContext.Default.ListVitrineBrokerTariffDto);

                if (list is null)
                {
                    // Значение есть, но не разбирается — считаем отказом кеша, не истины.
                    // Следующая запись перезапишет ключ свежим значением.
                    VitrineCacheLogMessages.CacheCorrupt(_logger, CacheKey);
                    return null;
                }

                return list;
            }
            catch (Exception ex)
            {
                // Хранилище недоступно или иная ошибка — не роняем выдачу, идём в базу.
                VitrineCacheLogMessages.CacheUnavailable(_logger, ex, CacheKey, ex.GetType().Name);
                return null;
            }
        }

        public async Task InvalidateAsync()
        {
            // Удаление ключа по событию изменения справочника. Ближайшее чтение получит
            // промах и перечитает свежие данные из базы истины. Неудача некритична:
            // суточный срок жизни ключа и без того обновит кеш со временем.
            try
            {
                IDatabase db = _redis.GetDatabase();
                await db.KeyDeleteAsync(CacheKey);
                VitrineCacheLogMessages.CacheInvalidated(_logger, CacheKey);
            }
            catch (Exception ex)
            {
                VitrineCacheLogMessages.CacheInvalidateFailed(_logger, ex, CacheKey, ex.GetType().Name);
            }
        }
        private async Task TryWriteToCacheAsync(List<VitrineBrokerTariffDto> list)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(
                    list, VitrineJsonContext.Default.ListVitrineBrokerTariffDto);
                await db.StringSetAsync(CacheKey, json, _ttl);
                VitrineCacheLogMessages.CacheFilled(_logger, CacheKey, list.Count, _ttl);
            }
            catch (Exception ex)
            {
                VitrineCacheLogMessages.CacheWriteFailed(_logger, ex, CacheKey, ex.GetType().Name);
            }
        }
    }
}
