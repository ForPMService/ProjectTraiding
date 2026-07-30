using StackExchange.Redis;

namespace ProjectTraiding.Vitrine.StorageBase.Redis
{
    /// <summary>
    /// Читатель потока изменения диапазонов. Техническая механика находится в базовом типе.
    /// Код инструмента из записи разбирает слушатель, не читатель.
    /// </summary>
    public sealed class LoadedRangeEventReader : VitrineStreamReader
    {
        public LoadedRangeEventReader(
            IConnectionMultiplexer redis,
            ILogger<LoadedRangeEventReader> logger)
            : base(redis, logger, streamKey: "loaded-ranges:changed")
        {
        }
    }
}
