using Npgsql;
using NpgsqlTypes;
using System.Diagnostics;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public readonly record struct SubscriptionWriteResult(
        int RowsWritten,
        TimeSpan Elapsed);

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
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                VALUES
                    (@secid, 'trades', NULL, true, now(), now())
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeTradesEnabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> EnableOrderbookAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                VALUES
                    (@secid, 'orderbook', NULL, true, now(), now())
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeOrderbookEnabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> EnableCandlesAsync(string secid, CancellationToken ct)
        {
            ManagementWriterLogMessages.WriteStarted(_logger, Table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_realtime_subscriptions
                    (secid, data_kind, candle_interval, enabled, created_at, updated_at)
                VALUES
                    (@secid, 'candles', 1, true, now(), now())
                ON CONFLICT (secid, data_kind)
                DO UPDATE SET enabled = true, updated_at = now()
                """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                int rowsWritten = await cmd.ExecuteNonQueryAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.RealtimeCandlesEnabled(_logger, secid, rowsWritten, elapsed);
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableTradesAsync(string secid, CancellationToken ct)
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
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableOrderbookAsync(string secid, CancellationToken ct)
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
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableCandlesAsync(string secid, CancellationToken ct)
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
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }

        public async Task<SubscriptionWriteResult> DisableInstrumentAsync(string secid, CancellationToken ct)
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
                return new SubscriptionWriteResult(rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, Table, ex.GetType().Name);
                throw;
            }
        }
    }
}
