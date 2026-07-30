using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Читатель потока изменения тарифов. Техническая механика находится в базовом типе.
    /// </summary>
    public sealed class TariffEventReader : VitrineStreamReader
    {
        public TariffEventReader(IConnectionMultiplexer redis, ILogger<TariffEventReader> logger)
            : base(redis, logger, streamKey: "tariffs:changed")
        {
        }
    }
}
