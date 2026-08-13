using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class StatusReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<StatusReadQuery> _logger;

        public StatusReadQuery(NpgsqlDataSource dataSource, ILogger<StatusReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<VitrineStatusDto> GetAsync(CancellationToken ct)
        {
            const string table = "status";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT
                        CURRENT_DATE AS as_of_date,
                        (SELECT count(*) FROM moex_instruments
                            WHERE instrument_type <> 'futures_series')                           AS instruments_total,
                        (SELECT count(*) FROM moex_instruments WHERE instrument_type = 'stock')  AS instruments_stock,
                        (SELECT count(*) FROM moex_instruments WHERE instrument_type = 'futures') AS instruments_futures,
                        (SELECT is_traded = 1 FROM moex_calendar_days
                            WHERE market = 'stock'   AND trade_date = CURRENT_DATE)              AS stock_trading_today,
                        (SELECT is_traded = 1 FROM moex_calendar_days
                            WHERE market = 'futures' AND trade_date = CURRENT_DATE)              AS futures_trading_today,
                        (SELECT count(*) FROM moex_broker_tariffs)                               AS tariffs_count,
                        (SELECT count(*) FROM moex_instrument_relations)                         AS relations_count
                    """, connection);

                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                await reader.ReadAsync(ct);

                VitrineStatusDto status = new VitrineStatusDto(
                    AsOfDate: reader.GetFieldValue<DateOnly>(0),
                    InstrumentsTotal: (int)reader.GetInt64(1),
                    InstrumentsStock: (int)reader.GetInt64(2),
                    InstrumentsFutures: (int)reader.GetInt64(3),
                    StockTradingToday: reader.IsDBNull(4) ? null : (bool?)reader.GetBoolean(4),
                    FuturesTradingToday: reader.IsDBNull(5) ? null : (bool?)reader.GetBoolean(5),
                    TariffsCount: (int)reader.GetInt64(6),
                    RelationsCount: (int)reader.GetInt64(7));

                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                VitrineReadLogMessages.ReadCompleted(_logger, table, 1, elapsed);
                return status;
            }
            catch (Exception ex)
            {
                VitrineReadLogMessages.ReadFailed(_logger, ex, table, ex.GetType().Name);
                throw;
            }
        }
    }
}
