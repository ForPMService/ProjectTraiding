using Npgsql;
using NpgsqlTypes;
using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using System.Diagnostics;

namespace ProjectTraiding.CustomFeatures.StorageBase.Postgres;

public sealed class CalendarReferenceWriter
{
    private readonly NpgsqlDataSource _dataSource;

    public CalendarReferenceWriter(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    public async Task<CalendarBulkWriteResult> ReplaceIntervalsAsync(
        IReadOnlyList<InstrumentBoardIntervalDTO> intervals,
        CancellationToken ct)
    {
        long startTimestamp = Stopwatch.GetTimestamp();
        await using NpgsqlConnection connection = await _dataSource.OpenConnectionAsync(ct);
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync(ct);

        await using NpgsqlCommand deleteCommand = new NpgsqlCommand(
            "DELETE FROM moex_instrument_board_intervals", connection, transaction);
        await deleteCommand.ExecuteNonQueryAsync(ct);

        string[] markets = new string[intervals.Count];
        string[] secIds = new string[intervals.Count];
        string[] boardIds = new string[intervals.Count];
        DateOnly[] validFrom = new DateOnly[intervals.Count];
        DateOnly?[] validTill = new DateOnly?[intervals.Count];

        for (int index = 0; index < intervals.Count; index++)
        {
            InstrumentBoardIntervalDTO interval = intervals[index];
            markets[index] = interval.Market;
            secIds[index] = interval.SecId;
            boardIds[index] = interval.BoardId;
            validFrom[index] = interval.ValidFrom;
            validTill[index] = interval.ValidTill;
        }

        await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
            INSERT INTO moex_instrument_board_intervals
                (market, secid, boardid, valid_from, valid_till)
            SELECT s.market, s.secid, s.boardid, s.valid_from, s.valid_till
            FROM (
                SELECT DISTINCT ON (t.market, t.secid, t.boardid, t.valid_from)
                       t.market, t.secid, t.boardid, t.valid_from, t.valid_till
                FROM unnest(@market, @secid, @boardid, @valid_from, @valid_till)
                     WITH ORDINALITY AS t(market, secid, boardid, valid_from, valid_till, ord)
                ORDER BY t.market, t.secid, t.boardid, t.valid_from, t.ord DESC
            ) AS s
            ON CONFLICT (market, secid, boardid, valid_from) DO UPDATE SET
                valid_till = EXCLUDED.valid_till,
                updated_at = now()
            """, connection, transaction);
        insertCommand.Parameters.Add("@market", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = markets;
        insertCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
        insertCommand.Parameters.Add("@boardid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = boardIds;
        insertCommand.Parameters.Add("@valid_from", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = validFrom;
        insertCommand.Parameters.Add("@valid_till", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = validTill;

        int rowsWritten = await insertCommand.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        return new CalendarBulkWriteResult(
            intervals.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
    }

    public async Task<CalendarBulkWriteResult> ReplaceExpirationsAsync(
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

        string[] secIds = new string[expirations.Count];
        string?[] assetCodes = new string?[expirations.Count];
        DateOnly[] expirationDates = new DateOnly[expirations.Count];
        string?[] expirationTypes = new string?[expirations.Count];
        DateOnly?[] endDates = new DateOnly?[expirations.Count];
        short?[] weekendSessions = new short?[expirations.Count];

        for (int index = 0; index < expirations.Count; index++)
        {
            FuturesExpirationDTO expiration = expirations[index];
            secIds[index] = expiration.SecId;
            assetCodes[index] = expiration.AssetCode;
            expirationDates[index] = expiration.ExpirationDate;
            expirationTypes[index] = expiration.ExpirationType;
            endDates[index] = expiration.EndDate;
            weekendSessions[index] = expiration.WeekendSession;
        }

        await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
            INSERT INTO moex_futures_expirations
                (secid, asset_code, expiration_date, expiration_type, end_date, weekend_session)
            SELECT s.secid, s.asset_code, s.expiration_date, s.expiration_type, s.end_date,
                   s.weekend_session
            FROM (
                SELECT DISTINCT ON (t.secid)
                       t.secid, t.asset_code, t.expiration_date, t.expiration_type, t.end_date,
                       t.weekend_session
                FROM unnest(@secid, @asset_code, @expiration_date, @expiration_type, @end_date,
                            @weekend_session)
                     WITH ORDINALITY AS t(secid, asset_code, expiration_date, expiration_type,
                                          end_date, weekend_session, ord)
                ORDER BY t.secid, t.ord DESC
            ) AS s
            ON CONFLICT (secid) DO UPDATE SET
                asset_code      = EXCLUDED.asset_code,
                expiration_date = EXCLUDED.expiration_date,
                expiration_type = EXCLUDED.expiration_type,
                end_date        = EXCLUDED.end_date,
                weekend_session = EXCLUDED.weekend_session,
                updated_at      = now()
            """, connection, transaction);
        insertCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
        insertCommand.Parameters.Add("@asset_code", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = assetCodes;
        insertCommand.Parameters.Add("@expiration_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = expirationDates;
        insertCommand.Parameters.Add("@expiration_type", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = expirationTypes;
        insertCommand.Parameters.Add("@end_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = endDates;
        insertCommand.Parameters.Add("@weekend_session", NpgsqlDbType.Array | NpgsqlDbType.Smallint).Value = weekendSessions;

        int rowsWritten = await insertCommand.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        return new CalendarBulkWriteResult(
            expirations.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
    }

    public async Task<CalendarBulkWriteResult> ReplaceSplitsAsync(
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

        DateOnly[] tradeDates = new DateOnly[splits.Count];
        string[] secIds = new string[splits.Count];
        int[] beforeQty = new int[splits.Count];
        int[] afterQty = new int[splits.Count];

        for (int index = 0; index < splits.Count; index++)
        {
            SplitWriteDTO split = splits[index];
            tradeDates[index] = split.TradeDate;
            secIds[index] = split.SecId;
            beforeQty[index] = split.BeforeQty;
            afterQty[index] = split.AfterQty;
        }

        await using NpgsqlCommand insertCommand = new NpgsqlCommand("""
            INSERT INTO moex_splits (trade_date, secid, before_qty, after_qty)
            SELECT s.trade_date, s.secid, s.before_qty, s.after_qty
            FROM (
                SELECT DISTINCT ON (t.trade_date, t.secid)
                       t.trade_date, t.secid, t.before_qty, t.after_qty
                FROM unnest(@trade_date, @secid, @before_qty, @after_qty)
                     WITH ORDINALITY AS t(trade_date, secid, before_qty, after_qty, ord)
                ORDER BY t.trade_date, t.secid, t.ord DESC
            ) AS s
            ON CONFLICT (trade_date, secid) DO UPDATE SET
                before_qty = EXCLUDED.before_qty,
                after_qty  = EXCLUDED.after_qty,
                updated_at = now()
            """, connection, transaction);
        insertCommand.Parameters.Add("@trade_date", NpgsqlDbType.Array | NpgsqlDbType.Date).Value = tradeDates;
        insertCommand.Parameters.Add("@secid", NpgsqlDbType.Array | NpgsqlDbType.Text).Value = secIds;
        insertCommand.Parameters.Add("@before_qty", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = beforeQty;
        insertCommand.Parameters.Add("@after_qty", NpgsqlDbType.Array | NpgsqlDbType.Integer).Value = afterQty;

        int rowsWritten = await insertCommand.ExecuteNonQueryAsync(ct);

        await transaction.CommitAsync(ct);
        return new CalendarBulkWriteResult(
            splits.Count, rowsWritten, Stopwatch.GetElapsedTime(startTimestamp));
    }
}
