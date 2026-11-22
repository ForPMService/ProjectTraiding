using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class FutoiHistoryParser
{
    internal static void Parse(ReadOnlySpan<byte> body, MoexSeriesSpec spec, string taskSecId,
        ProtobufBatchWriter<FutoiRow> writer, DateTime? lastSourceTime,
        HistoricalReadState state)
    {
        if (spec.DataKind != "futoi" || spec.Market != "futures" || spec.RootKey != "futoi"
            || spec.Pagination != PaginationKind.DaySplit || !spec.InputColumns.SequenceEqual(MoexColumnSets.Futoi))
            throw new ArgumentException("Typed FUTOI parsing requires the registered day-split futoi series.", nameof(spec));

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
                ParseHelpersUtf8.ValidateInputColumnsUtf8(ref reader, MoexColumnSets.Futoi, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch("[futoi] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                ReadRows(ref reader, spec, writer, lastSourceTime, state);
            }
            else reader.Skip();
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
    }

    private static void ReadRows(ref Utf8JsonReader reader, MoexSeriesSpec spec,
        ProtobufBatchWriter<FutoiRow> writer, DateTime? lastSourceTime, HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? ticker = null;
            string? error = null;
            FutoiRow row = new();
            row.SessId = ReadInt(ref reader, spec, rowIndex, 0, ref error);
            row.Seqnum = ReadInt(ref reader, spec, rowIndex, 1, ref error);
            ReadMomentDate(ref reader, spec, rowIndex, 2, ref timeParts, ref error);
            ReadMomentTime(ref reader, spec, rowIndex, 3, ref timeParts, ref error);
            ticker = ReadString(ref reader, spec, rowIndex, 4, ref error);
            row.Clgroup = ReadString(ref reader, spec, rowIndex, 5, ref error)!;
            row.Pos = ReadLong(ref reader, spec, rowIndex, 6, ref error);
            row.PosLong = ReadLong(ref reader, spec, rowIndex, 7, ref error);
            row.PosShort = ReadLong(ref reader, spec, rowIndex, 8, ref error);
            row.PosLongNum = ReadLong(ref reader, spec, rowIndex, 9, ref error);
            row.PosShortNum = ReadLong(ref reader, spec, rowIndex, 10, ref error);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 11, ref error);
            row.TradeSessionDate = ReadString(ref reader, spec, rowIndex, 12, ref error);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.Futoi.Length);

            DateTime? sourceTime = TryReadSourceTime(in timeParts);
            state.SetPageLastSourceTime(sourceTime);
            try
            {
                if (error is not null) throw new InvalidOperationException(error);
                if (string.IsNullOrWhiteSpace(ticker))
                    throw new InvalidOperationException("Строка открытого интереса отвергнута: ticker пустой.");
                if (string.IsNullOrWhiteSpace(row.Clgroup))
                    throw new InvalidOperationException("Строка открытого интереса отвергнута: clgroup пустой.");
                row.Secid = ticker;
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts));
            }
            catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; }

            if (error is null)
            {
                state.RecordParsedValidRow(receivedMetric: true);
                if (!IsBeforeCursor(sourceTime, lastSourceTime))
                {
                    writer.Append(in row);
                    state.RecordAccepted(sourceTime);
                }
            }
            else state.RecordSkipped(rowIndex, sourceTime, error);
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
    private static string? ReadString(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int col, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableString(ref reader, row, spec.RootKey, col); }
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
    private static bool IsBeforeCursor(DateTime? sourceTime, DateTime? cursor)
        => cursor is DateTime cursorTime && sourceTime is DateTime time && time < cursorTime;
    private static bool IsRowRejection(Exception ex) => ex is InvalidOperationException or FormatException or ArgumentException or OverflowException;
}
