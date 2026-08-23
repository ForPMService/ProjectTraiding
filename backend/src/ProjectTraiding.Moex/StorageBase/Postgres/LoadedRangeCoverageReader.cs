using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Loading.Planning;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class LoadedRangeCoverageReader
    {
        private readonly NpgsqlDataSource _dataSource;
        public LoadedRangeCoverageReader(NpgsqlDataSource dataSource) => _dataSource = dataSource;

        public async Task<IReadOnlyList<CoverageInterval>> GetCoveredRangesAsync(
            string secid, string market, string boardid, string dataKind, int? candleInterval,
            string storageTarget, DateOnly dateFrom, DateOnly dateTill, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT date_from, date_till FROM moex_loaded_ranges
                WHERE secid = @secid AND market = @market AND boardid = @boardid
                  AND data_kind = @data_kind
                  AND candle_interval IS NOT DISTINCT FROM @candle_interval
                  AND storage_target = @storage_target AND status = 'ok'
                  AND date_from <= @date_till AND date_till >= @date_from
                ORDER BY date_from, date_till
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;
            cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value = (object?)candleInterval ?? DBNull.Value;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = storageTarget;
            cmd.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = dateFrom;
            cmd.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = dateTill;

            List<CoverageInterval> ranges = new();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                ranges.Add(new CoverageInterval(reader.GetFieldValue<DateOnly>(0), reader.GetFieldValue<DateOnly>(1)));
            return ranges;
        }
    }
}
