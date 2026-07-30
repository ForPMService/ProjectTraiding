using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Читатель потока изменения справочника. Техническая механика находится в базовом типе.
    /// </summary>
    public sealed class CatalogEventReader : VitrineStreamReader
    {
        public CatalogEventReader(IConnectionMultiplexer redis, ILogger<CatalogEventReader> logger)
            : base(redis, logger, streamKey: "catalog:changed")
        {
        }
    }
}
