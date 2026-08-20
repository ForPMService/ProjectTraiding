using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.Moex.Contracts.Dto.Calendar;

namespace ProjectTraiding.Moex.StorageBase.Postgres
{
    public sealed class MoexManualEventWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public MoexManualEventWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<int> InsertAsync(ManualEventWriteDTO manualEvent, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand command = new NpgsqlCommand("""
                INSERT INTO moex_manual_events
                    (secid, event_type, event_date, known_from, record_date,
                     last_trade_date, payment_date, amount, currency, source_note)
                VALUES (@secid, @event_type, @event_date, @known_from, @record_date,
                        @last_trade_date, @payment_date, @amount, @currency, @source_note)
                """, connection);
            command.Parameters.Add("@secid", NpgsqlDbType.Text).Value = manualEvent.SecId;
            command.Parameters.Add("@event_type", NpgsqlDbType.Text).Value = manualEvent.EventType;
            command.Parameters.Add("@event_date", NpgsqlDbType.Date).Value = manualEvent.EventDate;
            command.Parameters.Add("@known_from", NpgsqlDbType.Date).Value = manualEvent.KnownFrom;
            command.Parameters.Add("@record_date", NpgsqlDbType.Date).Value =
                (object?)manualEvent.RecordDate ?? DBNull.Value;
            command.Parameters.Add("@last_trade_date", NpgsqlDbType.Date).Value =
                (object?)manualEvent.LastTradeDate ?? DBNull.Value;
            command.Parameters.Add("@payment_date", NpgsqlDbType.Date).Value =
                (object?)manualEvent.PaymentDate ?? DBNull.Value;
            command.Parameters.Add("@amount", NpgsqlDbType.Numeric).Value =
                (object?)manualEvent.Amount ?? DBNull.Value;
            command.Parameters.Add("@currency", NpgsqlDbType.Text).Value =
                (object?)manualEvent.Currency ?? DBNull.Value;
            command.Parameters.Add("@source_note", NpgsqlDbType.Text).Value =
                (object?)manualEvent.SourceNote ?? DBNull.Value;
            return await command.ExecuteNonQueryAsync(ct);
        }
    }
}
