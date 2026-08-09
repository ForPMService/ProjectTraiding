using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public readonly record struct PostgresDeleteCounts(
        int LoadedRangesDeleted,
        int StreamCursorsDeleted,
        int LoadTasksDeleted,
        TimeSpan Elapsed);

    /// <summary>
    /// Удаление рабочих данных инструмента из PostgreSQL: покрытие, отметки приёма,
    /// журнал заданий загрузки.
    ///
    /// Не удаляются: карточка инструмента, справочные детали акции и фьючерса,
    /// связи, тарифы, календарь, список наблюдения приёмника. Инструмент как
    /// сущность после удаления остаётся полностью работоспособным.
    ///
    /// Порядок удаления задан внешним ключом moex_loaded_ranges.last_task_id →
    /// moex_load_tasks (id): каскада у него нет, поэтому покрытие уходит раньше
    /// заданий. Обратный порядок даёт ошибку 23503.
    ///
    /// Все три удаления в одной транзакции: между ними база не должна оказаться
    /// в состоянии «задания есть, покрытия нет».
    /// </summary>
    public sealed class InstrumentPostgresDataDeleter
    {
        private const string Table = "moex_loaded_ranges";
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
                int rangesDeleted = await DeleteFromAsync(
                    connection, transaction,
                    "DELETE FROM moex_loaded_ranges WHERE secid = @secid",
                    secid, ct);

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
                ManagementWriterLogMessages.InstrumentPostgresDataDeleted(
                    _logger, secid, rangesDeleted, cursorsDeleted, tasksDeleted, elapsed);

                return new PostgresDeleteCounts(
                    rangesDeleted, cursorsDeleted, tasksDeleted, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(
                    _logger, ex, Table, ex.GetType().Name);
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
