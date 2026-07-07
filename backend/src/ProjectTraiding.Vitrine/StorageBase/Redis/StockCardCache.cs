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
    /// Кеширующий читатель карточек акций по схеме «чтение через кеш».
    /// Единственный владелец записи ключа кеша карточек акций — витрина.
    /// Точка запрашивает одну карточку по коду инструмента; кеш держит весь список
    /// карточек рынка под одним ключом и отдаёт из него нужную. Наполнение повторяет
    /// InstrumentCatalogCache: промах, недоступность хранилища или негодное значение →
    /// чтение всех карточек из базы истины и запись в кеш со сроком жизни.
    /// </summary>
    public sealed class StockCardCache
    {
        // Отметка версии в имени ключа разводит несовместимые формы значения по разным
        // ключам: при смене формы данных переходим на :v2, старые значения истекают сами.
        private const string CacheKey = "vitrine:cards:stock:cache:v1";

        private readonly IConnectionMultiplexer _redis;
        private readonly StockCardReadQuery _readQuery;
        private readonly ILogger<StockCardCache> _logger;
        private readonly TimeSpan _ttl;

        public StockCardCache(
            IConnectionMultiplexer redis,
            StockCardReadQuery readQuery,
            ILogger<StockCardCache> logger,
            TimeSpan ttl)
        {
            _redis = redis;
            _readQuery = readQuery;
            _logger = logger;
            _ttl = ttl;
        }

        public async Task<VitrineStockCardDto?> GetBySecidAsync(string secid, CancellationToken ct)
        {
            List<VitrineStockCardDto> all = await GetAllThroughCacheAsync(ct);

            // Поиск по коду инструмента прямым перебором: LINQ запрещён.
            foreach (VitrineStockCardDto card in all)
            {
                if (card.Secid == secid)
                    return card;
            }
            return null;
        }

        private async Task<List<VitrineStockCardDto>> GetAllThroughCacheAsync(CancellationToken ct)
        {
            // 1. Попытка чтения из кеша. Любой сбой хранилища не роняет выдачу.
            List<VitrineStockCardDto>? cached = await TryReadFromCacheAsync();
            if (cached is not null)
            {
                VitrineCacheLogMessages.CacheHit(_logger, CacheKey, cached.Count);
                return cached;
            }

            // 2. Промах, недоступность или негодное значение → истина из базы.
            List<VitrineStockCardDto> fromDb = await _readQuery.GetAllAsync(ct);

            // 3. Наполнение кеша. Неудача записи некритична — данные уже получены.
            await TryWriteToCacheAsync(fromDb);

            return fromDb;
        }

        private async Task<List<VitrineStockCardDto>?> TryReadFromCacheAsync()
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

                List<VitrineStockCardDto>? list = JsonSerializer.Deserialize(
                    (string)value!, VitrineJsonContext.Default.ListVitrineStockCardDto);

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
        private async Task TryWriteToCacheAsync(List<VitrineStockCardDto> list)
        {
            try
            {
                IDatabase db = _redis.GetDatabase();
                string json = JsonSerializer.Serialize(
                    list, VitrineJsonContext.Default.ListVitrineStockCardDto);
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
