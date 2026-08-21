using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Management.Contracts.Dto;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class TradingPeriodTypeWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public TradingPeriodTypeWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<int> CreateAsync(TradingPeriodTypeCreateRequest request, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                INSERT INTO moex_trading_period_types (market, type_code, title)
                VALUES (@market, @type_code, @title)
                """, connection);
            command.Parameters.Add("@market", NpgsqlDbType.Text).Value = request.Market!;
            command.Parameters.Add("@type_code", NpgsqlDbType.Text).Value = request.TypeCode!;
            command.Parameters.Add("@title", NpgsqlDbType.Text).Value = request.Title!;
            return await command.ExecuteNonQueryAsync(ct);
        }
    }
}
