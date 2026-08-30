using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
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

        public async Task<SubscriptionWriteResult> EnableTradesAsync(string secid, CancellationToken ct)
        {
            SubscriptionWriteResult result = await EnableAsync(secid, "trades", null, ct);
            MoexCommandWriterLogMessages.RealtimeTradesEnabled(
                _logger, secid, result.RowsWritten, result.Elapsed);
            return result;
        }

        public async Task<SubscriptionWriteResult> EnableOrderbookAsync(string secid, CancellationToken ct)
        {
            SubscriptionWriteResult result = await EnableAsync(secid, "orderbook", null, ct);
            MoexCommandWriterLogMessages.RealtimeOrderbookEnabled(
                _logger, secid, result.RowsWritten, result.Elapsed);
            return result;
        }

        public async Task<SubscriptionWriteResult> EnableCandlesAsync(string secid, CancellationToken ct)
        {
            SubscriptionWriteResult result = await EnableAsync(secid, "candles", 1, ct);
            MoexCommandWriterLogMessages.RealtimeCandlesEnabled(
                _logger, secid, result.RowsWritten, result.Elapsed);
            return result;
        }

        /// Возврат RowsWritten = 0 означает, что по инструменту идёт удаление данных:
        /// при обычном ходе строка либо вставляется, либо обновляется через ON CONFLICT,
        /// и ноль затронутых строк невозможен. Отсекающее условие стоит внутри вставки,
        /// потому что проверка отдельным запросом перед ней оставляет окно шире.
        private async Task<SubscriptionWriteResult> EnableAsync(
            string secid, string dataKind, int? candleInterval, CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                SELECT @secid, @data_kind, @candle_interval, true, now(), now()
                WHERE NOT EXISTS (
                    SELECT 1 FROM moex_instrument_data_deletions d
                    WHERE d.secid = @secid AND d.status = 'started'
                )
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
                cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;
                cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                    (object?)candleInterval ?? DBNull.Value;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> EnableInstrumentAsync(
            string secid, CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                SELECT instrument.secid,
                       subscription.data_kind,
                       subscription.candle_interval,
                       true,
                       now(),
                       now()
                FROM moex_instruments instrument
                JOIN (VALUES
                    ('stock',   'trades',      NULL::int),
                    ('stock',   'orderbook',   NULL::int),
                    ('stock',   'candles',     1),
                    ('stock',   'tradestats',  NULL::int),
                    ('stock',   'obstats',     NULL::int),
                    ('stock',   'orderstats',  NULL::int),
                    ('stock',   'mega_alerts', NULL::int),
                    ('stock',   'hi2',         NULL::int),
                    ('futures', 'trades',      NULL::int),
                    ('futures', 'orderbook',   NULL::int),
                    ('futures', 'candles',     1),
                    ('futures', 'tradestats',  NULL::int),
                    ('futures', 'obstats',     NULL::int),
                    ('futures', 'futoi',       NULL::int),
                    ('futures', 'mega_alerts', NULL::int),
                    ('futures', 'hi2',         NULL::int)
                ) AS subscription(instrument_type, data_kind, candle_interval)
                    ON subscription.instrument_type = instrument.instrument_type
                WHERE instrument.secid = @secid
                  AND instrument.instrument_type <> 'futures_series'
                  AND NOT EXISTS (
                      SELECT 1 FROM moex_instrument_data_deletions d
                      WHERE d.secid = @secid AND d.status = 'started'
                  )
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET
                    candle_interval = EXCLUDED.candle_interval,
                    enabled = true,
                    updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                MoexCommandWriterLogMessages.RealtimeInstrumentEnabled(
                    _logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableTradesAsync(string secid, CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
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
                MoexCommandWriterLogMessages.RealtimeTradesDisabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableOrderbookAsync(string secid, CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
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
                MoexCommandWriterLogMessages.RealtimeOrderbookDisabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableCandlesAsync(string secid, CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
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
                MoexCommandWriterLogMessages.RealtimeCandlesDisabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableInstrumentAsync(string secid, CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
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
                MoexCommandWriterLogMessages.RealtimeInstrumentDisabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
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
        public async Task<SubscriptionWriteResult> DisableAllAsync(CancellationToken ct)
        {
            MoexCommandWriterLogMessages.WriteStarted(_logger, Table);
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
                MoexCommandWriterLogMessages.RealtimeAllDisabled(_logger, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexCommandWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }
    }
}
