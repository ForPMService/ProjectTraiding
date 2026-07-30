using ProjectTraiding.Vitrine.Contracts;
using ProjectTraiding.Vitrine.Contracts.Dto;
using ProjectTraiding.Vitrine.StorageBase.Postgres;
using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Кеширующий читатель диапазонов загрузки по инструменту. Ключ и сброс остаются
    /// адресными: завершение загрузки по одному инструменту затрагивает только его ключ.
    /// </summary>
    public sealed class LoadedRangeCache
    {
        // Отметка версии в имени ключа разводит несовместимые формы значения.
        private const string KeyPrefix = "vitrine:loaded-ranges:";
        private const string KeySuffix = ":v1";

        private readonly LoadedRangeReadQuery _readQuery;
        private readonly VitrineListCache<VitrineLoadedRangeDto> _cache;

        public LoadedRangeCache(
            IConnectionMultiplexer redis,
            LoadedRangeReadQuery readQuery,
            ILogger<LoadedRangeCache> logger,
            TimeSpan ttl)
        {
            _readQuery = readQuery;
            _cache = new VitrineListCache<VitrineLoadedRangeDto>(
                redis,
                logger,
                VitrineJsonContext.Default.ListVitrineLoadedRangeDto,
                ttl);
        }

        private static string KeyFor(string secid)
        {
            return KeyPrefix + secid + KeySuffix;
        }

        public async Task<List<VitrineLoadedRangeDto>> GetBySecidAsync(
            string secid,
            CancellationToken ct)
        {
            string key = KeyFor(secid);
            List<VitrineLoadedRangeDto>? cached = await _cache.TryReadAsync(key);
            if (cached is not null)
                return cached;

            List<VitrineLoadedRangeDto> fromDb =
                await _readQuery.GetBySecidAsync(secid, ct);
            await _cache.WriteAsync(key, fromDb);
            return fromDb;
        }

        public async Task InvalidateAsync(string secid)
        {
            string key = KeyFor(secid);
            await _cache.InvalidateAsync(key);
        }
    }
}
