using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Infrastructure;
using System;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Писатель покрытия приёма в moex_loaded_ranges (V023). Ведёт журнал непрерывности:
    /// один сеанс приёма ряда — одна строка. Открытие ставит status='open' и границы времени,
    /// сердцебиение двигает time_till, штатная остановка закрывает в 'closed', а строки,
    /// пережившие падение процесса, при следующем старте закрываются в 'crashed'.
    ///
    /// Это НЕ идемпотентность вставки (её несёт токен ClickHouse) и НЕ высокая отметка
    /// (её несёт moex_stream_cursors). Это средство честности: по закрытым строкам видно,
    /// где в приёме была дыра, чтобы анализ не считал признаки по пустоте.
    /// </summary>
    public sealed class StreamCoverageWriter
    {
        private const string StorageTarget = "clickhouse";

        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<StreamCoverageWriter> _logger;

        public StreamCoverageWriter(NpgsqlDataSource dataSource, ILogger<StreamCoverageWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        /// <summary>
        /// Закрывает в 'crashed' все строки этого ряда, оставшиеся 'open' от прошлого запуска.
        /// Вызывается при старте приёма ряда ДО открытия нового сеанса.
        /// time_till не трогаем: он достоверен с точностью до последнего сердцебиения (V023).
        /// </summary>
        public async Task CloseCrashedAsync(
            string secid, string market, string boardid, string dataKind, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_loaded_ranges
                SET status = 'crashed'
                WHERE secid = @secid
                  AND market = @market
                  AND boardid = @boardid
                  AND data_kind = @data_kind
                  AND candle_interval IS NULL
                  AND storage_target = @storage_target
                  AND status = 'open'
                """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = StorageTarget;

            int closed = await cmd.ExecuteNonQueryAsync(ct);
            if (closed > 0)
                MoexStreamCoverageLogMessages.CrashedClosed(_logger, secid, dataKind, closed);
        }

        /// <summary>
        /// Открывает новый сеанс приёма ряда: одна строка status='open', границы времени = сейчас.
        /// Возвращает id строки — им двигают сердцебиение и его же закрывают.
        /// </summary>
        public async Task<long> OpenSessionAsync(
            string secid, string market, string boardid, string dataKind, CancellationToken ct)
        {
            DateOnly today = MoexTime.Today;
            DateTime now = ToUtc(MoexTime.Now);

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_loaded_ranges
                    (secid, market, boardid, data_kind, candle_interval,
                     date_from, date_till, time_from, time_till,
                     rows_total, storage_target, status,
                     source_contract_version, writer_version)
                VALUES
                    (@secid, @market, @boardid, @data_kind, NULL,
                     @date, @date, @now, @now,
                     0, @storage_target, 'open',
                     'moex_realtime_v1', 'clickhouse_receiver_v1')
                RETURNING id
                """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;
            cmd.Parameters.Add("@date", NpgsqlDbType.Date).Value = today;
            cmd.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = now;
            cmd.Parameters.Add("@storage_target", NpgsqlDbType.Text).Value = StorageTarget;

            object? scalar = await cmd.ExecuteScalarAsync(ct);
            long id = (long)scalar!;
            MoexStreamCoverageLogMessages.SessionOpened(_logger, secid, dataKind, id);
            return id;
        }

        /// <summary>
        /// Сердцебиение: двигает конец открытого сеанса и накопленный счётчик строк.
        /// </summary>
        public async Task HeartbeatAsync(long sessionId, long rowsTotal, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_loaded_ranges
                SET time_till = @now, rows_total = @rows
                WHERE id = @id AND status = 'open'
                """, connection);

            cmd.Parameters.Add("@id", NpgsqlDbType.Bigint).Value = sessionId;
            cmd.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = ToUtc(MoexTime.Now);
            cmd.Parameters.Add("@rows", NpgsqlDbType.Bigint).Value = rowsTotal;

            await cmd.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Штатное закрытие сеанса: остановка службы, конец торгов, разрыв соединения.
        /// </summary>
        public async Task CloseSessionAsync(long sessionId, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_loaded_ranges
                SET status = 'closed', time_till = @now
                WHERE id = @id AND status = 'open'
                """, connection);

            cmd.Parameters.Add("@id", NpgsqlDbType.Bigint).Value = sessionId;
            cmd.Parameters.Add("@now", NpgsqlDbType.TimestampTz).Value = ToUtc(MoexTime.Now);

            await cmd.ExecuteNonQueryAsync(ct);
            MoexStreamCoverageLogMessages.SessionClosed(_logger, sessionId);
        }

        private static DateTime ToUtc(DateTime value)
        {
            return DateTime.SpecifyKind(value - MoexTime.Offset, DateTimeKind.Utc);
        }
    }
}
