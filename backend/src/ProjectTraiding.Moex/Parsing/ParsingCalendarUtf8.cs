using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingCalendarUtf8
    {
        public static (List<CalendarStockSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types)
            ParseStockSession(ReadOnlySpan<byte> jsonBytes)
        {
            List<CalendarStockSessionDTO> sessions = new List<CalendarStockSessionDTO>();
            ColumnAndNumbersForParsing.ExpectedSchema sessionSchema =
                ColumnAndNumbersForParsing.CalendarStockSessionSchema;
            Utf8JsonReader sessionReader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref sessionReader, sessionSchema.RootKey);
            ReadStockSessionBlock(ref sessionReader, sessions, sessionSchema);

            List<CalendarSessionTypeDTO> types = new List<CalendarSessionTypeDTO>();
            ColumnAndNumbersForParsing.ExpectedSchema typeSchema =
                ColumnAndNumbersForParsing.CalendarSessionTypesSchema;
            Utf8JsonReader typeReader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref typeReader, typeSchema.RootKey);
            ReadSessionTypesBlock(ref typeReader, types, typeSchema);

            return (sessions, types);
        }

        public static (List<CalendarFuturesSessionDTO> Sessions, List<CalendarSessionTypeDTO> Types)
            ParseFuturesSession(ReadOnlySpan<byte> jsonBytes)
        {
            List<CalendarFuturesSessionDTO> sessions = new List<CalendarFuturesSessionDTO>();
            ColumnAndNumbersForParsing.ExpectedSchema sessionSchema =
                ColumnAndNumbersForParsing.CalendarFuturesSessionSchema;
            Utf8JsonReader sessionReader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref sessionReader, sessionSchema.RootKey);
            ReadFuturesSessionBlock(ref sessionReader, sessions, sessionSchema);

            List<CalendarSessionTypeDTO> types = new List<CalendarSessionTypeDTO>();
            ColumnAndNumbersForParsing.ExpectedSchema typeSchema =
                ColumnAndNumbersForParsing.CalendarSessionTypesSchema;
            Utf8JsonReader typeReader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref typeReader, typeSchema.RootKey);
            ReadSessionTypesBlock(ref typeReader, types, typeSchema);

            return (sessions, types);
        }

        public static (List<CalendarFortsContractDTO> Forts, List<CalendarOptionsSeriesDTO> Options)
            ParseFuturesSecurities(ReadOnlySpan<byte> jsonBytes)
        {
            List<CalendarFortsContractDTO> forts = new List<CalendarFortsContractDTO>();
            ColumnAndNumbersForParsing.ExpectedSchema fortsSchema =
                ColumnAndNumbersForParsing.CalendarFortsContractsSchema;
            Utf8JsonReader fortsReader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref fortsReader, fortsSchema.RootKey);
            ReadFortsBlock(ref fortsReader, forts, fortsSchema);

            List<CalendarOptionsSeriesDTO> options = new List<CalendarOptionsSeriesDTO>();
            ColumnAndNumbersForParsing.ExpectedSchema optionsSchema =
                ColumnAndNumbersForParsing.CalendarOptionsSeriesSchema;
            Utf8JsonReader optionsReader = new Utf8JsonReader(jsonBytes);
            ParseHelpersUtf8.SkipToRootObject(ref optionsReader, optionsSchema.RootKey);
            ReadOptionsBlock(ref optionsReader, options, optionsSchema);

            return (forts, options);
        }

        private static void ReadStockSessionBlock(
            ref Utf8JsonReader reader,
            List<CalendarStockSessionDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'.");
                    foundData = true;
                    ReadStockSessionData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }
            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
        }

        private static void ReadFuturesSessionBlock(
            ref Utf8JsonReader reader,
            List<CalendarFuturesSessionDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'.");
                    foundData = true;
                    ReadFuturesSessionData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }
            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
        }

        private static void ReadSessionTypesBlock(
            ref Utf8JsonReader reader,
            List<CalendarSessionTypeDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'.");
                    foundData = true;
                    ReadSessionTypesData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }
            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
        }

        private static void ReadFortsBlock(
            ref Utf8JsonReader reader,
            List<CalendarFortsContractDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'.");
                    foundData = true;
                    ReadFortsData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }
            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
        }

        private static void ReadOptionsBlock(
            ref Utf8JsonReader reader,
            List<CalendarOptionsSeriesDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
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
                    ParseHelpersUtf8.ValidateColumnsUtf8(ref reader, schema);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'.");
                    foundData = true;
                    ReadOptionsData(ref reader, list, schema);
                }
                else
                {
                    reader.Skip();
                }
            }
            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
        }

        private static void ReadStockSessionData(
            ref Utf8JsonReader reader,
            List<CalendarStockSessionDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? tradeDate = null;
                int? tradingSession = null;
                string? boardId = null;
                string? secId = null;
                string? type = null;
                string? timeFrom = null;
                string? timeTill = null;
                DateTime? updateTime = null;
                for (int position = 0; position < schema.TotalColumns; position++)
                {
                    ReadRowValue(ref reader, schema, rowIndex, position);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    switch (position)
                    {
                        case 0: tradeDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 1: tradingSession = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, schema.RootKey); break;
                        case 2: boardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 3: secId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 4: type = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 5: timeFrom = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 6: timeTill = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 7: updateTime = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, schema.RootKey); break;
                    }
                }
                ExpectRowEnd(ref reader, schema, rowIndex);
                list.Add(new CalendarStockSessionDTO
                {
                    TradeDate = tradeDate,
                    TradingSession = tradingSession,
                    BoardId = boardId,
                    SecId = secId,
                    Type = type,
                    TimeFrom = timeFrom,
                    TimeTill = timeTill,
                    UpdateTime = updateTime,
                });
                rowIndex++;
            }
        }

        private static void ReadFuturesSessionData(
            ref Utf8JsonReader reader,
            List<CalendarFuturesSessionDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? tradeSessionDate = null;
                string? boardId = null;
                string? secId = null;
                string? type = null;
                DateTime? timeFrom = null;
                DateTime? timeTill = null;
                DateTime? updateTime = null;
                for (int position = 0; position < schema.TotalColumns; position++)
                {
                    ReadRowValue(ref reader, schema, rowIndex, position);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    switch (position)
                    {
                        case 0: tradeSessionDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 1: boardId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 2: secId = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 3: type = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey); break;
                        case 4: timeFrom = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, schema.RootKey); break;
                        case 5: timeTill = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, schema.RootKey); break;
                        case 6: updateTime = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, position, schema.RootKey); break;
                    }
                }
                ExpectRowEnd(ref reader, schema, rowIndex);
                list.Add(new CalendarFuturesSessionDTO
                {
                    TradeSessionDate = tradeSessionDate,
                    BoardId = boardId,
                    SecId = secId,
                    Type = type,
                    TimeFrom = timeFrom,
                    TimeTill = timeTill,
                    UpdateTime = updateTime,
                });
                rowIndex++;
            }
        }

        private static void ReadSessionTypesData(
            ref Utf8JsonReader reader,
            List<CalendarSessionTypeDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? type = null;
                string? title = null;
                for (int position = 0; position < schema.TotalColumns; position++)
                {
                    ReadRowValue(ref reader, schema, rowIndex, position);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    if (position == 0)
                        type = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey);
                    else
                        title = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey);
                }
                ExpectRowEnd(ref reader, schema, rowIndex);
                list.Add(new CalendarSessionTypeDTO { Type = type, Title = title });
                rowIndex++;
            }
        }

        private static void ReadFortsData(
            ref Utf8JsonReader reader,
            List<CalendarFortsContractDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string?[] values = new string?[9];
                int? weekendSession = null;
                for (int position = 0; position < schema.TotalColumns; position++)
                {
                    ReadRowValue(ref reader, schema, rowIndex, position);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    if (position < 9)
                        values[position] = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey);
                    else
                        weekendSession = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, schema.RootKey);
                }
                ExpectRowEnd(ref reader, schema, rowIndex);
                list.Add(new CalendarFortsContractDTO
                {
                    SecId = values[0],
                    AssetCode = values[1],
                    ShortName = values[2],
                    ExecType = values[3],
                    ContractName = values[4],
                    ExpirationDate = values[5],
                    EndDate = values[6],
                    ExpirationType = values[7],
                    ExpirationTime = values[8],
                    WeekendSession = weekendSession,
                });
                rowIndex++;
            }
        }

        private static void ReadOptionsData(
            ref Utf8JsonReader reader,
            List<CalendarOptionsSeriesDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);
            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string?[] values = new string?[10];
                int? weekendSession = null;
                for (int position = 0; position < schema.TotalColumns; position++)
                {
                    ReadRowValue(ref reader, schema, rowIndex, position);
                    if (reader.TokenType == JsonTokenType.Null)
                        continue;
                    if (position < 10)
                        values[position] = ParseHelpersUtf8.ReadString(ref reader, rowIndex, position, schema.RootKey);
                    else
                        weekendSession = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, position, schema.RootKey);
                }
                ExpectRowEnd(ref reader, schema, rowIndex);
                list.Add(new CalendarOptionsSeriesDTO
                {
                    AssetTypeName = values[0],
                    AssetCode = values[1],
                    SeriesName = values[2],
                    SeriesType = values[3],
                    ExecType = values[4],
                    MarginStyle = values[5],
                    ContractName = values[6],
                    ExpirationDate = values[7],
                    ExpirationType = values[8],
                    ExpirationTime = values[9],
                    WeekendSession = weekendSession,
                });
                rowIndex++;
            }
        }

        private static void ReadRowValue(
            ref Utf8JsonReader reader,
            ColumnAndNumbersForParsing.ExpectedSchema schema,
            int rowIndex,
            int position)
        {
            if (!reader.Read())
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {position}.");
            if (reader.TokenType == JsonTokenType.EndArray)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Короткая строка данных: ожидалось {schema.TotalColumns} колонок, " +
                    $"получено {position} (строка {rowIndex}).");
        }

        private static void ExpectRowEnd(
            ref Utf8JsonReader reader,
            ColumnAndNumbersForParsing.ExpectedSchema schema,
            int rowIndex)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок " +
                    $"(строка {rowIndex}).");
        }

        // ═══════════════════════════════════════════════════════════
        // ParseOffDaysMarket — один проход, одна таблица
        // Endpoint: GET /iss/calendars/stock.json или /iss/calendars/futures.json
        // Таблица: off_days (5 колонок)
        // ═══════════════════════════════════════════════════════════

        public static List<CalendarOffDaysMarketDTO> ParseOffDaysMarket(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.CalendarOffDaysMarketSchema;
            var list = new List<CalendarOffDaysMarketDTO>();
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
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    ReadOffDaysMarketData(ref reader, list, schema);
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
        // Чтение строк off_days (market) — 5 колонок
        // ═══════════════════════════════════════════════════════════

        private static void ReadOffDaysMarketData(
            ref Utf8JsonReader reader,
            List<CalendarOffDaysMarketDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? tradeDate = null;
                int? isTraded = null;
                string? tradeSessionDate = null;
                string? reason = null;
                DateTime? updateTime = null;

                {
                    int expectedIdx = 0;
                    // 0=tradedate 1=is_traded 2=trade_session_date 3=reason 4=updatetime
                    for (int pos = 0; pos < schema.TotalColumns; pos++)
                    {
                        if (!reader.Read())
                            ParseHelpersUtf8.SchemaMismatch(
                                $"[{schema.RootKey}] Неожиданный конец JSON в строке {rowIndex}, позиция {pos}.");

                        if (reader.TokenType == JsonTokenType.EndArray)
                            ParseHelpersUtf8.SchemaMismatch(
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
                                    case 0: tradeDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 1: isTraded = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 2: tradeSessionDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 3: reason = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 4: updateTime = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new CalendarOffDaysMarketDTO
                {
                    TradeDate = tradeDate,
                    IsTraded = isTraded,
                    TradeSessionDate = tradeSessionDate,
                    Reason = reason,
                    UpdateTime = updateTime,
                });

                rowIndex++;
            }
        }
    }
}
