using ProjectTraiding.Moex.Contracts.Dto.Iss;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Разбор карточек инструментов — паспортный блок securities из списочного ответа.
    ///
    /// Акции:    /engines/stock/markets/shares/boards/tqbr/securities.json
    /// Фьючерсы: /engines/futures/markets/forts/boards/RFUD/securities.json
    ///
    /// Блоки dataversion, marketdata и marketdata_yields — пропускаются
    /// (парсер их не ищет; ценовые поля не хранятся).
    /// </summary>
    public static class ParsingInstrumentCardUtf8
    {
        // ═══════════════════════════════════════════════════════════
        // Акции — один проход: securities(27)
        // ═══════════════════════════════════════════════════════════

        public static List<StockInstrumentCardDTO> ParseStockCards(ReadOnlySpan<byte> jsonBytes)
        {
            // Проход 1 — securities
            var list = new List<StockInstrumentCardDTO>();
            {
                var schema = ColumnAndNumbersForParsing.StockCardSecuritiesSchema;
                var reader = new Utf8JsonReader(jsonBytes);

                ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

                bool foundColumns = false;
                bool foundData = false;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

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
                        ReadStockSecuritiesData(ref reader, list, schema);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            }

            return list;
        }

        // ── Чтение строк securities акций — 13 колонок из 27 ──

        private static void ReadStockSecuritiesData(
            ref Utf8JsonReader reader,
            List<StockInstrumentCardDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? secId = null, boardId = null, shortName = null;
                string? status = null, secName = null, isin = null;
                string? currency = null, secType = null;
                int? lotSize = null, decimals = null, listLevel = null;
                double? minStep = null;
                long? issueSize = null;

                {
                    int expectedIdx = 0;
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
                                    case 0:  secId     = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SECID [0]
                                    case 1:  boardId   = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // BOARDID [1]
                                    case 2:  shortName = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SHORTNAME [2]
                                    case 3:  lotSize   = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey);    break; // LOTSIZE [4]
                                    case 4:  status    = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // STATUS [6]
                                    case 5:  decimals  = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey);    break; // DECIMALS [8]
                                    case 6:  secName   = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SECNAME [9]
                                    case 7:  minStep   = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // MINSTEP [14]
                                    case 8:  issueSize = ParseHelpersUtf8.ReadLong(ref reader, rowIndex, expectedIdx, schema.RootKey);   break; // ISSUESIZE [18]
                                    case 9:  isin      = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // ISIN [19]
                                    case 10: currency  = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // CURRENCYID [23]
                                    case 11: secType   = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SECTYPE [24]
                                    case 12: listLevel = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey);    break; // LISTLEVEL [25]
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new StockInstrumentCardDTO
                {
                    SecId = secId, BoardId = boardId, ShortName = shortName,
                    LotSize = lotSize, Status = status, Decimals = decimals,
                    SecName = secName, MinStep = minStep, IssueSize = issueSize,
                    Isin = isin, Currency = currency, SecType = secType,
                    ListLevel = listLevel,
                });

                rowIndex++;
            }
        }

        // ═══════════════════════════════════════════════════════════
        // Фьючерсы — один проход: securities(26)
        // ═══════════════════════════════════════════════════════════

        public static List<FuturesInstrumentCardDTO> ParseFuturesCards(ReadOnlySpan<byte> jsonBytes)
        {
            // Проход 1 — securities
            var list = new List<FuturesInstrumentCardDTO>();
            {
                var schema = ColumnAndNumbersForParsing.FuturesCardSecuritiesSchema;
                var reader = new Utf8JsonReader(jsonBytes);

                ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

                bool foundColumns = false;
                bool foundData = false;

                while (reader.Read())
                {
                    if (reader.TokenType == JsonTokenType.EndObject) break;
                    if (reader.TokenType != JsonTokenType.PropertyName) continue;

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
                        ReadFuturesSecuritiesData(ref reader, list, schema);
                    }
                    else
                    {
                        reader.Skip();
                    }
                }

                ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            }

            return list;
        }

        // ── Чтение строк securities фьючерсов — 16 колонок из 26 ──

        private static void ReadFuturesSecuritiesData(
            ref Utf8JsonReader reader,
            List<FuturesInstrumentCardDTO> list,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                string? secId = null, boardId = null, shortName = null, secName = null;
                string? lastTradeDate = null, lastDelDate = null, secType = null, assetCode = null;
                int? decimals = null, lotVolume = null;
                double? minStep = null, initialMargin = null, highLimit = null;
                double? lowLimit = null, stepPrice = null, buySellFee = null;

                {
                    int expectedIdx = 0;
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
                                    case 0:  secId         = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SECID [0]
                                    case 1:  boardId       = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // BOARDID [1]
                                    case 2:  shortName     = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SHORTNAME [2]
                                    case 3:  secName       = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SECNAME [3]
                                    case 4:  decimals      = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey);    break; // DECIMALS [5]
                                    case 5:  minStep       = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // MINSTEP [6]
                                    case 6:  lastTradeDate = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // LASTTRADEDATE [7]
                                    case 7:  lastDelDate   = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // LASTDELDATE [8]
                                    case 8:  secType       = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // SECTYPE [9]
                                    case 9:  assetCode     = ParseHelpersUtf8.ReadString(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // ASSETCODE [11]
                                    case 10: lotVolume     = ParseHelpersUtf8.ReadInt(ref reader, rowIndex, expectedIdx, schema.RootKey);    break; // LOTVOLUME [13]
                                    case 11: initialMargin = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // INITIALMARGIN [14]
                                    case 12: highLimit     = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // HIGHLIMIT [15]
                                    case 13: lowLimit      = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // LOWLIMIT [16]
                                    case 14: stepPrice     = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // STEPPRICE [17]
                                    case 15: buySellFee    = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break; // BUYSELLFEE [21]
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                list.Add(new FuturesInstrumentCardDTO
                {
                    SecId = secId, BoardId = boardId, ShortName = shortName,
                    SecName = secName, Decimals = decimals, MinStep = minStep,
                    LastTradeDate = lastTradeDate, LastDelDate = lastDelDate,
                    SecType = secType, AssetCode = assetCode, LotVolume = lotVolume,
                    InitialMargin = initialMargin, HighLimit = highLimit,
                    LowLimit = lowLimit, StepPrice = stepPrice, BuySellFee = buySellFee,
                });

                rowIndex++;
            }
        }

    }
}
