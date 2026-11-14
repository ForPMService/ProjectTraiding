using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class MegaAlertsHistoryParser
{
    internal static HistoricalSeriesParsedPage ParseStock(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<MegaAlertsStockRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<MegaAlertsStockRow>? tail)
    {
        ValidateSpec(spec, "stock", 8);
        _ = spec.SourceColumnKindsValid;
        cursor = new PaginationCursorDTO();
        tail = new HistoricalSeriesTimeGroupTail<MegaAlertsStockRow>(writer, lastSourceTime);
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
                ReadStockRows(ref reader, spec, taskSecId, tail);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
        return tail.PrefixPage;
    }

    internal static HistoricalSeriesParsedPage ParseFutures(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<MegaAlertsFuturesRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow>? tail)
    {
        ValidateSpec(spec, "futures", 9);
        _ = spec.SourceColumnKindsValid;
        cursor = new PaginationCursorDTO();
        tail = new HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow>(writer, lastSourceTime);
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
                ReadFuturesRows(ref reader, spec, taskSecId, tail);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        cursor = ReadCursor(ref reader);
        return tail.PrefixPage;
    }

    private static void ReadStockRows(ref Utf8JsonReader reader, MoexSeriesSpec spec, string taskSecId, HistoricalSeriesTimeGroupTail<MegaAlertsStockRow> tail)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = tail.FullSourceRowsCount;
            if (reader.TokenType != JsonTokenType.StartArray)
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
            SourceTimeParts timeParts = default;
            string? rowError = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка мегаалёртов отвергнута: secid пустой." : null;
            MegaAlertsStockRow row = new() { Secid = taskSecId };
            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                ReadCell(ref reader, spec, rowIndex, position);
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                try
                {
                    switch (position)
                    {
                        case 0: timeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(ref reader, rowIndex, position, spec.RootKey, out timeParts.RawDate); break;
                        case 1: timeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(ref reader, rowIndex, position, spec.RootKey, out timeParts.RawTime); break;
                        case 2: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 3: row.AlertType = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey)!; break;
                        case 4: row.Threshold = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 5: row.Value = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 6: row.Reference = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 7: row.Systime = ReadOptionalWallClock(ref reader, spec, rowIndex, position, ref rowError); break;
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
                if (string.IsNullOrWhiteSpace(row.AlertType))
                    throw new InvalidOperationException("Строка мегаалёртов отвергнута: alert_type пустой.");
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                rowError ??= ex.Message;
            }
            if (rowError is null)
                tail.AddAccepted(rowIndex, time, in row);
            else
                tail.AddSkipped(rowIndex, time, rowError);
        }
    }

    private static void ReadFuturesRows(ref Utf8JsonReader reader, MoexSeriesSpec spec, string taskSecId, HistoricalSeriesTimeGroupTail<MegaAlertsFuturesRow> tail)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int rowIndex = tail.FullSourceRowsCount;
            if (reader.TokenType != JsonTokenType.StartArray)
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
            SourceTimeParts timeParts = default;
            string? rowError = string.IsNullOrWhiteSpace(taskSecId) ? "Загрузка мегаалёртов отвергнута: secid пустой." : null;
            MegaAlertsFuturesRow row = new() { Secid = taskSecId };
            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                ReadCell(ref reader, spec, rowIndex, position);
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                try
                {
                    switch (position)
                    {
                        case 0: timeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(ref reader, rowIndex, position, spec.RootKey, out timeParts.RawDate); break;
                        case 1: timeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(ref reader, rowIndex, position, spec.RootKey, out timeParts.RawTime); break;
                        case 2: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 3: row.AssetCode = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 4: row.AlertType = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey)!; break;
                        case 5: row.Threshold = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 6: row.Value = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, spec.RootKey); break;
                        case 7: row.Reference = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 8: row.Systime = ReadOptionalWallClock(ref reader, spec, rowIndex, position, ref rowError); break;
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
                if (string.IsNullOrWhiteSpace(row.AlertType))
                    throw new InvalidOperationException("Строка мегаалёртов отвергнута: alert_type пустой.");
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(MoexClickHouseTime.BuildSourceTime(in timeParts));
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
            {
                rowError ??= ex.Message;
            }
            if (rowError is null)
                tail.AddAccepted(rowIndex, time, in row);
            else
                tail.AddSkipped(rowIndex, time, rowError);
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
        if (spec.DataKind != "mega_alerts" || spec.Market != market || spec.Pagination != PaginationKind.Cursor
            || !spec.PreserveCursorTimeGroup || spec.SourceColumns.Length != sourceColumns)
            throw new ArgumentException($"Typed MegaAlerts parsing requires {market} mega_alerts with cursor time groups.", nameof(spec));
    }
}
