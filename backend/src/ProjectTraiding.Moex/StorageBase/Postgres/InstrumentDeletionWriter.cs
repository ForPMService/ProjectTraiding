using Npgsql;
using NpgsqlTypes;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Владелец таблицы moex_instrument_data_deletions: постановка заявки и её
    /// закрытие после успешной очистки всех хранилищ.
    /// </summary>
    public sealed class InstrumentDeletionWriter
    {
        private const string Table = "moex_instrument_data_deletions";
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<InstrumentDeletionWriter> _logger;

        public InstrumentDeletionWriter(
            NpgsqlDataSource dataSource,
            ILogger<InstrumentDeletionWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        /// <summary>
        /// Ставит заявку удаления со статусом 'started'. Возврат null означает, что
        /// по инструменту уже есть активная заявка.
        /// </summary>
        public async Task<Guid?> TryStartAsync(string secid, CancellationToken ct)
        {
            MoexWriterLogMessages.WriteStarted(_logger, Table, 1);

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_instrument_data_deletions (secid)
                VALUES (@secid)
                ON CONFLICT (secid) WHERE status = 'started'
                DO NOTHING
                RETURNING id
                """, connection);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

            object? idObj = await cmd.ExecuteScalarAsync(ct);
            return idObj is Guid id ? id : null;
        }

        /// <summary>
        /// Закрывает заявку: 'started' → 'finished'. Вызывается только после того,
        /// как данные удалены из всех хранилищ.
        /// </summary>
        public async Task MarkFinishedAsync(Guid deletionId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_instrument_data_deletions
                SET status = 'finished'
                WHERE id = @id AND status = 'started'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = deletionId;

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
