using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class InstrumentReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<InstrumentReadQuery> _logger;

        public InstrumentReadQuery(NpgsqlDataSource dataSource, ILogger<InstrumentReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<List<VitrineInstrumentDto>> GetAllAsync(CancellationToken ct)
        {
            const string table = "moex_instruments";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT secid, instrument_type, asset_code, shortname, secname
                    FROM moex_instruments
                    ORDER BY secid
                    """, connection);

                List<VitrineInstrumentDto> result = new();
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new VitrineInstrumentDto(
                        Secid: reader.GetString(0),
                        InstrumentType: reader.GetString(1),
                        AssetCode: reader.IsDBNull(2) ? null : reader.GetString(2),
                        Shortname: reader.GetString(3),
                        Secname: reader.GetString(4)));
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
