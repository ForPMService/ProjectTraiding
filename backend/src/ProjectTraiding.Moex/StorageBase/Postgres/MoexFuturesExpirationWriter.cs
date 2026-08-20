using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexFuturesExpirationWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexFuturesExpirationWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<DbWriteResult> ReplaceRangeAsync(
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
    }
}
