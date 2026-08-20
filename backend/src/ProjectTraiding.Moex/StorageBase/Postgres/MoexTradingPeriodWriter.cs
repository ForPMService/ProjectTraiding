using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexTradingPeriodWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexTradingPeriodWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<DbWriteResult> AppendAsync(
            IReadOnlyList<TradingPeriodDTO> periods,
            CancellationToken ct)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                INSERT INTO moex_trading_periods
                    (market, trade_date, snapshot_at, boardid, secid, session,
                     period_type, time_from, time_till, moex_update_time)
                VALUES (@market, @trade_date, @snapshot_at, @boardid, @secid, @session,
                        @period_type, @time_from, @time_till, @moex_update_time)
                """, connection, transaction);

            NpgsqlParameter marketParameter = command.Parameters.Add("@market", NpgsqlDbType.Text);
            NpgsqlParameter tradeDateParameter = command.Parameters.Add("@trade_date", NpgsqlDbType.Date);
            NpgsqlParameter snapshotAtParameter = command.Parameters.Add("@snapshot_at", NpgsqlDbType.TimestampTz);
            NpgsqlParameter boardIdParameter = command.Parameters.Add("@boardid", NpgsqlDbType.Text);
            NpgsqlParameter secIdParameter = command.Parameters.Add("@secid", NpgsqlDbType.Text);
            NpgsqlParameter sessionParameter = command.Parameters.Add("@session", NpgsqlDbType.Smallint);
            NpgsqlParameter periodTypeParameter = command.Parameters.Add("@period_type", NpgsqlDbType.Text);
            NpgsqlParameter timeFromParameter = command.Parameters.Add("@time_from", NpgsqlDbType.Timestamp);
            NpgsqlParameter timeTillParameter = command.Parameters.Add("@time_till", NpgsqlDbType.Timestamp);
            NpgsqlParameter updateTimeParameter = command.Parameters.Add("@moex_update_time", NpgsqlDbType.Timestamp);

            int rowsWritten = 0;
            for (int index = 0; index < periods.Count; index++)
            {
                TradingPeriodDTO period = periods[index];
                marketParameter.Value = period.Market;
                tradeDateParameter.Value = period.TradeDate;
                snapshotAtParameter.Value = period.SnapshotAt;
                boardIdParameter.Value = period.BoardId;
                secIdParameter.Value = period.SecId;
                sessionParameter.Value = (object?)period.Session ?? DBNull.Value;
                periodTypeParameter.Value = period.PeriodType;
                timeFromParameter.Value = period.TimeFrom;
                timeTillParameter.Value = (object?)period.TimeTill ?? DBNull.Value;
                updateTimeParameter.Value = (object?)period.MoexUpdateTime ?? DBNull.Value;
                rowsWritten += await command.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new DbWriteResult(
                periods.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
        }
    }
}
