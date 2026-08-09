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
        /// Атомарно берёт задачу в работу: pending или error → running.
        /// Чистит весь хвост прошлой попытки. started_at = now(), attempt_count += 1.
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
                    attempt_count = attempt_count + 1
                WHERE id = @id AND status IN ('pending', 'error', 'partial')
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
        /// Закрывает задачу частичной загрузкой: running → partial.
        /// ВНИМАНИЕ (А1): координатор больше НЕ вызывает этот метод. Срабатывание защитного
        /// предела страниц теперь закрывается через MarkErrorAsync (stop_reason='safety_cap_hit')
        /// без записи покрытия, а автоподбор берёт только 'pending'. Метод оставлен для возможного
        /// ручного сценария; статус 'partial' сохранён в схеме. Кандидат на удаление при ревизии
        /// мёртвого кода (блок Д3), если ручной путь его так и не использует.
        /// </summary>
        public async Task MarkPartialAsync(
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
                SET status = 'partial', finished_at = now(),
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
                    $"Задача {taskId} не в статусе running — закрыть частичной нельзя (affected={affected}).");

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.TaskPartial(_logger, taskId, rowsLoaded, elapsed);
        }

        /// <summary>
        /// Возвращает прерванную остановкой хоста задачу в очередь: running → pending,
        /// finished_at обнуляется. Вызывается координатором из catch(OperationCanceledException)
        /// вместо пометки error — иначе задача осталась бы сиротой, ведь автоподбор error не
        /// берёт. После перезапуска хоста автоподбор снова возьмёт её из pending. Если строка
        /// уже не в running (успела закрыться другим путём) — ничего не делаем: гонка с
        /// завершением здесь безопасна, поэтому исключение при affected≠1 НЕ бросаем.
        /// </summary>
        public async Task RequeueAfterCancelAsync(Guid taskId, CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'pending', finished_at = null
                WHERE id = @id AND status = 'running'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);

            MoexLoadTaskLogMessages.TaskRequeuedAfterCancel(_logger, taskId, affected, elapsed);
        }
    }
}
