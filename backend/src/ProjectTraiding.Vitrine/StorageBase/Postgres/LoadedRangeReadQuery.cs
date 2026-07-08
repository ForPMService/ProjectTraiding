using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class LoadedRangeReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<LoadedRangeReadQuery> _logger;

        public LoadedRangeReadQuery(NpgsqlDataSource dataSource, ILogger<LoadedRangeReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<List<VitrineLoadedRangeDto>> GetBySecidAsync(string secid, CancellationToken ct)
        {
            const string table = "moex_loaded_ranges";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT id, secid, market, boardid, data_kind, candle_interval,
                           date_from, date_till, rows_total, status, storage_target, last_success_at
                    FROM moex_loaded_ranges
                    WHERE secid = @secid
                    ORDER BY data_kind, candle_interval, date_from, date_till
                    """, connection);
                cmd.Parameters.Add("@secid", NpgsqlDbType.Text).Value = secid;

                List<VitrineLoadedRangeDto> result = new();
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new VitrineLoadedRangeDto(
                        Id: reader.GetInt64(0),
                        Secid: reader.GetString(1),
                        Market: reader.GetString(2),
                        Boardid: reader.GetString(3),
                        DataKind: reader.GetString(4),
                        CandleInterval: reader.IsDBNull(5) ? null : (int?)reader.GetInt32(5),
                        DateFrom: reader.GetFieldValue<DateOnly>(6),
                        DateTill: reader.GetFieldValue<DateOnly>(7),
                        RowsTotal: reader.GetInt64(8),
                        Status: reader.GetString(9),
                        StorageTarget: reader.GetString(10),
                        LastSuccessAt: reader.GetFieldValue<DateTimeOffset>(11)));
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
