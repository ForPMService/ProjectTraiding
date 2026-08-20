using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexSplitWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexSplitWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<DbWriteResult> ReplaceRangeAsync(
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
