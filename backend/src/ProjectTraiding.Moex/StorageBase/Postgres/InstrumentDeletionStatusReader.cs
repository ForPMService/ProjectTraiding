using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>Последняя заявка удаления инструмента для ответа оператору.</summary>
    public sealed record InstrumentDeletionStatus(
        string Secid,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ClaimedAt,
        DateTimeOffset NextAttemptAt,
        string? ErrorMessage);

    /// <summary>Только чтение последнего состояния заявки удаления.</summary>
    public sealed class InstrumentDeletionStatusReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public InstrumentDeletionStatusReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<InstrumentDeletionStatus?> GetLatestAsync(string secid, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT secid, status, created_at, claimed_at, next_attempt_at, error_message
                FROM moex_instrument_data_deletions
                WHERE secid = @secid
                ORDER BY created_at DESC
                LIMIT 1
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new InstrumentDeletionStatus(
                Secid: reader.GetString(0),
                Status: reader.GetString(1),
                CreatedAt: reader.GetFieldValue<DateTimeOffset>(2),
                ClaimedAt: reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3),
                NextAttemptAt: reader.GetFieldValue<DateTimeOffset>(4),
                ErrorMessage: reader.IsDBNull(5) ? null : reader.GetString(5));
        }
    }
}
