using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Чтение задачи загрузки по идентификатору для исполнения (внутреннее чтение контура Moex).
    /// </summary>
    public sealed class MoexLoadTaskReader
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexLoadTaskReader(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<bool> IsCancelRequestedAsync(Guid taskId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT cancel_requested_at IS NOT NULL
                FROM moex_load_tasks
                WHERE id = @id
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            await cmd.PrepareAsync(ct);

            // Типизированное чтение вместо возврата через объект: логическое значение
            // упаковывалось бы в кучу на каждую проверку. Отсутствующая строка даст ложь,
            // как и прежде: выражение сравнения с пустым значением само пустым не бывает.
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) && reader.GetBoolean(0);
        }

        /// <summary>
        /// Возвращает после аварийной остановки незавершённые задачи в очередь, а задачи с
        /// запросом отмены закрывает тем же правилом, что штатная отмена исполнителя.
        /// </summary>
        public async Task RecoverInterruptedTasksAsync(CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = CASE
                        WHEN cancel_requested_at IS NOT NULL THEN 'cancelled'
                        ELSE 'pending' END,
                    finished_at = CASE
                        WHEN cancel_requested_at IS NOT NULL THEN now()
                        ELSE null END,
                    stop_reason = CASE
                        WHEN cancel_requested_at IS NOT NULL THEN 'operator_cancelled'
                        ELSE null END,
                    error_message = null
                WHERE status = 'running'
                """, connection);
            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Захватывает следующую ожидающую задачу и возвращает её готовой строкой.
        /// </summary>
        public async Task<MoexLoadTask?> ClaimNextPendingTaskAsync(CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            // Блокировка транзакционная, снимается фиксацией: держится вокруг подбора,
            // а не вокруг загрузки. Число произвольное, но одно на все дорожки.
            await using (NpgsqlCommand lockCmd = new NpgsqlCommand(
                "SELECT pg_advisory_xact_lock(8771001)", connection, transaction))
            {
                await lockCmd.ExecuteNonQueryAsync(ct);
            }

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'running',
                    started_at = now(),
                    finished_at = null,
                    error_message = null,
                    stop_reason = null,
                    rows_loaded = 0,
                    attempt_count = attempt_count + 1
                WHERE id = (
                    SELECT id FROM moex_load_tasks t
                    WHERE t.status = 'pending'
                    AND t.cancel_requested_at IS NULL
                    AND t.storage_target = 'clickhouse'
                    AND NOT EXISTS (
                        SELECT 1 FROM moex_instrument_data_deletions d
                        WHERE d.secid = t.secid AND d.status = 'started'
                    )
                    AND NOT EXISTS (
                        SELECT 1 FROM moex_load_tasks r
                        WHERE r.status = 'running'
                          AND r.secid = t.secid
                          AND r.market = t.market
                          AND r.boardid = t.boardid
                          AND r.data_kind = t.data_kind
                          AND r.candle_interval IS NOT DISTINCT FROM t.candle_interval
                          AND r.storage_target = t.storage_target
                    )
                    ORDER BY t.created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                    )
                RETURNING id,
                          secid,
                          market,
                          boardid,
                          data_kind,
                          candle_interval,
                          date_from,
                          date_till,
                          storage_target,
                          source_contract_version,
                          writer_version
                """, connection, transaction);

            await cmd.PrepareAsync(ct);
            MoexLoadTask? task;
            await using (NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct))
            {
                task = await reader.ReadAsync(ct)
                    ? new MoexLoadTask(
                        Id: reader.GetGuid(0),
                        Secid: reader.GetString(1),
                        Market: reader.GetString(2),
                        Boardid: reader.GetString(3),
                        DataKind: reader.GetString(4),
                        CandleInterval: reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        DateFrom: reader.GetFieldValue<DateOnly>(6),
                        DateTill: reader.GetFieldValue<DateOnly>(7),
                        StorageTarget: reader.GetString(8),
                        SourceContractVersion: reader.GetString(9),
                        WriterVersion: reader.GetString(10))
                    : null;
            }

            // Читатель закрыт до фиксации.
            await transaction.CommitAsync(ct);
            return task;
        }
    }
}
