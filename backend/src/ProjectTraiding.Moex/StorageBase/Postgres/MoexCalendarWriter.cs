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

        public Task<DbWriteResult> UpsertStockOffDaysAsync(
            IReadOnlyList<CalendarOffDaysMarketDTO> days,
            CancellationToken ct)
        {
            return UpsertOffDaysAsync(days, "stock", ct);
        }

        public Task<DbWriteResult> UpsertFuturesOffDaysAsync(
            IReadOnlyList<CalendarOffDaysMarketDTO> days,
            CancellationToken ct)
        {
            return UpsertOffDaysAsync(days, "futures", ct);
        }

        public Task<DbWriteResult> UpsertDaysAsync(
            IReadOnlyList<CalendarDayWriteDTO> days,
            CancellationToken ct)
        {
            return UpsertDaysCoreAsync(days, preserveEngineIsWorkDay: false, ct);
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

        private Task<DbWriteResult> UpsertOffDaysAsync(
            IReadOnlyList<CalendarOffDaysMarketDTO> days,
            string market,
            CancellationToken ct)
        {
            List<CalendarDayWriteDTO> rows = new List<CalendarDayWriteDTO>(days.Count);
            for (int index = 0; index < days.Count; index++)
            {
                CalendarOffDaysMarketDTO day = days[index];
                if (day.IsTraded is null)
                    throw new InvalidOperationException(
                        $"IsTraded null у {day.TradeDate} (market={market})");

                rows.Add(new CalendarDayWriteDTO
                {
                    TradeDate = day.TradeDate,
                    Market = market,
                    IsTraded = day.IsTraded.Value,
                    TradeSessionDate = day.TradeSessionDate,
                    Reason = day.Reason,
                    DataSource = "calendar",
                    MoexUpdateTime = day.UpdateTime,
                });
            }
            return UpsertDaysCoreAsync(rows, preserveEngineIsWorkDay: true, ct);
        }

        private async Task<DbWriteResult> UpsertDaysCoreAsync(
            IReadOnlyList<CalendarDayWriteDTO> days,
            bool preserveEngineIsWorkDay,
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
                await using NpgsqlCommand command = new NpgsqlCommand("""
                    INSERT INTO moex_calendar_days
                        (trade_date, market, is_traded, trade_session_date, reason,
                         start_time, stop_time, data_source, moex_update_time, engine_is_work_day)
                    VALUES (@trade_date, @market, @is_traded, @trade_session_date, @reason,
                            @start_time, @stop_time, @data_source, @moex_update_time,
                            @engine_is_work_day)
                    ON CONFLICT (trade_date, market) DO UPDATE SET
                        is_traded          = EXCLUDED.is_traded,
                        trade_session_date = EXCLUDED.trade_session_date,
                        reason             = EXCLUDED.reason,
                        start_time         = EXCLUDED.start_time,
                        stop_time          = EXCLUDED.stop_time,
                        data_source        = EXCLUDED.data_source,
                        moex_update_time   = EXCLUDED.moex_update_time,
                        engine_is_work_day = CASE
                            WHEN @preserve_engine_is_work_day
                                THEN moex_calendar_days.engine_is_work_day
                            ELSE EXCLUDED.engine_is_work_day
                        END,
                        updated_at         = now()
                    WHERE moex_calendar_days.data_source NOT IN ('observed', 'manual');
                    """, connection, transaction);

                NpgsqlParameter tradeDateParameter =
                    command.Parameters.Add("@trade_date", NpgsqlDbType.Date);
                NpgsqlParameter marketParameter =
                    command.Parameters.Add("@market", NpgsqlDbType.Text);
                NpgsqlParameter isTradedParameter =
                    command.Parameters.Add("@is_traded", NpgsqlDbType.Integer);
                NpgsqlParameter tradeSessionDateParameter =
                    command.Parameters.Add("@trade_session_date", NpgsqlDbType.Date);
                NpgsqlParameter reasonParameter =
                    command.Parameters.Add("@reason", NpgsqlDbType.Text);
                NpgsqlParameter startTimeParameter =
                    command.Parameters.Add("@start_time", NpgsqlDbType.Time);
                NpgsqlParameter stopTimeParameter =
                    command.Parameters.Add("@stop_time", NpgsqlDbType.Time);
                NpgsqlParameter dataSourceParameter =
                    command.Parameters.Add("@data_source", NpgsqlDbType.Text);
                NpgsqlParameter updateTimeParameter =
                    command.Parameters.Add("@moex_update_time", NpgsqlDbType.Timestamp);
                NpgsqlParameter engineIsWorkDayParameter =
                    command.Parameters.Add("@engine_is_work_day", NpgsqlDbType.Integer);
                command.Parameters.Add("@preserve_engine_is_work_day", NpgsqlDbType.Boolean).Value =
                    preserveEngineIsWorkDay;

                for (int index = 0; index < days.Count; index++)
                {
                    CalendarDayWriteDTO day = days[index];
                    currentKey = $"{day.Market}/{day.TradeDate:yyyy-MM-dd}";
                    tradeDateParameter.Value = day.TradeDate;
                    marketParameter.Value = day.Market;
                    isTradedParameter.Value = day.IsTraded;
                    tradeSessionDateParameter.Value =
                        (object?)day.TradeSessionDate ?? DBNull.Value;
                    reasonParameter.Value = (object?)day.Reason ?? DBNull.Value;
                    startTimeParameter.Value = (object?)day.StartTime ?? DBNull.Value;
                    stopTimeParameter.Value = (object?)day.StopTime ?? DBNull.Value;
                    dataSourceParameter.Value = day.DataSource;
                    updateTimeParameter.Value = (object?)day.MoexUpdateTime ?? DBNull.Value;
                    engineIsWorkDayParameter.Value = (object?)day.EngineIsWorkDay ?? DBNull.Value;
                    rowsWritten += await command.ExecuteNonQueryAsync(ct);
                }

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

    }
}
