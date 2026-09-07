using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public sealed class DividendEventWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public DividendEventWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<IReadOnlyList<Guid>> CreateManyAsync(
            IReadOnlyList<DividendEventCreateCommand> commands,
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

        public async Task<int> UpdateAsync(DividendEventUpdateCommand command, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                UPDATE moex_dividend_events
                SET secid = @secid,
                    event_type = @event_type,
                    event_date = @event_date,
                    known_at = @known_at,
                    dividend_amount = @dividend_amount,
                    currency = @currency,
                    record_date = @record_date,
                    last_eligible_trade_date = @last_eligible_trade_date,
                    payment_date = @payment_date,
                    source_note = @source_note,
                    updated_at = now()
                WHERE id = @id
                """, connection);
            AddUpdateParameters(dbCommand, command);
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }

        public async Task<int> DeleteAsync(Guid id, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                DELETE FROM moex_dividend_events
                WHERE id = @id
                """, connection);
            dbCommand.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = id;
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }

        private static async Task<Guid> CreateAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            DividendEventCreateCommand command,
            CancellationToken ct)
        {
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO moex_dividend_events
                    (secid, event_type, event_date, known_at, is_cancelled, dividend_amount,
                     currency, record_date, last_eligible_trade_date, payment_date, source_note)
                VALUES (@secid, @event_type, @event_date, @known_at, @is_cancelled, @dividend_amount,
                        @currency, @record_date, @last_eligible_trade_date, @payment_date, @source_note)
                RETURNING id
                """, connection, transaction);
            dbCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = command.Secid;
            dbCommand.Parameters.Add("@event_type", NpgsqlDbType.Text).Value = command.EventType;
            dbCommand.Parameters.Add("@event_date", NpgsqlDbType.Date).Value = command.EventDate;
            dbCommand.Parameters.Add("@known_at", NpgsqlDbType.Timestamp).Value = command.KnownAt;
            dbCommand.Parameters.Add("@is_cancelled", NpgsqlDbType.Boolean).Value = command.IsCancelled;
            AddOptionalParameters(
                dbCommand,
                command.DividendAmount,
                command.Currency,
                command.RecordDate,
                command.LastEligibleTradeDate,
                command.PaymentDate,
                command.SourceNote);

            object? scalar = await dbCommand.ExecuteScalarAsync(ct);
            return scalar is Guid id
                ? id
                : throw new InvalidOperationException("INSERT INTO moex_dividend_events did not return id.");
        }

        private static void AddUpdateParameters(
            NpgsqlCommand dbCommand,
            DividendEventUpdateCommand command)
        {
            dbCommand.Parameters.Add("@id", NpgsqlDbType.Uuid).Value = command.Id;
            dbCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = command.Secid;
            dbCommand.Parameters.Add("@event_type", NpgsqlDbType.Text).Value = command.EventType;
            dbCommand.Parameters.Add("@event_date", NpgsqlDbType.Date).Value = command.EventDate;
            dbCommand.Parameters.Add("@known_at", NpgsqlDbType.Timestamp).Value = command.KnownAt;
            AddOptionalParameters(
                dbCommand,
                command.DividendAmount,
                command.Currency,
                command.RecordDate,
                command.LastEligibleTradeDate,
                command.PaymentDate,
                command.SourceNote);
        }

        private static void AddOptionalParameters(
            NpgsqlCommand dbCommand,
            decimal? dividendAmount,
            string? currency,
            DateOnly? recordDate,
            DateOnly? lastEligibleTradeDate,
            DateOnly? paymentDate,
            string? sourceNote)
        {
            dbCommand.Parameters.Add("@dividend_amount", NpgsqlDbType.Numeric).Value =
                (object?)dividendAmount ?? DBNull.Value;
            dbCommand.Parameters.Add("@currency", NpgsqlDbType.Text).Value =
                (object?)currency ?? DBNull.Value;
            dbCommand.Parameters.Add("@record_date", NpgsqlDbType.Date).Value =
                (object?)recordDate ?? DBNull.Value;
            dbCommand.Parameters.Add("@last_eligible_trade_date", NpgsqlDbType.Date).Value =
                (object?)lastEligibleTradeDate ?? DBNull.Value;
            dbCommand.Parameters.Add("@payment_date", NpgsqlDbType.Date).Value =
                (object?)paymentDate ?? DBNull.Value;
            dbCommand.Parameters.Add("@source_note", NpgsqlDbType.Text).Value =
                (object?)sourceNote ?? DBNull.Value;
        }
    }
}
