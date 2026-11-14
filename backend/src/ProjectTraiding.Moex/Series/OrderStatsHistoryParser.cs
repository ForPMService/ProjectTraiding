using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class OrderStatsHistoryParser
{
    internal static HistoricalSeriesParsedPage Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<OrderStatsStockRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<OrderStatsStockRow>? tail)
    {
        if (spec.DataKind != "orderstats" || spec.Market != "stock" || spec.Pagination != PaginationKind.Cursor || spec.SourceColumns.Length != 26)
            throw new ArgumentException("Typed order statistics require stock orderstats with cursor pagination.", nameof(spec));

        _ = spec.SourceColumnKindsValid;
        tail = null;
        cursor = new PaginationCursorDTO();
        int sourceRows = 0;
        int acceptedRows = 0;
        DateTime? pageLastSourceTime = null;
        DateTime? maxAcceptedSourceTime = null;
        List<SkippedSourceRow>? skipped = null;
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
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(ref reader, spec.SourceColumns, spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch("[data] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                ReadRows(ref reader, spec, taskSecId, writer, lastSourceTime,
                    ref sourceRows, ref acceptedRows, ref pageLastSourceTime,
                    ref maxAcceptedSourceTime, ref skipped);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
        return new HistoricalSeriesParsedPage(sourceRows, acceptedRows, skipped, pageLastSourceTime)
        {
            MaxAcceptedSourceTime = maxAcceptedSourceTime,
            ParsedValidRowsCount = sourceRows - (skipped?.Count ?? 0),
        };
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<OrderStatsStockRow> writer,
        DateTime? lastSourceTime,
        ref int sourceRows,
        ref int acceptedRows,
        ref DateTime? pageLastSourceTime,
        ref DateTime? maxAcceptedSourceTime,
        ref List<SkippedSourceRow>? skipped)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = sourceRows;
            if (reader.TokenType != JsonTokenType.StartArray)
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");

            SourceTimeParts timeParts = default;
            string? rowError = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка статистики заявок отвергнута: secid пустой." : null;
            OrderStatsStockRow row = new() { Secid = taskSecId };
            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                ReadCell(ref reader, spec, rowIndex, position);
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                try
                {
                    switch (position)
                    {
                        case 0: ReadMomentDate(ref reader, spec, rowIndex, ref timeParts); break;
                        case 1: ReadMomentTime(ref reader, spec, rowIndex, ref timeParts); break;
                        case 2: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 3: row.PutOrdersB = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 4: row.PutOrdersS = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 5: row.PutValB = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 6: row.PutValS = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 7: row.PutVolB = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 8: row.PutVolS = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 9: row.PutVwapB = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 10: row.PutVwapS = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 11: row.PutVol = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 12: row.PutVal = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 13: row.PutOrders = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 14: row.CancelOrdersB = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 15: row.CancelOrdersS = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 16: row.CancelValB = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 17: row.CancelValS = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 18: row.CancelVolB = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 19: row.CancelVolS = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 20: row.CancelVwapB = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 21: row.CancelVwapS = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 22: row.CancelVol = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 23: row.CancelVal = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 24: row.CancelOrders = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 25: row.Systime = ReadOptionalWallClock(ref reader, spec, rowIndex, position, ref rowError); break;
                        default: ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Неизвестная исходная колонка {position}."); break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
                {
                    rowError ??= ex.Message;
                    reader.Skip();
                }
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался EndArray после 26 колонок (строка {rowIndex}).");
            DateTime? time = TryReadSourceTime(in timeParts);
            try
            {
                if (rowError is not null)
                    throw new InvalidOperationException(rowError);
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                rowError ??= ex.Message;
            }

            pageLastSourceTime = time;
            if (rowError is null)
            {
                if (lastSourceTime is null || time is DateTime rowTime && rowTime >= lastSourceTime.Value)
                {
                    writer.Append(in row);
                    acceptedRows++;
                    if (time is DateTime acceptedTime
                        && (maxAcceptedSourceTime is null || acceptedTime > maxAcceptedSourceTime.Value))
                    {
                        maxAcceptedSourceTime = acceptedTime;
                    }
                }
            }
            else
            {
                skipped ??= [];
                skipped.Add(new SkippedSourceRow(rowIndex, time, rowError));
            }
            sourceRows++;
        }
    }

    private static void ReadCell(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex, int position)
    {
        if (!reader.Read())
            ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {position}.");
        if (reader.TokenType == JsonTokenType.EndArray)
            ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Короткая строка: ожидалось {spec.SourceColumns.Length} колонок, получено {position} (строка {rowIndex}).");
    }

    private static void ReadMomentDate(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex, ref SourceTimeParts parts)
        => parts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(ref reader, rowIndex, 0, spec.RootKey, out parts.RawDate);

    private static void ReadMomentTime(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex, ref SourceTimeParts parts)
        => parts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(ref reader, rowIndex, 1, spec.RootKey, out parts.RawTime);

    private static DateTime? TryReadSourceTime(in SourceTimeParts parts)
    {
        try { return MoexClickHouseTime.BuildSourceTime(in parts); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException) { return null; }
    }

    private static ulong? ReadOptionalWallClock(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex, int position, ref string? rowError)
    {
        DateTime? value = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, spec.RootKey);
        if (value is null && !reader.ValueTextEquals(""u8))
            rowError ??= $"[{spec.RootKey}] Некорректное непустое systime в строке {rowIndex}.";
        return value.HasValue ? CandleDateTime64.ToUnixWholeSeconds(value.Value) : null;
    }

    private static PaginationCursorDTO ReadCursor(ref Utf8JsonReader reader)
    {
        PaginationCursorDTO cursor = new();
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
        return cursor;
    }
}
