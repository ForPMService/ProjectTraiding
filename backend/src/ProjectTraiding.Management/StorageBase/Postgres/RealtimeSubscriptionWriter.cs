using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class RealtimeSubscriptionWriter
    {
        private const string Table = "moex_realtime_subscriptions";
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<RealtimeSubscriptionWriter> _logger;

        public RealtimeSubscriptionWriter(
            NpgsqlDataSource dataSource,
            ILogger<RealtimeSubscriptionWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        /// Возврат RowsWritten = 0 означает, что по инструменту идёт удаление данных:
        /// при обычном ходе строка либо вставляется, либо обновляется через ON CONFLICT,
        /// и ноль затронутых строк невозможен. Отсекающее условие стоит внутри вставки,
        /// потому что проверка отдельным запросом перед ней оставляет окно шире.
        public async Task<ManagementWriteResult> EnableTradesAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                SELECT @secid, 'trades', NULL::int, true, now(), now()
                WHERE NOT EXISTS (
                    SELECT 1 FROM moex_instrument_data_deletions d
                    WHERE d.secid = @secid AND d.status = 'started'
                )
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeTradesEnabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        /// Возврат RowsWritten = 0 означает, что по инструменту идёт удаление данных:
        /// при обычном ходе строка либо вставляется, либо обновляется через ON CONFLICT,
        /// и ноль затронутых строк невозможен. Отсекающее условие стоит внутри вставки,
        /// потому что проверка отдельным запросом перед ней оставляет окно шире.
        public async Task<ManagementWriteResult> EnableOrderbookAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                SELECT @secid, 'orderbook', NULL::int, true, now(), now()
                WHERE NOT EXISTS (
                    SELECT 1 FROM moex_instrument_data_deletions d
                    WHERE d.secid = @secid AND d.status = 'started'
                )
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeOrderbookEnabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        /// Возврат RowsWritten = 0 означает, что по инструменту идёт удаление данных:
        /// при обычном ходе строка либо вставляется, либо обновляется через ON CONFLICT,
        /// и ноль затронутых строк невозможен. Отсекающее условие стоит внутри вставки,
        /// потому что проверка отдельным запросом перед ней оставляет окно шире.
        public async Task<ManagementWriteResult> EnableCandlesAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                SELECT @secid, 'candles', 1, true, now(), now()
                WHERE NOT EXISTS (
                    SELECT 1 FROM moex_instrument_data_deletions d
                    WHERE d.secid = @secid AND d.status = 'started'
                )
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeCandlesEnabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<ManagementWriteResult> DisableTradesAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_realtime_subscriptions
                SET enabled = false, updated_at = now()
                WHERE secid = @secid AND data_kind = 'trades' AND enabled = true
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeTradesDisabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<ManagementWriteResult> DisableOrderbookAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_realtime_subscriptions
                SET enabled = false, updated_at = now()
                WHERE secid = @secid AND data_kind = 'orderbook' AND enabled = true
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeOrderbookDisabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<ManagementWriteResult> DisableCandlesAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_realtime_subscriptions
                SET enabled = false, updated_at = now()
                WHERE secid = @secid AND data_kind = 'candles' AND enabled = true
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeCandlesDisabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<ManagementWriteResult> DisableInstrumentAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_realtime_subscriptions
                SET enabled = false, updated_at = now()
                WHERE secid = @secid AND enabled = true
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeInstrumentDisabled(_logger, secid, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// Массовое снятие с наблюдения: гасит все включённые подписки по всем инструментам.
        /// Строки не удаляются, гасится только признак enabled — приёмник исключит
        /// инструменты из опроса на ближайшем обороте и штатно закроет их сеансы покрытия.
        /// Повторный вызов при неизменившемся состоянии возвращает ноль изменённых строк;
        /// если между вызовами подписки снова включили, ненулевой результат штатен.
        /// </summary>
        public async Task<ManagementWriteResult> DisableAllAsync(CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                UPDATE moex_realtime_subscriptions
                SET enabled = false, updated_at = now()
                WHERE enabled = true
                """, connection);

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeAllDisabled(_logger, rowsWritten, elapsed);
                return new ManagementWriteResult(null, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }
    }
}
