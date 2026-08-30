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
    /// наличии запроса отмены либо в running → pending без него. Создание заданий и
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
        public async Task MarkDoneAsync(
            Guid taskId,
            long rowsLoaded,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_load_tasks
                SET status = 'done', finished_at = now()
                WHERE id = @id AND status = 'running'
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected != 1)
                throw new InvalidOperationException(
                    $"Задача {taskId} не в статусе running — закрыть успехом нельзя (affected={affected}).");

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.TaskDone(_logger, taskId, rowsLoaded, elapsed);
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
        /// Закрывает выполняющееся задание после отмены, выбирая финал по актуальному запросу
        /// оператора: есть запрос — cancelled, нет — возврат в очередь. Возвращает новый статус
        /// либо null, если строки в running уже нет.
        /// Пустой результат — не отказ, а безопасная гонка с естественным завершением: успевшее
        /// закрыться успехом задание остаётся успешным. При возврате в очередь текст ошибки
        /// очищается; захват чистит то же поле при следующем запуске.
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
