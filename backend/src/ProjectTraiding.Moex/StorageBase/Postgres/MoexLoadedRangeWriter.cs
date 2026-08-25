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
    /// Повтор диапазона ожидаем, поэтому UPSERT.
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
            long rowsSkipped,
            CancellationToken ct)
        {
            long startTs = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);

            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_loaded_ranges
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, last_success_at, last_task_id,
                     rows_total, rows_skipped, storage_target, status,
                     source_contract_version, writer_version)
                VALUES
                    (@secid, @market, @boardid, @data_kind, @candle_interval,
                     @date_from, @date_till, now(), @last_task_id,
                     @rows_total, @rows_skipped, @storage_target, 'ok',
                     @scv, @wv)
                ON CONFLICT ON CONSTRAINT uq_moex_loaded_ranges_span
                DO UPDATE SET
                     last_success_at = now(),
                     last_task_id = @last_task_id,
                     rows_total = @rows_total,
                     rows_skipped = @rows_skipped,
                     status = 'ok',
                     source_contract_version = @scv,
                     writer_version = @wv
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
            cmd.Parameters.Add("@rows_skipped", NpgsqlDbType.Bigint).Value = rowsSkipped;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = task.StorageTarget;
            cmd.Parameters.Add("@scv", NpgsqlDbType.Text).Value = task.SourceContractVersion;
            cmd.Parameters.Add("@wv", NpgsqlDbType.Text).Value = task.WriterVersion;

            await cmd.ExecuteNonQueryAsync(ct);

            TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
            MoexLoadTaskLogMessages.RangeRecorded(_logger, task.Secid, task.DataKind, rowsTotal, elapsed);
        }

        /// <summary>
        /// Переписывает успешное историческое покрытие, пересекающее диапазон задачи, до
        /// удаления данных из ClickHouse. Остатки создаются только из полного покрытия:
        /// rows_skipped у них равен нулю, а rows_total намеренно обнулён.
        /// </summary>
        public async Task RewriteOverlappingHistoryAsync(MoexLoadTask task, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            int affected = 0;

            await using (NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_loaded_ranges
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, last_success_at, last_task_id,
                     rows_total, rows_skipped, storage_target, status,
                     source_contract_version, writer_version)
                SELECT secid, market, boardid, data_kind, candle_interval,
                       date_from, @date_from - 1, last_success_at, last_task_id,
                       0, 0, storage_target, status,
                       source_contract_version, writer_version
                FROM moex_loaded_ranges
                WHERE secid = @secid AND market = @market AND boardid = @boardid
                  AND data_kind = @data_kind
                  AND candle_interval IS NOT DISTINCT FROM @candle_interval
                  AND storage_target = @storage_target
                  AND time_from IS NULL AND status = 'ok'
                  AND rows_skipped = 0
                  AND date_from <= @date_till AND date_till >= @date_from
                  AND date_from < @date_from
                ON CONFLICT ON CONSTRAINT uq_moex_loaded_ranges_span DO NOTHING
                """, connection, transaction))
            {
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = task.Secid;
                cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = task.Market;
                cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = task.Boardid;
                cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = task.DataKind;
                cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                    (object?)task.CandleInterval ?? DBNull.Value;
                cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = task.StorageTarget;
                cmd.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = task.DateFrom;
                cmd.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = task.DateTill;
                affected += await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_loaded_ranges
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, last_success_at, last_task_id,
                     rows_total, rows_skipped, storage_target, status,
                     source_contract_version, writer_version)
                SELECT secid, market, boardid, data_kind, candle_interval,
                       @date_till + 1, date_till, last_success_at, last_task_id,
                       0, 0, storage_target, status,
                       source_contract_version, writer_version
                FROM moex_loaded_ranges
                WHERE secid = @secid AND market = @market AND boardid = @boardid
                  AND data_kind = @data_kind
                  AND candle_interval IS NOT DISTINCT FROM @candle_interval
                  AND storage_target = @storage_target
                  AND time_from IS NULL AND status = 'ok'
                  AND rows_skipped = 0
                  AND date_from <= @date_till AND date_till >= @date_from
                  AND date_till > @date_till
                ON CONFLICT ON CONSTRAINT uq_moex_loaded_ranges_span DO NOTHING
                """, connection, transaction))
            {
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = task.Secid;
                cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = task.Market;
                cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = task.Boardid;
                cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = task.DataKind;
                cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                    (object?)task.CandleInterval ?? DBNull.Value;
                cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = task.StorageTarget;
                cmd.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = task.DateFrom;
                cmd.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = task.DateTill;
                affected += await cmd.ExecuteNonQueryAsync(ct);
            }

            await using (NpgsqlCommand cmd = new NpgsqlCommand("""
                DELETE FROM moex_loaded_ranges
                WHERE secid = @secid AND market = @market AND boardid = @boardid
                  AND data_kind = @data_kind
                  AND candle_interval IS NOT DISTINCT FROM @candle_interval
                  AND storage_target = @storage_target
                  AND time_from IS NULL AND status = 'ok'
                  AND date_from <= @date_till AND date_till >= @date_from
                """, connection, transaction))
            {
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = task.Secid;
                cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = task.Market;
                cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = task.Boardid;
                cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = task.DataKind;
                cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                    (object?)task.CandleInterval ?? DBNull.Value;
                cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = task.StorageTarget;
                cmd.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = task.DateFrom;
                cmd.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = task.DateTill;
                affected += await cmd.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            MoexWriterLogMessages.HistoryCoverageRewritten(
                _logger, task.Secid, task.DataKind, affected);
        }
    }
}
