using System.Text.Json;
using System.Collections.Immutable;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class ObStatsHistoryParser
{
    internal static void ParseStock(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<ObStatsStockRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "stock", MoexColumnSets.ObStatsStock);
        ParseEnvelope(body, spec, MoexColumnSets.ObStatsStock, state, out cursor,
            (ref Utf8JsonReader reader) => ReadStockRows(ref reader, spec, taskSecId, writer, lastSourceTime, state));
    }

    internal static void ParseFutures(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<ObStatsFuturesRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state, out PaginationCursorDTO cursor)
    {
        ValidateSpec(spec, "futures", MoexColumnSets.ObStatsFutures);
        ParseEnvelope(body, spec, MoexColumnSets.ObStatsFutures, state, out cursor,
            (ref Utf8JsonReader reader) => ReadFuturesRows(ref reader, spec, taskSecId, writer, lastSourceTime, state));
    }

    private delegate void ReadRows(ref Utf8JsonReader reader);

    private static void ParseEnvelope(ReadOnlySpan<byte> body, MoexSeriesSpec spec,
        ImmutableArray<string> columns, HistoricalReadState state,
        out PaginationCursorDTO cursor, ReadRows readRows)
    {
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.SkipToRootObject(ref reader, spec.RootKey);
        bool foundColumns = false;
        bool foundData = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            if (reader.ValueTextEquals("columns"u8))
            {
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(ref reader, columns, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                readRows(ref reader);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
    }

    private static void ReadStockRows(ref Utf8JsonReader reader, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<ObStatsStockRow> writer, DateTime? lastSourceTime, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? error = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка статистики стакана отвергнута: secid пустой." : null;
            ObStatsStockRow row = new() { Secid = taskSecId };
            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref error);
            ExpectString(ref reader, spec, rowIndex, 2, ref error);
            row.SpreadBbo = ReadDouble(ref reader, spec, rowIndex, 3, ref error);
            row.SpreadLv10 = ReadDouble(ref reader, spec, rowIndex, 4, ref error);
            row.Spread1Mio = ReadDouble(ref reader, spec, rowIndex, 5, ref error);
            row.LevelsB = ReadInt(ref reader, spec, rowIndex, 6, ref error);
            row.LevelsS = ReadInt(ref reader, spec, rowIndex, 7, ref error);
            row.VolB = ReadLong(ref reader, spec, rowIndex, 8, ref error);
            row.VolS = ReadLong(ref reader, spec, rowIndex, 9, ref error);
            row.ValB = ReadLong(ref reader, spec, rowIndex, 10, ref error);
            row.ValS = ReadLong(ref reader, spec, rowIndex, 11, ref error);
            row.ImbalanceVolBbo = ReadDouble(ref reader, spec, rowIndex, 12, ref error);
            row.ImbalanceValBbo = ReadDouble(ref reader, spec, rowIndex, 13, ref error);
            row.ImbalanceVol = ReadDouble(ref reader, spec, rowIndex, 14, ref error);
            row.ImbalanceVal = ReadDouble(ref reader, spec, rowIndex, 15, ref error);
            row.VwapB = ReadDouble(ref reader, spec, rowIndex, 16, ref error);
            row.VwapS = ReadDouble(ref reader, spec, rowIndex, 17, ref error);
            row.VwapB1Mio = ReadDouble(ref reader, spec, rowIndex, 18, ref error);
            row.VwapS1Mio = ReadDouble(ref reader, spec, rowIndex, 19, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 20, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.ObStatsStock.Length);

            DateTime? time = TryReadSourceTime(in timeParts);
            state.SetPageLastSourceTime(time);
            if (error is null)
            {
                try { row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts)); }
                catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }
            }
            if (error is null)
            {
                state.RecordParsedValidRow(receivedMetric: true);
                if (!IsBeforeCursor(time, lastSourceTime))
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
        ProtobufBatchWriter<ObStatsFuturesRow> writer, DateTime? lastSourceTime, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? error = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка статистики стакана отвергнута: secid пустой." : null;
            ObStatsFuturesRow row = new() { Secid = taskSecId };
            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref error);
            ExpectString(ref reader, spec, rowIndex, 2, ref error);
            row.AssetCode = ReadString(ref reader, spec, rowIndex, 3, ref error);
            row.MidPrice = ReadDouble(ref reader, spec, rowIndex, 4, ref error);
            row.MicroPrice = ReadDouble(ref reader, spec, rowIndex, 5, ref error);
            row.SpreadL1 = ReadDouble(ref reader, spec, rowIndex, 6, ref error);
            row.SpreadL2 = ReadDouble(ref reader, spec, rowIndex, 7, ref error);
            row.SpreadL3 = ReadDouble(ref reader, spec, rowIndex, 8, ref error);
            row.SpreadL5 = ReadDouble(ref reader, spec, rowIndex, 9, ref error);
            row.SpreadL10 = ReadDouble(ref reader, spec, rowIndex, 10, ref error);
            row.SpreadL20 = ReadDouble(ref reader, spec, rowIndex, 11, ref error);
            row.LevelsB = ReadInt(ref reader, spec, rowIndex, 12, ref error);
            row.LevelsS = ReadInt(ref reader, spec, rowIndex, 13, ref error);
            row.VolBL1 = ReadLong(ref reader, spec, rowIndex, 14, ref error);
            row.VolBL2 = ReadLong(ref reader, spec, rowIndex, 15, ref error);
            row.VolBL3 = ReadLong(ref reader, spec, rowIndex, 16, ref error);
            row.VolBL5 = ReadLong(ref reader, spec, rowIndex, 17, ref error);
            row.VolBL10 = ReadLong(ref reader, spec, rowIndex, 18, ref error);
            row.VolBL20 = ReadLong(ref reader, spec, rowIndex, 19, ref error);
            row.VolSL1 = ReadLong(ref reader, spec, rowIndex, 20, ref error);
            row.VolSL2 = ReadLong(ref reader, spec, rowIndex, 21, ref error);
            row.VolSL3 = ReadLong(ref reader, spec, rowIndex, 22, ref error);
            row.VolSL5 = ReadLong(ref reader, spec, rowIndex, 23, ref error);
            row.VolSL10 = ReadLong(ref reader, spec, rowIndex, 24, ref error);
            row.VolSL20 = ReadLong(ref reader, spec, rowIndex, 25, ref error);
            row.VwapBL3 = ReadDouble(ref reader, spec, rowIndex, 26, ref error);
            row.VwapBL5 = ReadDouble(ref reader, spec, rowIndex, 27, ref error);
            row.VwapBL10 = ReadDouble(ref reader, spec, rowIndex, 28, ref error);
            row.VwapBL20 = ReadDouble(ref reader, spec, rowIndex, 29, ref error);
            row.VwapSL3 = ReadDouble(ref reader, spec, rowIndex, 30, ref error);
            row.VwapSL5 = ReadDouble(ref reader, spec, rowIndex, 31, ref error);
            row.VwapSL10 = ReadDouble(ref reader, spec, rowIndex, 32, ref error);
            row.VwapSL20 = ReadDouble(ref reader, spec, rowIndex, 33, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 34, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.ObStatsFutures.Length);

            DateTime? time = TryReadSourceTime(in timeParts);
            state.SetPageLastSourceTime(time);
            if (error is null)
            {
                try { row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts)); }
                catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }
            }
            if (error is null)
            {
                state.RecordParsedValidRow(receivedMetric: true);
                if (!IsBeforeCursor(time, lastSourceTime))
                {
                    writer.Append(in row);
                    state.RecordAccepted(time);
                }
            }
            else state.RecordSkipped(rowIndex, time, error);
            rowIndex++;
        }
    }

    private static void ReadMomentDate(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column,
        ref SourceTimeParts parts, ref string? error)
    {
        try { parts.Date = ParseHelpersUtf8.ReadNextMomentDate(ref reader, row, column, spec.RootKey, out parts.RawDate); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); }
    }

    private static void ReadMomentTime(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column,
        ref SourceTimeParts parts, ref string? error)
    {
        try { parts.Time = ParseHelpersUtf8.ReadNextMomentTime(ref reader, row, column, spec.RootKey, out parts.RawTime); }
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

    private static int? ReadInt(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableInt(ref reader, row, spec.RootKey, col); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static long? ReadLong(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableLong(ref reader, row, spec.RootKey, col); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static ulong? ReadSystime(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try
        {
            DateTime? value = ParseHelpersUtf8.ReadNextNullableDateTime(ref reader, row, spec.RootKey, col, out bool wasNull);
            if (value is null && !wasNull && !reader.ValueTextEquals(""u8))
                error ??= $"[{spec.RootKey}] Некорректное непустое systime в строке {row}.";
            return value.HasValue ? CandleDateTime64.ToUnixWholeSeconds(value.Value) : null;
        }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static void ExpectRowStart(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {row}, получено {reader.TokenType}.");
    }

    private static DateTime? TryReadSourceTime(in SourceTimeParts parts)
    {
        try { return MoexClickHouseTime.BuildSourceTime(in parts); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { return null; }
    }

    private static bool IsBeforeCursor(DateTime? sourceTime, DateTime? cursor)
        => cursor is DateTime cursorTime && sourceTime is DateTime time && time < cursorTime;

    private static bool IsRowRejection(Exception exception)
        => exception is InvalidOperationException or FormatException or ArgumentException or OverflowException;

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
        if (spec.DataKind != "obstats" || spec.Market != market || spec.Pagination != PaginationKind.Cursor
            || !spec.InputColumns.SequenceEqual(columns))
            throw new ArgumentException($"Typed order-book statistics require {market} obstats with cursor pagination.", nameof(spec));
    }
}
