using System.Text.Json;
using System.Collections.Immutable;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class MegaAlertsHistoryParser
{
    internal static void ParseStock(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<MegaAlertsStockRow> writer, DateTime? lastSourceTime, HistoricalReadState state,
        ref HistoricalSeriesTimeGroupTail<MegaAlertsStockRow>? tail, out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "stock", MoexColumnSets.MegaAlertsStock);
        HistoricalSeriesTimeGroupTail<MegaAlertsStockRow> pageTail =
            tail ??= new HistoricalSeriesTimeGroupTail<MegaAlertsStockRow>(writer, state, lastSourceTime);
        cursor = ParseEnvelope(body, spec, MoexColumnSets.MegaAlertsStock,
            (ref Utf8JsonReader reader) => ReadStockRows(ref reader, spec, taskSecId, pageTail, state));
    }

    internal static void ParseFutures(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<MegaAlertsFuturesRow> writer, DateTime? lastSourceTime, HistoricalReadState state,
        ref HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow>? tail, out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "futures", MoexColumnSets.MegaAlertsFutures);
        HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow> pageTail =
            tail ??= new HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow>(writer, state, lastSourceTime);
        cursor = ParseEnvelope(body, spec, MoexColumnSets.MegaAlertsFutures,
            (ref Utf8JsonReader reader) => ReadFuturesRows(ref reader, spec, taskSecId, pageTail, state));
    }

    private delegate void ReadRows(ref Utf8JsonReader reader);

    private static PaginationCursorDTO ParseEnvelope(ReadOnlySpan<byte> body, MoexSeriesSpec spec,
        ImmutableArray<string> columns, ReadRows readRows)
    {
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.SkipToRootObject(ref reader, spec.RootKey);
        bool foundColumns = false;
        bool foundData = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName) continue;
            if (reader.ValueTextEquals("columns"u8))
            {
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(ref reader, columns, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch("[data] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                readRows(ref reader);
            }
            else reader.Skip();
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        return ReadCursor(ref reader);
    }

    private static void ReadStockRows(ref Utf8JsonReader reader, MoexSeriesSpec spec, string taskSecId,
        HistoricalSeriesTimeGroupTail<MegaAlertsStockRow> tail, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = tail.FullSourceRowsCount;
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? error = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка мегаалёртов отвергнута: secid пустой." : null;
            MegaAlertsStockRow row = new() { Secid = taskSecId };
            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref error);
            ExpectString(ref reader, spec, rowIndex, 2, ref error);
            row.AlertType = ReadString(ref reader, spec, rowIndex, 3, ref error)!;
            row.Threshold = ReadDouble(ref reader, spec, rowIndex, 4, ref error);
            row.Value = ReadDouble(ref reader, spec, rowIndex, 5, ref error);
            row.Reference = ReadString(ref reader, spec, rowIndex, 6, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 7, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.MegaAlertsStock.Length);
            DateTime? time = FinishRow(ref timeParts, ref error, row.AlertType);
            if (error is null)
            {
                try { row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts)); }
                catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }
            }
            if (error is null) tail.AddAccepted(rowIndex, time, in row);
            else tail.AddSkipped(rowIndex, time, error);
        }
    }

    private static void ReadFuturesRows(ref Utf8JsonReader reader, MoexSeriesSpec spec, string taskSecId,
        HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow> tail, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = tail.FullSourceRowsCount;
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? error = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка мегаалёртов отвергнута: secid пустой." : null;
            MegaAlertsFuturesRow row = new() { Secid = taskSecId };
            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref error);
            ExpectString(ref reader, spec, rowIndex, 2, ref error);
            row.AssetCode = ReadString(ref reader, spec, rowIndex, 3, ref error);
            row.AlertType = ReadString(ref reader, spec, rowIndex, 4, ref error)!;
            row.Threshold = ReadDouble(ref reader, spec, rowIndex, 5, ref error);
            row.Value = ReadDouble(ref reader, spec, rowIndex, 6, ref error);
            row.Reference = ReadString(ref reader, spec, rowIndex, 7, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 8, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.MegaAlertsFutures.Length);
            DateTime? time = FinishRow(ref timeParts, ref error, row.AlertType);
            if (error is null)
            {
                try { row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts)); }
                catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }
            }
            if (error is null) tail.AddAccepted(rowIndex, time, in row);
            else tail.AddSkipped(rowIndex, time, error);
        }
    }

    private static DateTime? FinishRow(ref SourceTimeParts timeParts, ref string? error, string? alertType)
    {
        DateTime? time = TryReadSourceTime(in timeParts);
        if (error is not null) return time;
        if (string.IsNullOrWhiteSpace(alertType))
            error = "Строка мегаалёртов отвергнута: alert_type пустой.";
        return time;
    }

    private static void ReadMomentDate(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col,
        ref SourceTimeParts parts, ref string? error)
    {
        try { parts.Date = ParseHelpersUtf8.ReadNextMomentDate(ref reader, row, col, spec.RootKey, out parts.RawDate); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); }
    }
    private static void ReadMomentTime(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col,
        ref SourceTimeParts parts, ref string? error)
    {
        try { parts.Time = ParseHelpersUtf8.ReadNextMomentTime(ref reader, row, col, spec.RootKey, out parts.RawTime); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); }
    }
    private static void ExpectString(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, row, spec.RootKey, col); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); }
    }
    private static string? ReadString(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableString(ref reader, row, spec.RootKey, col); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }
    private static double? ReadDouble(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableDouble(ref reader, row, spec.RootKey, col); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }
    private static ulong? ReadSystime(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try
        {
            DateTime? value = ParseHelpersUtf8.ReadNextNullableDateTime(ref reader, row, spec.RootKey, col, out bool wasNull);
            if (value is null && !wasNull && !reader.ValueTextEquals(""u8)) error ??= $"[{spec.RootKey}] Некорректное непустое systime в строке {row}.";
            return value.HasValue ? CandleDateTime64.ToUnixWholeSeconds(value.Value) : null;
        }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }
    private static void ExpectRowStart(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row)
    {
        if (reader.TokenType != JsonTokenType.StartArray) ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {row}, получено {reader.TokenType}.");
    }
    private static DateTime? TryReadSourceTime(in SourceTimeParts parts)
    {
        try { return MoexClickHouseTime.BuildSourceTime(in parts); }
        catch (Exception ex) when (IsRowRejection(ex)) { return null; }
    }
    private static bool IsRowRejection(Exception ex) => ex is InvalidOperationException or FormatException or ArgumentException or OverflowException;
    private static PaginationCursorDTO ReadCursor(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("data.cursor"u8))
            {
                try { return ParseHelpersUtf8.ReadCursorRootObject(ref reader); }
                catch (InvalidOperationException ex) { ParseHelpersUtf8.SchemaMismatch(ex.Message); }
            }
            reader.Skip();
        }
        return new PaginationCursorDTO();
    }
    private static void ValidateSpec(MoexSeriesSpec spec, string market, IReadOnlyList<string> columns)
    {
        if (spec.DataKind != "mega_alerts" || spec.Market != market || spec.Pagination != PaginationKind.Cursor
            || !spec.PreserveCursorTimeGroup || !spec.InputColumns.SequenceEqual(columns))
            throw new ArgumentException($"Typed MegaAlerts parsing requires {market} mega_alerts with cursor time groups.", nameof(spec));
    }
}
