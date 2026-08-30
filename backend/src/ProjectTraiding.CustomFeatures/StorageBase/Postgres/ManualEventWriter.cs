using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public sealed class ManualEventWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public ManualEventWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<Guid> CreateAsync(ManualEventCreateCommand command, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO moex_manual_events
                    (secid, event_type, event_stage, event_date, known_from, record_date,
                     last_trade_date, payment_date, amount, currency, source_note)
                VALUES (@secid, @event_type, @event_stage, @event_date, @known_from, @record_date,
                        @last_trade_date, @payment_date, @amount, @currency, @source_note)
                RETURNING id
                """, connection);
            dbCommand.Parameters.Add("@secid", NpgsqlDbType.Text).Value = command.Secid;
            dbCommand.Parameters.Add("@event_type", NpgsqlDbType.Text).Value = command.EventType;
            dbCommand.Parameters.Add("@event_stage", NpgsqlDbType.Text).Value = command.EventStage;
            dbCommand.Parameters.Add("@event_date", NpgsqlDbType.Date).Value = command.EventDate;
            dbCommand.Parameters.Add("@known_from", NpgsqlDbType.Date).Value = command.KnownFrom;
            dbCommand.Parameters.Add("@record_date", NpgsqlDbType.Date).Value =
                (object?)command.RecordDate ?? DBNull.Value;
            dbCommand.Parameters.Add("@last_trade_date", NpgsqlDbType.Date).Value =
                (object?)command.LastTradeDate ?? DBNull.Value;
            dbCommand.Parameters.Add("@payment_date", NpgsqlDbType.Date).Value =
                (object?)command.PaymentDate ?? DBNull.Value;
            dbCommand.Parameters.Add("@amount", NpgsqlDbType.Numeric).Value =
                (object?)command.Amount ?? DBNull.Value;
            dbCommand.Parameters.Add("@currency", NpgsqlDbType.Text).Value =
                (object?)command.Currency ?? DBNull.Value;
            dbCommand.Parameters.Add("@source_note", NpgsqlDbType.Text).Value =
                (object?)command.SourceNote ?? DBNull.Value;

            return (Guid)(await dbCommand.ExecuteScalarAsync(ct))!;
        }
    }
}
