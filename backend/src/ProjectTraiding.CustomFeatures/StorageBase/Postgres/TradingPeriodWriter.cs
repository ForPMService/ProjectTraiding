using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;
using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using System.Diagnostics;

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

        public async Task<CalendarBulkWriteResult> ReplaceCalendarSessionsAsync(
            IReadOnlyList<TradingPeriodWriteDTO> rows,
            CancellationToken ct)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            if (rows.Count == 0)
                return new CalendarBulkWriteResult(0, 0, Stopwatch.GetElapsedTime(startTimestamp));

            HashSet<TradingPeriodDayKey> days = new HashSet<TradingPeriodDayKey>();
            for (int index = 0; index < rows.Count; index++)
                days.Add(new TradingPeriodDayKey(rows[index].Market, rows[index].TradeDate));

            string[] markets = new string[days.Count];
            DateOnly[] tradeDates = new DateOnly[days.Count];
            int dayIndex = 0;
            foreach (TradingPeriodDayKey day in days)
            {
                markets[dayIndex] = day.Market;
                tradeDates[dayIndex] = day.TradeDate;
                dayIndex++;
            }

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                await using NpgsqlCommand deleteCommand = new NpgsqlCommand("""
                    DELETE FROM moex_trading_periods
                    WHERE data_source = 'calendar'
                      AND (market, trade_date) IN (
                          SELECT market, trade_date
                          FROM unnest(@market, @trade_date) AS days(market, trade_date))
                    """, connection, transaction);
                deleteCommand.Parameters.Add("@market", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = markets;
                deleteCommand.Parameters.Add("@trade_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = tradeDates;
                await deleteCommand.ExecuteNonQueryAsync(ct);

                await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
                    INSERT INTO moex_trading_periods
                        (trade_date, market, boardid, secid, session, period_type,
                         time_from, time_till, moex_update_time, data_source)
                    VALUES (@trade_date, @market, @boardid, @secid, @session, @period_type,
                            @time_from, @time_till, @moex_update_time, 'calendar')
                    ON CONFLICT ON CONSTRAINT uq_moex_trading_periods_business DO NOTHING
                    """, connection, transaction);
                insertCommand.Parameters.Add("@trade_date", NpgsqlDbType.Date);
                insertCommand.Parameters.Add("@market", NpgsqlDbType.Text);
                insertCommand.Parameters.Add("@boardid", NpgsqlDbType.Text);
                insertCommand.Parameters.Add("@secid", NpgsqlDbType.Text);
                insertCommand.Parameters.Add("@session", NpgsqlDbType.Smallint);
                insertCommand.Parameters.Add("@period_type", NpgsqlDbType.Text);
                insertCommand.Parameters.Add("@time_from", NpgsqlDbType.Timestamp);
                insertCommand.Parameters.Add("@time_till", NpgsqlDbType.Timestamp);
                insertCommand.Parameters.Add("@moex_update_time", NpgsqlDbType.Timestamp);

                int rowsWritten = 0;
                for (int index = 0; index < rows.Count; index++)
                {
                    TradingPeriodWriteDTO row = rows[index];
                    insertCommand.Parameters["@trade_date"].Value = row.TradeDate;
                    insertCommand.Parameters["@market"].Value = row.Market;
                    insertCommand.Parameters["@boardid"].Value = row.BoardId;
                    insertCommand.Parameters["@secid"].Value = row.SecId;
                    insertCommand.Parameters["@session"].Value = (object?)row.Session ?? DBNull.Value;
                    insertCommand.Parameters["@period_type"].Value = row.PeriodType;
                    insertCommand.Parameters["@time_from"].Value = row.TimeFrom;
                    insertCommand.Parameters["@time_till"].Value = (object?)row.TimeTill ?? DBNull.Value;
                    insertCommand.Parameters["@moex_update_time"].Value =
                        (object?)row.MoexUpdateTime ?? DBNull.Value;
                    rowsWritten += await insertCommand.ExecuteNonQueryAsync(ct);
                }

                await transaction.CommitAsync(ct);
                return new CalendarBulkWriteResult(
                    rows.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<IReadOnlyList<Guid>> CreateManyAsync(
            IReadOnlyList<TradingPeriodCreateCommand> commands,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                List<Guid> ids = new List<Guid>(commands.Count);
                for (int index = 0; index < commands.Count; index++)
                    ids.Add(await CreateAsync(connection, transaction, commands[index], ct));

                await transaction.CommitAsync(ct);
                return ids;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<int> UpdateAsync(
            Guid id,
            TradingPeriodCreateCommand command,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                UPDATE moex_trading_periods
                SET trade_date = @trade_date,
                    market = @market,
                    boardid = @boardid,
                    secid = @secid,
                    session = @session,
                    period_type = @period_type,
                    time_from = @time_from,
                    time_till = @time_till,
                    note = @note,
                    updated_at = now()
                WHERE id = @id AND data_source = 'manual'
                """, connection);
            dbCommand.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = id;
            AddCommandParameters(dbCommand, command);
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> DeleteAsync(Guid id, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                DELETE FROM moex_trading_periods
                WHERE id = @id AND data_source = 'manual'
                """, connection);
            dbCommand.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = id;
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }

        private static async Task<Guid> CreateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            TradingPeriodCreateCommand command,
            CancellationToken ct)
        {
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO moex_trading_periods
                    (trade_date, market, boardid, secid, session, period_type,
                     time_from, time_till, data_source, note)
                VALUES (@trade_date, @market, @boardid, @secid, @session, @period_type,
                        @time_from, @time_till, 'manual', @note)
                RETURNING id
                """, connection, transaction);
            AddCommandParameters(dbCommand, command);

            object? scalar = await dbCommand.ExecuteScalarAsync(ct);
            return scalar is Guid id
                ? id
                : throw new InvalidOperationException("INSERT INTO moex_trading_periods did not return id.");
        }

        private static void AddCommandParameters(
            NpgsqlCommand dbCommand,
            TradingPeriodCreateCommand command)
        {
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
        }

        private readonly record struct TradingPeriodDayKey(string Market, DateOnly TradeDate);
    }
}
