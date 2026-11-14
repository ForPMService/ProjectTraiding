using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

internal static class FutoiHistoryParser
{
    internal static HistoricalSeriesParsedPage Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<FutoiRow> writer,
        DateTime? lastSourceTime,
        out PaginationCursorDTO cursor,
        out HistoricalSeriesTimeGroupTail<FutoiRow>? tail)
    {
        if (spec.DataKind != "futoi" || spec.Market != "futures" || spec.RootKey != "futoi"
            || spec.Pagination != PaginationKind.DaySplit || spec.SourceColumns.Length != 13)
            throw new ArgumentException("Typed FUTOI parsing requires the registered day-split futoi series.", nameof(spec));

        _ = spec.SourceColumnKindsValid;
        cursor = new PaginationCursorDTO();
        tail = null;
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
                    ParseHelpersUtf8.SchemaMismatch("[futoi] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                ReadRows(ref reader, spec, writer, lastSourceTime,
                    ref sourceRows, ref acceptedRows, ref pageLastSourceTime,
                    ref maxAcceptedSourceTime, ref skipped);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, spec.RootKey);
        return new HistoricalSeriesParsedPage(sourceRows, acceptedRows, skipped, pageLastSourceTime)
        {
            MaxAcceptedSourceTime = maxAcceptedSourceTime,
            ParsedValidRowsCount = sourceRows - (skipped?.Count ?? 0),
        };
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        ProtobufBatchWriter<FutoiRow> writer,
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
            string? ticker = null;
            string? rowError = null;
            FutoiRow row = new();
            for (int position = 0; position < spec.SourceColumns.Length; position++)
            {
                ReadCell(ref reader, spec, rowIndex, position);
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                try
                {
                    switch (position)
                    {
                        case 0: row.SessId = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 1: row.Seqnum = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, spec.RootKey); break;
                        case 2: timeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(ref reader, rowIndex, position, spec.RootKey, out timeParts.RawDate); break;
                        case 3: timeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(ref reader, rowIndex, position, spec.RootKey, out timeParts.RawTime); break;
                        case 4: ticker = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey); break;
                        case 5: row.Clgroup = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey)!; break;
                        case 6: row.Pos = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 7: row.PosLong = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 8: row.PosShort = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 9: row.PosLongNum = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 10: row.PosShortNum = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, spec.RootKey); break;
                        case 11: row.Systime = ReadOptionalWallClock(ref reader, spec, rowIndex, position, ref rowError); break;
                        case 12: row.TradeSessionDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, spec.RootKey); break;
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
                ParseHelpersUtf8.SchemaMismatch($"[{spec.RootKey}] Ожидался EndArray после 13 колонок (строка {rowIndex}).");

            DateTime? time = TryReadSourceTime(in timeParts);
            try
            {
                if (rowError is not null)
                    throw new InvalidOperationException(rowError);
                if (string.IsNullOrWhiteSpace(ticker))
                    throw new InvalidOperationException("Строка открытого интереса отвергнута: ticker пустой.");
                if (string.IsNullOrWhiteSpace(row.Clgroup))
                    throw new InvalidOperationException("Строка открытого интереса отвергнута: clgroup пустой.");
                row.Secid = ticker;
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
}
