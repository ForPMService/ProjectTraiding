using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class RealtimeSubscriptionReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public RealtimeSubscriptionReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<string[]> GetEnabledDataKindsAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
            SELECT data_kind
            FROM moex_realtime_subscriptions
            WHERE secid = @secid AND enabled = true
            ORDER BY data_kind
            """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            var dataKinds = new List<string>();
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                dataKinds.Add(reader.GetString(0));

            return dataKinds.ToArray();
        }
    }
}
