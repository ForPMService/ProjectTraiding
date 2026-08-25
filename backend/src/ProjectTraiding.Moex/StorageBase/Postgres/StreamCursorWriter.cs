using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Infrastructure;
using System;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    /// <summary>
    /// Писатель высокой отметки приёма в moex_stream_cursors.
    /// Единственный владелец записи курсоров приёма — контур Moex.
    /// Upsert по ключу ряда: повторный приём двигает отметку вперёд.
    ///
    /// last_source_time — обязателен (рыночное время, докуда принято).
    /// last_trade_no — номер последней сделки; только у ленты сделок, иначе NULL.
    /// candle_interval — только у свечей, иначе NULL.
    /// </summary>
    public sealed class StreamCursorWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<StreamCursorWriter> _logger;

        public StreamCursorWriter(NpgsqlDataSource dataSource, ILogger<StreamCursorWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task UpsertAsync(
            string secid,
            string market,
            string boardid,
            string dataKind,
            int? candleInterval,
            DateTime lastSourceTime,
            long? lastTradeNo,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                INSERT INTO moex_stream_cursors
                    (secid, market, boardid, data_kind, candle_interval,
                     last_source_time, last_trade_no, updated_at)
                VALUES
                    (@secid, @market, @boardid, @data_kind, @candle_interval,
                     @last_source_time, @last_trade_no, now())
                ON CONFLICT (secid, market, boardid, data_kind, candle_interval)
                DO UPDATE SET
                    last_source_time = EXCLUDED.last_source_time,
                    last_trade_no    = EXCLUDED.last_trade_no,
                    updated_at       = now()
                """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;
            cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                (object?)candleInterval ?? DBNull.Value;
            cmd.Parameters.Add("@last_source_time", NpgsqlDbType.TimestampTz).Value =
                ToUtc(lastSourceTime);
            cmd.Parameters.Add("@last_trade_no", NpgsqlDbType.Bigint).Value =
                (object?)lastTradeNo ?? DBNull.Value;

            // Текст команды постоянен, а сама команда повторяется на каждую пачку приёма:
            // подготовка снимает разбор и построение плана на стороне базы. Подготовленное
            // выражение живёт на физическом соединении и переживает возврат в пул.
            await cmd.PrepareAsync(ct);
            await cmd.ExecuteNonQueryAsync(ct);
            MoexStreamCursorLogMessages.CursorUpserted(_logger, secid, dataKind, lastSourceTime);
        }

        /// <summary>
        /// Читает высокую отметку ряда, если она есть. Приёмник при старте берёт last_trade_no
        /// отсюда, чтобы продолжить ленту сделок с места, а не с открытия торгов.
        /// </summary>
        public async Task<StreamCursorState?> TryGetAsync(
            string secid,
            string market,
            string boardid,
            string dataKind,
            int? candleInterval,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand cmd = new NpgsqlCommand("""
                SELECT last_trade_no
                FROM moex_stream_cursors
                WHERE secid = @secid
                  AND market = @market
                  AND boardid = @boardid
                  AND data_kind = @data_kind
                  AND candle_interval IS NOT DISTINCT FROM @candle_interval
                """, connection);

            cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;
            cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
            cmd.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = boardid;
            cmd.Parameters.Add("@data_kind", NpgsqlDbType.Text).Value = dataKind;
            cmd.Parameters.Add("@candle_interval", NpgsqlDbType.Integer).Value =
                (object?)candleInterval ?? DBNull.Value;

            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return null;

            return new StreamCursorState(
                LastTradeNo: reader.IsDBNull(0) ? null : reader.GetInt64(0));
        }

        private static DateTime ToUtc(DateTime value)
        {
            if (value.Kind == DateTimeKind.Utc)
                return value;

            if (value.Kind == DateTimeKind.Local)
                return value.ToUniversalTime();

            return DateTime.SpecifyKind(value - MoexTime.Offset, DateTimeKind.Utc);
        }

    }

    public readonly record struct StreamCursorState(long? LastTradeNo);
}
