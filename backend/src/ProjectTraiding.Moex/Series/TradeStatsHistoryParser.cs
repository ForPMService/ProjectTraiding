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
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow(receivedMetric: true);
            SourceTimeParts timeParts = default;
            string? rowError = null;
            TradeStatsStockRow row = new() { Secid = taskSecId };

            ReadNextMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref rowError);
            ReadNextMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref rowError);
            ExpectString(ref reader, spec, rowIndex, 2, ref rowError);
            row.PrOpen = ReadDouble(ref reader, spec, rowIndex, 3, ref rowError);
            row.PrHigh = ReadDouble(ref reader, spec, rowIndex, 4, ref rowError);
            row.PrLow = ReadDouble(ref reader, spec, rowIndex, 5, ref rowError);
            row.PrClose = ReadDouble(ref reader, spec, rowIndex, 6, ref rowError);
            row.PrStd = ReadDouble(ref reader, spec, rowIndex, 7, ref rowError);
            row.Vol = ReadInt(ref reader, spec, rowIndex, 8, ref rowError);
            row.Val = ReadDouble(ref reader, spec, rowIndex, 9, ref rowError);
            row.Trades = ReadInt(ref reader, spec, rowIndex, 10, ref rowError);
            row.PrVwap = ReadDouble(ref reader, spec, rowIndex, 11, ref rowError);
            row.PrChange = ReadDouble(ref reader, spec, rowIndex, 12, ref rowError);
            row.TradesB = ReadInt(ref reader, spec, rowIndex, 13, ref rowError);
            row.TradesS = ReadInt(ref reader, spec, rowIndex, 14, ref rowError);
            row.ValB = ReadDouble(ref reader, spec, rowIndex, 15, ref rowError);
            row.ValS = ReadDouble(ref reader, spec, rowIndex, 16, ref rowError);
            row.VolB = ReadLong(ref reader, spec, rowIndex, 17, ref rowError);
            row.VolS = ReadLong(ref reader, spec, rowIndex, 18, ref rowError);
            row.Disb = ReadDouble(ref reader, spec, rowIndex, 19, ref rowError);
            row.PrVwapB = ReadDouble(ref reader, spec, rowIndex, 20, ref rowError);
            row.PrVwapS = ReadDouble(ref reader, spec, rowIndex, 21, ref rowError);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 22, ref rowError);
            row.SecPrOpen = ReadInt(ref reader, spec, rowIndex, 23, ref rowError);
            row.SecPrHigh = ReadInt(ref reader, spec, rowIndex, 24, ref rowError);
            row.SecPrLow = ReadInt(ref reader, spec, rowIndex, 25, ref rowError);
            row.SecPrClose = ReadInt(ref reader, spec, rowIndex, 26, ref rowError);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.TradeStatsStock.Length);

            FinishStockRow(spec, taskSecId, statsWriter, candleWriter, acceptedCandles,
                lastSourceTime, ingestPriority, rowIndex, ref timeParts, ref rowError, row, state);
            rowIndex++;
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
            ExpectRowStart(ref reader, spec, rowIndex);
            state.RecordPhysicalRow(receivedMetric: true);
            SourceTimeParts timeParts = default;
            string? rowError = null;
            TradeStatsFuturesRow row = new() { Secid = taskSecId };

            ReadNextMomentDate(ref reader, spec, rowIndex, 0, ref timeParts, ref rowError);
            ReadNextMomentTime(ref reader, spec, rowIndex, 1, ref timeParts, ref rowError);
            ExpectString(ref reader, spec, rowIndex, 2, ref rowError);
            row.AssetCode = ReadString(ref reader, spec, rowIndex, 3, ref rowError);
            row.PrOpen = ReadDouble(ref reader, spec, rowIndex, 4, ref rowError);
            row.PrHigh = ReadDouble(ref reader, spec, rowIndex, 5, ref rowError);
            row.PrLow = ReadDouble(ref reader, spec, rowIndex, 6, ref rowError);
            row.PrClose = ReadDouble(ref reader, spec, rowIndex, 7, ref rowError);
            row.PrStd = ReadDouble(ref reader, spec, rowIndex, 8, ref rowError);
            row.Vol = ReadLong(ref reader, spec, rowIndex, 9, ref rowError);
            row.Val = ReadLong(ref reader, spec, rowIndex, 10, ref rowError);
            row.Trades = ReadInt(ref reader, spec, rowIndex, 11, ref rowError);
            row.PrVwap = ReadDouble(ref reader, spec, rowIndex, 12, ref rowError);
            row.PrChange = ReadDouble(ref reader, spec, rowIndex, 13, ref rowError);
            row.TradesB = ReadInt(ref reader, spec, rowIndex, 14, ref rowError);
            row.TradesS = ReadInt(ref reader, spec, rowIndex, 15, ref rowError);
            row.ValB = ReadDouble(ref reader, spec, rowIndex, 16, ref rowError);
            row.ValS = ReadDouble(ref reader, spec, rowIndex, 17, ref rowError);
            row.VolB = ReadLong(ref reader, spec, rowIndex, 18, ref rowError);
            row.VolS = ReadLong(ref reader, spec, rowIndex, 19, ref rowError);
            row.Disb = ReadDouble(ref reader, spec, rowIndex, 20, ref rowError);
            row.PrVwapB = ReadDouble(ref reader, spec, rowIndex, 21, ref rowError);
            row.PrVwapS = ReadDouble(ref reader, spec, rowIndex, 22, ref rowError);
            row.Im = ReadDouble(ref reader, spec, rowIndex, 23, ref rowError);
            row.OiOpen = ReadLong(ref reader, spec, rowIndex, 24, ref rowError);
            row.OiHigh = ReadLong(ref reader, spec, rowIndex, 25, ref rowError);
            row.OiLow = ReadLong(ref reader, spec, rowIndex, 26, ref rowError);
            row.OiClose = ReadLong(ref reader, spec, rowIndex, 27, ref rowError);
            row.SecPrOpen = ReadInt(ref reader, spec, rowIndex, 28, ref rowError);
            row.SecPrHigh = ReadInt(ref reader, spec, rowIndex, 29, ref rowError);
            row.SecPrLow = ReadInt(ref reader, spec, rowIndex, 30, ref rowError);
            row.SecPrClose = ReadInt(ref reader, spec, rowIndex, 31, ref rowError);
            row.Systime = ReadSystime(ref reader, spec, rowIndex, 32, ref rowError);
            ParseHelpersUtf8.ExpectRowEnd(ref reader, rowIndex, spec.RootKey, MoexColumnSets.TradeStatsFutures.Length);

            FinishFuturesRow(spec, taskSecId, statsWriter, candleWriter, acceptedCandles,
                lastSourceTime, ingestPriority, rowIndex, ref timeParts, ref rowError, row, state);
            rowIndex++;
        }
    }

    private static void FinishStockRow(
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<TradeStatsStockRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        List<CandleRow>? acceptedCandles,
        DateTime? lastSourceTime,
        uint ingestPriority,
        int rowIndex,
        ref SourceTimeParts timeParts,
        ref string? rowError,
        TradeStatsStockRow row,
        HistoricalReadState state)
    {
        DateTime? sourceTime = TryReadSourceTime(in timeParts);
        state.SetPageLastSourceTime(sourceTime);
        if (rowError is null)
            state.RecordParsedValidRow();
        if (IsBeforeCursor(sourceTime, lastSourceTime))
        {
            if (rowError is not null)
                state.RecordSkipped(rowIndex, sourceTime, rowError);
            return;
        }

        CandleRow candle;
        DateTime requiredSourceTime;
        try
        {
            if (rowError is not null)
                throw new InvalidOperationException(rowError);
            ValidateSecId(taskSecId);
            requiredSourceTime = MoexClickHouseTime.BuildSourceTime(in timeParts);
            row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(requiredSourceTime);
            candle = BuildCandle(
                taskSecId, row.PrOpen, row.PrClose, row.PrHigh, row.PrLow,
                row.Val, row.Vol.HasValue ? (double)row.Vol.Value : null,
                requiredSourceTime, ingestPriority);
            ValidateCandle(candle, taskSecId);
        }
        catch (Exception ex) when (IsRowRejection(ex))
        {
            state.RecordSkipped(rowIndex, sourceTime, rowError ?? ex.Message);
            return;
        }

        statsWriter.Append(in row);
        candleWriter.Append(in candle);
        acceptedCandles?.Add(candle);
        state.RecordAccepted(requiredSourceTime);
    }

    private static void FinishFuturesRow(
        MoexSeriesSpec spec,
        string taskSecId,
        ProtobufBatchWriter<TradeStatsFuturesRow> statsWriter,
        ProtobufBatchWriter<CandleRow> candleWriter,
        List<CandleRow>? acceptedCandles,
        DateTime? lastSourceTime,
        uint ingestPriority,
        int rowIndex,
        ref SourceTimeParts timeParts,
        ref string? rowError,
        TradeStatsFuturesRow row,
        HistoricalReadState state)
    {
        DateTime? sourceTime = TryReadSourceTime(in timeParts);
        state.SetPageLastSourceTime(sourceTime);
        if (rowError is null)
            state.RecordParsedValidRow();
        if (IsBeforeCursor(sourceTime, lastSourceTime))
        {
            if (rowError is not null)
                state.RecordSkipped(rowIndex, sourceTime, rowError);
            return;
        }

        CandleRow candle;
        DateTime requiredSourceTime;
        try
        {
            if (rowError is not null)
                throw new InvalidOperationException(rowError);
            ValidateSecId(taskSecId);
            requiredSourceTime = MoexClickHouseTime.BuildSourceTime(in timeParts);
            row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(requiredSourceTime);
            if (!TryConvertInt64ExactlyToDouble(row.Vol, out double? volume))
                throw new InvalidOperationException(
                    $"Строка отвергнута: Int64 vol={row.Vol} не представим точно в Float64 для свечи.");
            if (!TryConvertInt64ExactlyToDouble(row.Val, out double? value))
                throw new InvalidOperationException(
                    $"Строка отвергнута: Int64 val={row.Val} не представим точно в Float64 для свечи.");
            candle = BuildCandle(
                taskSecId, row.PrOpen, row.PrClose, row.PrHigh, row.PrLow,
                value, volume, requiredSourceTime, ingestPriority);
            ValidateCandle(candle, taskSecId);
        }
        catch (Exception ex) when (IsRowRejection(ex))
        {
            state.RecordSkipped(rowIndex, sourceTime, rowError ?? ex.Message);
            return;
        }

        statsWriter.Append(in row);
        candleWriter.Append(in candle);
        acceptedCandles?.Add(candle);
        state.RecordAccepted(requiredSourceTime);
    }

    private static void ExpectRowStart(ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex)
    {
        if (reader.TokenType != JsonTokenType.StartArray)
            ParseHelpersUtf8.SchemaMismatch(
                $"[{spec.RootKey}] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
    }

    private static void ReadNextMomentDate(
        ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex, int columnIndex,
        ref SourceTimeParts parts, ref string? rowError)
    {
        try
        {
            parts.Date = ParseHelpersUtf8.ReadNextMomentDate(
                ref reader, rowIndex, columnIndex, spec.RootKey, out parts.RawDate);
        }
        catch (Exception ex) when (IsRowRejection(ex))
        {
            rowError ??= ex.Message;
            reader.Skip();
        }
    }

    private static void ReadNextMomentTime(
        ref Utf8JsonReader reader, MoexSeriesSpec spec, int rowIndex, int columnIndex,
        ref SourceTimeParts parts, ref string? rowError)
    {
        try
        {
            parts.Time = ParseHelpersUtf8.ReadNextMomentTime(
                ref reader, rowIndex, columnIndex, spec.RootKey, out parts.RawTime);
        }
        catch (Exception ex) when (IsRowRejection(ex))
        {
            rowError ??= ex.Message;
            reader.Skip();
        }
    }

    private static void ExpectString(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, row, spec.RootKey, column); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); }
    }

    private static string? ReadString(ref Utf8JsonReader reader, MoexSeriesSpec spec, int row, int column, ref string? error)
    {
        try { return ParseHelpersUtf8.ReadNextNullableString(ref reader, row, spec.RootKey, column); }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
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
            DateTime? value = ParseHelpersUtf8.ReadNextNullableDateTime(
                ref reader, row, spec.RootKey, column, out bool wasNull);
            if (value is null && !wasNull && !reader.ValueTextEquals(""u8))
                error ??= $"[{spec.RootKey}] Некорректное непустое systime в строке {row}.";
            return value.HasValue ? CandleDateTime64.ToUnixWholeSeconds(value.Value) : null;
        }
        catch (Exception ex) when (IsRowRejection(ex)) { error ??= ex.Message; reader.Skip(); return null; }
    }

    private static DateTime? TryReadSourceTime(in SourceTimeParts parts)
    {
        try { return MoexClickHouseTime.BuildSourceTime(in parts); }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException) { return null; }
    }

    private static bool IsBeforeCursor(DateTime? sourceTime, DateTime? lastSourceTime)
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

    private static void ValidateCandle(CandleRow candle, string taskSecId)
    {
        if (string.IsNullOrWhiteSpace(candle.Secid) || candle.Secid != taskSecId)
            throw new InvalidOperationException("Свеча отвергнута: secid пустой или не совпадает с заданием.");
        if (!candle.End.HasValue || candle.End.Value < candle.Begin)
            throw new InvalidOperationException("Свеча отвергнута: некорректный диапазон begin/end.");
    }

    private static bool TryConvertInt64ExactlyToDouble(long? value, out double? converted)
    {
        converted = null;
        if (!value.HasValue)
            return true;
        double asDouble = (double)value.Value;
        const double TwoTo63 = 9223372036854775808d;
        if (asDouble < -TwoTo63 || asDouble >= TwoTo63)
            return false;
        long restored = (long)asDouble;
        if (restored != value.Value)
            return false;
        converted = asDouble;
        return true;
    }

    private static bool IsRowRejection(Exception exception)
        => exception is InvalidOperationException or FormatException or ArgumentException or OverflowException;

    private static void ValidateSecId(string taskSecId)
    {
        if (!string.IsNullOrWhiteSpace(taskSecId))
            return;
        throw new InvalidOperationException("Загрузка статистики сделок отвергнута: secid пустой.");
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
