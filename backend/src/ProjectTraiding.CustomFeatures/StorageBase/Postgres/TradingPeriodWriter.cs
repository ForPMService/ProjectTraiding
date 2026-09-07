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

        public async Task<Guid> CreateAsync(TradingPeriodCreateCommand command, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO moex_trading_periods
                    (trade_date, market, boardid, secid, session, period_type,
                     time_from, time_till, data_source, note)
                VALUES (@trade_date, @market, @boardid, @secid, @session, @period_type,
                        @time_from, @time_till, 'manual', @note)
                RETURNING id
                """, connection);
            dbCommand.Parameters.Add("@trade_date", NpgsqlDbType.Date).Value = command.TradeDate;
            dbCommand.Parameters.Add("@market", NpgsqlDbType.Text).Value = command.Market;
            dbCommand.Parameters.Add("@boardid", NpgsqlDbType.Text).Value = command.Boardid;
            dbCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = command.Secid;
            dbCommand.Parameters.Add("@session", NpgsqlDbType.Smallint).Value =
                (object?)command.Session ?? DBNull.Value;
            dbCommand.Parameters.Add("@period_type", NpgsqlDbType.Text).Value = command.PeriodType;
            dbCommand.Parameters.Add("@time_from", NpgsqlDbType.Timestamp).Value = command.TimeFrom;
            dbCommand.Parameters.Add("@time_till", NpgsqlDbType.Timestamp).Value =
                (object?)command.TimeTill ?? DBNull.Value;
            dbCommand.Parameters.Add("@note", NpgsqlDbType.Text).Value =
                (object?)command.Note ?? DBNull.Value;

            object? scalar = await dbCommand.ExecuteScalarAsync(ct);
            return scalar is Guid id
                ? id
                : throw new InvalidOperationException("INSERT INTO moex_trading_periods did not return id.");
        }
    }
}
