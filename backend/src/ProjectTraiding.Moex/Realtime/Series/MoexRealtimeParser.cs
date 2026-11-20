using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ProjectTraiding.Moex.Clients;
using ProjectTraiding.Moex.DataFormats;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Realtime.Receiver;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>Parsers for MOEX realtime response shapes.</summary>
public static class MoexRealtimeParser
{
    private const string DataVersionRootKey = "dataversion";

    public static void ParseStockTrades(
        ReadOnlySpan<byte> body,
        string secid,
        ProtobufBatchWriter<RealtimeStockTradeRow>? writer,
        long? afterTradeNo,
        bool appendRows,
        ILogger logger,
        out int sourceRowsCount,
        out int acceptedRowsCount,
        out int rowFailuresCount,
        out DateTime lastTime,
        out long? lastTradeNo,
        out long? maxTradeNo,
        out DateTime? maxTradeNoTime,
        out Exception? maxTradeNoTimeFailure)
    {
        if (appendRows && writer is null)
            throw new ArgumentNullException(nameof(writer));

        DateOnly? sessionDate = ReadDataVersionFirstPass(body, out Exception? sessionDateFailure);
        sourceRowsCount = 0;
        acceptedRowsCount = 0;
        rowFailuresCount = 0;
        lastTime = default;
        lastTradeNo = null;
        maxTradeNo = null;
        maxTradeNoTime = null;
        maxTradeNoTimeFailure = null;

        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        bool foundTrades = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            if (reader.ValueTextEquals("trades"u8))
            {
                if (foundTrades)
                {
                    reader.Skip();
                    continue;
                }
                foundTrades = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, "trades");
                ReadStockTradesTable(
                    ref reader, writer, logger, secid, sessionDate, sessionDateFailure,
                    afterTradeNo, appendRows, ref sourceRowsCount, ref acceptedRowsCount,
                    ref rowFailuresCount, ref lastTime, ref lastTradeNo, ref maxTradeNo,
                    ref maxTradeNoTime, ref maxTradeNoTimeFailure);
            }
            else
            {
                reader.Skip();
            }
        }

        if (!foundTrades)
            ParseHelpersUtf8.RootKeyMissing("trades");
        if (sessionDateFailure is not null && acceptedRowsCount > 0)
        {
            acceptedRowsCount = 0;
        }
    }

    public static void ParseFuturesTrades(
        ReadOnlySpan<byte> body,
        string secid,
        ProtobufBatchWriter<RealtimeFuturesTradeRow>? writer,
        long? afterTradeNo,
        bool appendRows,
        ILogger logger,
        out int sourceRowsCount,
        out int acceptedRowsCount,
        out int rowFailuresCount,
        out DateTime lastTime,
        out long? lastTradeNo,
        out long? maxTradeNo,
        out DateTime? maxTradeNoTime,
        out Exception? maxTradeNoTimeFailure)
    {
        if (appendRows && writer is null)
            throw new ArgumentNullException(nameof(writer));

        DateOnly? sessionDate = ReadDataVersionFirstPass(body, out Exception? sessionDateFailure);
        sourceRowsCount = 0;
        acceptedRowsCount = 0;
        rowFailuresCount = 0;
        lastTime = default;
        lastTradeNo = null;
        maxTradeNo = null;
        maxTradeNoTime = null;
        maxTradeNoTimeFailure = null;

        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        bool foundTrades = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            if (reader.ValueTextEquals("trades"u8))
            {
                if (foundTrades)
                {
                    reader.Skip();
                    continue;
                }
                foundTrades = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, "trades");
                ReadFuturesTradesTable(
                    ref reader, writer, logger, secid, sessionDate, sessionDateFailure,
                    afterTradeNo, appendRows, ref sourceRowsCount, ref acceptedRowsCount,
                    ref rowFailuresCount, ref lastTime, ref lastTradeNo, ref maxTradeNo,
                    ref maxTradeNoTime, ref maxTradeNoTimeFailure);
            }
            else
            {
                reader.Skip();
            }
        }

        if (!foundTrades)
            ParseHelpersUtf8.RootKeyMissing("trades");
        if (sessionDateFailure is not null && acceptedRowsCount > 0)
        {
            acceptedRowsCount = 0;
        }
    }

    public static void ParseOrderbook(
        ReadOnlySpan<byte> body,
        string secid,
        ProtobufBatchWriter<RealtimeOrderbookRow> writer,
        ILogger logger,
        out int sourceRowsCount,
        out int acceptedRowsCount,
        out int rowFailuresCount,
        out DateTime firstTime)
    {
        DateOnly? sessionDate = ReadDataVersionFirstPass(body, out Exception? sessionDateFailure);
        sourceRowsCount = 0;
        acceptedRowsCount = 0;
        rowFailuresCount = 0;
        firstTime = default;

        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        bool foundOrderbook = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            if (reader.ValueTextEquals("orderbook"u8))
            {
                if (foundOrderbook)
                {
                    reader.Skip();
                    continue;
                }
                foundOrderbook = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, "orderbook");
                ReadOrderbookTable(
                    ref reader, writer, logger, secid, sessionDate, sessionDateFailure,
                    ref sourceRowsCount, ref acceptedRowsCount, ref rowFailuresCount, ref firstTime);
            }
            else
            {
                reader.Skip();
            }
        }

        if (!foundOrderbook)
            ParseHelpersUtf8.RootKeyMissing("orderbook");
        if (sessionDateFailure is not null && acceptedRowsCount > 0)
        {
            acceptedRowsCount = 0;
        }
    }

    public static void ParseCandles(
        ReadOnlySpan<byte> body,
        string secid,
        ProtobufBatchWriter<CandleRow> writer,
        DateTime now,
        DateTime? lastBegin,
        int interval,
        int start,
        ILogger logger,
        List<CandleRow>? acceptedCandles,
        out int sourceRowsCount,
        out int acceptedRowsCount,
        out int rowFailuresCount,
        out DateTime? maxAcceptedBegin)
    {
        sourceRowsCount = 0;
        acceptedRowsCount = 0;
        rowFailuresCount = 0;
        maxAcceptedBegin = null;
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.SkipToRootObject(ref reader, "candles");

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
                ParseHelpersUtf8.ValidateInputColumnsUtf8(
                    ref reader, MoexColumnSets.Candles, "candles");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (foundData)
                    ParseHelpersUtf8.SchemaMismatch("[candles] Повторная секция 'data'.");
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[candles] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }

                foundData = true;
                ReadCandleRows(
                    ref reader,
                    logger,
                    writer,
                    secid,
                    interval,
                    start,
                    acceptedCandles,
                    now,
                    lastBegin,
                    ref sourceRowsCount,
                    ref acceptedRowsCount,
                    ref rowFailuresCount,
                    ref maxAcceptedBegin);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, "candles");
    }

    private static DateOnly? ReadDataVersionFirstPass(
        ReadOnlySpan<byte> body,
        out Exception? sessionDateFailure)
    {
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            if (reader.ValueTextEquals(DataVersionRootKey))
            {
                ParseHelpersUtf8.ExpectObjectStart(ref reader, DataVersionRootKey);
                return ReadDataVersionBlock(ref reader, out sessionDateFailure);
            }
            reader.Skip();
        }
        ParseHelpersUtf8.RootKeyMissing(DataVersionRootKey);
        sessionDateFailure = null;
        return null;
    }

    private static void LogTradeRowFailure(ILogger logger, string secid, int position, string reason)
        => logger.LogWarning(
            "MOEX row skipped: secid={Secid}, dataKind={DataKind}, position={Position}, reason={Reason}",
            secid, "trades", position, reason);

    private static void LogOrderbookRowFailure(ILogger logger, string secid, int position, string reason)
        => logger.LogWarning(
            "MOEX row skipped: secid={Secid}, dataKind={DataKind}, position={Position}, reason={Reason}",
            secid, "orderbook", position, reason);

    private static void ReadStockTradesTable(
        ref Utf8JsonReader reader,
        ProtobufBatchWriter<RealtimeStockTradeRow>? writer,
        ILogger logger,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        long? afterTradeNo,
        bool appendRows,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure)
    {
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
                    ParseHelpersUtf8.SchemaMismatch("[trades] Повторная секция 'columns'.");
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(
                    ref reader, MoexColumnSets.TradesStock, "trades");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (foundData)
                    ParseHelpersUtf8.SchemaMismatch("[trades] Повторная секция 'data'.");
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[trades] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                ReadStockTradeRows(
                    ref reader, writer, logger, secid, sessionDate, sessionDateFailure,
                    afterTradeNo, appendRows, ref sourceRowsCount, ref acceptedRowsCount,
                    ref rowFailuresCount,
                    ref lastTime, ref lastTradeNo, ref maxTradeNo,
                    ref maxTradeNoTime, ref maxTradeNoTimeFailure);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, "trades");
    }

    private static void ReadFuturesTradesTable(
        ref Utf8JsonReader reader,
        ProtobufBatchWriter<RealtimeFuturesTradeRow>? writer,
        ILogger logger,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        long? afterTradeNo,
        bool appendRows,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure)
    {
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
                    ParseHelpersUtf8.SchemaMismatch("[trades] Повторная секция 'columns'.");
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(
                    ref reader, MoexColumnSets.TradesFutures, "trades");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (foundData)
                    ParseHelpersUtf8.SchemaMismatch("[trades] Повторная секция 'data'.");
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[trades] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                ReadFuturesTradeRows(
                    ref reader, writer, logger, secid, sessionDate, sessionDateFailure,
                    afterTradeNo, appendRows, ref sourceRowsCount, ref acceptedRowsCount,
                    ref rowFailuresCount,
                    ref lastTime, ref lastTradeNo, ref maxTradeNo,
                    ref maxTradeNoTime, ref maxTradeNoTimeFailure);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, "trades");
    }

    private static void ReadOrderbookTable(
        ref Utf8JsonReader reader,
        ProtobufBatchWriter<RealtimeOrderbookRow> writer,
        ILogger logger,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime firstTime)
    {
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
                    ParseHelpersUtf8.SchemaMismatch("[orderbook] Повторная секция 'columns'.");
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(
                    ref reader, MoexColumnSets.Orderbook, "orderbook");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (foundData)
                    ParseHelpersUtf8.SchemaMismatch("[orderbook] Повторная секция 'data'.");
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[orderbook] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                ReadOrderbookRows(
                    ref reader, writer, logger, secid, sessionDate, sessionDateFailure,
                    ref sourceRowsCount, ref acceptedRowsCount, ref rowFailuresCount, ref firstTime);
            }
            else
            {
                reader.Skip();
            }
        }
        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, "orderbook");
    }

    private static void ReadStockTradeRows(
        ref Utf8JsonReader reader,
        ProtobufBatchWriter<RealtimeStockTradeRow>? writer,
        ILogger logger,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        long? afterTradeNo,
        bool appendRows,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", "trades");
        int rowIndex = 0;
        long? previousTradeNo = afterTradeNo;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[trades] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
            }

            RealtimeStockTradeRow row = new() { Secid = secid, IngestPriority = 0 };
            long? tradeNo = null;
            SourceTimeParts timeParts = default;
            Exception? rowFailure = null;
            try { tradeNo = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "trades", 0); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { timeParts.Time = ParseHelpersUtf8.ReadNextMomentTime(ref reader, rowIndex, 1, "trades", out timeParts.RawTime); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, rowIndex, "trades", 2); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Price = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "trades", 3); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Quantity = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "trades", 4); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Period = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "trades", 5); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Value = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "trades", 6); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.BoardId = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "trades", 7); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try
            {
                DateTime? systemTime = ParseHelpersUtf8.ReadNextNullableDateTime(
                    ref reader, rowIndex, "trades", 8, out _);
                row.Systime = systemTime is DateTime value
                    ? CandleDateTime64.ToUnixWholeSeconds(value)
                    : null;
            }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.BuySell = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "trades", 9); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Decimals = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, "trades", 10); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.TradingSession = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "trades", 11); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { timeParts.Date = ParseHelpersUtf8.ReadNextMomentDate(ref reader, rowIndex, 12, "trades", out timeParts.RawDate); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, rowIndex, "trades", 13); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            ParseHelpersUtf8.ExpectRowEnd(
                ref reader, rowIndex, "trades", MoexColumnSets.TradesStock.Length);

            DateTime? sourceTime = TryBuildSourceTime(in timeParts, out Exception? timeFailure);
            if (rowFailure is null && string.IsNullOrWhiteSpace(secid))
                rowFailure = new InvalidOperationException("Запись сделок отвергнута: secid пустой.");
            if (rowFailure is null && sourceTime is null)
                rowFailure = timeFailure;
            if (rowFailure is null && tradeNo is null)
                rowFailure = new InvalidOperationException("Сделка отвергнута: TRADENO пуст.");

            row.TradeNo = tradeNo.GetValueOrDefault();
            if (rowFailure is null)
            {
                if (sessionDateFailure is not null && acceptedRowsCount == 0)
                {
                    LogTradeRowFailure(logger, secid, -1, sessionDateFailure.Message);
                    rowFailuresCount++;
                }
                if (appendRows && previousTradeNo is not null && tradeNo!.Value <= previousTradeNo.Value)
                {
                    throw new InvalidOperationException(
                        "Нарушен порядок номеров сделок.");
                }
                previousTradeNo = tradeNo;
                row.TradeSessionDate = sessionDate is DateOnly date
                    ? ToClickHouseDate(date)
                    : null;
                ulong sourceTimeValue = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                row.SourceTime = sourceTimeValue;
                if (appendRows && sessionDateFailure is null)
                    writer!.Append(in row);
                acceptedRowsCount++;
                lastTime = sourceTime.Value;
                lastTradeNo = tradeNo;
                if (maxTradeNo is null || tradeNo!.Value > maxTradeNo.Value)
                {
                    maxTradeNo = tradeNo;
                    maxTradeNoTime = sourceTime;
                    maxTradeNoTimeFailure = null;
                }
            }
            else
            {
                LogTradeRowFailure(logger, secid, rowIndex, rowFailure.Message);
                rowFailuresCount++;
            }
            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static void ReadFuturesTradeRows(
        ref Utf8JsonReader reader,
        ProtobufBatchWriter<RealtimeFuturesTradeRow>? writer,
        ILogger logger,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        long? afterTradeNo,
        bool appendRows,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", "trades");
        int rowIndex = 0;
        long? previousTradeNo = afterTradeNo;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[trades] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
            }

            RealtimeFuturesTradeRow row = new() { Secid = secid, IngestPriority = 0 };
            long? tradeNo = null;
            SourceTimeParts timeParts = default;
            Exception? rowFailure = null;
            try { tradeNo = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "trades", 0); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.BoardName = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "trades", 1); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, rowIndex, "trades", 2); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { timeParts.Date = ParseHelpersUtf8.ReadNextMomentDate(ref reader, rowIndex, 3, "trades", out timeParts.RawDate); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { timeParts.Time = ParseHelpersUtf8.ReadNextMomentTime(ref reader, rowIndex, 4, "trades", out timeParts.RawTime); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Price = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "trades", 5); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Quantity = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "trades", 6); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try
            {
                DateTime? systemTime = ParseHelpersUtf8.ReadNextNullableDateTime(
                    ref reader, rowIndex, "trades", 7, out _);
                row.Systime = systemTime is DateTime value
                    ? CandleDateTime64.ToUnixWholeSeconds(value)
                    : null;
            }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.RecNo = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "trades", 8); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.OpenPosition = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "trades", 9); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.OffMarketDeal = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, "trades", 10); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.BuySell = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "trades", 11); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, rowIndex, "trades", 12); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            ParseHelpersUtf8.ExpectRowEnd(
                ref reader, rowIndex, "trades", MoexColumnSets.TradesFutures.Length);

            DateTime? sourceTime = TryBuildSourceTime(in timeParts, out Exception? timeFailure);
            if (rowFailure is null && string.IsNullOrWhiteSpace(secid))
                rowFailure = new InvalidOperationException("Запись сделок отвергнута: secid пустой.");
            if (rowFailure is null && sourceTime is null)
                rowFailure = timeFailure;
            if (rowFailure is null && tradeNo is null)
                rowFailure = new InvalidOperationException("Сделка отвергнута: TRADENO пуст.");

            row.TradeNo = tradeNo.GetValueOrDefault();
            if (rowFailure is null)
            {
                if (sessionDateFailure is not null && acceptedRowsCount == 0)
                {
                    LogTradeRowFailure(logger, secid, -1, sessionDateFailure.Message);
                    rowFailuresCount++;
                }
                if (appendRows && previousTradeNo is not null && tradeNo!.Value <= previousTradeNo.Value)
                {
                    throw new InvalidOperationException(
                        "Нарушен порядок номеров сделок.");
                }
                previousTradeNo = tradeNo;
                row.TradeSessionDate = sessionDate is DateOnly date
                    ? ToClickHouseDate(date)
                    : null;
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                if (appendRows && sessionDateFailure is null)
                    writer!.Append(in row);
                acceptedRowsCount++;
                lastTime = sourceTime.Value;
                lastTradeNo = tradeNo;
                if (maxTradeNo is null || tradeNo!.Value > maxTradeNo.Value)
                {
                    maxTradeNo = tradeNo;
                    maxTradeNoTime = sourceTime;
                    maxTradeNoTimeFailure = null;
                }
            }
            else
            {
                LogTradeRowFailure(logger, secid, rowIndex, rowFailure.Message);
                rowFailuresCount++;
            }
            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static void ReadOrderbookRows(
        ref Utf8JsonReader reader,
        ProtobufBatchWriter<RealtimeOrderbookRow> writer,
        ILogger logger,
        string secid,
        DateOnly? sessionDate,
        Exception? sessionDateFailure,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime firstTime)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", "orderbook");
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[orderbook] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
            }

            RealtimeOrderbookRow row = new() { Secid = secid, IngestPriority = 0 };
            string? buySell = null;
            double? price = null;
            long? seqNum = null;
            Exception? rowFailure = null;
            try { row.BoardId = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "orderbook", 0); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, rowIndex, "orderbook", 1); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { buySell = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, "orderbook", 2); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { price = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "orderbook", 3); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Quantity = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "orderbook", 4); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { seqNum = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "orderbook", 5); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { ParseHelpersUtf8.ExpectNextNullableString(ref reader, rowIndex, "orderbook", 6); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Decimals = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, "orderbook", 7); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            ParseHelpersUtf8.ExpectRowEnd(
                ref reader, rowIndex, "orderbook", MoexColumnSets.Orderbook.Length);

            DateTime? sourceTime = ReadSnapshotTime(seqNum, out Exception? timeFailure);
            if (rowFailure is null && string.IsNullOrWhiteSpace(secid))
                rowFailure = new InvalidOperationException("Запись стакана отвергнута: secid пустой.");
            if (rowFailure is null && sourceTime is null)
                rowFailure = timeFailure;
            if (rowFailure is null && string.IsNullOrWhiteSpace(buySell))
                rowFailure = new InvalidOperationException("Строка стакана отвергнута: BUYSELL пуст.");
            if (rowFailure is null && price is null)
                rowFailure = new InvalidOperationException("Строка стакана отвергнута: PRICE пуст.");

            if (rowFailure is null)
            {
                if (sessionDateFailure is not null && acceptedRowsCount == 0)
                {
                    LogOrderbookRowFailure(logger, secid, -1, sessionDateFailure.Message);
                    rowFailuresCount++;
                }
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                row.BuySell = buySell!;
                row.Price = price!.Value;
                row.TradeSessionDate = sessionDate is DateOnly date
                    ? ToClickHouseDate(date)
                    : null;
                if (acceptedRowsCount == 0)
                    firstTime = sourceTime.Value;
                else if (firstTime != sourceTime.Value)
                    throw new InvalidOperationException(
                        "Строки стакана относятся к разным снимкам.");
                if (sessionDateFailure is null)
                    writer.Append(in row);
                acceptedRowsCount++;
            }
            else
            {
                LogOrderbookRowFailure(logger, secid, rowIndex, rowFailure.Message);
                rowFailuresCount++;
            }
            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static void ReadCandleRows(
        ref Utf8JsonReader reader,
        ILogger logger,
        ProtobufBatchWriter<CandleRow> writer,
        string secid,
        int interval,
        int start,
        List<CandleRow>? acceptedCandles,
        DateTime now,
        DateTime? lastBegin,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
        ref int rowFailuresCount,
        ref DateTime? maxAcceptedBegin)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", "candles");
        int rowIndex = 0;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[candles] Ожидался StartArray строки {rowIndex}, получено {reader.TokenType}.");
            }

            CandleRow row = new() { Secid = secid, IngestPriority = 0 };
            DateTime? begin = null;
            DateTime? end = null;
            ulong beginTimestamp = 0;
            ulong endTimestamp = 0;
            Exception? rowFailure = null;
            try { row.Open = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "candles", 0); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Close = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "candles", 1); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.High = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "candles", 2); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Low = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "candles", 3); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Value = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "candles", 4); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try { row.Volume = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, "candles", 5); }
            catch (Exception ex) when (IsRowValueFailure(ex)) { rowFailure ??= ex; }
            try
            {
                begin = ParseHelpersUtf8.ReadNextNullableDateTime(
                    ref reader, rowIndex, "candles", 6, out bool beginWasNull);
                if (beginWasNull)
                    rowFailure ??= new InvalidOperationException("Свеча отвергнута: begin пустой.");
                else if (begin is null)
                    rowFailure ??= new InvalidOperationException(
                        "Свеча отвергнута: begin имеет неверный формат времени.");
            }
            catch (Exception ex) when (IsRowValueFailure(ex))
            {
                rowFailure ??= new InvalidOperationException($"Свеча отвергнута: begin: {ex.Message}", ex);
            }
            try
            {
                end = ParseHelpersUtf8.ReadNextNullableDateTime(
                    ref reader, rowIndex, "candles", 7, out bool endWasNull);
                if (endWasNull)
                    rowFailure ??= new InvalidOperationException("Свеча отвергнута: end пустой.");
                else if (end is null)
                    rowFailure ??= new InvalidOperationException(
                        "Свеча отвергнута: end имеет неверный формат времени.");
            }
            catch (Exception ex) when (IsRowValueFailure(ex))
            {
                rowFailure ??= new InvalidOperationException($"Свеча отвергнута: end: {ex.Message}", ex);
            }
            ParseHelpersUtf8.ExpectRowEnd(
                ref reader, rowIndex, "candles", MoexColumnSets.Candles.Length);

            if (rowFailure is null)
            {
                try
                {
                    beginTimestamp = CandleDateTime64.ToUnixWholeSeconds(begin!.Value);
                }
                catch (ArgumentException ex)
                {
                    rowFailure = new InvalidOperationException(
                        $"Свеча отвергнута: begin не является допустимым временем. {ex.Message}", ex);
                }

                if (rowFailure is null)
                {
                    try
                    {
                        endTimestamp = CandleDateTime64.ToUnixWholeSeconds(end!.Value);
                    }
                    catch (ArgumentException ex)
                    {
                        rowFailure = new InvalidOperationException(
                            $"Свеча отвергнута: end не является допустимым временем. {ex.Message}", ex);
                    }
                }
            }

            if (rowFailure is not null)
            {
                MoexRealtimeReceiverLogMessages.CandleRowSkipped(
                    logger, secid, "candles", interval,
                    checked(start + sourceRowsCount), rowFailure.Message);
                rowFailuresCount++;
            }
            else
            {
                DateTime beginValue = begin!.Value;
                if (beginValue <= now && (lastBegin is null || beginValue >= lastBegin.Value))
                {
                    row.Begin = beginTimestamp;
                    row.End = endTimestamp;
                    writer.Append(in row);
                    if (acceptedCandles is not null)
                        acceptedCandles.Add(row);
                    acceptedRowsCount++;
                    if (maxAcceptedBegin is null || beginValue > maxAcceptedBegin.Value)
                        maxAcceptedBegin = beginValue;
                }
            }

            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static DateOnly? ReadDataVersionBlock(
        ref Utf8JsonReader reader,
        out Exception? failure)
    {
        bool foundColumns = false;
        bool foundData = false;
        int rowCount = 0;
        string? sessionDate = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;
            if (reader.ValueTextEquals("columns"u8))
            {
                if (foundColumns)
                    ParseHelpersUtf8.SchemaMismatch($"[{DataVersionRootKey}] Повторная секция 'columns'.");
                foundColumns = true;
                ParseHelpersUtf8.ValidateInputColumnsUtf8(
                    ref reader, MoexColumnSets.DataVersion, DataVersionRootKey);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (foundData)
                    ParseHelpersUtf8.SchemaMismatch($"[{DataVersionRootKey}] Повторная секция 'data'.");
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{DataVersionRootKey}] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                int currentRowCount = 0;
                sessionDate = ReadSessionDateData(ref reader, ref currentRowCount);
                rowCount = currentRowCount;
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, DataVersionRootKey);
        if (rowCount == 0)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{DataVersionRootKey}] Блок dataversion не содержит ни одной строки.");
        }
        try
        {
            failure = null;
            return MoexClickHouseTime.ParseDate(sessionDate);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            failure = ex;
            return null;
        }
    }

    private static string? ReadSessionDateData(
        ref Utf8JsonReader reader,
        ref int rowCount)
    {
        ParseHelpersUtf8.ReadAndExpect(
            ref reader, JsonTokenType.StartArray, "data", DataVersionRootKey);
        string? sessionDate = null;
        bool foundDataEnd = false;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                foundDataEnd = true;
                break;
            }

            if (rowCount > 0)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{DataVersionRootKey}] Ожидалась ровно 1 строка в dataversion, но найдено больше.");
            }

            if (reader.TokenType != JsonTokenType.StartArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{DataVersionRootKey}] Ожидалась строка-массив в data, " +
                    $"получено {reader.TokenType}.");
            }

            _ = ParseHelpersUtf8.ReadNextNullableInt(
                ref reader, rowCount, DataVersionRootKey, 0);
            _ = ParseHelpersUtf8.ReadNextNullableLong(
                ref reader, rowCount, DataVersionRootKey, 1);
            ParseHelpersUtf8.ExpectNextNullableString(
                ref reader, rowCount, DataVersionRootKey, 2);
            sessionDate = ParseHelpersUtf8.ReadNextNullableString(
                ref reader, rowCount, DataVersionRootKey, 3);

            ParseHelpersUtf8.ExpectRowEnd(
                ref reader, rowCount, DataVersionRootKey, MoexColumnSets.DataVersion.Length);
            rowCount++;
        }

        if (!foundDataEnd)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{DataVersionRootKey}] Ожидался EndArray у секции data.");
        }

        return sessionDate;
    }

    private static DateTime? TryBuildSourceTime(
        in SourceTimeParts parts,
        out Exception? failure)
    {
        try
        {
            DateTime value = MoexClickHouseTime.BuildSourceTime(in parts);
            failure = null;
            return value;
        }
        catch (Exception ex) when (
            ex is InvalidOperationException or FormatException or ArgumentOutOfRangeException)
        {
            failure = ex;
            return null;
        }
    }

    private static DateTime? ReadSnapshotTime(long? seqNum, out Exception? failure)
    {
        if (seqNum is null || seqNum.Value <= 0)
        {
            failure = new InvalidOperationException("Строка стакана отвергнута: SEQNUM пуст.");
            return null;
        }
        long value = seqNum.Value;
        if (value < 10_000_000_000_000L || value > 99_999_999_999_999L)
        {
            string text = value.ToString(CultureInfo.InvariantCulture);
            failure = new InvalidOperationException(
                $"Строка стакана отвергнута: SEQNUM='{text}' не в форме YYYYMMDDHHmmss.");
            return null;
        }

        long rest = value;
        int second = (int)(rest % 100);
        rest /= 100;
        int minute = (int)(rest % 100);
        rest /= 100;
        int hour = (int)(rest % 100);
        rest /= 100;
        int day = (int)(rest % 100);
        rest /= 100;
        int month = (int)(rest % 100);
        int year = (int)(rest / 100);
        try
        {
            failure = null;
            return new DateTime(year, month, day, hour, minute, second, DateTimeKind.Unspecified);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            failure = ex;
            return null;
        }
    }

    private static uint? ToClickHouseDate(DateOnly date)
    {
        int epochDayNumber = DateOnly.FromDateTime(DateTime.UnixEpoch).DayNumber;
        return checked((uint)(date.DayNumber - epochDayNumber));
    }

    private static bool IsRowValueFailure(Exception exception)
        => exception is InvalidOperationException or FormatException or OverflowException;
}
