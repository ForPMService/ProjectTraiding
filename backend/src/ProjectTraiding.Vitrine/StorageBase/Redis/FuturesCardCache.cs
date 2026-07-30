using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Кеширующий читатель карточек фьючерсов. Кеш держит весь рынок под одним ключом,
    /// а предметная операция возвращает одну карточку по коду инструмента.
    /// </summary>
    public sealed class FuturesCardCache
    {
        // Отметка версии разводит несовместимые формы значения по разным ключам.
        private const string CacheKey = "vitrine:cards:futures:cache:v1";

        private readonly FuturesCardReadQuery _readQuery;
        private readonly VitrineListCache<VitrineFuturesCardDto> _cache;

        public FuturesCardCache(
            IConnectionMultiplexer redis,
            FuturesCardReadQuery readQuery,
            ILogger<FuturesCardCache> logger,
            TimeSpan ttl)
        {
            _readQuery = readQuery;
            _cache = new VitrineListCache<VitrineFuturesCardDto>(
                redis,
                logger,
                VitrineJsonContext.Default.ListVitrineFuturesCardDto,
                ttl);
        }

        public async Task<VitrineFuturesCardDto?> GetBySecidAsync(
            string secid,
            CancellationToken ct)
        {
            List<VitrineFuturesCardDto> all = await GetAllThroughCacheAsync(ct);

            // Поиск по коду инструмента прямым перебором: LINQ запрещён.
            foreach (VitrineFuturesCardDto card in all)
            {
                if (card.Secid == secid)
                    return card;
            }
            return null;
        }

        private async Task<List<VitrineFuturesCardDto>> GetAllThroughCacheAsync(
            CancellationToken ct)
        {
            List<VitrineFuturesCardDto>? cached = await _cache.TryReadAsync(CacheKey);
            if (cached is not null)
                return cached;

            List<VitrineFuturesCardDto> fromDb = await _readQuery.GetAllAsync(ct);
            await _cache.WriteAsync(CacheKey, fromDb);
            return fromDb;
        }

        public async Task InvalidateAsync()
        {
            await _cache.InvalidateAsync(CacheKey);
        }
    }
}
