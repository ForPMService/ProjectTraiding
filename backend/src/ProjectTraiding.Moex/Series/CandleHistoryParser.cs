using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Parsing.Errors;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>Parses MOEX historical candle pages directly into the generated protobuf row.</summary>
public static class CandleHistoryParser
{
    private const string RootKey = "candles";

    public static CandleParsedPage Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        out PaginationCursorDTO cursor)
    {
        if (spec.DataKind != "candles"
            || spec.RootKey != RootKey
            || spec.SourceColumns.Length != 8)
        {
            throw new InvalidOperationException(
                "CandleHistoryParser requires a registered candles series specification.");
        }

        _ = spec.SourceColumnKindsValid;
        cursor = new PaginationCursorDTO();

        List<(CandleRow Row, DateTime Time)> rows = [];
        int sourceRowsCount = 0;
        List<SkippedSourceRow>? skippedSourceRows = null;
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.SkipToRootObject(ref reader, RootKey);

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
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, spec.SourceColumns, RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[candles] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }

                foundData = true;
                ReadRows(
                    ref reader,
                    taskSecId,
                    rows,
                    ref sourceRowsCount,
                    ref skippedSourceRows);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, RootKey);
        return new CandleParsedPage(rows, sourceRowsCount, skippedSourceRows);
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        string taskSecId,
        List<(CandleRow Row, DateTime Time)> rows,
        ref int sourceRowIndex,
        ref List<SkippedSourceRow>? skippedSourceRows)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", RootKey);

        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[candles] Ожидался StartArray строки {sourceRowIndex}, " +
                    $"получено {reader.TokenType}.");
            }

            string? rowError = string.IsNullOrWhiteSpace(taskSecId)
                ? "Загрузка свечей отвергнута: secid пустой."
                : null;
            double? open = null;
            double? close = null;
            double? high = null;
            double? low = null;
            double? value = null;
            double? volume = null;
            DateTime? beginTime = null;
            DateTime? endTime = null;

            for (int position = 0; position < 8; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[candles] Неожиданный конец JSON в строке {sourceRowIndex}, " +
                        $"позиция {position}.");
                }

                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[candles] Короткая строка данных: ожидалось 8 колонок, " +
                        $"получено {position} (строка {sourceRowIndex}).");
                }

                switch (position)
                {
                    case 0:
                        open = ReadOptionalDouble(ref reader, sourceRowIndex, position, ref rowError);
                        break;
                    case 1:
                        close = ReadOptionalDouble(ref reader, sourceRowIndex, position, ref rowError);
                        break;
                    case 2:
                        high = ReadOptionalDouble(ref reader, sourceRowIndex, position, ref rowError);
                        break;
                    case 3:
                        low = ReadOptionalDouble(ref reader, sourceRowIndex, position, ref rowError);
                        break;
                    case 4:
                        value = ReadOptionalDouble(ref reader, sourceRowIndex, position, ref rowError);
                        break;
                    case 5:
                        volume = ReadOptionalDouble(ref reader, sourceRowIndex, position, ref rowError);
                        break;
                    case 6:
                        beginTime = ReadDateTime(
                            ref reader, sourceRowIndex, position, required: true, ref rowError);
                        break;
                    case 7:
                        endTime = ReadDateTime(
                            ref reader, sourceRowIndex, position, required: false, ref rowError);
                        break;
                }
            }

            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[candles] Ожидался EndArray после 8 колонок " +
                    $"(строка {sourceRowIndex}).");
            }

            double begin = 0;
            double? end = null;
            if (beginTime is DateTime sourceBegin)
            {
                try
                {
                    begin = CandleDateTime64.ToUnixSeconds(sourceBegin);
                }
                catch (ArgumentException ex)
                {
                    rowError ??= ex.Message;
                }
            }

            if (endTime is DateTime sourceEnd)
            {
                try
                {
                    end = CandleDateTime64.ToUnixSeconds(sourceEnd);
                }
                catch (ArgumentException ex)
                {
                    rowError ??= ex.Message;
                }
            }

            if (rowError is null && beginTime is DateTime acceptedTime)
            {
                CandleRow row = new()
                {
                    Secid = taskSecId,
                    Open = open,
                    Close = close,
                    High = high,
                    Low = low,
                    Value = value,
                    Volume = volume,
                    Begin = begin,
                    End = end,
                    IngestPriority = 1,
                };
                rows.Add((row, acceptedTime));
            }
            else
            {
                skippedSourceRows ??= new List<SkippedSourceRow>();
                skippedSourceRows.Add(new SkippedSourceRow(
                    sourceRowIndex,
                    beginTime,
                    rowError ?? "Свеча отвергнута: begin пустой."));
            }

            sourceRowIndex++;
        }
    }

    private static double? ReadOptionalDouble(
        ref Utf8JsonReader reader,
        int rowIndex,
        int columnIndex,
        ref string? rowError)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;

        try
        {
            return ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, columnIndex, RootKey);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            rowError ??= ex.Message;
            reader.Skip();
            return null;
        }
    }

    private static DateTime? ReadDateTime(
        ref Utf8JsonReader reader,
        int rowIndex,
        int columnIndex,
        bool required,
        ref string? rowError)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            if (required)
                rowError ??= "Свеча отвергнута: begin пустой.";
            return null;
        }

        try
        {
            DateTime? value = ParseHelpersUtf8.ReadDateTimeUtf8(
                ref reader, rowIndex, columnIndex, RootKey);
            if (required && value is null)
                rowError ??= "Свеча отвергнута: begin имеет неверный формат времени.";
            else if (!required && value is null)
                rowError ??= "Свеча отвергнута: end имеет неверный формат времени.";

            return value;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            rowError ??= ex.Message;
            reader.Skip();
            return null;
        }
    }
}
