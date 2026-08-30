using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public sealed class TradingPeriodTypeWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public TradingPeriodTypeWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<int> CreateAsync(TradingPeriodTypeCreateCommand command, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO moex_trading_period_types (market, type_code, title)
                VALUES (@market, @type_code, @title)
                """, connection);
            dbCommand.Parameters.Add("@market", NpgsqlDbType.Text).Value = command.Market;
            dbCommand.Parameters.Add("@type_code", NpgsqlDbType.Text).Value = command.TypeCode;
            dbCommand.Parameters.Add("@title", NpgsqlDbType.Text).Value = command.Title;
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }
    }
}
