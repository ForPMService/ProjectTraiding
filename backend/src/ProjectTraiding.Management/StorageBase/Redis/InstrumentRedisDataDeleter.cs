using ProjectTraiding.Management.StorageBase.Postgres;
using StackExchange.Redis;
using System.Diagnostics;

namespace ProjectTraiding.Management.StorageBase.Redis
{
    /// <summary>
    /// Удаление ключей Redis, принадлежащих одному инструменту: последние принятые
    /// значения приёма и кеш покрытия витрины.
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
    ///
    /// Ключ прогресса задания load:task:progress:{taskId} не удаляется: он привязан
    /// к заданию, а не к инструменту, и уходит сам по сроку годности.
    ///
    /// Общие кеши витрины (каталог инструментов, карточки, тарифы) не трогаются:
    /// справочник при удалении рабочих данных не меняется.
    /// </summary>
    public sealed class InstrumentRedisDataDeleter
    {
        private const string StockTradeKeyPrefix = "moex:latest:trade:stock:v1:";
        private const string FuturesTradeKeyPrefix = "moex:latest:trade:futures:v1:";
        private const string OrderbookKeyPrefix = "moex:latest:orderbook:v1:";
        private const string CandleKeyPrefix = "moex:latest:candle:1m:v1:";
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
                StockTradeKeyPrefix + secid,
                FuturesTradeKeyPrefix + secid,
                OrderbookKeyPrefix + secid,
                CandleKeyPrefix + secid,
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
