using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;
using System.Diagnostics;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
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

        public async Task<ContextWriteResult> CreateAsync(BrokerTariffCreateCommand command, CancellationToken ct)
        {
            const string table = "moex_broker_tariffs";
            CustomFeaturesWriterLogMessages.WriteStarted(_logger, table);
            long startTs = Stopwatch.GetTimestamp();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            try
            {
                bool hasCurrency = command.FeeCurrency is not null;
                string sql = hasCurrency ? SqlWithCurrency : SqlWithoutCurrency;
                await using NpgsqlCommand cmd = new NpgsqlCommand(sql, connection, transaction);

                cmd.Parameters.Add("@broker_name", NpgsqlDbType.Text).Value = command.BrokerName;
                cmd.Parameters.Add("@tariff_name", NpgsqlDbType.Text).Value = command.TariffName;
                cmd.Parameters.Add("@market", NpgsqlDbType.Text).Value = command.Market;
                cmd.Parameters.Add("@fee_type", NpgsqlDbType.Text).Value = command.FeeType;
                cmd.Parameters.Add("@fee_value", NpgsqlDbType.Numeric).Value = command.FeeValue;
                cmd.Parameters.Add("@min_fee", NpgsqlDbType.Numeric).Value = (object?)command.MinFee ?? DBNull.Value;
                cmd.Parameters.Add("@turnover_threshold", NpgsqlDbType.Numeric).Value = (object?)command.TurnoverThreshold ?? DBNull.Value;
                cmd.Parameters.Add("@valid_from", NpgsqlDbType.Date).Value = command.ValidFrom;
                cmd.Parameters.Add("@valid_till", NpgsqlDbType.Date).Value = (object?)command.ValidTill ?? DBNull.Value;
                cmd.Parameters.Add("@comment", NpgsqlDbType.Text).Value = (object?)command.Comment ?? DBNull.Value;

                if (command.FeeCurrency is string feeCurrency)
                    cmd.Parameters.Add("@fee_currency", NpgsqlDbType.Text).Value = feeCurrency;

                object? scalar = await cmd.ExecuteScalarAsync(ct);
                long id = scalar is long value
                    ? value
                    : throw new InvalidOperationException("INSERT INTO moex_broker_tariffs did not return id.");

                await transaction.CommitAsync(ct);
                TimeSpan elapsed = Stopwatch.GetElapsedTime(startTs);
                CustomFeaturesWriterLogMessages.WriteCompleted(_logger, table, id, 1, elapsed);

                return new ContextWriteResult(Id: id, RowsWritten: 1, Elapsed: elapsed);
            }
            catch (Exception ex)
            {
                CustomFeaturesWriterLogMessages.WriteRolledBack(_logger, ex, table, ex.GetType().Name);
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }
    }
}
