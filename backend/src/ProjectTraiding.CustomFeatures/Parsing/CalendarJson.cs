using System.Globalization;
using System.Text.Json;

namespace ProjectTraiding.CustomFeatures.Parsing;

public static class CalendarJson
{
    public static JsonElement Block(
        JsonDocument document,
        string rootKey,
        string[] expectedColumns)
    {
        JsonElement root;
        if (!document.RootElement.TryGetProperty(rootKey, out root))
            CalendarSchema.Mismatch($"[{rootKey}] Раздел отсутствует в ответе.");

        JsonElement columns;
        if (!root.TryGetProperty("columns", out columns))
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'columns' отсутствует.");
        if (columns.ValueKind != JsonValueKind.Array)
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'columns' не является массивом.");
        if (columns.GetArrayLength() != expectedColumns.Length)
            CalendarSchema.Mismatch(
                $"[{rootKey}] Ожидалось {expectedColumns.Length} колонок, " +
                $"получено {columns.GetArrayLength()}.");

        int position = 0;
        foreach (JsonElement column in columns.EnumerateArray())
        {
            if (!column.ValueEquals(expectedColumns[position]))
                CalendarSchema.Mismatch(
                    $"[{rootKey}] Колонка {position}: ожидалась '{expectedColumns[position]}'.");
            position++;
        }

        JsonElement data;
        if (!root.TryGetProperty("data", out data))
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'data' отсутствует.");
        if (data.ValueKind != JsonValueKind.Array)
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'data' не является массивом.");
        return data;
    }

    public static void CheckRow(JsonElement row, int expectedLength, string rootKey, int rowIndex)
    {
        if (row.ValueKind != JsonValueKind.Array)
            CalendarSchema.Mismatch($"[{rootKey}] Строка {rowIndex} не является массивом.");
        if (row.GetArrayLength() != expectedLength)
            CalendarSchema.Mismatch(
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
            CalendarSchema.Mismatch($"[{rootKey}] Пустое обязательное поле '{field}'.");
        return value!;
    }

    public static int? Int(JsonElement row, int position)
    {
        JsonElement cell = row[position];
        return cell.ValueKind == JsonValueKind.Null ? null : cell.GetInt32();
    }

    public static short? Int16(JsonElement row, int position, string rootKey, string field)
    {
        int? value = Int(row, position);
        if (value is null)
            return null;
        if (value.Value < short.MinValue || value.Value > short.MaxValue)
            CalendarSchema.Mismatch(
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
            CalendarSchema.Mismatch($"[{rootKey}] Некорректная дата '{field}': '{raw}'.");
        return parsed;
    }

    public static DateOnly RequiredDate(JsonElement row, int position, string rootKey, string field)
    {
        DateOnly? value = Date(row, position, rootKey, field);
        if (value is null)
            CalendarSchema.Mismatch($"[{rootKey}] Пустая обязательная дата '{field}'.");
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
            CalendarSchema.Mismatch($"[{rootKey}] Некорректное время '{field}': '{raw}'.");
        return parsed;
    }

    public static DateTime? Stamp(JsonElement row, int position, string rootKey, string field)
    {
        string? raw = Str(row, position);
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        DateTime parsed;
        if (!DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            CalendarSchema.Mismatch($"[{rootKey}] Некорректная отметка '{field}': '{raw}'.");
        return parsed;
    }
}
