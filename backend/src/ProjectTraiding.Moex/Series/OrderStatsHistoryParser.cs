using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class OrderStatsHistoryParser
{
    internal static void Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<OrderStatsStockRow> writer,
        DateTime? lastSourceTime,
        HistoricalReadState state,
        out PaginationCursorDTO cursor)
    {
        if (spec.DataKind != "orderstats" || spec.Market != "stock"
            || spec.Pagination != PaginationKind.Cursor
            || !spec.InputColumns.SequenceEqual(MoexColumnSets.OrderStatsStock))
            throw new ArgumentException("Typed order statistics require stock orderstats with cursor pagination.", nameof(spec));

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
                ParseHelpersUtf8.ValidateInputColumnsUtf8(ref reader, MoexColumnSets.OrderStatsStock, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch("[orderstats] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                ReadRows(ref reader, spec, taskSecId, writer, lastSourceTime, state);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<OrderStatsStockRow> writer,
        DateTime? lastSourceTime,
        HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow();
            SourceTimeParts timeParts = default;
            string? rowError = string.IsNullOrWhiteSpace(taskSecId)
                ? "Загрузка статистики заявок отвергнута: secid пустой."
                : null;
            OrderStatsStockRow row = new() { Secid = taskSecId };

            ReadMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref rowError);
            ReadMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref rowError);
            ExpectString(ref reader, spec, rowIndex, 2, ref rowError);
            row.PutOrdersB = ReadInt(ref reader, spec, rowIndex, 3, ref rowError);
            row.PutOrdersS = ReadInt(ref reader, spec, rowIndex, 4, ref rowError);
            row.PutValB = ReadDouble(ref reader, spec, rowIndex, 5, ref rowError);
            row.PutValS = ReadDouble(ref reader, spec, rowIndex, 6, ref rowError);
            row.PutVolB = ReadInt(ref reader, spec, rowIndex, 7, ref rowError);
            row.PutVolS = ReadInt(ref reader, spec, rowIndex, 8, ref rowError);
            row.PutVwapB = ReadDouble(ref reader, spec, rowIndex, 9, ref rowError);
            row.PutVwapS = ReadDouble(ref reader, spec, rowIndex, 10, ref rowError);
            row.PutVol = ReadInt(ref reader, spec, rowIndex, 11, ref rowError);
            row.PutVal = ReadDouble(ref reader, spec, rowIndex, 12, ref rowError);
            row.PutOrders = ReadInt(ref reader, spec, rowIndex, 13, ref rowError);
            row.CancelOrdersB = ReadInt(ref reader, spec, rowIndex, 14, ref rowError);
            row.CancelOrdersS = ReadInt(ref reader, spec, rowIndex, 15, ref rowError);
            row.CancelValB = ReadDouble(ref reader, spec, rowIndex, 16, ref rowError);
            row.CancelValS = ReadDouble(ref reader, spec, rowIndex, 17, ref rowError);
            row.CancelVolB = ReadInt(ref reader, spec, rowIndex, 18, ref rowError);
            row.CancelVolS = ReadLong(ref reader, spec, rowIndex, 19, ref rowError);
            row.CancelVwapB = ReadDouble(ref reader, spec, rowIndex, 20, ref rowError);
            row.CancelVwapS = ReadDouble(ref reader, spec, rowIndex, 21, ref rowError);
            row.CancelVol = ReadLong(ref reader, spec, rowIndex, 22, ref rowError);
            row.CancelVal = ReadDouble(ref reader, spec, rowIndex, 23, ref rowError);
            row.CancelOrders = ReadLong(ref reader, spec, rowIndex, 24, ref rowError);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 25, ref rowError);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.OrderStatsStock.Length);

            DateTime? time = TryReadSourceTime(in timeParts);
            state.SetPageLastSourceTime(time);
            if (rowError is null)
            {
                try
                {
                    row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(
                        MoexClickHouseTime.BuildSourceTime(in timeParts));
                }
                catch (Exception ex) when (IsRowRejection(ex))
                {
                    rowError ??= ex.Message;
                }
            }

            if (rowError is null)
            {
                state.RecordParsedValidRow(receivedMetric: true);
                if (!IsBeforeCursor(time, lastSourceTime))
                {
                    writer.Append(in row);
                    state.RecordAccepted(time);
                }
            }
            else
            {
                state.RecordSkipped(rowIndex, time, rowError);
            }
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

    private static void ExpectString(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, row, spec.RootKey, column); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); }
    }

    private static double? ReadDouble(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableDouble(ref reader, row, spec.RootKey, column); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static int? ReadInt(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableInt(ref reader, row, spec.RootKey, column); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static long? ReadLong(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableLong(ref reader, row, spec.RootKey, column); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static ulong? ReadSystime(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try
        {
            DateTime? value = ParseHelpersUtf8.ReadNextNullableDateTime(ref reader, row, spec.RootKey, column, out bool wasNull);
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

    private static bool IsBeforeCursor(DateTime? sourceTime, DateTime? lastSourceTime)
        => lastSourceTime is DateTime cursor && sourceTime is DateTime time && time < cursor;

    private static bool IsRowRejection(Exception exception)
        => exception is InvalidOperationException or FormatException or ArgumentException or OverflowException;

    private static PaginationCursorDTO ReadCursor(ref Utf8JsonReader reader)
    {
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("data.cursor"u8))
            {
                try { return ParseHelpersUtf8.ReadCursorRootObject(ref reader); }
                catch (InvalidOperationException ex) { ParseHelpersUtf8.SchemaMismatch(ex.Message); }
            }
            reader.Skip();
        }
        return new PaginationCursorDTO();
    }
}
