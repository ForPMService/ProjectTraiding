using ProjectTraiding.Management.StorageBase.Postgres;
using StackExchange.Redis;
using System.Diagnostics;

namespace ProjectTraiding.Management.StorageBase.Redis
{
    /// <summary>
    /// Удаление ключа Redis, принадлежащего одному инструменту: кеша покрытия витрины.
    ///
    /// Ключи собираются по известным образцам, а не перебором пространства ключей:
    /// команда сканирования на боевом Redis обходит все ключи и её незачем звать,
    /// когда полный список образцов известен на этапе сборки.
    ///
    /// Шаг вызывается ПОСЛЕ удаления строк PostgreSQL, и порядок обязателен.
    /// Ключ vitrine:loaded-ranges — не данные, а кеш поверх moex_loaded_ranges
    /// со сроком жизни в сутки: LoadedRangeCache при промахе идёт в базу и пишет
    /// прочитанное обратно. Сбрось его раньше базы — и первое же обращение
    /// витрины восстановит кеш со старым покрытием на сутки вперёд.
    /// Общие кеши витрины (каталог инструментов, карточки, тарифы) не трогаются:
    /// справочник при удалении рабочих данных не меняется.
    /// </summary>
    public sealed class InstrumentRedisDataDeleter
    {
        private const string VitrineLoadedRangesKeyPrefix = "vitrine:loaded-ranges:";
        private const string VitrineLoadedRangesKeySuffix = ":v1";

        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<InstrumentRedisDataDeleter> _logger;

        public InstrumentRedisDataDeleter(
            IConnectionMultiplexer redis,
            ILogger<InstrumentRedisDataDeleter> logger)
        {
            _redis = redis;
            _logger = logger;
        }

        /// <summary>Возвращает число фактически удалённых ключей.</summary>
        public async Task<long> DeleteAsync(string secid, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            long startTs = Stopwatch.GetTimestamp();
            IDatabase db = _redis.GetDatabase();

            RedisKey[] keys = new RedisKey[]
            {
                VitrineLoadedRangesKeyPrefix + secid + VitrineLoadedRangesKeySuffix,
            };

            long deleted = await db.KeyDeleteAsync(keys);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            ManagementWriterLogMessages.InstrumentRedisDataDeleted(
                _logger, secid, deleted, elapsed);

            return deleted;
        }
    }
}
