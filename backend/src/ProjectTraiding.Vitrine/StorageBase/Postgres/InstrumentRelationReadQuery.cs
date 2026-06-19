using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class InstrumentRelationReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<InstrumentRelationReadQuery> _logger;

        public InstrumentRelationReadQuery(NpgsqlDataSource dataSource, ILogger<InstrumentRelationReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<List<VitrineInstrumentRelationDto>> GetAllAsync(CancellationToken ct)
        {
            const string table = "moex_instrument_relations";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT id, source_secid, target_secid, target_asset_code, relation_type, confidence, comment
                    FROM moex_instrument_relations
                    ORDER BY id
                    """, connection);

                List<VitrineInstrumentRelationDto> result = new();
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new VitrineInstrumentRelationDto(
                        Id: reader.GetInt64(0),
                        SourceSecid: reader.GetString(1),
                        TargetSecid: reader.IsDBNull(2) ? null : reader.GetString(2),
                        TargetAssetCode: reader.IsDBNull(3) ? null : reader.GetString(3),
                        RelationType: reader.GetString(4),
                        Confidence: reader.GetString(5),
                        Comment: reader.IsDBNull(6) ? null : reader.GetString(6)));
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
