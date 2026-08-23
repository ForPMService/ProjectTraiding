using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>Захваченная заявка очереди удаления.</summary>
    public readonly record struct InstrumentDeletionClaim(Guid Id, string Secid);

    /// <summary>
    /// Читает и управляет захватами очереди заявок удаления. Активность заявки
    /// определяется статусом started, а claimed_at принадлежит только исполнителю.
    /// </summary>
    public sealed class InstrumentDeletionQueueReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public InstrumentDeletionQueueReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<InstrumentDeletionClaim?> ClaimNextAsync(CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_instrument_data_deletions
                SET claimed_at = now(), error_message = null
                WHERE id = (
                    SELECT id FROM moex_instrument_data_deletions
                    WHERE status = 'started'
                      AND claimed_at IS NULL
                      AND next_attempt_at <= now()
                    ORDER BY next_attempt_at, created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                RETURNING id, secid
                """, connection);

            await cmd.PrepareAsync(ct);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new InstrumentDeletionClaim(reader.GetGuid(0), reader.GetString(1));
        }

        /// <summary>При запуске исполнитель повторяет незавершённую очистку целиком.</summary>
        public async Task ReleaseInterruptedClaimsAsync(CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_instrument_data_deletions
                SET claimed_at = null
                WHERE status = 'started' AND claimed_at IS NOT NULL
                """, connection);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>Возвращает активную заявку в очередь до следующего интервала опроса.</summary>
        public async Task DeferAsync(
            Guid deletionId,
            string error,
            TimeSpan delay,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_instrument_data_deletions
                SET claimed_at = null,
                    error_message = @error,
                    next_attempt_at = now() + @delay
                WHERE id = @id
                """, connection);
            cmd.Parameters.Add("@error", NpgsqlDbType.Text).Value = error;
            cmd.Parameters.Add("@delay", NpgsqlDbType.Interval).Value = delay;
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = deletionId;

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
