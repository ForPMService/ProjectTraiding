using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Писатель завершает выполнение задачи загрузки (moex_load_tasks): running → done,
    /// running → error, а прерванное выполнение финализирует в running → cancelled при
    /// наличии запроса отмены либо в running → error без него. Создание заданий и
    /// операторская отмена перешли к LoadTaskCommandWriter в этом же контуре; захват
    /// задания остаётся за MoexLoadTaskReader. Два писателя одной таблицы допустимы.
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
        /// Закрывает задачу успехом: running → done. finished_at = now().
        /// Если строка не в running — аномалия, ошибка.
        /// </summary>
        public async Task<bool> MarkDoneAsync(
            Guid taskId,
            long rowsLoaded,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'done', finished_at = now()
                WHERE id = @id AND status = 'running' AND cancel_requested_at IS NULL
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected != 1)
                return false;

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.TaskDone(_logger, taskId, rowsLoaded, elapsed);
            return true;
        }

        /// <summary>
        /// Закрывает задачу отказом: running → error. finished_at = now(), error_message.
        /// Если строка не в running — аномалия, ошибка.
        /// </summary>
        public async Task MarkErrorAsync(
            Guid taskId,
            string errorMessage,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'error', finished_at = now(),
                    error_message = @msg
                WHERE id = @id AND status = 'running'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;
            cmd.Parameters.Add("@msg", NpgsqlDbType.Text).Value = errorMessage;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected != 1)
                throw new InvalidOperationException(
                    $"Задача {taskId} не в статусе running — закрыть отказом нельзя (affected={affected}).");

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.TaskError(_logger, taskId, elapsed);
        }

        /// <summary>
        /// Закрывает прерванное задание: операторская отмена или ошибка остановки.
        /// </summary>
        public async Task<string?> FinalizeCancellationAsync(Guid taskId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = CASE
                        WHEN cancel_requested_at IS NOT NULL THEN 'cancelled'
                        ELSE 'error' END,
                    finished_at = now(),
                    error_message = CASE
                        WHEN cancel_requested_at IS NOT NULL THEN null
                        ELSE 'Historical load interrupted by application shutdown' END
                WHERE id = @id AND status = 'running'
                RETURNING status
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            object? status = await cmd.ExecuteScalarAsync(ct);
            return status as string;
        }
    }
}
