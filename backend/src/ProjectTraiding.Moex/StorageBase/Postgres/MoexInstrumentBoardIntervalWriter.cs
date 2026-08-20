using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexInstrumentBoardIntervalWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexInstrumentBoardIntervalWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<DbWriteResult> ReplaceAllAsync(
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
    }
}
