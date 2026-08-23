using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Loading.Planning;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class LoadedRangeCoverageReader
    {
        private readonly NpgsqlDataSource _dataSource;
        public LoadedRangeCoverageReader(NpgsqlDataSource dataSource) => _dataSource = dataSource;

        /// <summary>
        /// Покрытие по всем перечисленным инструментам одним запросом. Прежний поштучный
        /// запрос выполнялся на каждую пару инструмент × вид данных внутри трёх циклов
        /// планирования и открывал под каждый своё соединение.
        ///
        /// Отбора по диапазону дат здесь нет намеренно: диапазон свой у каждого инструмента,
        /// а лишние записи отсекает обрезка в LoadedRangeCoverageCalculator.Subtract.
        /// Число покрытых интервалов в итоге считается после обрезки и потому не меняется.
        /// </summary>
        public async Task<Dictionary<CoverageKey, List<CoverageInterval>>> GetCoveredRangesAsync(
            string[] secids, string storageTarget, CancellationToken ct)
        {
            Dictionary<CoverageKey, List<CoverageInterval>> coverage = new();
            if (secids.Length == 0)
                return coverage;

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT secid, market, boardid, data_kind, candle_interval, date_from, date_till
                FROM moex_loaded_ranges
                WHERE secid = ANY(@secids) AND storage_target = @storage_target AND status = 'ok'
                ORDER BY date_from, date_till
                """, connection);
            cmd.Parameters.Add("@secids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secids;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = storageTarget;

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                CoverageKey key = new CoverageKey(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetFieldValue<int>(4));

                if (!coverage.TryGetValue(key, out List<CoverageInterval>? intervals))
                {
                    intervals = new List<CoverageInterval>();
                    coverage.Add(key, intervals);
                }

                intervals.Add(new CoverageInterval(
                    reader.GetFieldValue<DateOnly>(5), reader.GetFieldValue<DateOnly>(6)));
            }

            return coverage;
        }
    }
}
