using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public readonly record struct PostgresDeleteCounts(
        int StreamCursorsDeleted,
        int LoadTasksDeleted);

    /// <summary>
    /// Удаление рабочих данных инструмента из PostgreSQL: отметки приёма,
    /// журнал заданий загрузки.
    ///
    /// Не удаляются: карточка инструмента, справочные детали акции и фьючерса,
    /// связи, тарифы, календарь, список наблюдения приёмника. Инструмент как
    /// сущность после удаления остаётся полностью работоспособным.
    ///
    /// Курсоры и задания удаляются в одной транзакции.
    /// </summary>
    public sealed class InstrumentPostgresDataDeleter
    {
        private const string Tables = "moex_stream_cursors, moex_load_tasks";
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<InstrumentPostgresDataDeleter> _logger;

        public InstrumentPostgresDataDeleter(
            NpgsqlDataSource dataSource,
            ILogger<InstrumentPostgresDataDeleter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<PostgresDeleteCounts> DeleteAsync(string secid, CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                int cursorsDeleted = await DeleteFromAsync(
                    connection, transaction,
                    "DELETE FROM moex_stream_cursors WHERE secid = @secid",
                    secid, ct);

                int tasksDeleted = await DeleteFromAsync(
                    connection, transaction,
                    "DELETE FROM moex_load_tasks WHERE secid = @secid",
                    secid, ct);

                await transaction.CommitAsync(ct);

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                MoexWriterLogMessages.InstrumentPostgresDataDeleted(
                    _logger, secid, cursorsDeleted, tasksDeleted, elapsed);

                return new PostgresDeleteCounts(
                    cursorsDeleted, tasksDeleted);
            }
            catch (Exception ex)
            {
                MoexWriterLogMessages.WriteRolledBack(
                    _logger, ex, Tables, secid, 0, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private static async Task<int> DeleteFromAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            string sql,
            string secid,
            CancellationToken ct)
        {
            await using NpgsqlCommand cmd = new NpgsqlCommand(sql, connection, transaction);
            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
            return await cmd.ExecuteNonQueryAsync(ct);
        }
    }
}
