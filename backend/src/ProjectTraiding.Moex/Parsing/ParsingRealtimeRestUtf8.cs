using ProjectTraiding.Moex.Contracts.Dto.Realtime;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Парсер real-time REST-ответов MOEX (orderbook, trades, dataversion, trades_yields).
    /// 
    /// Паттерн: мульти-проход по одному JSON — для каждого root-блока
    /// создаётся отдельный Utf8JsonReader с собственной ExpectedSchema.
    /// Аналогично ParsingCalendarUtf8.ParseSecurityChangesWithAttributes.
    /// 
    /// Используемые хелперы из ParseHelpersUtf8:
    ///   SkipToRootObject, ValidateColumnsUtf8, ReadDataRow,
    ///   ReadString, ReadDouble, ReadLong, ReadInt,
    ///   ValidateStructure, SchemaMismatch.
    /// 
    /// Candles today парсятся существующим ParsingAlgUtf8.ParseAlgCandles —
    /// здесь для них метода нет.
    /// </summary>
    public static class ParsingRealtimeRestUtf8
    {
        // ═══════════════════════════════════════════════════════════
        // ParseOrderbook — orderbook + dataversion (2 прохода)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Парсит ответ orderbook endpoint-а MOEX real-time REST.
        /// 
        /// JSON содержит два root-блока:
        ///   "orderbook"   → строки стакана (8 колонок);
        ///   "dataversion"  → версия данных (4 колонки, ровно 1 строка).
        /// 
        /// Каждый блок парсится отдельным проходом по тем же байтам.
        /// </summary>
        public static RealtimeOrderbookParseResult ParseOrderbook(ReadOnlySpan<byte> jsonBytes)
        {
            // Проход 1: orderbook
            var rows = ParseOrderbookBlock(jsonBytes);

            // Проход 2: dataversion
            var dataVersion = ParseDataVersion(jsonBytes);

            return new RealtimeOrderbookParseResult(rows, dataVersion);
        }

        // ═══════════════════════════════════════════════════════════
        // ParseTradesStock — trades(15) + dataversion + trades_yields (3 прохода)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Парсит ответ trades endpoint-а MOEX real-time REST для акций.
        /// 
        /// JSON содержит три root-блока:
        ///   "trades"        → строки сделок (15 колонок);
        ///   "dataversion"   → версия данных (4 колонки, ровно 1 строка);
        ///   "trades_yields" → блок доходности (2 колонки, может быть пустым).
        /// 
        /// Каждый блок парсится отдельным проходом.
        /// </summary>
        public static RealtimeTradesParseResult<RealtimeTradesStockDTO> ParseTradesStock(ReadOnlySpan<byte> jsonBytes)
        {
            // Проход 1: trades (stock schema, 15 колонок)
            var rows = ParseTradesStockBlock(jsonBytes);

            // Проход 2: dataversion
            var dataVersion = ParseDataVersion(jsonBytes);

            // Проход 3: trades_yields
            var yields = ParseTradesYields(jsonBytes);

            return new RealtimeTradesParseResult<RealtimeTradesStockDTO>(rows, dataVersion, yields);
        }

        // ═══════════════════════════════════════════════════════════
        // ParseTradesFutures — trades(13) + dataversion + trades_yields (3 прохода)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Парсит ответ trades endpoint-а MOEX real-time REST для фьючерсов.
        /// 
        /// JSON содержит три root-блока:
        ///   "trades"        → строки сделок (13 колонок);
        ///   "dataversion"   → версия данных (4 колонки, ровно 1 строка);
        ///   "trades_yields" → блок доходности (2 колонки, может быть пустым).
        /// 
        /// Каждый блок парсится отдельным проходом.
        /// </summary>
        public static RealtimeTradesParseResult<RealtimeTradesFuturesDTO> ParseTradesFutures(ReadOnlySpan<byte> jsonBytes)
        {
            // Проход 1: trades (futures schema, 13 колонок)
            var rows = ParseTradesFuturesBlock(jsonBytes);

            // Проход 2: dataversion
            var dataVersion = ParseDataVersion(jsonBytes);

            // Проход 3: trades_yields
            var yields = ParseTradesYields(jsonBytes);

            return new RealtimeTradesParseResult<RealtimeTradesFuturesDTO>(rows, dataVersion, yields);
        }

        // ═══════════════════════════════════════════════════════════
        // ParseDataVersion — публичный, один проход
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Парсит блок "dataversion" из JSON ответа MOEX real-time REST.
        /// 
        /// Блок содержит ровно одну строку.
        /// 0 строк или более 1 строки — MoexSchemaMismatchException.
        /// 
        /// Публичный для использования в debug endpoints.
        /// </summary>
        public static RealtimeDataVersionDTO ParseDataVersion(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.RealtimeDataVersionSchema;
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

            bool foundColumns = false;
            bool foundData = false;
            RealtimeDataVersionDTO? result = null;

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
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    result = ReadDataVersionData(ref reader, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);

            if (result is null)
                ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                    $"[{schema.RootKey}] Блок dataversion не содержит ни одной строки.");

            return result;
        }

        // ═══════════════════════════════════════════════════════════
        // ParseTradesYields — публичный, один проход
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Парсит блок "trades_yields" из JSON ответа MOEX real-time REST.
        /// 
        /// В текущих raw samples data[] пустой — это штатно.
        /// Если MOEX начнёт наполнять data — парсер прочитает строки.
        /// 
        /// Публичный для использования в debug endpoints.
        /// </summary>
        public static List<RealtimeTradesYieldsDTO> ParseTradesYields(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.RealtimeTradesYieldsSchema;
            var list = new List<RealtimeTradesYieldsDTO>();
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    ReadTradesYieldsData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            return list;
        }

        // ═══════════════════════════════════════════════════════════
        // Приватные блочные парсеры
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Проход по блоку "orderbook" — строки стакана (8 колонок).
        /// </summary>
        private static List<RealtimeOrderbookRowDTO> ParseOrderbookBlock(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.RealtimeOrderbookSchema;
            var list = new List<RealtimeOrderbookRowDTO>();
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    ReadOrderbookData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            return list;
        }

        /// <summary>
        /// Проход по блоку "trades" с stock schema (15 колонок).
        /// </summary>
        private static List<RealtimeTradesStockDTO> ParseTradesStockBlock(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.RealtimeTradesStockSchema;
            var list = new List<RealtimeTradesStockDTO>();
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    ReadTradesStockData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            return list;
        }

        /// <summary>
        /// Проход по блоку "trades" с futures schema (13 колонок).
        /// </summary>
        private static List<RealtimeTradesFuturesDTO> ParseTradesFuturesBlock(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.RealtimeTradesFuturesSchema;
            var list = new List<RealtimeTradesFuturesDTO>();
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    ReadTradesFuturesData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            return list;
        }

        // ═══════════════════════════════════════════════════════════
        // ReadOrderbookData — 8 колонок
        // ═══════════════════════════════════════════════════════════

        private static void ReadOrderbookData(
            ref Utf8JsonReader reader,
            List<RealtimeOrderbookRowDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? boardId = null, secId = null, buySell = null, updateTime = null;
                double? price = null;
                long? quantity = null, seqNum = null, decimals = null;

                {
                    int expectedIdx = 0;
                    // 0=BOARDID 1=SECID 2=BUYSELL 3=PRICE 4=QUANTITY 5=SEQNUM 6=UPDATETIME 7=DECIMALS
                    for (int pos = 0; pos < schema.TotalColumns; pos++)
                    {
                        if (!reader.Read())
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {pos}.");

                        if (reader.TokenType == JsonTokenType.EndArray)
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Короткая строка данных: " +
                                $"ожидалось {schema.TotalColumns} колонок, получено {pos} " +
                                $"(строка {rowIndex}).");

                        if (expectedIdx < schema.Columns.Length
                            && pos == schema.Columns[expectedIdx].SourceIndex)
                        {
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                switch (expectedIdx)
                                {
                                    case 0: boardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 1: secId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 2: buySell = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 3: price = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 4: quantity = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 5: seqNum = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 6: updateTime = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 7: decimals = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new RealtimeOrderbookRowDTO
                {
                    BoardId    = boardId,
                    SecId      = secId,
                    BuySell    = buySell,
                    Price      = price,
                    Quantity   = quantity,
                    SeqNum     = seqNum,
                    UpdateTime = updateTime,
                    Decimals   = decimals,
                });

                rowIndex++;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ReadDataVersionData — 4 колонки, ровно 1 строка
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Читает блок data[] внутри "dataversion".
        /// 
        /// Ожидает ровно 1 строку.
        /// 0 строк → возвращает null (вызывающий код бросает SchemaMismatch).
        /// Более 1 строки → SchemaMismatch.
        /// </summary>
        private static RealtimeDataVersionDTO? ReadDataVersionData(
            ref Utf8JsonReader reader,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int? dataVersion = null;
            long? seqNum = null;
            string? tradeDate = null, tradeSessionDate = null;

            int rowCount = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (rowCount > 0)
                    ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                        $"[{schema.RootKey}] Ожидалась ровно 1 строка в dataversion, но найдено больше.");

                {
                    int expectedIdx = 0;
                    // 0=data_version 1=seqnum 2=trade_date 3=trade_session_date
                    for (int pos = 0; pos < schema.TotalColumns; pos++)
                    {
                        if (!reader.Read())
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowCount}, позиция {pos}.");

                        if (reader.TokenType == JsonTokenType.EndArray)
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Короткая строка данных: " +
                                $"ожидалось {schema.TotalColumns} колонок, получено {pos} " +
                                $"(строка {rowCount}).");

                        if (expectedIdx < schema.Columns.Length
                            && pos == schema.Columns[expectedIdx].SourceIndex)
                        {
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                switch (expectedIdx)
                                {
                                    case 0: dataVersion = ParseHelpersUtf8.ReadInt(ref reader, rowCount, expectedIdx, schema.RootKey); break;
                                    case 1: seqNum = ParseHelpersUtf8.ReadLong(ref reader, rowCount, expectedIdx, schema.RootKey); break;
                                    case 2: tradeDate = ParseHelpersUtf8.ReadString(ref reader, rowCount, expectedIdx, schema.RootKey); break;
                                    case 3: tradeSessionDate = ParseHelpersUtf8.ReadString(ref reader, rowCount, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowCount}).");
                }

                rowCount++;
            }

            if (rowCount == 0)
                return null;

            return new RealtimeDataVersionDTO
            {
                DataVersion      = dataVersion,
                SeqNum           = seqNum,
                TradeDate        = tradeDate,
                TradeSessionDate = tradeSessionDate,
            };
        }

        // ═══════════════════════════════════════════════════════════
        // ReadTradesStockData — 15 колонок
        // ═══════════════════════════════════════════════════════════

        private static void ReadTradesStockData(
            ref Utf8JsonReader reader,
            List<RealtimeTradesStockDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                long? tradeNo = null;
                string? tradeTime = null, boardId = null, secId = null;
                double? price = null, value = null;
                long? quantity = null;
                string? period = null;
                int? tradeTimeGrp = null;
                string? sysTime = null, buySell = null;
                int? decimals = null;
                string? tradingSession = null, tradeDate = null, tradeSessionDate = null;

                {
                    int expectedIdx = 0;
                    // 0=TRADENO 1=TRADETIME 2=BOARDID 3=SECID 4=PRICE 5=QUANTITY 6=VALUE 7=PERIOD 8=TRADETIME_GRP 9=SYSTIME 10=BUYSELL 11=DECIMALS 12=TRADINGSESSION 13=TRADEDATE 14=TRADE_SESSION_DATE
                    for (int pos = 0; pos < schema.TotalColumns; pos++)
                    {
                        if (!reader.Read())
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {pos}.");

                        if (reader.TokenType == JsonTokenType.EndArray)
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Короткая строка данных: " +
                                $"ожидалось {schema.TotalColumns} колонок, получено {pos} " +
                                $"(строка {rowIndex}).");

                        if (expectedIdx < schema.Columns.Length
                            && pos == schema.Columns[expectedIdx].SourceIndex)
                        {
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                switch (expectedIdx)
                                {
                                    case 0: tradeNo = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 1: tradeTime = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 2: boardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 3: secId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 4: price = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 5: quantity = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 6: value = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 7: period = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 8: tradeTimeGrp = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 9: sysTime = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 10: buySell = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 11: decimals = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 12: tradingSession = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 13: tradeDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 14: tradeSessionDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new RealtimeTradesStockDTO
                {
                    TradeNo          = tradeNo,
                    TradeTime        = tradeTime,
                    BoardId          = boardId,
                    SecId            = secId,
                    Price            = price,
                    Quantity         = quantity,
                    Value            = value,
                    Period           = period,
                    TradeTimeGrp     = tradeTimeGrp,
                    SysTime          = sysTime,
                    BuySell          = buySell,
                    Decimals         = decimals,
                    TradingSession   = tradingSession,
                    TradeDate        = tradeDate,
                    TradeSessionDate = tradeSessionDate,
                });

                rowIndex++;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ReadTradesFuturesData — 13 колонок
        // ═══════════════════════════════════════════════════════════

        private static void ReadTradesFuturesData(
            ref Utf8JsonReader reader,
            List<RealtimeTradesFuturesDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                long? tradeNo = null;
                string? boardName = null, secId = null, tradeDate = null, tradeTime = null;
                double? price = null;
                long? quantity = null;
                string? sysTime = null;
                long? recNo = null, openPosition = null;
                int? offMarketDeal = null;
                string? buySell = null, tradeSessionDate = null;

                {
                    int expectedIdx = 0;
                    // 0=TRADENO 1=BOARDNAME 2=SECID 3=TRADEDATE 4=TRADETIME 5=PRICE 6=QUANTITY 7=SYSTIME 8=RECNO 9=OPENPOSITION 10=OFFMARKETDEAL 11=BUYSELL 12=TRADE_SESSION_DATE
                    for (int pos = 0; pos < schema.TotalColumns; pos++)
                    {
                        if (!reader.Read())
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {pos}.");

                        if (reader.TokenType == JsonTokenType.EndArray)
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Короткая строка данных: " +
                                $"ожидалось {schema.TotalColumns} колонок, получено {pos} " +
                                $"(строка {rowIndex}).");

                        if (expectedIdx < schema.Columns.Length
                            && pos == schema.Columns[expectedIdx].SourceIndex)
                        {
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                switch (expectedIdx)
                                {
                                    case 0: tradeNo = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 1: boardName = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 2: secId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 3: tradeDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 4: tradeTime = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 5: price = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 6: quantity = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 7: sysTime = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 8: recNo = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 9: openPosition = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 10: offMarketDeal = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 11: buySell = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 12: tradeSessionDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new RealtimeTradesFuturesDTO
                {
                    TradeNo          = tradeNo,
                    BoardName        = boardName,
                    SecId            = secId,
                    TradeDate        = tradeDate,
                    TradeTime        = tradeTime,
                    Price            = price,
                    Quantity         = quantity,
                    SysTime          = sysTime,
                    RecNo            = recNo,
                    OpenPosition     = openPosition,
                    OffMarketDeal    = offMarketDeal,
                    BuySell          = buySell,
                    TradeSessionDate = tradeSessionDate,
                });

                rowIndex++;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // ReadTradesYieldsData — 2 колонки (data[] может быть пустым)
        // ═══════════════════════════════════════════════════════════

        private static void ReadTradesYieldsData(
            ref Utf8JsonReader reader,
            List<RealtimeTradesYieldsDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? boardId = null, secId = null;

                {
                    int expectedIdx = 0;
                    // 0=boardid 1=secid
                    for (int pos = 0; pos < schema.TotalColumns; pos++)
                    {
                        if (!reader.Read())
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {pos}.");

                        if (reader.TokenType == JsonTokenType.EndArray)
                            ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                                $"[{schema.RootKey}] Короткая строка данных: " +
                                $"ожидалось {schema.TotalColumns} колонок, получено {pos} " +
                                $"(строка {rowIndex}).");

                        if (expectedIdx < schema.Columns.Length
                            && pos == schema.Columns[expectedIdx].SourceIndex)
                        {
                            if (reader.TokenType != JsonTokenType.Null)
                            {
                                switch (expectedIdx)
                                {
                                    case 0: boardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 1: secId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new RealtimeTradesYieldsDTO
                {
                    BoardId = boardId,
                    SecId   = secId,
                });

                rowIndex++;
            }
        }
    }
}
