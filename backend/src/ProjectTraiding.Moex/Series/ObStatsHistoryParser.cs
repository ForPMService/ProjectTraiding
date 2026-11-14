using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class ObStatsHistoryParser
{
    internal static HistoricalSeriesParsedPage ParseStock(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<ObStatsStockRow> writer,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<ObStatsStockRow>? tail)
    {
        ValidateSpec(spec, "stock", 21);
        _ = spec.SourceColumnKindsValid;
        tail = null;
        cursor = new PaginationCursorDTO();
        int sourceRows = 0;
        int acceptedRows = 0;
        DateTime? lastSourceTime = null;
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
                ReadStockRows(ref reader, spec, taskSecId, writer, ref sourceRows, ref acceptedRows,
                    ref lastSourceTime, ref skipped);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
        return new HistoricalSeriesParsedPage(sourceRows, acceptedRows, skipped, lastSourceTime);
    }

    internal static HistoricalSeriesParsedPage ParseFutures(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<ObStatsFuturesRow> writer,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<ObStatsFuturesRow>? tail)
    {
        ValidateSpec(spec, "futures", 35);
        _ = spec.SourceColumnKindsValid;
        tail = null;
        cursor = new PaginationCursorDTO();
        int sourceRows = 0;
        int acceptedRows = 0;
        DateTime? lastSourceTime = null;
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
                ReadFuturesRows(ref reader, spec, taskSecId, writer, ref sourceRows, ref acceptedRows,
                    ref lastSourceTime, ref skipped);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
        return new HistoricalSeriesParsedPage(sourceRows, acceptedRows, skipped, lastSourceTime);
    }

    private static void ReadStockRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<ObStatsStockRow> writer,
        ref int sourceRows,
        ref int acceptedRows,
        ref DateTime? lastSourceTime,
        ref List<SkippedSourceRow>? skipped)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = sourceRows;
            if (reader.TokenType != JsonTokenType.StartArray)
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");

            SourceTimeParts timeParts = default;
            string? rowError = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка статистики стакана отвергнута: secid пустой." : null;
            ObStatsStockRow row = new() { Secid = taskSecId };
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
                        case 3: row.SpreadBbo = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 4: row.SpreadLv10 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 5: row.Spread1Mio = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 6: row.LevelsB = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 7: row.LevelsS = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 8: row.VolB = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 9: row.VolS = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 10: row.ValB = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 11: row.ValS = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 12: row.ImbalanceVolBbo = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 13: row.ImbalanceValBbo = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 14: row.ImbalanceVol = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 15: row.ImbalanceVal = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 16: row.VwapB = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 17: row.VwapS = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 18: row.VwapB1Mio = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 19: row.VwapS1Mio = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 20: row.Systime = ReadOptionalWallClock(ref reader, spec, rowIndex, position, ref rowError); break;
                        default: ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Неизвестная исходная колонка {position}."); break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
                {
                    rowError ??= ex.Message;
                    reader.Skip();
                }
            }

            FinishRow(ref reader, spec, rowIndex);
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

            lastSourceTime = time;
            if (rowError is null)
            {
                writer.Append(in row);
                acceptedRows++;
            }
            else
            {
                skipped ??= [];
                skipped.Add(new SkippedSourceRow(rowIndex, time, rowError));
            }
            sourceRows++;
        }
    }

    private static void ReadFuturesRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<ObStatsFuturesRow> writer,
        ref int sourceRows,
        ref int acceptedRows,
        ref DateTime? lastSourceTime,
        ref List<SkippedSourceRow>? skipped)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = sourceRows;
            if (reader.TokenType != JsonTokenType.StartArray)
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");

            SourceTimeParts timeParts = default;
            string? rowError = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка статистики стакана отвергнута: secid пустой." : null;
            ObStatsFuturesRow row = new() { Secid = taskSecId };
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
                        case 3: row.AssetCode = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 4: row.MidPrice = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 5: row.MicroPrice = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 6: row.SpreadL1 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 7: row.SpreadL2 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 8: row.SpreadL3 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 9: row.SpreadL5 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 10: row.SpreadL10 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 11: row.SpreadL20 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 12: row.LevelsB = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 13: row.LevelsS = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 14: row.VolBL1 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 15: row.VolBL2 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 16: row.VolBL3 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 17: row.VolBL5 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 18: row.VolBL10 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 19: row.VolBL20 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 20: row.VolSL1 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 21: row.VolSL2 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 22: row.VolSL3 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 23: row.VolSL5 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 24: row.VolSL10 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 25: row.VolSL20 = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 26: row.VwapBL3 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 27: row.VwapBL5 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 28: row.VwapBL10 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 29: row.VwapBL20 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 30: row.VwapSL3 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 31: row.VwapSL5 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 32: row.VwapSL10 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 33: row.VwapSL20 = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 34: row.Systime = ReadOptionalWallClock(ref reader, spec, rowIndex, position, ref rowError); break;
                        default: ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Неизвестная исходная колонка {position}."); break;
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
                {
                    rowError ??= ex.Message;
                    reader.Skip();
                }
            }

            FinishRow(ref reader, spec, rowIndex);
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

            lastSourceTime = time;
            if (rowError is null)
            {
                writer.Append(in row);
                acceptedRows++;
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

    private static void FinishRow(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался EndArray после {spec.SourceColumns.Length} колонок (строка {rowIndex}).");
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

    private static void ValidateSpec(MoexSeriesSpec spec, string market, int sourceColumns)
    {
        if (spec.DataKind != "obstats" || spec.Market != market || spec.Pagination != PaginationKind.Cursor || spec.SourceColumns.Length != sourceColumns)
            throw new ArgumentException($"Typed order-book statistics require {market} obstats with cursor pagination.", nameof(spec));
    }
}
