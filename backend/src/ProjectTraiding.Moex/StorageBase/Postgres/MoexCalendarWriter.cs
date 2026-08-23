using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexCalendarWriter
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<MoexCalendarWriter> _logger;

        public MoexCalendarWriter(NpgsqlDataSource dataSource, ILogger<MoexCalendarWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<DbWriteResult> UpsertDaysAsync(
            IReadOnlyList<CalendarDayWriteDTO> days,
            CancellationToken ct)
        {
            const string table = "moex_calendar_days";
            MoexWriterLogMessages.WriteStarted(_logger, table, days.Count);
            long startTimestamp = Stopwatch.GetTimestamp();
            int rowsWritten = 0;
            string currentKey = "?";

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                DateOnly[] tradeDates = new DateOnly[days.Count];
                string[] markets = new string[days.Count];
                int[] isTradedValues = new int[days.Count];
                DateOnly?[] tradeSessionDates = new DateOnly?[days.Count];
                string?[] reasons = new string?[days.Count];
                TimeOnly?[] startTimes = new TimeOnly?[days.Count];
                TimeOnly?[] stopTimes = new TimeOnly?[days.Count];
                string[] dataSources = new string[days.Count];
                DateTime?[] updateTimes = new DateTime?[days.Count];
                int?[] engineIsWorkDayValues = new int?[days.Count];

                for (int index = 0; index < days.Count; index++)
                {
                    CalendarDayWriteDTO day = days[index];
                    currentKey = $"{day.Market}/{day.TradeDate:yyyy-MM-dd}";
                    tradeDates[index] = day.TradeDate;
                    markets[index] = day.Market;
                    isTradedValues[index] = day.IsTraded;
                    tradeSessionDates[index] = day.TradeSessionDate;
                    reasons[index] = day.Reason;
                    startTimes[index] = day.StartTime;
                    stopTimes[index] = day.StopTime;
                    dataSources[index] = day.DataSource;
                    updateTimes[index] = day.MoexUpdateTime;
                    engineIsWorkDayValues[index] = day.EngineIsWorkDay;
                }

                // Дальше идёт единственное обращение к базе: конкретная строка в отказе
                // больше не видна, как и у пакетной записи справочника инструментов.
                currentKey = "<пачка>";

                await using NpgsqlCommand command = new NpgsqlCommand("""
                    INSERT INTO moex_calendar_days
                        (trade_date, market, is_traded, trade_session_date, reason,
                         start_time, stop_time, data_source, moex_update_time, engine_is_work_day)
                    SELECT s.trade_date, s.market, s.is_traded, s.trade_session_date, s.reason,
                           s.start_time, s.stop_time, s.data_source, s.moex_update_time,
                           s.engine_is_work_day
                    FROM (
                        SELECT DISTINCT ON (t.trade_date, t.market)
                               t.trade_date, t.market, t.is_traded, t.trade_session_date, t.reason,
                               t.start_time, t.stop_time, t.data_source, t.moex_update_time,
                               t.engine_is_work_day
                        FROM unnest(@trade_date, @market, @is_traded, @trade_session_date, @reason,
                                    @start_time, @stop_time, @data_source, @moex_update_time,
                                    @engine_is_work_day)
                             WITH ORDINALITY AS t(trade_date, market, is_traded, trade_session_date,
                                                  reason, start_time, stop_time, data_source,
                                                  moex_update_time, engine_is_work_day, ord)
                        ORDER BY t.trade_date, t.market, t.ord DESC
                    ) AS s
                    ON CONFLICT (trade_date, market) DO UPDATE SET
                        is_traded          = EXCLUDED.is_traded,
                        trade_session_date = EXCLUDED.trade_session_date,
                        reason             = EXCLUDED.reason,
                        start_time         = EXCLUDED.start_time,
                        stop_time          = EXCLUDED.stop_time,
                        data_source        = EXCLUDED.data_source,
                        moex_update_time   = EXCLUDED.moex_update_time,
                        engine_is_work_day = EXCLUDED.engine_is_work_day,
                        updated_at         = now()
                    WHERE moex_calendar_days.data_source NOT IN ('observed', 'manual');
                    """, connection, transaction);

                command.Parameters.Add("@trade_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = tradeDates;
                command.Parameters.Add("@market", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = markets;
                command.Parameters.Add("@is_traded", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = isTradedValues;
                command.Parameters.Add("@trade_session_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = tradeSessionDates;
                command.Parameters.Add("@reason", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = reasons;
                command.Parameters.Add("@start_time", NpgsqlDbType.Array | NpgsqlDbType.Time).Value = startTimes;
                command.Parameters.Add("@stop_time", NpgsqlDbType.Array | NpgsqlDbType.Time).Value = stopTimes;
                command.Parameters.Add("@data_source", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = dataSources;
                command.Parameters.Add("@moex_update_time", NpgsqlDbType.Array | NpgsqlDbType.Timestamp).Value = updateTimes;
                command.Parameters.Add("@engine_is_work_day", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = engineIsWorkDayValues;

                // Число записанных строк теперь считает база: повтор ключа внутри пачки
                // отсекается отбором и в счёт не идёт, тогда как последовательный цикл
                // засчитывал его дважды. При входе без повторов число прежнее.
                rowsWritten = await command.ExecuteNonQueryAsync(ct);

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                MoexWriterLogMessages.WriteCompleted(_logger, table, rowsWritten, elapsed);
                return new DbWriteResult(days.Count, rowsWritten, elapsed);
            }
            catch (Exception ex)
            {
                MoexWriterLogMessages.WriteRolledBack(
                    _logger, ex, table, currentKey, rowsWritten, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<int> OverrideDayAsync(
            string market,
            DateOnly date,
            int isTraded,
            string? note,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                UPDATE moex_calendar_days
                SET is_traded = @is_traded,
                    data_source = 'manual',
                    note = @note,
                    updated_at = now()
                WHERE market = @market
                  AND trade_date = @trade_date
                """, connection);
            command.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
            command.Parameters.Add("@trade_date", NpgsqlDbType.Date).Value = date;
            command.Parameters.Add("@is_traded", NpgsqlDbType.Integer).Value = isTraded;
            command.Parameters.Add("@note", NpgsqlDbType.Text).Value =
                (object?)note ?? DBNull.Value;
            return await command.ExecuteNonQueryAsync(ct);
        }

    }
}
