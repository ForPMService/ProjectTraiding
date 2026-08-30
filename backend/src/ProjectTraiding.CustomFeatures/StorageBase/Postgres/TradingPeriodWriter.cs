using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public sealed class TradingPeriodWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public TradingPeriodWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<int> CreateAsync(TradingPeriodCreateCommand command, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO moex_trading_periods
                    (market, valid_from, valid_till, boardid, secid, session,
                     period_type, time_from, time_till)
                VALUES (@market, @valid_from, @valid_till, @boardid, @secid, @session,
                        @period_type, @time_from, @time_till)
                """, connection);
            dbCommand.Parameters.Add("@market", NpgsqlDbType.Text).Value = command.Market;
            dbCommand.Parameters.Add("@valid_from", NpgsqlDbType.Date).Value = command.ValidFrom;
            dbCommand.Parameters.Add("@valid_till", NpgsqlDbType.Date).Value = command.ValidTill;
            dbCommand.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = command.Boardid;
            dbCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = command.Secid ?? string.Empty;
            dbCommand.Parameters.Add("@session", NpgsqlDbType.Smallint).Value =
                (object?)command.Session ?? DBNull.Value;
            dbCommand.Parameters.Add("@period_type", NpgsqlDbType.Text).Value = command.PeriodType;
            dbCommand.Parameters.Add("@time_from", NpgsqlDbType.Timestamp).Value = command.TimeFrom;
            dbCommand.Parameters.Add("@time_till", NpgsqlDbType.Timestamp).Value =
                (object?)command.TimeTill ?? DBNull.Value;
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }
    }
}
