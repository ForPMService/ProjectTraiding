using ProjectTraiding.Moex.Contracts.Dto.Algopack;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    public static class ParsingAlgUtf8
    {
        public static List<CandlesDTO> ParseAlgCandles(ReadOnlySpan<byte> jsonBytes)
        {
            var schema = ColumnAndNumbersForParsing.AlgCandlesSchema;
            var candlesList = new List<CandlesDTO>();
            var reader = new Utf8JsonReader(jsonBytes);

            // ── Шаг 1. Найти RootKey на верхнем уровне JSON (A1) ──
            ParseHelpersUtf8.SkipToRootObject(ref reader, schema.RootKey);

            // ── Шаг 2. Читать свойства ТОЛЬКО внутри RootKey-объекта ──
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
                    // A2: data без предшествующего columns — ошибка.
                    // Без валидации схемы данные нельзя читать —
                    // поля могут оказаться не в тех позициях.
                    if (!foundColumns)
                    {
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Секция 'data' встретилась до 'columns'. Порядок columns → data обязателен.");
                    }

                    foundData = true;
                    ReadCandlesData(ref reader, candlesList, schema);
                }
                else
                {
                    reader.Skip();
                }
            }

            // ── Шаг 3. Проверить что нашли обязательные секции ──
            ParseHelpersUtf8.ValidateStructure(foundColumns, foundData, schema.RootKey);

            return candlesList;
        }

        // ═══════════════════════════════════════════════════════════
        // Чтение данных свечей (A3 + A5)
        // ═══════════════════════════════════════════════════════════

        /// <summary>
        /// Чтение массива строк данных свечей.
        /// Инлайн-цикл по schema.TotalColumns позициям без делегата.
        /// Для свечей schema.TotalColumns == schema.Columns.Length == 8.
        /// </summary>
        private static void ReadCandlesData(
            ref Utf8JsonReader reader,
            List<CandlesDTO> candlesList,
            ColumnAndNumbersForParsing.ExpectedSchema schema)
        {
            ParseHelpersUtf8.ReadAndExpect(ref reader, JsonTokenType.StartArray, "data", schema.RootKey);

            int rowIndex = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                double? open = null, close = null, high = null, low = null;
                double? value = null, volume = null;
                DateTime? begin = null, end = null;

                {
                    int expectedIdx = 0;
                    // 0=open 1=close 2=high 3=low 4=value 5=volume 6=begin 7=end
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
                                    case 0: open = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 1: close = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 2: high = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 3: low = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 4: value = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 5: volume = ParseHelpersUtf8.ReadDouble(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 6: begin = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                    case 7: end = ParseHelpersUtf8.ReadDateTimeUtf8(ref reader, rowIndex, expectedIdx, schema.RootKey); break;
                                }
                            }

                            expectedIdx++;
                        }
                    }

                    if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray)
                        ParseHelpersUtf8.SchemaMismatch(schema.RootKey,
                            $"[{schema.RootKey}] Ожидался EndArray после {schema.TotalColumns} колонок (строка {rowIndex}).");
                }

                candlesList.Add(new CandlesDTO
                {
                    Open = open,
                    Close = close,
                    High = high,
                    Low = low,
                    Value = value,
                    Volume = volume,
                    Begin = begin,
                    End = end
                });

                rowIndex++;
            }
        }
    }
}
