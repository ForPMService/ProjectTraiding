using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Разрешение субъекта открытого интереса: контракт в серию, бессрочный фьючерс
    /// в себя, а уже переданный код серии остаётся без изменения.
    /// </summary>
    public sealed class FutoiSubjectReader
    {
        private readonly NpgsqlDataSource _dataSource;
        public FutoiSubjectReader(NpgsqlDataSource dataSource) => _dataSource = dataSource;

        public async Task<Dictionary<string, string>> ResolveAsync(string[] secids, CancellationToken ct)
        {
            Dictionary<string, string> result = new(StringComparer.Ordinal);
            if (secids.Length == 0)
                return result;

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT i.secid,
                       CASE
                           WHEN i.instrument_type = 'futures_series' THEN i.secid
                           WHEN f.last_trade_date >= DATE '2100-01-01' THEN i.secid
                           ELSE f.sectype
                       END AS subject
                FROM moex_instruments i
                LEFT JOIN moex_futures_details f ON f.secid = i.secid
                WHERE i.secid = ANY(@secids)
                  AND (CASE
                           WHEN i.instrument_type = 'futures_series' THEN i.secid
                           WHEN f.last_trade_date >= DATE '2100-01-01' THEN i.secid
                           ELSE f.sectype
                       END) IS NOT NULL
                """, connection);
            cmd.Parameters.Add("@secids", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secids;

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                result[reader.GetString(0)] = reader.GetString(1);
            return result;
        }
    }
}
