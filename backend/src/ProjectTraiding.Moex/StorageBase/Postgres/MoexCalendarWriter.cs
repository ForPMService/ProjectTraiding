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
                // Массивы значений по колонкам: одна ячейка на строку партии. Проверки
                // обязательных полей остаются здесь, построчно — это чистые проверки без
                // обращения к базе, и ключ отказавшей строки в журнале сохраняется.
                DateOnly[] tradeDates = new DateOnly[days.Count];
                int[] isTradedValues = new int[days.Count];
                DateOnly?[] sessionDates = new DateOnly?[days.Count];
                string?[] reasons = new string?[days.Count];
                DateTime?[] updateTimes = new DateTime?[days.Count];

                for (int i = 0; i < days.Count; i++)
                {
                    CalendarOffDaysMarketDTO day = days[i];
                    currentKey = day.TradeDate ?? "?";
                    // ── обязательные поля до SQL ──
                    if (string.IsNullOrWhiteSpace(day.TradeDate))
                        throw new InvalidOperationException(
                            $"TradeDate пустой (market={market})");
                    if (day.IsTraded is null)
                        throw new InvalidOperationException(
                            $"IsTraded null у {day.TradeDate} (market={market})");

                    // trade_date: date-колонка → DateOnly (урок 42804). PK, не null.
                    tradeDates[i] = DateOnly.Parse(day.TradeDate);

                    // is_traded: NOT NULL в таблице, int? в DTO → .Value после проверки.
                    isTradedValues[i] = day.IsTraded.Value;

                    // trade_session_date: date-колонка, nullable (null при is_traded=0).
                    sessionDates[i] = ParseNullableDate(
                        day.TradeSessionDate, table, "trade_session_date", day.TradeDate);

                    reasons[i] = day.Reason;

                    // moex_update_time: timestamp, в DTO уже DateTime? — НЕ парсить.
                    updateTimes[i] = day.UpdateTime;

                    processedCount++;
                }

                // Дальше идёт обращение к базе, и конкретная строка в отказе больше не видна:
                // в событие отката пойдёт отметка пачки.
                currentKey = "<пачка>";

                await using NpgsqlCommand command = new NpgsqlCommand("""
                    INSERT INTO moex_calendar_days
                        (trade_date, market, is_traded, trade_session_date,
                         reason, moex_update_time, updated_at)
                    SELECT s.trade_date, @market, s.is_traded, s.trade_session_date,
                           s.reason, s.moex_update_time, now()
                    FROM (
                        SELECT DISTINCT ON (t.trade_date)
                               t.trade_date, t.is_traded, t.trade_session_date,
                               t.reason, t.moex_update_time
                        FROM unnest(@trade_date, @is_traded, @trade_session_date,
                                    @reason, @moex_update_time)
                             WITH ORDINALITY
                             AS t(trade_date, is_traded, trade_session_date,
                                  reason, moex_update_time, ord)
                        ORDER BY t.trade_date, t.ord DESC
                    ) AS s
                    ON CONFLICT (trade_date, market) DO UPDATE SET
                        is_traded          = EXCLUDED.is_traded,
                        trade_session_date = EXCLUDED.trade_session_date,
                        reason             = EXCLUDED.reason,
                        moex_update_time   = EXCLUDED.moex_update_time,
                        updated_at         = now()
                    """, connection, transaction);

                command.Parameters.Add("@market", NpgsqlDbType.Text).Value = market;
                command.Parameters.Add("@trade_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = tradeDates;
                command.Parameters.Add("@is_traded", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = isTradedValues;
                command.Parameters.Add("@trade_session_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = sessionDates;
                command.Parameters.Add("@reason", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = reasons;
                command.Parameters.Add("@moex_update_time", NpgsqlDbType.Array | NpgsqlDbType.Timestamp).Value = updateTimes;

                await command.ExecuteNonQueryAsync(ct);

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                MoexWriterLogMessages.WriteCompleted(_logger, table, processedCount, elapsed);
                return new DbWriteResult(days.Count, processedCount, elapsed);
            }
            catch(Exception ex)
            {
                MoexWriterLogMessages.WriteRolledBack(_logger, ex, table, currentKey, processedCount, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        // Значение уходит в массив параметров, поэтому пустота выражается пустым значением
        // типа, а не признаком отсутствия для отдельной команды. Событие неудачного разбора
        // и его поля прежние.
        private DateOnly? ParseNullableDate(string? raw, string table, string field, string key)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            if (DateOnly.TryParse(raw, out DateOnly parsed))
            {
                return parsed;
            }

            MoexWriterLogMessages.DateParseFailed(_logger, table, field, key, raw);
            return null;
        }
    }
}
