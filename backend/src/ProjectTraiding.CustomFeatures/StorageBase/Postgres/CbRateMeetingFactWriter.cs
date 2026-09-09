using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public sealed class CbRateMeetingFactWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public CbRateMeetingFactWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<(Guid Id, Guid CycleId)> CreateAsync(
            CbRateMeetingFactCreateCommand command,
            CancellationToken ct)
        {
            Guid cycleId = command.CycleId ?? Guid.CreateVersion7();

            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO features_cb_rate_meeting_facts
                    (cycle_id, fact_type, meeting_date, scheduled_publication_at, known_at,
                     published_at, rate_before, rate_after, effective_from, is_cancelled)
                VALUES (@cycle_id, @fact_type, @meeting_date, @scheduled_publication_at, @known_at,
                        @published_at, @rate_before, @rate_after, @effective_from, @is_cancelled)
                RETURNING id
                """, connection);
            dbCommand.Parameters.Add("@cycle_id", NpgsqlDbType.Uuid).Value = cycleId;
            dbCommand.Parameters.Add("@fact_type", NpgsqlDbType.Text).Value = command.FactType;
            dbCommand.Parameters.Add("@meeting_date", NpgsqlDbType.Date).Value = command.MeetingDate;
            dbCommand.Parameters.Add("@scheduled_publication_at", NpgsqlDbType.Timestamp).Value =
                (object?)command.ScheduledPublicationAt ?? DBNull.Value;
            dbCommand.Parameters.Add("@known_at", NpgsqlDbType.Timestamp).Value = command.KnownAt;
            dbCommand.Parameters.Add("@published_at", NpgsqlDbType.Timestamp).Value =
                (object?)command.PublishedAt ?? DBNull.Value;
            dbCommand.Parameters.Add("@rate_before", NpgsqlDbType.Numeric).Value =
                (object?)command.RateBefore ?? DBNull.Value;
            dbCommand.Parameters.Add("@rate_after", NpgsqlDbType.Numeric).Value =
                (object?)command.RateAfter ?? DBNull.Value;
            dbCommand.Parameters.Add("@effective_from", NpgsqlDbType.Date).Value =
                (object?)command.EffectiveFrom ?? DBNull.Value;
            dbCommand.Parameters.Add("@is_cancelled", NpgsqlDbType.Boolean).Value = command.IsCancelled;

            object? scalar = await dbCommand.ExecuteScalarAsync(ct);
            return scalar is Guid id
                ? (id, cycleId)
                : throw new InvalidOperationException(
                    "INSERT INTO features_cb_rate_meeting_facts did not return id.");
        }
    }
}
