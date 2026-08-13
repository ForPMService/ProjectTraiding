using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Строка инструмента для приёмника: код и рынок. Режим торгов приёмник проставляет
    /// по рынку (TQBR у акций, RFUD у фьючерсов) — так же, как методы MoexRealtimeRestClient.
    /// </summary>
    public readonly record struct ReceiverInstrument(string Secid, string Market);

    /// <summary>
    /// Чтение списка наблюдения приёмника реального времени (контур Moex): какие инструменты
    /// и виды данных собирать. Читает moex_realtime_subscriptions, НЕ весь справочник —
    /// справочник отвечает «что известно», подписки отвечают «что собирать». Рынок берётся
    /// из moex_instruments соединением. Отдельный от витринного InstrumentReadQuery:
    /// контуры не делят код (инвариант проекта).
    /// </summary>
    public sealed class MoexReceiverInstrumentReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexReceiverInstrumentReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        /// <summary>
        /// Включённые подписки одного вида данных (trades или orderbook) с рынком инструмента.
        /// Пустой результат означает, что собирать по этому виду нечего.
        /// Инструмент, по которому идёт удаление, выпадает из списка наблюдения на
        /// ближайшем обороте; уже начавшийся оборот доработает по прежнему составу —
        /// ограничение разобрано в разделе 5.2 задания.
        /// </summary>
        public async Task<IReadOnlyList<ReceiverInstrument>> GetEnabledForDataKindAsync(
            string dataKind, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT i.secid, i.instrument_type
                FROM moex_realtime_subscriptions s
                JOIN moex_instruments i ON i.secid = s.secid
                WHERE s.data_kind = @data_kind AND s.enabled
                  -- Серия не торгуется: приёмник не знает для неё кода площадки.
                  AND i.instrument_type <> 'futures_series'
                  AND NOT EXISTS (
                      SELECT 1 FROM moex_instrument_data_deletions d
                      WHERE d.secid = s.secid AND d.status = 'started'
                  )
                ORDER BY i.secid
                """, connection);
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;

            List<ReceiverInstrument> result = new List<ReceiverInstrument>();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                result.Add(new ReceiverInstrument(
                    Secid: reader.GetString(0),
                    Market: reader.GetString(1)));
            }

            return result;
        }
    }
}
