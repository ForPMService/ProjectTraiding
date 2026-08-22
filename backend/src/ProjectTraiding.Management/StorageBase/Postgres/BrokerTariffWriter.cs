using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Management.Contracts.Dto;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class BrokerTariffWriter
    {
        private const string SqlWithoutCurrency = """
            INSERT INTO moex_broker_tariffs
                (broker_name, tariff_name, market, fee_type, fee_value,
                 min_fee, turnover_threshold, valid_from, valid_till, comment)
            VALUES
                (@broker_name, @tariff_name, @market, @fee_type, @fee_value,
                 @min_fee, @turnover_threshold, @valid_from, @valid_till, @comment)
            RETURNING id
            """;

        private const string SqlWithCurrency = """
            INSERT INTO moex_broker_tariffs
                (broker_name, tariff_name, market, fee_type, fee_value, fee_currency,
                 min_fee, turnover_threshold, valid_from, valid_till, comment)
            VALUES
                (@broker_name, @tariff_name, @market, @fee_type, @fee_value, @fee_currency,
                 @min_fee, @turnover_threshold, @valid_from, @valid_till, @comment)
            RETURNING id
            """;

        private readonly NpgsqlDataSource _dataSource;
        private readonly ILogger<BrokerTariffWriter> _logger;

        public BrokerTariffWriter(NpgsqlDataSource dataSource, ILogger<BrokerTariffWriter> logger)
        {
            _dataSource = dataSource;
            _logger = logger;
        }

        public async Task<ManagementWriteResult> CreateAsync(BrokerTariffCreateRequest request, CancellationToken ct)
        {
            const string table = "moex_broker_tariffs";
            ManagementWriterLogMessages.WriteStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                bool hasCurrency = request.FeeCurrency is not null;
                string sql = hasCurrency ? SqlWithCurrency : SqlWithoutCurrency;
                await using NpgsqlCommand cmd = new NpgsqlCommand(sql, connection, transaction);

                cmd.Parameters.Add("@broker_name", NpgsqlDbType.Text).Value = request.BrokerName!;
                cmd.Parameters.Add("@tariff_name", NpgsqlDbType.Text).Value = request.TariffName!;
                cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = request.Market!;
                cmd.Parameters.Add("@fee_type", NpgsqlDbType.Text).Value = request.FeeType!;
                cmd.Parameters.Add("@fee_value", NpgsqlDbType.Numeric).Value = request.FeeValue!.Value;
                cmd.Parameters.Add("@min_fee", NpgsqlDbType.Numeric).Value = (object?)request.MinFee ?? DBNull.Value;
                cmd.Parameters.Add("@turnover_threshold", NpgsqlDbType.Numeric).Value = (object?)request.TurnoverThreshold ?? DBNull.Value;
                cmd.Parameters.Add("@valid_from", NpgsqlDbType.Date).Value = DateOnly.Parse(request.ValidFrom!);
                cmd.Parameters.Add("@valid_till", NpgsqlDbType.Date).Value = request.ValidTill is null ? (object)DBNull.Value : DateOnly.Parse(request.ValidTill);
                cmd.Parameters.Add("@comment", NpgsqlDbType.Text).Value = (object?)request.Comment ?? DBNull.Value;

                if (hasCurrency)
                    cmd.Parameters.Add("@fee_currency", NpgsqlDbType.Text).Value = request.FeeCurrency!;

                object? scalar = await cmd.ExecuteScalarAsync(ct);
                long id = (long)scalar!;

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                ManagementWriterLogMessages.WriteCompleted(_logger, table, id, 1, elapsed);

                return new ManagementWriteResult(Id: id, RowsWritten: 1, Elapsed: elapsed);
            }
            catch (Exception ex)
            {
                ManagementWriterLogMessages.WriteRolledBack(_logger, ex, table, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
