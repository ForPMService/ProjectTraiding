using System.Text.Json;
using ProjectTraiding.Moex.Contracts.Dto;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Series;

/// <summary>Parses each TradeStats source row directly to its two final writers.</summary>
public static class TradeStatsHistoryParser
{
    private const string RootKey = "tradestats";

    internal static void ParseStock(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<TradeStatsStockRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime,
        uint ingestPriority,
        HistoricalReadState state,
        out PaginationCursorDTO cursor,
        List<CandleRow>? acceptedCandles = null)
    {
        ValidateSpec(spec, "stock", MoexColumnSets.TradeStatsStock);
        ParseEnvelope(body, spec, state, out cursor,
            (ref Utf8JsonReader reader) => ReadStockRows(ref reader, spec, taskSecId,
                statsWriter, candleWriter, acceptedCandles, lastSourceTime, ingestPriority, state));
    }

    internal static void ParseFutures(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<TradeStatsFuturesRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        DateTime? lastSourceTime,
        uint ingestPriority,
        HistoricalReadState state,
        out PaginationCursorDTO cursor,
        List<CandleRow>? acceptedCandles = null)
    {
        ValidateSpec(spec, "futures", MoexColumnSets.TradeStatsFutures);
        ParseEnvelope(body, spec, state, out cursor,
            (ref Utf8JsonReader reader) => ReadFuturesRows(ref reader, spec, taskSecId,
                statsWriter, candleWriter, acceptedCandles, lastSourceTime, ingestPriority, state));
    }

    private delegate void ReadRows(ref Utf8JsonReader reader);

