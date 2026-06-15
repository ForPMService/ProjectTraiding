using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

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
            => UpsertOffDaysAsync(days, "stock", ct);

        public Task<DbWriteResult> UpsertFuturesOffDaysAsync(
            IReadOnlyList<CalendarOffDaysMarketDTO> days,
            CancellationToken ct)
            => UpsertOffDaysAsync(days, "futures", ct);

        // Одна таблица, один UPSERT на строку, FK между строками нет —
        // stock/futures отличаются только константой market, тело общее.
        private async Task<DbWriteResult> UpsertOffDaysAsync(
            IReadOnlyList<CalendarOffDaysMarketDTO> days,
            string market,
            CancellationToken ct)
        {
            string table = $"moex_calendar_days ({market})";
            MoexWriterLogMessages.WriteStarted(_logger, table, days.Count);
            long startTs = Stopwatch.GetTimestamp();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            string currentKey = "?";
            int processedCount = 0;

            try
            {
                foreach (CalendarOffDaysMarketDTO day in days)
                {
                    currentKey = day.TradeDate ?? "?";
                    // ── обязательные поля до SQL ──
                    if (string.IsNullOrWhiteSpace(day.TradeDate))
                        throw new InvalidOperationException(
                            $"TradeDate пустой (market={market})");
                    if (day.IsTraded is null)
                        throw new InvalidOperationException(
                            $"IsTraded null у {day.TradeDate} (market={market})");

                    await using NpgsqlCommand command = new NpgsqlCommand("""
                        INSERT INTO moex_calendar_days
                            (trade_date, market, is_traded, trade_session_date,
                             reason, moex_update_time, updated_at)
                        VALUES
                            (@trade_date, @market, @is_traded, @trade_session_date,
                             @reason, @moex_update_time, now())
                        ON CONFLICT (trade_date, market) DO UPDATE SET
                            is_traded          = EXCLUDED.is_traded,
                            trade_session_date = EXCLUDED.trade_session_date,
                            reason             = EXCLUDED.reason,
                            moex_update_time   = EXCLUDED.moex_update_time,
                            updated_at         = now()
                        """, connection, transaction);

                    // trade_date: date-колонка → DateOnly + Date (урок 42804). PK, не null.
                    command.Parameters.Add("@trade_date", NpgsqlDbType.Date).Value =
                        DateOnly.Parse(day.TradeDate);

                    command.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;

                    // is_traded: NOT NULL в таблице, int? в DTO → .Value после проверки.
                    command.Parameters.Add("@is_traded", NpgsqlDbType.Integer).Value =
                        day.IsTraded.Value;

                    // trade_session_date: date-колонка, nullable (null при is_traded=0).
                    command.Parameters.Add("@trade_session_date", NpgsqlDbType.Date).Value =
                        ParseNullableDateOrDbNull(day.TradeSessionDate, table, "trade_session_date", day.TradeDate);

                    command.Parameters.Add("@reason", NpgsqlDbType.Text).Value =
                        (object?)day.Reason ?? DBNull.Value;

                    // moex_update_time: timestamp, в DTO уже DateTime? — НЕ парсить,
                    // только null-guard и Timestamp (не Date!).
                    command.Parameters.Add("@moex_update_time", NpgsqlDbType.Timestamp).Value =
                        (object?)day.UpdateTime ?? DBNull.Value;

                    await command.ExecuteNonQueryAsync(ct);

                    processedCount++;
                }

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                MoexWriterLogMessages.WriteCompleted(_logger, table, processedCount, elapsed);
                return new DbWriteResult(table, days.Count, processedCount, elapsed);
            }
            catch(Exception ex)
            {
                MoexWriterLogMessages.WriteRolledBack(_logger, ex, table, currentKey, processedCount, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        private object ParseNullableDateOrDbNull(string? raw, string table, string field, string key)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return DBNull.Value;
            }

            if (DateOnly.TryParse(raw, out DateOnly parsed))
            {
                return parsed;
            }

            MoexWriterLogMessages.DateParseFailed(_logger, table, field, key, raw);
            return DBNull.Value;
        }
    }
}
