using ProjectTraiding.Moex.Contracts.Dto.MarketStatistics;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Разбор MarketStatistics — securities-блок
    /// для акций (TQBR) и фьючерсов (RFUD).
    ///
    /// Источник: /engines/{engine}/markets/{market}/boards/{board}/securities/{ticker}.json?iss.only=securities
    ///
    /// Паттерн: один проход Utf8JsonReader, валидация columns, чтение data.
    /// Возвращает одну строку (single-ticker endpoint) или null если data[] пуст.
    /// </summary>
    public static class ParsingMarketStatisticsUtf8
    {
        // ═══════════════════════════════════════════════════════════
        // Stock Securities (10 из 27 колонок)
        // ═══════════════════════════════════════════════════════════

        public static MarketStatisticsStockSecuritiesDTO? ParseStockSecurities(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.MarketStatisticsStockSecuritiesSchema;
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

            bool foundColumns = false;
            bool foundData = false;
            MarketStatisticsStockSecuritiesDTO? result = null;

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
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    result = ReadStockSecuritiesFirstRow(ref reader, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            return result;
        }

        private static MarketStatisticsStockSecuritiesDTO? ReadStockSecuritiesFirstRow(
            ref Utf8JsonReader reader,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                return null;

            // 10 полей: SECID[0], BOARDID[1], STATUS[6], DECIMALS[8], MINSTEP[14],
            //           ISSUESIZE[18], ISIN[19], CURRENCYID[23], LISTLEVEL[25], SETTLEDATE[26]
            string? secId = null, boardId = null, status = null;
            string? isin = null, currencyId = null, settleDate = null;
            int? decimals = null, listLevel = null;
            double? minStep = null;
            long? issueSize = null;

            int pos = 0;
            int expectedIdx = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;

                if (expectedIdx < schema.Columns.Length
                    && pos == schema.Columns[expectedIdx].SourceIndex)
                {
                    if (reader.TokenType != JsonTokenType.Null)
                    {
                        switch (expectedIdx)
                        {
                            case 0: secId = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 1: boardId = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 2: status = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 3: decimals = ParseHelpersUtf8.ReadInt(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 4: minStep = ParseHelpersUtf8.ReadDouble(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 5: issueSize = ParseHelpersUtf8.ReadLong(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 6: isin = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 7: currencyId = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 8: listLevel = ParseHelpersUtf8.ReadInt(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 9: settleDate = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                        }
                    }
                    expectedIdx++;
                }
                pos++;
            }

            SkipRemainingRows(ref reader);

            return new MarketStatisticsStockSecuritiesDTO
            {
                SECID = secId, BOARDID = boardId, STATUS = status,
                DECIMALS = decimals, MINSTEP = minStep,
                ISSUESIZE = issueSize, ISIN = isin,
                CURRENCYID = currencyId, LISTLEVEL = listLevel,
                SETTLEDATE = settleDate,
            };
        }

        // ═══════════════════════════════════════════════════════════
        // Futures Securities (7 из 26 колонок)
        // ═══════════════════════════════════════════════════════════

        public static MarketStatisticsFuturesSecuritiesDTO? ParseFuturesSecurities(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.MarketStatisticsFuturesSecuritiesSchema;
            var reader = new Utf8JsonReader(jsonBytes);

            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

            bool foundColumns = false;
            bool foundData = false;
            MarketStatisticsFuturesSecuritiesDTO? result = null;

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
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");

                    foundData = true;
                    result = ReadFuturesSecuritiesFirstRow(ref reader, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);
            return result;
        }

        private static MarketStatisticsFuturesSecuritiesDTO? ReadFuturesSecuritiesFirstRow(
            ref Utf8JsonReader reader,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            if (!reader.Read() || reader.TokenType == JsonTokenType.EndArray)
                return null;

            // 7 полей: SECID[0], BOARDID[1], LASTSETTLEPRICE[18], IMTIME[20],
            //          BUYSELLFEE[21], SCALPERFEE[22], SETTLEPRICE_CLR[25]
            string? secId = null, boardId = null, imTime = null;
            double? lastSettlePrice = null, buySellFee = null, scalperFee = null, settlePriceClr = null;

            int pos = 0;
            int expectedIdx = 0;

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray) break;

                if (expectedIdx < schema.Columns.Length
                    && pos == schema.Columns[expectedIdx].SourceIndex)
                {
                    if (reader.TokenType != JsonTokenType.Null)
                    {
                        switch (expectedIdx)
                        {
                            case 0: secId = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 1: boardId = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 2: lastSettlePrice = ParseHelpersUtf8.ReadDouble(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 3: imTime = ParseHelpersUtf8.ReadString(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 4: buySellFee = ParseHelpersUtf8.ReadDouble(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 5: scalperFee = ParseHelpersUtf8.ReadDouble(ref reader, 0, expectedIdx, schema.RootKey); break;
                            case 6: settlePriceClr = ParseHelpersUtf8.ReadDouble(ref reader, 0, expectedIdx, schema.RootKey); break;
                        }
                    }
                    expectedIdx++;
                }
                pos++;
            }

            SkipRemainingRows(ref reader);

            return new MarketStatisticsFuturesSecuritiesDTO
            {
                SECID = secId, BOARDID = boardId,
                LASTSETTLEPRICE = lastSettlePrice, IMTIME = imTime,
                BUYSELLFEE = buySellFee, SCALPERFEE = scalperFee,
                SETTLEPRICE_CLR = settlePriceClr,
            };
        }

        // ═══════════════════════════════════════════════════════════
        // Helper: пропустить оставшиеся строки data[]
        // ═══════════════════════════════════════════════════════════

        private static void SkipRemainingRows(ref Utf8JsonReader reader)
        {
            int depth = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.StartArray)
                    depth++;
                else if (reader.TokenType == JsonTokenType.EndArray)
                {
                    if (depth == 0) return;
                    depth--;
                }
            }
        }
    }
}