    private static void ParseEnvelope(
        ReadOnlySpan<byte> body,
        MoexSeriesSpec spec,
        HistoricalReadState state,
        out PaginationCursorDTO cursor,
        ReadRows readRows)
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
                ParseHelpersUtf8.ValidateInputColumnsUtf8(
                    ref reader,
                    spec.Market == "stock" ? MoexColumnSets.TradeStatsStock : MoexColumnSets.TradeStatsFutures,
                    spec.RootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{spec.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
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

    private static void ReadStockRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<TradeStatsStockRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        List<CandleRow>? acceptedCandles,
        DateTime? lastSourceTime,
        uint ingestPriority,
        HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int currentRowIndex = rowIndex++;
            ExpectRowStart(ref reader, spec, currentRowIndex);
            int rowDepth = reader.CurrentDepth;
            state.RecordPhysicalRow(receivedMetric: true);
            if (!TryReadStockRow(ref reader, taskSecId, out TradeStatsStockRow row,
                    out DateTime? sourceTime, out int columnIndex, out string? error))
            {
                state.SetPageLastSourceTime(sourceTime);
                int failedIndex = columnIndex - 1;
                int? failedColumnIndex = null;
                string? columnName = null;
                if (failedIndex >= 0 && failedIndex < MoexColumnSets.TradeStatsStock.Length)
                {
                    failedColumnIndex = failedIndex;
                    columnName = MoexColumnSets.TradeStatsStock[failedIndex];
                }
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, error!, failedColumnIndex, columnName);
                continue;
            }

            state.SetPageLastSourceTime(sourceTime);
            state.RecordParsedValidRow();
            if (IsBeforeCursor(sourceTime, lastSourceTime))
                continue;

            if (string.IsNullOrWhiteSpace(taskSecId))
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, "Загрузка статистики сделок отвергнута: secid пустой.");
                continue;
            }

            CandleRow candle;
            try
            {
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                candle = BuildCandle(
                    taskSecId, row.PrOpen, row.PrClose, row.PrHigh, row.PrLow,
                    row.Val, row.Vol.HasValue ? (double)row.Vol.Value : null,
                    sourceTime.Value, ingestPriority);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, exception.Message);
                continue;
            }

            if (string.IsNullOrWhiteSpace(candle.Secid) || candle.Secid != taskSecId)
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, "Свеча отвергнута: secid пустой или не совпадает с заданием.");
                continue;
            }
            if (!candle.End.HasValue || candle.End.Value < candle.Begin)
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, "Свеча отвергнута: некорректный диапазон begin/end.");
                continue;
            }

            statsWriter.Append(in row);
            candleWriter.Append(in candle);
            acceptedCandles?.Add(candle);
            state.RecordAccepted(sourceTime.Value);
        }
    }

    private static void ReadFuturesRows(
        ref Utf8JsonReader reader,
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<TradeStatsFuturesRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        List<CandleRow>? acceptedCandles,
        DateTime? lastSourceTime,
        uint ingestPriority,
        HistoricalReadState state)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", spec.RootKey);
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            int currentRowIndex = rowIndex++;
            ExpectRowStart(ref reader, spec, currentRowIndex);
            int rowDepth = reader.CurrentDepth;
            state.RecordPhysicalRow(receivedMetric: true);
            if (!TryReadFuturesRow(ref reader, taskSecId, out TradeStatsFuturesRow row,
                    out DateTime? sourceTime, out int columnIndex, out string? error))
            {
                state.SetPageLastSourceTime(sourceTime);
                int failedIndex = columnIndex - 1;
                int? failedColumnIndex = null;
                string? columnName = null;
                if (failedIndex >= 0 && failedIndex < MoexColumnSets.TradeStatsFutures.Length)
                {
                    failedColumnIndex = failedIndex;
                    columnName = MoexColumnSets.TradeStatsFutures[failedIndex];
                }
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, error!, failedColumnIndex, columnName);
                continue;
            }

            state.SetPageLastSourceTime(sourceTime);
            state.RecordParsedValidRow();
            if (IsBeforeCursor(sourceTime, lastSourceTime))
                continue;

            if (string.IsNullOrWhiteSpace(taskSecId))
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, "Загрузка статистики сделок отвергнута: secid пустой.");
                continue;
            }

            CandleRow candle;
            try
            {
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                candle = BuildCandle(
                    taskSecId, row.PrOpen, row.PrClose, row.PrHigh, row.PrLow,
                    row.Val.HasValue ? (double)row.Val.Value : null,
                    row.Vol.HasValue ? (double)row.Vol.Value : null,
                    sourceTime.Value, ingestPriority);
            }
            catch (Exception exception) when (exception is ArgumentException or OverflowException)
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, exception.Message);
                continue;
            }

            if (string.IsNullOrWhiteSpace(candle.Secid) || candle.Secid != taskSecId)
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, "Свеча отвергнута: secid пустой или не совпадает с заданием.");
                continue;
            }
            if (!candle.End.HasValue || candle.End.Value < candle.Begin)
            {
                ParseHelpersUtf8.RejectRow(
                    ref reader, rowDepth, state, currentRowIndex,
                    sourceTime, "Свеча отвергнута: некорректный диапазон begin/end.");
                continue;
            }

            statsWriter.Append(in row);
            candleWriter.Append(in candle);
            acceptedCandles?.Add(candle);
            state.RecordAccepted(sourceTime.Value);
        }
    }

    private static bool TryReadStockRow(
        ref Utf8JsonReader reader,
        string taskSecId,
        out TradeStatsStockRow row,
        out DateTime? sourceTime,
        out int columnIndex,
        out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextStringToken(ref reader, ref columnIndex, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prOpen, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prHigh, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prLow, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prClose, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prStd, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? vol, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? val, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? trades, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwap, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prChange, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? disb, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapS, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrOpen, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrHigh, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrLow, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrClose, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.EndArray, "строка TradeStats", RootKey);
        row = new TradeStatsStockRow
        {
            Secid = taskSecId,
            PrOpen = prOpen,
            PrHigh = prHigh,
            PrLow = prLow,
            PrClose = prClose,
            PrStd = prStd,
            Vol = vol,
            Val = val,
            Trades = trades,
            PrVwap = prVwap,
            PrChange = prChange,
            TradesB = tradesB,
            TradesS = tradesS,
            ValB = valB,
            ValS = valS,
            VolB = volB,
            VolS = volS,
            Disb = disb,
            PrVwapB = prVwapB,
            PrVwapS = prVwapS,
            Systime = systime,
            SecPrOpen = secPrOpen,
            SecPrHigh = secPrHigh,
            SecPrLow = secPrLow,
            SecPrClose = secPrClose,
        };
        return true;
    }

    private static bool TryReadFuturesRow(
        ref Utf8JsonReader reader,
        string taskSecId,
        out TradeStatsFuturesRow row,
        out DateTime? sourceTime,
        out int columnIndex,
        out string? error)
    {
        row = default;
        sourceTime = null;
        columnIndex = 0;
        error = null;
        DateOnly? date = null;
        TimeOnly? time = null;

        if (!(ParseHelpersUtf8.TryNextDate(ref reader, ref columnIndex, out date, out error)
            && ParseHelpersUtf8.TryNextTime(ref reader, ref columnIndex, out time, out error)
            && ParseHelpersUtf8.TryNextStringToken(ref reader, ref columnIndex, out error)
            && ParseHelpersUtf8.TryNextString(ref reader, ref columnIndex, out string? assetCode, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prOpen, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prHigh, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prLow, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prClose, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prStd, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? vol, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? val, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? trades, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwap, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prChange, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesB, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? tradesS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? valS, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volB, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? volS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? disb, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapB, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? prVwapS, out error)
            && ParseHelpersUtf8.TryNextDouble(ref reader, ref columnIndex, out double? im, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiOpen, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiHigh, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiLow, out error)
            && ParseHelpersUtf8.TryNextLong(ref reader, ref columnIndex, out long? oiClose, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrOpen, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrHigh, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrLow, out error)
            && ParseHelpersUtf8.TryNextInt(ref reader, ref columnIndex, out int? secPrClose, out error)
            && ParseHelpersUtf8.TryNextSystime(ref reader, ref columnIndex, out ulong? systime, out error)))
        {
            if (date.HasValue && time.HasValue)
                sourceTime = date.Value.ToDateTime(time.Value, DateTimeKind.Unspecified);
            return false;
        }

        sourceTime = date!.Value.ToDateTime(time!.Value, DateTimeKind.Unspecified);
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.EndArray, "строка TradeStats", RootKey);
        row = new TradeStatsFuturesRow
        {
            Secid = taskSecId,
            AssetCode = assetCode,
            PrOpen = prOpen,
            PrHigh = prHigh,
            PrLow = prLow,
            PrClose = prClose,
            PrStd = prStd,
            Vol = vol,
            Val = val,
            Trades = trades,
            PrVwap = prVwap,
            PrChange = prChange,
            TradesB = tradesB,
            TradesS = tradesS,
            ValB = valB,
            ValS = valS,
            VolB = volB,
            VolS = volS,
            Disb = disb,
            PrVwapB = prVwapB,
            PrVwapS = prVwapS,
            Im = im,
            OiOpen = oiOpen,
            OiHigh = oiHigh,
            OiLow = oiLow,
            OiClose = oiClose,
            SecPrOpen = secPrOpen,
            SecPrHigh = secPrHigh,
            SecPrLow = secPrLow,
            SecPrClose = secPrClose,
            Systime = systime,
        };
        return true;
    }

    private static void ExpectRowStart(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            ParseHelpersUtf8.SchemaMismatch(
                $"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
    }

    private static bool IsBeforeCursor(DateTime? sourceTime, DateTime? lastSourceTime)
        // Исключаем только ранее прочитанное время текущего дня; равный момент остаётся допустимым.
        => sourceTime.HasValue && lastSourceTime.HasValue && sourceTime.Value < lastSourceTime.Value;

    private static CandleRow BuildCandle(
        string taskSecId, double? open, double? close, double? high, double? low,
        double? value, double? volume, DateTime sourceTime, uint ingestPriority)
    {
        DateTime end = sourceTime.AddMinutes(5).AddSeconds(-1);
        return new CandleRow
        {
            Secid = taskSecId,
            Open = open,
            Close = close,
            High = high,
            Low = low,
            Value = value,
            Volume = volume,
            Begin = CandleDateTime64.ToUnixWholeSeconds(sourceTime),
            End = CandleDateTime64.ToUnixWholeSeconds(end),
            IngestPriority = ingestPriority,
        };
    }

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

    private static void ValidateSpec(MoexSeriesSpec spec, string expectedMarket, IReadOnlyList<string> columns)
    {
        if (spec.DataKind != RootKey || spec.Market != expectedMarket
            || spec.Pagination != PaginationKind.Cursor || !spec.InputColumns.SequenceEqual(columns))
            throw new ArgumentException(
                $"Typed trade statistics parsing requires registered {expectedMarket} TradeStats input columns.",
                nameof(spec));
    }
}
