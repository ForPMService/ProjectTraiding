using ClickHouse.Driver;
using ClickHouse.Driver.ADO.Parameters;
using ProjectTraiding.Moex.StorageBase.Postgres;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.ClickHouse
{
    /// <summary>
    /// Удаление всех данных инструмента из ClickHouse.
    ///
    /// Все семнадцать таблиц данных несут колонку secid, поэтому форма запроса
    /// одна на все — список таблиц перебирается, запрос не меняется.
    ///
    /// ALTER TABLE ... DELETE, а не облегчённое DELETE FROM: облегчённое удаление
    /// лишь помечает строки скрытыми, а физически убирает их когда-то потом.
    /// Задача просит освободить место, а не спрятать данные.
    ///
    /// Параметр ожидания со значением 1 переводит мутацию из отложенной в ожидаемую: без этой
    /// настройки запрос возвращается сразу, и задание удаления закрылось бы
    /// статусом 'finished' раньше, чем данные исчезли. Значение 1, а не 2,
    /// потому что узел ClickHouse в проекте одиночный, без реплик.
    ///
    /// Представления, читающие эти таблицы, в списке отсутствуют намеренно:
    /// собственных данных они не хранят и удалять из них нечего.
    /// </summary>
    public sealed class InstrumentClickHouseDataDeleter
    {
        private static readonly string[] Tables =
        {
            "moex_candles_1m",
            "moex_candles_10m",
            "moex_candles_1h",
            "moex_candles_1d",
            "moex_trades_stock",
            "moex_trades_futures",
            "moex_orderbook",
            "moex_trade_stats_5m_stock",
            "moex_trade_stats_5m_futures",
            "moex_ob_stats_5m_stock",
            "moex_ob_stats_5m_futures",
            "moex_order_stats_5m_stock",
            "moex_futoi_5m",
            "moex_hi2_daily_stock",
            "moex_hi2_daily_futures",
            "moex_megaalerts_stock",
            "moex_megaalerts_futures",
        };

        private readonly ClickHouseClient _client;
        private readonly ILogger<InstrumentClickHouseDataDeleter> _logger;

        public InstrumentClickHouseDataDeleter(
            ClickHouseClient client,
            ILogger<InstrumentClickHouseDataDeleter> logger)
        {
            _client = client;
            _logger = logger;
        }

        /// <summary>
        /// Возвращает число обработанных таблиц. Ошибка на любой таблице поднимается:
        /// частично очищенный ClickHouse при задании в статусе 'started' — верное
        /// состояние, фоновый исполнитель повторит очистку целиком; повтор безопасен
        /// на каждой таблице.
        /// </summary>
        public async Task<int> DeleteAsync(string secid, CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();

            QueryOptions options = new QueryOptions
            {
                CustomSettings = new Dictionary<string, object>
                {
                    ["mutations_sync"] = 1,
                },
            };

            for (int i = 0; i < Tables.Length; i++)
            {
                string table = Tables[i];

                // Имя таблицы подставляется в текст запроса, а не параметром: имена
                // взяты из константы этого класса, извне не приходят.
                string sql = $"ALTER TABLE {table} DELETE WHERE secid = {{secid:String}}";

                ClickHouseParameterCollection parameters = new ClickHouseParameterCollection();
                parameters.Add(new ClickHouseDbParameter
                {
                    ParameterName = "secid",
                    ClickHouseType = "String",
                    Value = secid,
                });

                await _client.ExecuteNonQueryAsync(sql, parameters, options, ct);

                MoexWriterLogMessages.InstrumentClickHouseTableCleared(
                    _logger, secid, table);
            }

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexWriterLogMessages.InstrumentClickHouseDataDeleted(
                _logger, secid, Tables.Length, elapsed);

            return Tables.Length;
        }
    }
}
