using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Писатель жизненного цикла задачи загрузки (moex_load_tasks): взять в работу,
    /// закрыть успехом, закрыть отказом. Создание задачи — забота команды Management.
    /// </summary>
    public sealed class MoexLoadTaskWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<MoexLoadTaskWriter> _logger;

        public MoexLoadTaskWriter(NpgsqlDataSource dataSource, ILogger<MoexLoadTaskWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        /// <summary>
        /// Атомарно берёт задачу в работу: pending, error или partial → running.
        /// Pending с незавершённым запросом отмены не запускается; явный повтор error или
        /// partial начинает новую попытку и очищает запрос вместе с хвостом прошлой.
        /// started_at = now(), attempt_count += 1.
        /// Возврат true — взяли; false — задачи нет, она уже running/done либо по
        /// инструменту идёт удаление данных.
        /// </summary>
        public async Task<bool> MarkRunningAsync(Guid taskId, CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'running',
                    started_at = now(),
                    finished_at = null,
                    error_message = null,
                    stop_reason = null,
                    rows_loaded = 0,
                    last_insert_deduplication_token = null,
                    cancel_requested_at = null,
                    attempt_count = attempt_count + 1
                WHERE id = @id AND status IN ('pending', 'error', 'partial')
                  AND (status <> 'pending' OR cancel_requested_at IS NULL)
                  AND NOT EXISTS (
                      SELECT 1 FROM moex_instrument_data_deletions d
                      WHERE d.secid = moex_load_tasks.secid AND d.status = 'started'
                  )
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);

            if (affected == 1)
            {
                MoexLoadTaskLogMessages.TaskRunning(_logger, taskId, elapsed);
                return true;
            }

            MoexLoadTaskLogMessages.TaskClaimMissed(_logger, taskId);
            return false;
        }

        /// <summary>
        /// Закрывает задачу успехом: running → done. finished_at = now(), rows_loaded,
        /// stop_reason, аудиторный токен. Если строка не в running — аномалия, ошибка.
        /// </summary>
        public async Task MarkDoneAsync(
            Guid taskId,
            long rowsLoaded,
            string? stopReason,
            string? lastDeduplicationToken,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'done', finished_at = now(),
                    rows_loaded = @rows, stop_reason = @stop_reason,
                    last_insert_deduplication_token = @token
                WHERE id = @id AND status = 'running'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;
            cmd.Parameters.Add("@rows", NpgsqlDbType.Bigint).Value = rowsLoaded;
            cmd.Parameters.Add("@stop_reason", NpgsqlDbType.Text).Value = (object?)stopReason ?? DBNull.Value;
            cmd.Parameters.Add("@token", NpgsqlDbType.Text).Value = (object?)lastDeduplicationToken ?? DBNull.Value;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected != 1)
                throw new InvalidOperationException(
                    $"Задача {taskId} не в статусе running — закрыть успехом нельзя (affected={affected}).");

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.TaskDone(_logger, taskId, rowsLoaded, elapsed);
        }

        /// <summary>
        /// Закрывает задачу отказом: running → error. finished_at = now(), error_message,
        /// stop_reason (отмена помечается явной причиной). Если строка не в running — аномалия, ошибка.
        /// </summary>
        public async Task MarkErrorAsync(
            Guid taskId,
            string errorMessage,
            string? stopReason,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'error', finished_at = now(),
                    error_message = @msg, stop_reason = @stop_reason
                WHERE id = @id AND status = 'running'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;
            cmd.Parameters.Add("@msg", NpgsqlDbType.Text).Value = errorMessage;
            cmd.Parameters.Add("@stop_reason", NpgsqlDbType.Text).Value = (object?)stopReason ?? DBNull.Value;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected != 1)
                throw new InvalidOperationException(
                    $"Задача {taskId} не в статусе running — закрыть отказом нельзя (affected={affected}).");

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.TaskError(_logger, taskId, elapsed);
        }

        /// <summary>
        /// Закрывает выполняющееся задание после отмены, выбирая финал по актуальному запросу
        /// оператора: есть запрос — cancelled, нет — возврат в очередь. Возвращает новый статус
        /// либо null, если строки в running уже нет.
        /// Пустой результат — не отказ, а безопасная гонка с естественным завершением: успевшее
        /// закрыться успехом задание остаётся успешным. При возврате в очередь причина остановки
        /// и текст ошибки очищаются; MarkRunningAsync чистит те же поля при следующем захвате.
        /// </summary>
        public async Task<string?> FinalizeCancellationAsync(Guid taskId, CancellationToken ct)
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
                WHERE id = @id AND status = 'running'
                RETURNING status
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            object? status = await cmd.ExecuteScalarAsync(ct);
            return status as string;
        }
    }
}
