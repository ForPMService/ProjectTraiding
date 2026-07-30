using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Кеширующий читатель карточек акций. Кеш держит весь рынок под одним ключом,
    /// а предметная операция возвращает одну карточку по коду инструмента.
    /// </summary>
    public sealed class StockCardCache
    {
        // Отметка версии разводит несовместимые формы значения по разным ключам.
        private const string CacheKey = "vitrine:cards:stock:cache:v1";

        private readonly StockCardReadQuery _readQuery;
        private readonly VitrineListCache<VitrineStockCardDto> _cache;

        public StockCardCache(
            IConnectionMultiplexer redis,
            StockCardReadQuery readQuery,
            ILogger<StockCardCache> logger,
            TimeSpan ttl)
        {
            _readQuery = readQuery;
            _cache = new VitrineListCache<VitrineStockCardDto>(
                redis,
                logger,
                VitrineJsonContext.Default.ListVitrineStockCardDto,
                ttl);
        }

        public async Task<VitrineStockCardDto?> GetBySecidAsync(
            string secid,
            CancellationToken ct)
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

        private async Task<List<VitrineStockCardDto>> GetAllThroughCacheAsync(
            CancellationToken ct)
        {
            List<VitrineStockCardDto>? cached = await _cache.TryReadAsync(CacheKey);
            if (cached is not null)
                return cached;

            List<VitrineStockCardDto> fromDb = await _readQuery.GetAllAsync(ct);
            await _cache.WriteAsync(CacheKey, fromDb);
            return fromDb;
        }

        public async Task InvalidateAsync()
        {
            await _cache.InvalidateAsync(CacheKey);
        }
    }
}
