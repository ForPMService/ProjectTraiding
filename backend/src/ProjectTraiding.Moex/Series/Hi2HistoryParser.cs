using System.Text.Json;
using System.Collections.Immutable;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class Hi2HistoryParser
{
    internal static void ParseStock(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<Hi2StockRow> writer, DateTime? lastSourceTime, HistoricalReadState state,
        out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "stock", MoexColumnSets.Hi2Stock);
        cursor = ParseEnvelope(body, spec, MoexColumnSets.Hi2Stock,
            (ref Utf8JsonReader reader) => ReadStockRows(ref reader, spec, taskSecId, writer, lastSourceTime, state));
    }

    internal static void ParseFutures(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<Hi2FuturesRow> writer, DateTime? lastSourceTime, HistoricalReadState state,
        out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "futures", MoexColumnSets.Hi2Futures);
        cursor = ParseEnvelope(body, spec, MoexColumnSets.Hi2Futures,
            (ref Utf8JsonReader reader) => ReadFuturesRows(ref reader, spec, taskSecId, writer, lastSourceTime, state));
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
        ProtobufBatchWriter<Hi2StockRow> writer, DateTime? lastSourceTime, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? error = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка концентрации отвергнута: secid пустой." : null;
            Hi2StockRow row = new() { Secid = taskSecId };
            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref error);
            ExpectString(ref reader, spec, rowIndex, 2, ref error);
            row.Metric = ReadString(ref reader, spec, rowIndex, 3, ref error)!;
            row.Value = ReadDouble(ref reader, spec, rowIndex, 4, ref error);
            row.Reference = ReadString(ref reader, spec, rowIndex, 5, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 6, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.Hi2Stock.Length);
            DateTime? time = TryReadSourceTime(in timeParts);
            state.SetPageLastSourceTime(time);
            try
            {
                if (error is not null) throw new InvalidOperationException(error);
                if (string.IsNullOrWhiteSpace(row.Metric))
                    throw new InvalidOperationException("Строка концентрации отвергнута: metric пустой.");
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts));
            }
            catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }
            if (error is null)
            {
                state.RecordParsedValidRow(receivedMetric: true);
                if (!(lastSourceTime is DateTime cursorTime && time is DateTime sourceTime && sourceTime < cursorTime))
                {
                    writer.Append(in row);
                    state.RecordAccepted(time);
                }
            }
            else state.RecordSkipped(rowIndex, time, error);
            rowIndex++;
        }
    }

    private static void ReadFuturesRows(ref Utf8JsonReader reader, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<Hi2FuturesRow> writer, DateTime? lastSourceTime, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? error = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка концентрации отвергнута: secid пустой." : null;
            Hi2FuturesRow row = new() { Secid = taskSecId };
            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref error);
            ExpectString(ref reader, spec, rowIndex, 2, ref error);
            row.AssetCode = ReadString(ref reader, spec, rowIndex, 3, ref error);
            row.Metric = ReadString(ref reader, spec, rowIndex, 4, ref error)!;
            row.Value = ReadDouble(ref reader, spec, rowIndex, 5, ref error);
            row.Reference = ReadString(ref reader, spec, rowIndex, 6, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 7, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.Hi2Futures.Length);
            DateTime? time = TryReadSourceTime(in timeParts);
            state.SetPageLastSourceTime(time);
            try
            {
                if (error is not null) throw new InvalidOperationException(error);
                if (string.IsNullOrWhiteSpace(row.Metric))
                    throw new InvalidOperationException("Строка концентрации отвергнута: metric пустой.");
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts));
            }
            catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }
            if (error is null)
            {
                state.RecordParsedValidRow(receivedMetric: true);
                if (!(lastSourceTime is DateTime cursorTime && time is DateTime sourceTime && sourceTime < cursorTime))
                {
                    writer.Append(in row);
                    state.RecordAccepted(time);
                }
            }
            else state.RecordSkipped(rowIndex, time, error);
            rowIndex++;
        }
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
        if (spec.DataKind != "hi2" || spec.Market != market || spec.Pagination != PaginationKind.Cursor
            || !spec.InputColumns.SequenceEqual(columns))
            throw new ArgumentException($"Typed HI2 parsing requires {market} hi2.", nameof(spec));
    }
}
