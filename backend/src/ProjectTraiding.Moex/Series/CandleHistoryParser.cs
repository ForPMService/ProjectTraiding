using System.Text.Json;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>Parses MOEX historical candle rows directly into the protobuf writer.</summary>
public static class CandleHistoryParser
{
    private const string RootKey = "candles";

    /// <returns>The physical number of input rows in data[], used by FixedPage.</returns>
    internal static int Parse(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<CandleRow> writer,
        HistoricalReadState state)
    {
        if (spec.DataKind != RootKey
            || spec.RootKey != RootKey
            || !spec.InputColumns.SequenceEqual(MoexColumnSets.Candles))
        {
            throw new InvalidOperationException(
                "CandleHistoryParser requires the registered candles input schema.");
        }

        int sourceRowsCount = 0;
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
                if (foundColumns)
                    ParseHelpersUtf8.SchemaMismatch("[candles] Повторная секция 'columns'.");
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(ref reader, MoexColumnSets.Candles, RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (foundData)
                    ParseHelpersUtf8.SchemaMismatch("[candles] Повторная секция 'data'.");
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch(
                        "[candles] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                foundData = true;
                ReadRows(ref reader, taskSecId, writer, state, ref sourceRowsCount);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, RootKey);
        return sourceRowsCount;
    }

    private static void ReadRows(
        ref Utf8JsonReader reader,
        string taskSecId,
        ProtobufBatchWriter<CandleRow> writer,
        HistoricalReadState state,
        ref int sourceRowIndex)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", RootKey);
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[candles] Ожидался StartArray строки {sourceRowIndex}, получено {reader.TokenType}.");

            string? rowError = string.IsNullOrWhiteSpace(taskSecId)
                ? "Загрузка свечей отвергнута: secid пустой."
                : null;
            double? open = ReadNextDouble(ref reader, sourceRowIndex, 0, ref rowError);
            double? close = ReadNextDouble(ref reader, sourceRowIndex, 1, ref rowError);
            double? high = ReadNextDouble(ref reader, sourceRowIndex, 2, ref rowError);
            double? low = ReadNextDouble(ref reader, sourceRowIndex, 3, ref rowError);
            double? value = ReadNextDouble(ref reader, sourceRowIndex, 4, ref rowError);
            double? volume = ReadNextDouble(ref reader, sourceRowIndex, 5, ref rowError);
            DateTime? beginTime = ReadNextDateTime(ref reader, sourceRowIndex, 6, "begin", ref rowError);
            DateTime? endTime = ReadNextDateTime(ref reader, sourceRowIndex, 7, "end", ref rowError);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, sourceRowIndex, RootKey, MoexColumnSets.Candles.Length);

            sourceRowIndex++;
            state.RecordPhysicalRow();
            state.SetPageLastSourceTime(beginTime);

            ulong begin = 0;
            ulong? end = null;
            if (beginTime is DateTime sourceBegin)
            {
                try
                {
                    begin = CandleDateTime64.ToUnixWholeSeconds(sourceBegin);
                }
                catch (ArgumentException ex)
                {
                    rowError ??= $"Свеча отвергнута: begin не является допустимым временем. {ex.Message}";
                }
            }

            if (endTime is DateTime sourceEnd)
            {
                try
                {
                    end = CandleDateTime64.ToUnixWholeSeconds(sourceEnd);
                }
                catch (ArgumentException ex)
                {
                    rowError ??= $"Свеча отвергнута: end не является допустимым временем. {ex.Message}";
                }
            }

            if (rowError is null && beginTime is not null)
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
                writer.Append(in row);
                state.RecordAccepted(beginTime, covered: true, receivedMetric: true);
            }
            else
            {
                state.RecordSkipped(
                    sourceRowIndex - 1,
                    beginTime,
                    rowError ?? "Свеча отвергнута: begin пустой.");
            }
        }
    }

    private static double? ReadNextDouble(
        ref Utf8JsonReader reader,
        int rowIndex,
        int columnIndex,
        ref string? rowError)
    {
        try
        {
            return ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, columnIndex);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            rowError ??= ex.Message;
            reader.Skip();
            return null;
        }
    }

    private static DateTime? ReadNextDateTime(
        ref Utf8JsonReader reader,
        int rowIndex,
        int columnIndex,
        string fieldName,
        ref string? rowError)
    {
        try
        {
            DateTime? value = ParseHelpersUtf8.ReadNextNullableDateTime(
                ref reader, rowIndex, RootKey, columnIndex, out bool wasNull);
            if (wasNull)
            {
                rowError ??= $"Свеча отвергнута: {fieldName} пустой.";
                return null;
            }

            if (value is null)
            {
                rowError ??= $"Свеча отвергнута: {fieldName} имеет неверный формат времени.";
                return null;
            }
            return value;
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            rowError ??= $"Свеча отвергнута: {fieldName}: {ex.Message}";
            reader.Skip();
            return null;
        }
    }
}
