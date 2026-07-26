using Npgsql;
using NpgsqlTypes;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Запись факта успешно загруженного диапазона (moex_loaded_ranges).
    /// Единица учёта — диапазон дат, ключ — составная уникальность таблицы.
    /// Повтор диапазона ожидаем, поэтому UPSERT. Токен — аудиторный след.
    /// </summary>
    public sealed class MoexLoadedRangeWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<MoexLoadedRangeWriter> _logger;

        public MoexLoadedRangeWriter(NpgsqlDataSource dataSource, ILogger<MoexLoadedRangeWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task UpsertAsync(
            MoexLoadTask task,
            long rowsTotal,
            string? lastDeduplicationToken,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_loaded_ranges
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, last_success_at, last_task_id,
                     rows_total, storage_target, status,
                     source_contract_version, writer_version, last_insert_deduplication_token)
                VALUES
                    (@secid, @market, @boardid, @data_kind, @candle_interval,
                     @date_from, @date_till, now(), @last_task_id,
                     @rows_total, @storage_target, 'ok',
                     @scv, @wv, @token)
                ON CONFLICT ON CONSTRAINT uq_moex_loaded_ranges_span
                DO UPDATE SET
                     last_success_at = now(),
                     last_task_id = @last_task_id,
                     rows_total = @rows_total,
                     status = 'ok',
                     source_contract_version = @scv,
                     writer_version = @wv,
                     last_insert_deduplication_token = @token
                """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = task.Secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = task.Market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = task.Boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = task.DataKind;
            cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                (object?)task.CandleInterval ?? DBNull.Value;
            cmd.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = task.DateFrom;
            cmd.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = task.DateTill;
            cmd.Parameters.Add("@last_task_id", NpgsqlDbType.Uuid).Value = task.Id;
            cmd.Parameters.Add("@rows_total", NpgsqlDbType.Bigint).Value = rowsTotal;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = task.StorageTarget;
            cmd.Parameters.Add("@scv", NpgsqlDbType.Text).Value = task.SourceContractVersion;
            cmd.Parameters.Add("@wv", NpgsqlDbType.Text).Value = task.WriterVersion;
            cmd.Parameters.Add("@token", NpgsqlDbType.Text).Value =
                (object?)lastDeduplicationToken ?? DBNull.Value;

            await cmd.ExecuteNonQueryAsync(ct);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.RangeRecorded(_logger, task.Secid, task.DataKind, rowsTotal, elapsed);
        }
    }
}
