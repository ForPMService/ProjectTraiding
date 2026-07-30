using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Кеширующий читатель справочника инструментов по схеме «чтение через кеш».
    /// Единственный владелец записи ключа vitrine:instruments:cache:v1 — витрина.
    /// При промахе или недоступности кеша читает базу истины и наполняет кеш.
    /// </summary>
    public sealed class InstrumentCatalogCache
    {
        // Отметка версии разводит несовместимые формы значения по разным ключам.
        private const string CacheKey = "vitrine:instruments:cache:v1";

        private readonly InstrumentReadQuery _readQuery;
        private readonly VitrineListCache<VitrineInstrumentDto> _cache;

        public InstrumentCatalogCache(
            IConnectionMultiplexer redis,
            InstrumentReadQuery readQuery,
            ILogger<InstrumentCatalogCache> logger,
            TimeSpan ttl)
        {
            _readQuery = readQuery;
            _cache = new VitrineListCache<VitrineInstrumentDto>(
                redis,
                logger,
                VitrineJsonContext.Default.ListVitrineInstrumentDto,
                ttl);
        }

        public async Task<List<VitrineInstrumentDto>> GetAllAsync(CancellationToken ct)
        {
            List<VitrineInstrumentDto>? cached = await _cache.TryReadAsync(CacheKey);
            if (cached is not null)
                return cached;

            List<VitrineInstrumentDto> fromDb = await _readQuery.GetAllAsync(ct);
            await _cache.WriteAsync(CacheKey, fromDb);
            return fromDb;
        }

        public async Task InvalidateAsync()
        {
            await _cache.InvalidateAsync(CacheKey);
        }
    }
}
