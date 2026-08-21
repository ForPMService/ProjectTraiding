using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Management.Contracts.Dto;

namespace ProjectTraiding.Management.StorageBase.Postgres
{
    public sealed class ManualEventWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public ManualEventWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<Guid> CreateAsync(ManualEventCreateRequest request, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                INSERT INTO moex_manual_events
                    (secid, event_type, event_stage, event_date, known_from, record_date,
                     last_trade_date, payment_date, amount, currency, source_note)
                VALUES (@secid, @event_type, @event_stage, @event_date, @known_from, @record_date,
                        @last_trade_date, @payment_date, @amount, @currency, @source_note)
                RETURNING id
                """, connection);
            command.Parameters.Add("@secid", NpgsqlDbType.Text).Value = request.Secid!;
            command.Parameters.Add("@event_type", NpgsqlDbType.Text).Value = request.EventType!;
            command.Parameters.Add("@event_stage", NpgsqlDbType.Text).Value = request.EventStage!;
            command.Parameters.Add("@event_date", NpgsqlDbType.Date).Value = request.EventDate!.Value;
            command.Parameters.Add("@known_from", NpgsqlDbType.Date).Value = request.KnownFrom!.Value;
            command.Parameters.Add("@record_date", NpgsqlDbType.Date).Value =
                (object?)request.RecordDate ?? DBNull.Value;
            command.Parameters.Add("@last_trade_date", NpgsqlDbType.Date).Value =
                (object?)request.LastTradeDate ?? DBNull.Value;
            command.Parameters.Add("@payment_date", NpgsqlDbType.Date).Value =
                (object?)request.PaymentDate ?? DBNull.Value;
            command.Parameters.Add("@amount", NpgsqlDbType.Numeric).Value =
                (object?)request.Amount ?? DBNull.Value;
            command.Parameters.Add("@currency", NpgsqlDbType.Text).Value =
                (object?)request.Currency ?? DBNull.Value;
            command.Parameters.Add("@source_note", NpgsqlDbType.Text).Value =
                (object?)request.SourceNote ?? DBNull.Value;

            return (Guid)(await command.ExecuteScalarAsync(ct))!;
        }
    }
}
