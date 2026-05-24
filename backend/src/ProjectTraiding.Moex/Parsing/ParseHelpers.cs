using System.Globalization;
using System.Text.Json;

namespace History_DataMoex.Parsing
{
    internal static class ParseHelpers
    {
        // HISTORICAL: kept for source-contract audit. All callers migrated to Utf8 parsers (B9.5 / B10).
        // Uncomment only if need to compare JsonDocument vs Utf8JsonReader output.
        /*
        
        public static string? GetStringOrNull(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                return element.GetString();
            }
            return null;
        }
        public static decimal? GetDecimalOrNull(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDecimal();
            }
            return null;
        }
        public static double? GetDoubleOrNull(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetDouble();
            }
            return null;
        }

        public static long? GetLongOrNull(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt64();
            }
            return null;
        }

        public static int? GetIntOrNull(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Number)
            {
                return element.GetInt32();
            }
            return null;
        }

        public static DateTime? GetDateTimeOrNull(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParseExact(element.GetString(), "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dateTime))
                {
                    return dateTime;
                }
            }
            return null;
        }


        public static void ValidateColumns(JsonElement columns, ColumnAndNumbersForParsing.ExpectedColumn[] expectedColumns)
        {
            if (columns.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Ошибка валидации столбцов. Ожидался массив columns[].");
            }

            int columnCount = columns.GetArrayLength();


            foreach (var expectedColumn in expectedColumns)
            {
                if(expectedColumn.SourceIndex <0 || expectedColumn.SourceIndex >= columnCount )
                {
                    throw new InvalidOperationException("Ошибка валидации столбцов. Ожидалось: " + System.Text.Encoding.UTF8.GetString(expectedColumn.Name) + " на позиции " + expectedColumn.SourceIndex + ", но всего колонок: " + columnCount);
                }

                if (!columns[expectedColumn.SourceIndex].ValueEquals(expectedColumn.Name))
                {
                    string actualName =
                        columns[expectedColumn.SourceIndex].ValueKind == JsonValueKind.String
                            ? columns[expectedColumn.SourceIndex].GetString() ?? "<null>"
                            : columns[expectedColumn.SourceIndex].ValueKind.ToString();

                    throw new InvalidOperationException("Ошибка валидации столбцов. " + "Ожидалось: " + System.Text.Encoding.UTF8.GetString(expectedColumn.Name) + " на позиции " + expectedColumn.SourceIndex +
                        ". Фактически: " + actualName);
                }

            }


            
        }
        */
       

    }
}
