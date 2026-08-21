using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexCalendarReferenceWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexCalendarReferenceWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<DbWriteResult> ReplaceIntervalsAsync(
            IReadOnlyList<InstrumentBoardIntervalDTO> intervals,
            CancellationToken ct)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            await using NpgsqlCommand deleteCommand = new NpgsqlCommand(
                "DELETE FROM moex_instrument_board_intervals", connection, transaction);
            await deleteCommand.ExecuteNonQueryAsync(ct);

            await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
                INSERT INTO moex_instrument_board_intervals
                    (market, secid, boardid, valid_from, valid_till)
                VALUES (@market, @secid, @boardid, @valid_from, @valid_till)
                ON CONFLICT (market, secid, boardid, valid_from) DO UPDATE SET
                    valid_till = EXCLUDED.valid_till,
                    updated_at = now()
                """, connection, transaction);
            NpgsqlParameter marketParameter =
                insertCommand.Parameters.Add("@market", NpgsqlDbType.Text);
            NpgsqlParameter secIdParameter =
                insertCommand.Parameters.Add("@secid", NpgsqlDbType.Text);
            NpgsqlParameter boardIdParameter =
                insertCommand.Parameters.Add("@boardid", NpgsqlDbType.Text);
            NpgsqlParameter validFromParameter =
                insertCommand.Parameters.Add("@valid_from", NpgsqlDbType.Date);
            NpgsqlParameter validTillParameter =
                insertCommand.Parameters.Add("@valid_till", NpgsqlDbType.Date);

            int rowsWritten = 0;
            for (int index = 0; index < intervals.Count; index++)
            {
                InstrumentBoardIntervalDTO interval = intervals[index];
                marketParameter.Value = interval.Market;
                secIdParameter.Value = interval.SecId;
                boardIdParameter.Value = interval.BoardId;
                validFromParameter.Value = interval.ValidFrom;
                validTillParameter.Value = (object?)interval.ValidTill ?? DBNull.Value;
                rowsWritten += await insertCommand.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new DbWriteResult(
                intervals.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
        }

        public async Task<DbWriteResult> ReplaceExpirationsAsync(
            DateOnly dateFrom,
            DateOnly dateTill,
            IReadOnlyList<FuturesExpirationDTO> expirations,
            CancellationToken ct)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            await using NpgsqlCommand deleteCommand = new NpgsqlCommand("""
                DELETE FROM moex_futures_expirations
                WHERE expiration_date BETWEEN @date_from AND @date_till
                """, connection, transaction);
            deleteCommand.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = dateFrom;
            deleteCommand.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = dateTill;
            await deleteCommand.ExecuteNonQueryAsync(ct);

            await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
                INSERT INTO moex_futures_expirations
                    (secid, asset_code, expiration_date, expiration_type, end_date, weekend_session)
                VALUES (@secid, @asset_code, @expiration_date, @expiration_type, @end_date, @weekend_session)
                ON CONFLICT (secid) DO UPDATE SET
                    asset_code      = EXCLUDED.asset_code,
                    expiration_date = EXCLUDED.expiration_date,
                    expiration_type = EXCLUDED.expiration_type,
                    end_date        = EXCLUDED.end_date,
                    weekend_session = EXCLUDED.weekend_session,
                    updated_at      = now()
                """, connection, transaction);
            NpgsqlParameter secIdParameter = insertCommand.Parameters.Add("@secid", NpgsqlDbType.Text);
            NpgsqlParameter assetCodeParameter = insertCommand.Parameters.Add("@asset_code", NpgsqlDbType.Text);
            NpgsqlParameter expirationDateParameter = insertCommand.Parameters.Add("@expiration_date", NpgsqlDbType.Date);
            NpgsqlParameter expirationTypeParameter = insertCommand.Parameters.Add("@expiration_type", NpgsqlDbType.Text);
            NpgsqlParameter endDateParameter = insertCommand.Parameters.Add("@end_date", NpgsqlDbType.Date);
            NpgsqlParameter weekendSessionParameter = insertCommand.Parameters.Add("@weekend_session", NpgsqlDbType.Smallint);

            int rowsWritten = 0;
            for (int index = 0; index < expirations.Count; index++)
            {
                FuturesExpirationDTO expiration = expirations[index];
                secIdParameter.Value = expiration.SecId;
                assetCodeParameter.Value = (object?)expiration.AssetCode ?? DBNull.Value;
                expirationDateParameter.Value = expiration.ExpirationDate;
                expirationTypeParameter.Value = (object?)expiration.ExpirationType ?? DBNull.Value;
                endDateParameter.Value = (object?)expiration.EndDate ?? DBNull.Value;
                weekendSessionParameter.Value = (object?)expiration.WeekendSession ?? DBNull.Value;
                rowsWritten += await insertCommand.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new DbWriteResult(
                expirations.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
        }

        public async Task<DbWriteResult> ReplaceSplitsAsync(
            DateOnly dateFrom,
            DateOnly dateTill,
            IReadOnlyList<SplitWriteDTO> splits,
            CancellationToken ct)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

            await using NpgsqlCommand deleteCommand = new NpgsqlCommand("""
                DELETE FROM moex_splits
                WHERE trade_date BETWEEN @date_from AND @date_till
                """, connection, transaction);
            deleteCommand.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = dateFrom;
            deleteCommand.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = dateTill;
            await deleteCommand.ExecuteNonQueryAsync(ct);

            await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
                INSERT INTO moex_splits (trade_date, secid, before_qty, after_qty)
                VALUES (@trade_date, @secid, @before_qty, @after_qty)
                ON CONFLICT (trade_date, secid) DO UPDATE SET
                    before_qty = EXCLUDED.before_qty,
                    after_qty  = EXCLUDED.after_qty,
                    updated_at = now()
                """, connection, transaction);
            NpgsqlParameter tradeDateParameter = insertCommand.Parameters.Add("@trade_date", NpgsqlDbType.Date);
            NpgsqlParameter secIdParameter = insertCommand.Parameters.Add("@secid", NpgsqlDbType.Text);
            NpgsqlParameter beforeQtyParameter = insertCommand.Parameters.Add("@before_qty", NpgsqlDbType.Integer);
            NpgsqlParameter afterQtyParameter = insertCommand.Parameters.Add("@after_qty", NpgsqlDbType.Integer);

            int rowsWritten = 0;
            for (int index = 0; index < splits.Count; index++)
            {
                SplitWriteDTO split = splits[index];
                tradeDateParameter.Value = split.TradeDate;
                secIdParameter.Value = split.SecId;
                beforeQtyParameter.Value = split.BeforeQty;
                afterQtyParameter.Value = split.AfterQty;
                rowsWritten += await insertCommand.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new DbWriteResult(
                splits.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
        }
    }
}
