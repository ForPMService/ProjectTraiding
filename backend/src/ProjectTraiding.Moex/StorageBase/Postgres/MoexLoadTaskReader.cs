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

        public async Task<MoexLoadTask?> GetByIdAsync(Guid taskId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT id, secid, market, boardid, data_kind, candle_interval,
                       date_from, date_till, storage_target,
                       source_contract_version, writer_version, status
                FROM moex_load_tasks
                WHERE id = @id
                """, connection);
            cmd.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = taskId;

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new MoexLoadTask(
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
                WriterVersion: reader.GetString(10),
                Status: reader.GetString(11));
        }

        /// <summary>
        /// Атомарно берёт в работу самую старую задачу под ClickHouse в статусе pending
        /// (FIFO по created_at) и возвращает её идентификатор, либо null, если очереди нет.
        /// Один запрос: подзапрос блокирует строку с пропуском уже заблокированных другими
        /// дорожками (FOR UPDATE SKIP LOCKED), внешний UPDATE переводит её в running и чистит
        /// хвост прошлой попытки. Несколько дорожек берут разные задачи без холостых проигрышей.
        /// Вид данных здесь не различается — маршрутизацию по data_kind делает координатор.
        /// </summary>
        public async Task<Guid?> ClaimNextPendingTaskIdAsync(CancellationToken ct)
        {
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
                WHERE id = (
                    SELECT id FROM moex_load_tasks
                    WHERE status = 'pending'
                    AND storage_target = 'clickhouse'
                    ORDER BY created_at
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                    )
                RETURNING id
                """, connection);

            object? idObj = await cmd.ExecuteScalarAsync(ct);
            return idObj is Guid id ? id : null;
        }
    }
}
