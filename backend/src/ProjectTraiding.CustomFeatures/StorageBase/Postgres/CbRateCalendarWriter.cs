using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres
{
    public sealed class CbRateCalendarWriter
    {
        private readonly NpgsqlDataSource _dataSource;

        public CbRateCalendarWriter(NpgsqlDataSource dataSource)
        {
            _dataSource = dataSource;
        }

        public async Task<int> UpsertDaysAsync(
            IReadOnlyList<CbRateCalendarDayUpsertCommand> days,
            CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);
            try
            {
                int rowsWritten = 0;
                for (int index = 0; index < days.Count; index++)
                    rowsWritten += await UpsertAsync(connection, transaction, days[index], ct);

                await transaction.CommitAsync(ct);
                return rowsWritten;
            }
            catch
            {
                await transaction.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        public async Task<int> DeleteRangeAsync(DateOnly dateFrom, DateOnly dateTill, CancellationToken ct)
        {
            await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                DELETE FROM features_cb_rate_calendar
                WHERE d >= @date_from AND d <= @date_till
                """, connection);
            dbCommand.Parameters.Add("@date_from", NpgsqlDbType.Date).Value = dateFrom;
            dbCommand.Parameters.Add("@date_till", NpgsqlDbType.Date).Value = dateTill;
            return await dbCommand.ExecuteNonQueryAsync(ct);
        }

        /// <summary>
        /// Повторная запись того же дня строку не дублирует. Переданный блок
        /// перезаписывается целиком, непереданный остаётся прежним.
        /// </summary>
        private static async Task<int> UpsertAsync(
            NpgsqlConnection connection,
            NpgsqlTransaction transaction,
            CbRateCalendarDayUpsertCommand command,
            CancellationToken ct)
        {
            await using NpgsqlCommand dbCommand = new NpgsqlCommand("""
                INSERT INTO features_cb_rate_calendar
                    (d, rate_announced, rate_effective,
                     roisfix_1w, roisfix_2w, roisfix_1m, roisfix_2m, roisfix_3m,
                     roisfix_6m, roisfix_1y, roisfix_2y, roisfix_known_at,
                     ruonia, ruonia_status, ruonia_known_at,
                     expert_rate, expert_source, expert_known_at)
                VALUES (@d, @rate_announced, @rate_effective,
                        @roisfix_1w, @roisfix_2w, @roisfix_1m, @roisfix_2m, @roisfix_3m,
                        @roisfix_6m, @roisfix_1y, @roisfix_2y, @roisfix_known_at,
                        @ruonia, @ruonia_status, @ruonia_known_at,
                        @expert_rate, @expert_source, @expert_known_at)
                ON CONFLICT (d) DO UPDATE SET
                    rate_announced   = CASE WHEN @rate_present    THEN EXCLUDED.rate_announced   ELSE features_cb_rate_calendar.rate_announced   END,
                    rate_effective   = CASE WHEN @rate_present    THEN EXCLUDED.rate_effective   ELSE features_cb_rate_calendar.rate_effective   END,
                    roisfix_1w       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_1w       ELSE features_cb_rate_calendar.roisfix_1w       END,
                    roisfix_2w       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_2w       ELSE features_cb_rate_calendar.roisfix_2w       END,
                    roisfix_1m       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_1m       ELSE features_cb_rate_calendar.roisfix_1m       END,
                    roisfix_2m       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_2m       ELSE features_cb_rate_calendar.roisfix_2m       END,
                    roisfix_3m       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_3m       ELSE features_cb_rate_calendar.roisfix_3m       END,
                    roisfix_6m       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_6m       ELSE features_cb_rate_calendar.roisfix_6m       END,
                    roisfix_1y       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_1y       ELSE features_cb_rate_calendar.roisfix_1y       END,
                    roisfix_2y       = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_2y       ELSE features_cb_rate_calendar.roisfix_2y       END,
                    roisfix_known_at = CASE WHEN @roisfix_present THEN EXCLUDED.roisfix_known_at ELSE features_cb_rate_calendar.roisfix_known_at END,
                    ruonia           = CASE WHEN @ruonia_present  THEN EXCLUDED.ruonia           ELSE features_cb_rate_calendar.ruonia           END,
                    ruonia_status    = CASE WHEN @ruonia_present  THEN EXCLUDED.ruonia_status    ELSE features_cb_rate_calendar.ruonia_status    END,
                    ruonia_known_at  = CASE WHEN @ruonia_present  THEN EXCLUDED.ruonia_known_at  ELSE features_cb_rate_calendar.ruonia_known_at  END,
                    expert_rate      = CASE WHEN @expert_present  THEN EXCLUDED.expert_rate      ELSE features_cb_rate_calendar.expert_rate      END,
                    expert_source    = CASE WHEN @expert_present  THEN EXCLUDED.expert_source    ELSE features_cb_rate_calendar.expert_source    END,
                    expert_known_at  = CASE WHEN @expert_present  THEN EXCLUDED.expert_known_at  ELSE features_cb_rate_calendar.expert_known_at  END
                """, connection, transaction);

            dbCommand.Parameters.Add("@d", NpgsqlDbType.Date).Value = command.D;
            dbCommand.Parameters.Add("@rate_present", NpgsqlDbType.Boolean).Value = command.RatePresent;
            dbCommand.Parameters.Add("@roisfix_present", NpgsqlDbType.Boolean).Value = command.RoisfixPresent;
            dbCommand.Parameters.Add("@ruonia_present", NpgsqlDbType.Boolean).Value = command.RuoniaPresent;
            dbCommand.Parameters.Add("@expert_present", NpgsqlDbType.Boolean).Value = command.ExpertPresent;

            AddNumeric(dbCommand, "@rate_announced", command.RateAnnounced);
            AddNumeric(dbCommand, "@rate_effective", command.RateEffective);
            AddNumeric(dbCommand, "@roisfix_1w", command.Roisfix1w);
            AddNumeric(dbCommand, "@roisfix_2w", command.Roisfix2w);
            AddNumeric(dbCommand, "@roisfix_1m", command.Roisfix1m);
            AddNumeric(dbCommand, "@roisfix_2m", command.Roisfix2m);
            AddNumeric(dbCommand, "@roisfix_3m", command.Roisfix3m);
            AddNumeric(dbCommand, "@roisfix_6m", command.Roisfix6m);
            AddNumeric(dbCommand, "@roisfix_1y", command.Roisfix1y);
            AddNumeric(dbCommand, "@roisfix_2y", command.Roisfix2y);
            AddTimestamp(dbCommand, "@roisfix_known_at", command.RoisfixKnownAt);
            AddNumeric(dbCommand, "@ruonia", command.Ruonia);
            AddText(dbCommand, "@ruonia_status", command.RuoniaStatus);
            AddTimestamp(dbCommand, "@ruonia_known_at", command.RuoniaKnownAt);
            AddNumeric(dbCommand, "@expert_rate", command.ExpertRate);
            AddText(dbCommand, "@expert_source", command.ExpertSource);
            AddTimestamp(dbCommand, "@expert_known_at", command.ExpertKnownAt);

            return await dbCommand.ExecuteNonQueryAsync(ct);
        }

        private static void AddNumeric(NpgsqlCommand dbCommand, string name, decimal? value) =>
            dbCommand.Parameters.Add(name, NpgsqlDbType.Numeric).Value = (object?)value ?? DBNull.Value;

        private static void AddTimestamp(NpgsqlCommand dbCommand, string name, DateTime? value) =>
            dbCommand.Parameters.Add(name, NpgsqlDbType.Timestamp).Value = (object?)value ?? DBNull.Value;

        private static void AddText(NpgsqlCommand dbCommand, string name, string? value) =>
            dbCommand.Parameters.Add(name, NpgsqlDbType.Text).Value = (object?)value ?? DBNull.Value;
    }
}
