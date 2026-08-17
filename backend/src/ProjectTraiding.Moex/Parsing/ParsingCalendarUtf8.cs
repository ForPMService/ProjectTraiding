using ProjectTraiding.Moex.Contracts.Dto.Calendar;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingCalendarUtf8
    {
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
