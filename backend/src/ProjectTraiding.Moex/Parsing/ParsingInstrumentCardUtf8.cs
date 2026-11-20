using ProjectTraiding.Moex.Contracts.Dto.Iss;
using ProjectTraiding.Moex.DataFormats;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Разбор карточек инструментов — паспортный блок securities из списочного ответа.
    /// </summary>
    public static class ParsingInstrumentCardUtf8
    {
        private const string RootKey = "securities";

        public static List<StockInstrumentCardDTO> ParseStockCards(ReadOnlySpan<byte> jsonBytes)
        {
            List<StockInstrumentCardDTO> list = new();
            Utf8JsonReader reader = new(jsonBytes);
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
                    ParseHelpersUtf8.ValidateInputColumnsUtf8(
                        ref reader, MoexColumnSets.SecuritiesStock, RootKey);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                    {
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{RootKey}] Секция 'data' встретилась до 'columns'. " +
                            "Порядок columns → data обязателен.");
                    }

                    foundData = true;
                    ReadStockSecuritiesData(ref reader, list);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, RootKey);
            return list;
        }

        private static void ReadStockSecuritiesData(
            ref Utf8JsonReader reader,
            List<StockInstrumentCardDTO> list)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", RootKey);

            int rowIndex = 0;
            bool foundDataEnd = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    foundDataEnd = true;
                    break;
                }

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{RootKey}] Ожидалась строка-массив в data, получено {reader.TokenType}.");
                }

                string? secId = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 0);
                string? boardId = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 1);
                string? shortName = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 2);
                int? lotSize = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, RootKey, 3);
                string? status = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 4);
                int? decimals = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, RootKey, 5);
                string? secName = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 6);
                double? minStep = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 7);
                long? issueSize = ParseHelpersUtf8.ReadNextNullableLong(ref reader, rowIndex, RootKey, 8);
                string? isin = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 9);
                string? currency = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 10);
                string? secType = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 11);
                int? listLevel = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, RootKey, 12);

                ParseHelpersUtf8.ExpectRowEnd(
                    ref reader, rowIndex, RootKey, MoexColumnSets.SecuritiesStock.Length);

                list.Add(new StockInstrumentCardDTO
                {
                    SecId = secId,
                    BoardId = boardId,
                    ShortName = shortName,
                    LotSize = lotSize,
                    Status = status,
                    Decimals = decimals,
                    SecName = secName,
                    MinStep = minStep,
                    IssueSize = issueSize,
                    Isin = isin,
                    Currency = currency,
                    SecType = secType,
                    ListLevel = listLevel,
                });

                rowIndex++;
            }

            if (!foundDataEnd)
            {
                ParseHelpersUtf8.SchemaMismatch($"[{RootKey}] Ожидался EndArray у секции data.");
            }
        }

        public static List<FuturesInstrumentCardDTO> ParseFuturesCards(ReadOnlySpan<byte> jsonBytes)
        {
            List<FuturesInstrumentCardDTO> list = new();
            Utf8JsonReader reader = new(jsonBytes);
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
                    ParseHelpersUtf8.ValidateInputColumnsUtf8(
                        ref reader, MoexColumnSets.SecuritiesFutures, RootKey);
                }
                else if (reader.ValueTextEquals("data"u8))
                {
                    if (!foundColumns)
                    {
                        ParseHelpersUtf8.SchemaMismatch(
                            $"[{RootKey}] Секция 'data' встретилась до 'columns'. " +
                            "Порядок columns → data обязателен.");
                    }

                    foundData = true;
                    ReadFuturesSecuritiesData(ref reader, list);
                }
                else
                {
                    reader.Skip();
                }
            }

            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, RootKey);
            return list;
        }

        private static void ReadFuturesSecuritiesData(
            ref Utf8JsonReader reader,
            List<FuturesInstrumentCardDTO> list)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", RootKey);

            int rowIndex = 0;
            bool foundDataEnd = false;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                {
                    foundDataEnd = true;
                    break;
                }

                if (reader.TokenType != JsonTokenType.StartArray)
                {
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{RootKey}] Ожидалась строка-массив в data, получено {reader.TokenType}.");
                }

                string? secId = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 0);
                string? boardId = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 1);
                string? shortName = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 2);
                string? secName = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 3);
                int? decimals = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, RootKey, 4);
                double? minStep = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 5);
                string? lastTradeDate = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 6);
                string? lastDelDate = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 7);
                string? secType = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 8);
                string? assetCode = ParseHelpersUtf8.ReadNextNullableString(ref reader, rowIndex, RootKey, 9);
                int? lotVolume = ParseHelpersUtf8.ReadNextNullableInt(ref reader, rowIndex, RootKey, 10);
                double? initialMargin = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 11);
                double? highLimit = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 12);
                double? lowLimit = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 13);
                double? stepPrice = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 14);
                double? buySellFee = ParseHelpersUtf8.ReadNextNullableDouble(ref reader, rowIndex, RootKey, 15);

                ParseHelpersUtf8.ExpectRowEnd(
                    ref reader, rowIndex, RootKey, MoexColumnSets.SecuritiesFutures.Length);

                list.Add(new FuturesInstrumentCardDTO
                {
                    SecId = secId,
                    BoardId = boardId,
                    ShortName = shortName,
                    SecName = secName,
                    Decimals = decimals,
                    MinStep = minStep,
                    LastTradeDate = lastTradeDate,
                    LastDelDate = lastDelDate,
                    SecType = secType,
                    AssetCode = assetCode,
                    LotVolume = lotVolume,
                    InitialMargin = initialMargin,
                    HighLimit = highLimit,
                    LowLimit = lowLimit,
                    StepPrice = stepPrice,
                    BuySellFee = buySellFee,
                });

                rowIndex++;
            }

            if (!foundDataEnd)
            {
                ParseHelpersUtf8.SchemaMismatch($"[{RootKey}] Ожидался EndArray у секции data.");
            }
        }
    }
}
