using Microsoft.Extensions.Logging;
using Npgsql;
using ProjectTraiding.Vitrine.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Vitrine.StorageBase.Postgres
{
    public sealed class BrokerTariffReadQuery
    {
        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<BrokerTariffReadQuery> _logger;

        public BrokerTariffReadQuery(NpgsqlDataSource dataSource, ILogger<BrokerTariffReadQuery> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<List<VitrineBrokerTariffDto>> GetAllAsync(CancellationToken ct)
        {
            const string table = "moex_broker_tariffs";
            VitrineReadLogMessages.ReadStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            try
            {
                await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
                await using NpgsqlCommand cmd = new NpgsqlCommand("""
                    SELECT id, broker_name, tariff_name, market, fee_type, fee_value, fee_currency,
                           min_fee, turnover_threshold, valid_from, valid_till, comment
                    FROM moex_broker_tariffs
                    ORDER BY id
                    """, connection);

                List<VitrineBrokerTariffDto> result = new();
                await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(ct);
                while (await reader.ReadAsync(ct))
                {
                    result.Add(new VitrineBrokerTariffDto(
                        Id: reader.GetInt64(0),
                        BrokerName: reader.GetString(1),
                        TariffName: reader.GetString(2),
                        Market: reader.GetString(3),
                        FeeType: reader.GetString(4),
                        FeeValue: reader.GetDecimal(5),
                        FeeCurrency: reader.GetString(6),
                        MinFee: reader.IsDBNull(7) ? null : (decimal?)reader.GetDecimal(7),
                        TurnoverThreshold: reader.IsDBNull(8) ? null : (decimal?)reader.GetDecimal(8),
                        ValidFrom: reader.GetFieldValue<DateOnly>(9),
                        ValidTill: reader.IsDBNull(10) ? null : (DateOnly?)reader.GetFieldValue<DateOnly>(10),
                        Comment: reader.IsDBNull(11) ? null : reader.GetString(11)));
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
