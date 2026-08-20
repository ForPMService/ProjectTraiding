using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexTradingPeriodTypeWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexTradingPeriodTypeWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<DbWriteResult> UpsertAsync(
            IReadOnlyList<TradingPeriodTypeDTO> types,
            CancellationToken ct)
        {
            long startTimestamp = Stopwatch.GetTimestamp();
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                INSERT INTO moex_trading_period_types (market, type_code, title)
                VALUES (@market, @type_code, @title)
                ON CONFLICT (market, type_code) DO UPDATE SET
                    title = EXCLUDED.title
                """, connection, transaction);

            NpgsqlParameter marketParameter = command.Parameters.Add("@market", NpgsqlDbType.Text);
            NpgsqlParameter typeCodeParameter = command.Parameters.Add("@type_code", NpgsqlDbType.Text);
            NpgsqlParameter titleParameter = command.Parameters.Add("@title", NpgsqlDbType.Text);

            int rowsWritten = 0;
            for (int index = 0; index < types.Count; index++)
            {
                TradingPeriodTypeDTO type = types[index];
                marketParameter.Value = type.Market;
                typeCodeParameter.Value = type.TypeCode;
                titleParameter.Value = type.Title;
                rowsWritten += await command.ExecuteNonQueryAsync(ct);
            }

            await transaction.CommitAsync(ct);
            return new DbWriteResult(
                types.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
        }
    }
}
