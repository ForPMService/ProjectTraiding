using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class CalendarReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<CalendarReadQuery> _logger;

        public CalendarReadQuery(NpgsqlDataSource dataSource, ILogger<CalendarReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<List<VitrineCalendarDayDto>> GetAllAsync(CancellationToken ct)
        {
            const string table = "moex_calendar_days";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT trade_date, market, is_traded, trade_session_date, reason
                    FROM moex_calendar_days
                    ORDER BY trade_date, market
                    """, connection);

                List<VitrineCalendarDayDto> result = new();
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new VitrineCalendarDayDto(
                        TradeDate: reader.GetFieldValue<DateOnly>(0),
                        Market: reader.GetString(1),
                        IsTraded: reader.GetInt32(2) == 1,
                        TradeSessionDate: reader.IsDBNull(3) ? null : (DateOnly?)reader.GetFieldValue<DateOnly>(3),
                        Reason: reader.IsDBNull(4) ? null : reader.GetString(4)));
                }

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                VitrineReadLogMessages.ReadCompleted(_logger, table, result.Count, elapsed);
                return result;
            }
            catch (Exception ex)
            {
                VitrineReadLogMessages.ReadFailed(_logger, ex, table, ex.GetType().Name);
                throw;
            }
        }
    }
}
