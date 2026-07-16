using Npgsql;
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
    /// Чтение полного списка инструментов для приёмника реального времени (контур Moex).
    /// Отдельный от витринного InstrumentReadQuery: контуры не делят код (инвариант проекта).
    /// Возвращает только то, что нужно приёмнику: код и рынок.
    /// </summary>
    public sealed class MoexReceiverInstrumentReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexReceiverInstrumentReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<IReadOnlyList<ReceiverInstrument>> GetAllAsync(CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT secid, instrument_type
                FROM moex_instruments
                ORDER BY secid
                """, connection);

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
