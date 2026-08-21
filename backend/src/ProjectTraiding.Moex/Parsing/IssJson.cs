using System.Globalization;
using System.Text.Json;

namespace ProjectTraiding.Moex.Parsing
{
    /// <summary>
    /// Чтение ответов биржи в формате «колонки и данные» через дерево документа.
    /// Применяется только на холодных путях: календарь и справочники по команде оператора.
    /// Горячие пути приёма рядов остаются на Utf8JsonReader.
    /// </summary>
    public static class IssJson
    {
        /// <summary>
        /// Находит раздел, сверяет состав и порядок колонок, возвращает массив строк данных.
        /// </summary>
        public static JsonElement Block(
            JsonDocument document,
            string rootKey,
            string[] expectedColumns)
        {
            JsonElement root;
            if (!document.RootElement.TryGetProperty(rootKey, out root))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Раздел отсутствует в ответе.");

            JsonElement columns;
            if (!root.TryGetProperty("columns", out columns))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Раздел 'columns' отсутствует.");
            if (columns.ValueKind != JsonValueKind.Array)
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Раздел 'columns' не является массивом.");
            if (columns.GetArrayLength() != expectedColumns.Length)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Ожидалось {expectedColumns.Length} колонок, " +
                    $"получено {columns.GetArrayLength()}.");

            int position = 0;
            foreach (JsonElement column in columns.EnumerateArray())
            {
                if (!column.ValueEquals(expectedColumns[position]))
                    ParseHelpersUtf8.SchemaMismatch(
                        $"[{rootKey}] Колонка {position}: ожидалась '{expectedColumns[position]}'.");
                position++;
            }

            JsonElement data;
            if (!root.TryGetProperty("data", out data))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Раздел 'data' отсутствует.");
            if (data.ValueKind != JsonValueKind.Array)
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Раздел 'data' не является массивом.");
            return data;
        }

        /// <summary>
        /// Проверка формы строки данных. Вызывается один раз на строку в начале её разбора,
        /// заменяет прежние проверки короткой и длинной строки в ручном читателе.
        /// </summary>
        public static void CheckRow(JsonElement row, int expectedLength, string rootKey, int rowIndex)
        {
            if (row.ValueKind != JsonValueKind.Array)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Строка {rowIndex} не является массивом.");
            if (row.GetArrayLength() != expectedLength)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Строка {rowIndex}: ожидалось {expectedLength} значений, " +
                    $"получено {row.GetArrayLength()}.");
        }

        public static string? Str(JsonElement row, int position)
        {
            JsonElement cell = row[position];
            return cell.ValueKind == JsonValueKind.Null ? null : cell.GetString();
        }

        public static string Required(JsonElement row, int position, string rootKey, string field)
        {
            string? value = Str(row, position);
            if (string.IsNullOrWhiteSpace(value))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Пустое обязательное поле '{field}'.");
            return value!;
        }

        public static int? Int(JsonElement row, int position)
        {
            JsonElement cell = row[position];
            return cell.ValueKind == JsonValueKind.Null ? null : cell.GetInt32();
        }

        /// <summary>
        /// Целое в диапазоне smallint. Выход за диапазон останавливает загрузку:
        /// молчаливое усечение значения недопустимо.
        /// </summary>
        public static short? Int16(JsonElement row, int position, string rootKey, string field)
        {
            int? value = Int(row, position);
            if (value is null)
                return null;
            if (value.Value < short.MinValue || value.Value > short.MaxValue)
                ParseHelpersUtf8.SchemaMismatch(
                    $"[{rootKey}] Значение '{field}' вне диапазона smallint: {value.Value}.");
            return (short)value.Value;
        }

        public static DateOnly? Date(JsonElement row, int position, string rootKey, string field)
        {
            string? raw = Str(row, position);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            DateOnly parsed;
            if (!DateOnly.TryParseExact(
                    raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Некорректная дата '{field}': '{raw}'.");
            return parsed;
        }

        public static DateOnly RequiredDate(JsonElement row, int position, string rootKey, string field)
        {
            DateOnly? value = Date(row, position, rootKey, field);
            if (value is null)
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Пустая обязательная дата '{field}'.");
            return value!.Value;
        }

        public static TimeOnly? Time(JsonElement row, int position, string rootKey, string field)
        {
            string? raw = Str(row, position);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            TimeOnly parsed;
            if (!TimeOnly.TryParseExact(
                    raw, "HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Некорректное время '{field}': '{raw}'.");
            return parsed;
        }

        public static DateTime? Stamp(JsonElement row, int position, string rootKey, string field)
        {
            string? raw = Str(row, position);
            if (string.IsNullOrWhiteSpace(raw))
                return null;
            DateTime parsed;
            if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
                ParseHelpersUtf8.SchemaMismatch($"[{rootKey}] Некорректная отметка '{field}': '{raw}'.");
            return parsed;
        }
    }
}
