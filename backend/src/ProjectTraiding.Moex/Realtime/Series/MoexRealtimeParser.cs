using System.Globalization;
using System.Text.Json;
using ProjectTraiding.Moex.Parsing;
using ProjectTraiding.Moex.Series;
using ProjectTraiding.Moex.StorageBase.ClickHouse;

namespace ProjectTraiding.Moex.Realtime.Series;

/// <summary>Stateless, one-pass parsers for the MOEX realtime response shapes.</summary>
public static class MoexRealtimeParser
{
    public static RealtimeStockTradesPage ParseStockTrades(
        ReadOnlySpan<byte> body,
        string secid)
    {
        List<RealtimeStockTradeRow> rows = new();
        List<RealtimeRowFailure> failures = new();
        DateOnly? sessionDate = null;
        Exception? sessionDateFailure = null;
        int sourceRowsCount = 0;
        DateTime lastTime = default;
        long? lastTradeNo = null;
        long? maxTradeNo = null;
        DateTime? maxTradeNoTime = null;
        Exception? maxTradeNoTimeFailure = null;

        ColumnAndNumbersForParsing.ExpectedSchema dataVersionSchema =
            ColumnAndNumbersForParsing.RealtimeDataVersionSchema;
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        bool foundTrades = false;
        bool foundDataVersion = false;

        while (!(foundTrades && foundDataVersion) && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (!foundDataVersion && reader.ValueTextEquals(dataVersionSchema.RootKey))
            {
                foundDataVersion = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, dataVersionSchema.RootKey);
                sessionDate = ReadDataVersionBlock(
                    ref reader, dataVersionSchema, out sessionDateFailure);
            }
            else if (!foundTrades && reader.ValueTextEquals("trades"u8))
            {
                foundTrades = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, "trades");
                ReadStockTradesTable(
                    ref reader,
                    rows,
                    failures,
                    secid,
                    ref sourceRowsCount,
                    ref lastTime,
                    ref lastTradeNo,
                    ref maxTradeNo,
                    ref maxTradeNoTime,
                    ref maxTradeNoTimeFailure);
            }
            else
            {
                reader.Skip();
            }
        }

        if (!foundDataVersion)
            ParseHelpersUtf8.RootKeyMissing(dataVersionSchema.RootKey);
        if (!foundTrades)
            ParseHelpersUtf8.RootKeyMissing("trades");

        if (sessionDateFailure is not null && rows.Count > 0)
        {
            failures.Add(new RealtimeRowFailure(-1, sessionDateFailure.Message));
            rows.Clear();
        }
        else if (sessionDate is DateOnly date)
        {
            uint? clickHouseDate = ToClickHouseDate(date);
            for (int i = 0; i < rows.Count; i++)
            {
                RealtimeStockTradeRow row = rows[i];
                row.TradeSessionDate = clickHouseDate;
                rows[i] = row;
            }
        }

        return new RealtimeStockTradesPage
        {
            Rows = rows,
            RowFailures = failures,
            SourceRowsCount = sourceRowsCount,
            LastTime = lastTime,
            LastTradeNo = lastTradeNo,
            MaxTradeNo = maxTradeNo,
            MaxTradeNoTime = maxTradeNoTime,
            MaxTradeNoTimeFailure = maxTradeNoTimeFailure,
        };
    }

    public static RealtimeFuturesTradesPage ParseFuturesTrades(
        ReadOnlySpan<byte> body,
        string secid)
    {
        List<RealtimeFuturesTradeRow> rows = new();
        List<RealtimeRowFailure> failures = new();
        DateOnly? sessionDate = null;
        Exception? sessionDateFailure = null;
        int sourceRowsCount = 0;
        DateTime lastTime = default;
        long? lastTradeNo = null;
        long? maxTradeNo = null;
        DateTime? maxTradeNoTime = null;
        Exception? maxTradeNoTimeFailure = null;

        ColumnAndNumbersForParsing.ExpectedSchema dataVersionSchema =
            ColumnAndNumbersForParsing.RealtimeDataVersionSchema;
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        bool foundTrades = false;
        bool foundDataVersion = false;

        while (!(foundTrades && foundDataVersion) && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (!foundDataVersion && reader.ValueTextEquals(dataVersionSchema.RootKey))
            {
                foundDataVersion = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, dataVersionSchema.RootKey);
                sessionDate = ReadDataVersionBlock(
                    ref reader, dataVersionSchema, out sessionDateFailure);
            }
            else if (!foundTrades && reader.ValueTextEquals("trades"u8))
            {
                foundTrades = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, "trades");
                ReadFuturesTradesTable(
                    ref reader,
                    rows,
                    failures,
                    secid,
                    ref sourceRowsCount,
                    ref lastTime,
                    ref lastTradeNo,
                    ref maxTradeNo,
                    ref maxTradeNoTime,
                    ref maxTradeNoTimeFailure);
            }
            else
            {
                reader.Skip();
            }
        }

        if (!foundDataVersion)
            ParseHelpersUtf8.RootKeyMissing(dataVersionSchema.RootKey);
        if (!foundTrades)
            ParseHelpersUtf8.RootKeyMissing("trades");

        if (sessionDateFailure is not null && rows.Count > 0)
        {
            failures.Add(new RealtimeRowFailure(-1, sessionDateFailure.Message));
            rows.Clear();
        }
        else if (sessionDate is DateOnly date)
        {
            uint? clickHouseDate = ToClickHouseDate(date);
            for (int i = 0; i < rows.Count; i++)
            {
                RealtimeFuturesTradeRow row = rows[i];
                row.TradeSessionDate = clickHouseDate;
                rows[i] = row;
            }
        }

        return new RealtimeFuturesTradesPage
        {
            Rows = rows,
            RowFailures = failures,
            SourceRowsCount = sourceRowsCount,
            LastTime = lastTime,
            LastTradeNo = lastTradeNo,
            MaxTradeNo = maxTradeNo,
            MaxTradeNoTime = maxTradeNoTime,
            MaxTradeNoTimeFailure = maxTradeNoTimeFailure,
        };
    }

    public static RealtimeOrderbookPage ParseOrderbook(
        ReadOnlySpan<byte> body,
        string secid)
    {
        List<RealtimeOrderbookRow> rows = new();
        List<RealtimeRowFailure> failures = new();
        DateOnly? sessionDate = null;
        Exception? sessionDateFailure = null;
        int sourceRowsCount = 0;
        DateTime firstTime = default;

        ColumnAndNumbersForParsing.ExpectedSchema dataVersionSchema =
            ColumnAndNumbersForParsing.RealtimeDataVersionSchema;
        Utf8JsonReader reader = new(body);
        ParseHelpersUtf8.EnterResponseObject(ref reader);
        bool foundOrderbook = false;
        bool foundDataVersion = false;

        while (!(foundOrderbook && foundDataVersion) && reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
                break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                continue;

            if (!foundDataVersion && reader.ValueTextEquals(dataVersionSchema.RootKey))
            {
                foundDataVersion = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, dataVersionSchema.RootKey);
                sessionDate = ReadDataVersionBlock(
                    ref reader, dataVersionSchema, out sessionDateFailure);
            }
            else if (!foundOrderbook && reader.ValueTextEquals("orderbook"u8))
            {
                foundOrderbook = true;
                ParseHelpersUtf8.ExpectObjectStart(ref reader, "orderbook");
                ReadOrderbookTable(
                    ref reader, rows, failures, secid, ref sourceRowsCount, ref firstTime);
            }
            else
            {
                reader.Skip();
            }
        }

        if (!foundDataVersion)
            ParseHelpersUtf8.RootKeyMissing(dataVersionSchema.RootKey);
        if (!foundOrderbook)
            ParseHelpersUtf8.RootKeyMissing("orderbook");

        if (sessionDateFailure is not null && rows.Count > 0)
        {
            failures.Add(new RealtimeRowFailure(-1, sessionDateFailure.Message));
            rows.Clear();
        }
        else if (sessionDate is DateOnly date)
        {
            uint? clickHouseDate = ToClickHouseDate(date);
            for (int i = 0; i < rows.Count; i++)
            {
                RealtimeOrderbookRow row = rows[i];
                row.TradeSessionDate = clickHouseDate;
                rows[i] = row;
            }
        }

        return new RealtimeOrderbookPage
        {
            Rows = rows,
            RowFailures = failures,
            SourceRowsCount = sourceRowsCount,
            FirstTime = firstTime,
        };
    }

    public static RealtimeCandlesPageSummary ParseCandles(
        ReadOnlySpan<byte> body,
        string secid,
        ProtobufBatchWriter<CandleRow> writer,
        DateTime now,
        DateTime? lastBegin)
    {
        List<RealtimeRowFailure> failures = new();
        int sourceRowsCount = 0;
        int acceptedRowsCount = 0;
        DateTime? maxAcceptedBegin = null;
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
                foundColumns = true;
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, MoexRealtimeRegistry.CandleSourceColumns, "candles");
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
                ReadCandleRows(
                    ref reader,
                    failures,
                    writer,
                    secid,
                    now,
                    lastBegin,
                    ref sourceRowsCount,
                    ref acceptedRowsCount,
                    ref maxAcceptedBegin);
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, "candles");
        return new RealtimeCandlesPageSummary
        {
            RowFailures = failures,
            SourceRowsCount = sourceRowsCount,
            AcceptedRowsCount = acceptedRowsCount,
            MaxAcceptedBegin = maxAcceptedBegin,
        };
    }

    private static void ReadStockTradesTable(
        ref Utf8JsonReader reader,
        List<RealtimeStockTradeRow> rows,
        List<RealtimeRowFailure> failures,
        string secid,
        ref int sourceRowsCount,
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
                foundColumns = true;
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, MoexRealtimeRegistry.TradesStockSourceColumns, "trades");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[trades] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                ReadStockTradeRows(
                    ref reader, rows, failures, secid, ref sourceRowsCount,
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
        List<RealtimeFuturesTradeRow> rows,
        List<RealtimeRowFailure> failures,
        string secid,
        ref int sourceRowsCount,
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
                foundColumns = true;
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, MoexRealtimeRegistry.TradesFuturesSourceColumns, "trades");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[trades] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                ReadFuturesTradeRows(
                    ref reader, rows, failures, secid, ref sourceRowsCount,
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
        List<RealtimeOrderbookRow> rows,
        List<RealtimeRowFailure> failures,
        string secid,
        ref int sourceRowsCount,
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
                foundColumns = true;
                ParseHelpersUtf8.ValidateSourceColumnsUtf8(
                    ref reader, MoexRealtimeRegistry.OrderbookSourceColumns, "orderbook");
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        "[orderbook] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                ReadOrderbookRows(
                    ref reader, rows, failures, secid, ref sourceRowsCount, ref firstTime);
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
        List<RealtimeStockTradeRow> rows,
        List<RealtimeRowFailure> failures,
        string secid,
        ref int sourceRowsCount,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", "trades");
        int rowIndex = 0;
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
            for (int position = 0; position < 14; position++)
            {
                ReadCell(ref reader, rowIndex, position, 14, "trades");
                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                try
                {
                    switch (position)
                    {
                        case 0: tradeNo = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "trades"); break;
                        case 1: timeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(ref reader, rowIndex, position, "trades", out timeParts.RawTime); break;
                        case 2: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, "trades"); break;
                        case 3: row.Price = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "trades"); break;
                        case 4: row.Quantity = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "trades"); break;
                        case 5: row.Period = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "trades"); break;
                        case 6: row.Value = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "trades"); break;
                        case 7: row.BoardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "trades"); break;
                        case 8: row.Systime = ReadUnixTime(ref reader, rowIndex, position, "trades"); break;
                        case 9: row.BuySell = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "trades"); break;
                        case 10: row.Decimals = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, "trades"); break;
                        case 11: row.TradingSession = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "trades"); break;
                        case 12: timeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(ref reader, rowIndex, position, "trades", out timeParts.RawDate); break;
                        case 13: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, "trades"); break;
                    }
                }
                catch (Exception ex) when (IsRowValueFailure(ex))
                {
                    rowFailure ??= ex;
                }
            }
            FinishRow(ref reader, rowIndex, 14, "trades");

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
                ulong sourceTimeValue = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                row.SourceTime = sourceTimeValue;
                rows.Add(row);
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
                failures.Add(new RealtimeRowFailure(rowIndex, rowFailure.Message));
            }
            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static void ReadFuturesTradeRows(
        ref Utf8JsonReader reader,
        List<RealtimeFuturesTradeRow> rows,
        List<RealtimeRowFailure> failures,
        string secid,
        ref int sourceRowsCount,
        ref DateTime lastTime,
        ref long? lastTradeNo,
        ref long? maxTradeNo,
        ref DateTime? maxTradeNoTime,
        ref Exception? maxTradeNoTimeFailure)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", "trades");
        int rowIndex = 0;
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
            for (int position = 0; position < 13; position++)
            {
                ReadCell(ref reader, rowIndex, position, 13, "trades");
                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                try
                {
                    switch (position)
                    {
                        case 0: tradeNo = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "trades"); break;
                        case 1: row.BoardName = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "trades"); break;
                        case 2: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, "trades"); break;
                        case 3: timeParts.Date = ParseHelpersUtf8.ReadMomentDateUtf8(ref reader, rowIndex, position, "trades", out timeParts.RawDate); break;
                        case 4: timeParts.Time = ParseHelpersUtf8.ReadMomentTimeUtf8(ref reader, rowIndex, position, "trades", out timeParts.RawTime); break;
                        case 5: row.Price = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "trades"); break;
                        case 6: row.Quantity = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "trades"); break;
                        case 7: row.Systime = ReadUnixTime(ref reader, rowIndex, position, "trades"); break;
                        case 8: row.RecNo = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "trades"); break;
                        case 9: row.OpenPosition = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "trades"); break;
                        case 10: row.OffMarketDeal = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, "trades"); break;
                        case 11: row.BuySell = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "trades"); break;
                        case 12: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, "trades"); break;
                    }
                }
                catch (Exception ex) when (IsRowValueFailure(ex))
                {
                    rowFailure ??= ex;
                }
            }
            FinishRow(ref reader, rowIndex, 13, "trades");

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
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                rows.Add(row);
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
                failures.Add(new RealtimeRowFailure(rowIndex, rowFailure.Message));
            }
            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static void ReadOrderbookRows(
        ref Utf8JsonReader reader,
        List<RealtimeOrderbookRow> rows,
        List<RealtimeRowFailure> failures,
        string secid,
        ref int sourceRowsCount,
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
            for (int position = 0; position < 8; position++)
            {
                ReadCell(ref reader, rowIndex, position, 8, "orderbook");
                if (reader.TokenType == JsonTokenType.Null)
                    continue;

                try
                {
                    switch (position)
                    {
                        case 0: row.BoardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "orderbook"); break;
                        case 1: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, "orderbook"); break;
                        case 2: buySell = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, "orderbook"); break;
                        case 3: price = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "orderbook"); break;
                        case 4: row.Quantity = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "orderbook"); break;
                        case 5: seqNum = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "orderbook"); break;
                        case 6: ParseHelpersUtf8.ExpectString(ref reader, rowIndex, position, "orderbook"); break;
                        case 7: row.Decimals = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, position, "orderbook"); break;
                    }
                }
                catch (Exception ex) when (IsRowValueFailure(ex))
                {
                    rowFailure ??= ex;
                }
            }
            FinishRow(ref reader, rowIndex, 8, "orderbook");

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
                row.SourceTime = CandleDateTime64.ToUnixWholeSeconds(sourceTime!.Value);
                row.BuySell = buySell!;
                row.Price = price!.Value;
                if (rows.Count == 0)
                    firstTime = sourceTime.Value;
                rows.Add(row);
            }
            else
            {
                failures.Add(new RealtimeRowFailure(rowIndex, rowFailure.Message));
            }
            sourceRowsCount++;
            rowIndex++;
        }
    }

    private static void ReadCandleRows(
        ref Utf8JsonReader reader,
        List<RealtimeRowFailure> failures,
        ProtobufBatchWriter<CandleRow> writer,
        string secid,
        DateTime now,
        DateTime? lastBegin,
        ref int sourceRowsCount,
        ref int acceptedRowsCount,
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
            Exception? rowFailure = null;
            for (int position = 0; position < 8; position++)
            {
                ReadCell(ref reader, rowIndex, position, 8, "candles");
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                try
                {
                    switch (position)
                    {
                        case 0: row.Open = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "candles"); break;
                        case 1: row.Close = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "candles"); break;
                        case 2: row.High = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "candles"); break;
                        case 3: row.Low = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "candles"); break;
                        case 4: row.Value = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "candles"); break;
                        case 5: row.Volume = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, position, "candles"); break;
                        case 6: begin = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, "candles"); break;
                        case 7: end = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, "candles"); break;
                    }
                }
                catch (Exception ex) when (IsRowValueFailure(ex))
                {
                    rowFailure ??= ex;
                }
            }
            FinishRow(ref reader, rowIndex, 8, "candles");

            if (rowFailure is null && begin is null)
                rowFailure = new InvalidOperationException(
                    "Свеча отвергнута: начало пусто или неверно.");

            if (rowFailure is not null)
            {
                failures.Add(new RealtimeRowFailure(sourceRowsCount, rowFailure.Message));
            }
            else
            {
                DateTime beginValue = begin!.Value;
                if (beginValue <= now && (lastBegin is null || beginValue >= lastBegin.Value))
                {
                    row.Begin = CandleDateTime64.ToUnixWholeSeconds(beginValue);
                    row.End = end is DateTime endValue
                        ? CandleDateTime64.ToUnixWholeSeconds(endValue)
                        : null;
                    writer.Append(in row);
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
        ColumnAndNumbersForParsing.ExpectedSchema schema,
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
                foundColumns = true;
                ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
            }
            else if (reader.ValueTextEquals("data"u8))
            {
                if (!foundColumns)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. " +
                        "Порядок columns → data обязателен.");
                }
                foundData = true;
                int currentRowCount = 0;
                sessionDate = ReadSessionDateData(ref reader, schema, ref currentRowCount);
                rowCount = currentRowCount;
            }
            else
            {
                reader.Skip();
            }
        }

        ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
        if (rowCount == 0)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{schema.RootKey}] Блок dataversion не содержит ни одной строки.");
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
        ColumnAndNumbersForParsing.ExpectedSchema schema,
        ref int rowCount)
    {
        ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);
        string? sessionDate = null;
        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
        {
            if (rowCount > 0)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Ожидалась ровно 1 строка в dataversion, но найдено больше.");
            }
            for (int position = 0; position < schema.TotalColumns; position++)
            {
                if (!reader.Read())
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowCount}, позиция {position}.");
                }
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Короткая строка данных: ожидалось {schema.TotalColumns} колонок, " +
                        $"получено {position} (строка {rowCount}).");
                }
                if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject or JsonTokenType.EndObject)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{schema.RootKey}] Недопустимая структура значения в строке {rowCount}, колонка {position}.");
                }
                if (reader.TokenType == JsonTokenType.Null)
                    continue;
                switch (position)
                {
                    case 0: _ = ParseHelpersUtf8.ReadInt(ref reader, rowCount, position, schema.RootKey); break;
                    case 1: _ = ParseHelpersUtf8.ReadLong(ref reader, rowCount, position, schema.RootKey); break;
                    case 2: ParseHelpersUtf8.ExpectString(ref reader, rowCount, position, schema.RootKey); break;
                    case 3: sessionDate = ParseHelpersUtf8.ReadString(ref reader, rowCount, position, schema.RootKey); break;
                }
            }
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
            {
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowCount}).");
            }
            rowCount++;
        }
        return sessionDate;
    }

    private static ulong? ReadUnixTime(
        ref Utf8JsonReader reader,
        int rowIndex,
        int position,
        string rootKey)
    {
        DateTime? value = ParseHelpersUtf8.ReadDateTimeUtf8(
            ref reader, rowIndex, position, rootKey);
        return value is DateTime time ? CandleDateTime64.ToUnixWholeSeconds(time) : null;
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

    private static void ReadCell(
        ref Utf8JsonReader reader,
        int rowIndex,
        int position,
        int columnCount,
        string rootKey)
    {
        if (!reader.Read())
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{rootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {position}.");
        }
        if (reader.TokenType == JsonTokenType.EndArray)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{rootKey}] Короткая строка данных: ожидалось {columnCount} колонок, " +
                $"получено {position} (строка {rowIndex}).");
        }
        if (reader.TokenType is JsonTokenType.StartArray or JsonTokenType.StartObject or JsonTokenType.EndObject)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{rootKey}] Недопустимая структура значения в строке {rowIndex}, колонка {position}.");
        }
    }

    private static void FinishRow(
        ref Utf8JsonReader reader,
        int rowIndex,
        int columnCount,
        string rootKey)
    {
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
        {
            ParseHelpersUtf8.SchemaMismatch(
                $"[{rootKey}] Ожидался EndArray после {columnCount} колонок (строка {rowIndex}).");
        }
    }

    private static bool IsRowValueFailure(Exception exception)
        => exception is InvalidOperationException or FormatException or OverflowException;
}
