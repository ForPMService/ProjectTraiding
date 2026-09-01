using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using System.Text.Json;

namespace ProjectTraiding.CustomFeatures.Parsing;

public static class ParsingRfudSecurities
{
    public static List<RfudSecurityDTO> ParseSecIds(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "securities";
        using JsonDocument document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty(rootKey, out JsonElement root))
            CalendarSchema.Mismatch($"[{rootKey}] Раздел отсутствует в ответе.");

        if (!root.TryGetProperty("columns", out JsonElement columns))
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'columns' отсутствует.");
        if (columns.ValueKind != JsonValueKind.Array)
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'columns' не является массивом.");

        int secIdPosition = -1;
        int columnIndex = 0;
        foreach (JsonElement column in columns.EnumerateArray())
        {
            if (column.ValueEquals("SECID"))
            {
                secIdPosition = columnIndex;
                break;
            }
            columnIndex++;
        }

        if (secIdPosition < 0)
            CalendarSchema.Mismatch($"[{rootKey}] Колонка 'SECID' отсутствует.");

        if (!root.TryGetProperty("data", out JsonElement data))
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'data' отсутствует.");
        if (data.ValueKind != JsonValueKind.Array)
            CalendarSchema.Mismatch($"[{rootKey}] Раздел 'data' не является массивом.");

        List<RfudSecurityDTO> result = new(data.GetArrayLength());
        int rowIndex = 0;
        foreach (JsonElement row in data.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array)
                CalendarSchema.Mismatch($"[{rootKey}] Строка {rowIndex} не является массивом.");
            if (row.GetArrayLength() <= secIdPosition)
                CalendarSchema.Mismatch(
                    $"[{rootKey}] Строка {rowIndex}: отсутствует значение 'SECID'.");

            JsonElement value = row[secIdPosition];
            string? secId = value.ValueKind == JsonValueKind.Null ? null : value.GetString();
            if (!string.IsNullOrWhiteSpace(secId))
                result.Add(new RfudSecurityDTO { SecId = secId });

            rowIndex++;
        }

        return result;
    }
}
