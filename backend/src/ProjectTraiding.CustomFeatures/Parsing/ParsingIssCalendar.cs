using ProjectTraiding.CustomFeatures.Contracts.Dto.Calendar;
using System.Text.Json;

namespace ProjectTraiding.CustomFeatures.Parsing;

public static class ParsingIssCalendar
{
    private static readonly string[] EngineColumns =
        ["date", "is_work_day", "start_time", "stop_time"];

    private static readonly string[] ListingColumns =
    [
        "SECID", "SHORTNAME", "NAME", "BOARDID", "decimals", "history_from", "history_till",
    ];

    private static readonly string[] SplitsColumns =
        ["tradedate", "secid", "before", "after"];

    public static List<EngineDailyTableDTO> ParseEngine(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "dailytable";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, EngineColumns);
        List<EngineDailyTableDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, EngineColumns.Length, rootKey, rowIndex);
            result.Add(new EngineDailyTableDTO
            {
                TradeDate = CalendarJson.RequiredDate(row, 0, rootKey, "date"),
                IsWorkDay = CalendarJson.Int(row, 1),
                StartTime = CalendarJson.Time(row, 2, rootKey, "start_time"),
                StopTime = CalendarJson.Time(row, 3, rootKey, "stop_time"),
            });
            rowIndex++;
        }
        return result;
    }

    public static List<ListingIntervalDTO> ParseListing(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "securities";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, ListingColumns);
        List<ListingIntervalDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, ListingColumns.Length, rootKey, rowIndex);
            result.Add(new ListingIntervalDTO
            {
                SecId = CalendarJson.Required(row, 0, rootKey, "SECID"),
                BoardId = CalendarJson.Required(row, 3, rootKey, "BOARDID"),
                HistoryFrom = CalendarJson.Date(row, 5, rootKey, "history_from"),
                HistoryTill = CalendarJson.Date(row, 6, rootKey, "history_till"),
            });
            rowIndex++;
        }
        return result;
    }

    public static List<SplitWriteDTO> ParseSplits(ReadOnlyMemory<byte> json)
    {
        const string rootKey = "splits";
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement rows = CalendarJson.Block(document, rootKey, SplitsColumns);
        List<SplitWriteDTO> result = new(rows.GetArrayLength());
        int rowIndex = 0;
        foreach (JsonElement row in rows.EnumerateArray())
        {
            CalendarJson.CheckRow(row, SplitsColumns.Length, rootKey, rowIndex);
            int? before = CalendarJson.Int(row, 2);
            int? after = CalendarJson.Int(row, 3);
            if (before is null || after is null)
                CalendarSchema.Mismatch(
                    $"[{rootKey}] Строка {rowIndex}: пустое отношение дробления.");
            result.Add(new SplitWriteDTO
            {
                TradeDate = CalendarJson.RequiredDate(row, 0, rootKey, "tradedate"),
                SecId = CalendarJson.Required(row, 1, rootKey, "secid"),
                BeforeQty = before!.Value,
                AfterQty = after!.Value,
            });
            rowIndex++;
        }
        return result;
    }
}
