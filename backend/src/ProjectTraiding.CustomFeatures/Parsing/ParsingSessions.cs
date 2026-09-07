using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using System.Globalization;
using System.Text.Json;

namespace ProjectTraiding.CustomFeatures.Parsing;

public static class ParsingSessions
{
    private static readonly string[] StockSessionColumns =
    [
        "tradedate", "tradingsession", "boardid", "secid", "type", "time_from",
        "time_till", "updatetime",
    ];

    private static readonly string[] FuturesSessionColumns =
    [
        "tradedate", "secid", "boardid", "type", "time_from", "time_till", "updatetime",
    ];

    public static List<TradingPeriodWriteDTO> ParseStockSessions(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "session_schedule";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, StockSessionColumns);
        List<TradingPeriodWriteDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;

        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, StockSessionColumns.Length, rootKey, rowIndex);
            DateOnly tradeDate = CalendarJson.RequiredDate(row, 0, rootKey, "tradedate");
            result.Add(new TradingPeriodWriteDTO
            {
                TradeDate = tradeDate,
                Market = "stock",
                Session = CalendarJson.Int16(row, 1, rootKey, "tradingsession"),
                BoardId = CalendarJson.Str(row, 2) ?? string.Empty,
                SecId = CalendarJson.Str(row, 3) ?? string.Empty,
                PeriodType = CalendarJson.Required(row, 4, rootKey, "type"),
                TimeFrom = CombineStockTime(
                    tradeDate, CalendarJson.Required(row, 5, rootKey, "time_from"), rootKey, "time_from"),
                TimeTill = CombineOptionalStockTime(
                    tradeDate, CalendarJson.Str(row, 6), rootKey, "time_till"),
                MoexUpdateTime = CalendarJson.Stamp(row, 7, rootKey, "updatetime"),
            });
            rowIndex++;
        }

        return result;
    }

    public static List<TradingPeriodWriteDTO> ParseFuturesSessions(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "session_schedule";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, FuturesSessionColumns);
        List<TradingPeriodWriteDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;

        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, FuturesSessionColumns.Length, rootKey, rowIndex);
            result.Add(new TradingPeriodWriteDTO
            {
                TradeDate = CalendarJson.RequiredDate(row, 0, rootKey, "tradedate"),
                Market = "futures",
                SecId = NormalizeFuturesScope(CalendarJson.Str(row, 1)),
                BoardId = NormalizeFuturesScope(CalendarJson.Str(row, 2)),
                PeriodType = CalendarJson.Required(row, 3, rootKey, "type"),
                TimeFrom = RequiredTimestamp(row, 4, rootKey, "time_from"),
                TimeTill = CalendarJson.Stamp(row, 5, rootKey, "time_till"),
                MoexUpdateTime = CalendarJson.Stamp(row, 6, rootKey, "updatetime"),
            });
            rowIndex++;
        }

        return result;
    }

    private static DateTime CombineStockTime(
        DateOnly tradeDate,
        string raw,
        string rootKey,
        string field)
    {
        if (!TimeOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            CalendarSchema.Mismatch($"[{rootKey}] Некорректное время '{field}': '{raw}'.");
        return tradeDate.ToDateTime(time);
    }

    private static DateTime? CombineOptionalStockTime(
        DateOnly tradeDate,
        string? raw,
        string rootKey,
        string field)
    {
        return string.IsNullOrWhiteSpace(raw)
            ? null
            : CombineStockTime(tradeDate, raw, rootKey, field);
    }

    private static DateTime RequiredTimestamp(
        JsonElement row,
        int position,
        string rootKey,
        string field)
    {
        DateTime? value = CalendarJson.Stamp(row, position, rootKey, field);
        if (value is null)
            CalendarSchema.Mismatch($"[{rootKey}] Пустая обязательная отметка '{field}'.");
        return value!.Value;
    }

    private static string NormalizeFuturesScope(string? value) => value == "-" ? string.Empty : value ?? string.Empty;
}
