using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Кеширующий читатель тарифов брокеров по схеме «чтение через кеш».
    /// Единственный владелец записи ключа vitrine:tariffs:cache:v1 — витрина.
    /// </summary>
    public sealed class BrokerTariffCache
    {
        // Отметка версии разводит несовместимые формы значения по разным ключам.
        private const string CacheKey = "vitrine:tariffs:cache:v1";

        private readonly BrokerTariffReadQuery _readQuery;
        private readonly VitrineListCache<VitrineBrokerTariffDto> _cache;

        public BrokerTariffCache(
            IConnectionMultiplexer redis,
            BrokerTariffReadQuery readQuery,
            ILogger<BrokerTariffCache> logger,
            TimeSpan ttl)
        {
            _readQuery = readQuery;
            _cache = new VitrineListCache<VitrineBrokerTariffDto>(
                redis,
                logger,
                VitrineJsonContext.Default.ListVitrineBrokerTariffDto,
                ttl);
        }

        public async Task<List<VitrineBrokerTariffDto>> GetAllAsync(CancellationToken ct)
        {
            List<VitrineBrokerTariffDto>? cached = await _cache.TryReadAsync(CacheKey);
            if (cached is not null)
                return cached;

            List<VitrineBrokerTariffDto> fromDb = await _readQuery.GetAllAsync(ct);
            await _cache.WriteAsync(CacheKey, fromDb);
            return fromDb;
        }

        public async Task InvalidateAsync()
        {
            await _cache.InvalidateAsync(CacheKey);
        }
    }
}
