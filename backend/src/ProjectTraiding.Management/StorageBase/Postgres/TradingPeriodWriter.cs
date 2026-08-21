using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Management.Contracts.Dto;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class TradingPeriodWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public TradingPeriodWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<int> CreateAsync(TradingPeriodCreateRequest request, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                INSERT INTO moex_trading_periods
                    (market, valid_from, valid_till, boardid, secid, session,
                     period_type, time_from, time_till)
                VALUES (@market, @valid_from, @valid_till, @boardid, @secid, @session,
                        @period_type, @time_from, @time_till)
                """, connection);
            command.Parameters.Add("@market", NpgsqlDbType.Text).Value = request.Market!;
            command.Parameters.Add("@valid_from", NpgsqlDbType.Date).Value = request.ValidFrom!.Value;
            command.Parameters.Add("@valid_till", NpgsqlDbType.Date).Value = request.ValidTill!.Value;
            command.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = request.Boardid!;
            command.Parameters.Add("@secid", NpgsqlDbType.Text).Value = request.Secid ?? string.Empty;
            command.Parameters.Add("@session", NpgsqlDbType.Smallint).Value =
                (object?)request.Session ?? DBNull.Value;
            command.Parameters.Add("@period_type", NpgsqlDbType.Text).Value = request.PeriodType!;
            command.Parameters.Add("@time_from", NpgsqlDbType.Timestamp).Value = request.TimeFrom!.Value;
            command.Parameters.Add("@time_till", NpgsqlDbType.Timestamp).Value =
                (object?)request.TimeTill ?? DBNull.Value;
            return await command.ExecuteNonQueryAsync(ct);
        }
    }
}
